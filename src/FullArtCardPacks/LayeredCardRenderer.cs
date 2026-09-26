using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class LayeredCardRenderer
{
    private const string OverlayName = "_full_art_layered_card";
    private const string OverlayGroup = "full_art_card_packs_layered_cards";
    // Validation caches no textures. New texture IDs after eviction/reload
    // require fresh validation, even when their filenames have not changed.
    private readonly record struct ValidationKey(ulong Background, ulong Foreground, ulong Mask, string Channel,
        LayerRegistration? Registration, LayerCanvas BackgroundSize, LayerCanvas ForegroundSize, LayerCanvas MaskSize);
    private static readonly LruCache<ValidationKey, bool> ValidationCache = new(64);
    private static readonly LruCache<string, bool> Warnings = new(128, StringComparer.OrdinalIgnoreCase);
    private static Shader? _shader;
    private static bool _shaderAttempted;
    private static ulong _processTreeId;

    private readonly record struct LayerTextures(Texture2D Background, Texture2D Foreground, Texture2D? ForegroundMask,
        bool NormalizedSourceUv);

    internal static void InvalidateResources()
    {
        ValidationCache.Clear();
        Warnings.Clear();
        _shader = null;
        _shaderAttempted = false;
    }

    internal static void ApplyOrUpdate(NCard cardNode)
    {
        try
        {
            if (!FullArtPackRegistry.TryGetCard(cardNode.Model?.GetType().FullName, out var entry)
                || entry is null
                || !LayerPolicy.TryDecide(entry, FullArtModConfig.EnableLayeredCards,
                    cardNode.Visibility == ModelVisibility.Visible, FullArtModConfig.LayerDepth, out var decision)
                || PortraitResolver.Resolve(cardNode) is not { Texture: not null } portrait
                || LoadTextures(entry) is not { } textures
                || LoadShader() is not { } shader)
            {
                Clear(cardNode);
                return;
            }

            foreach (TextureRect inactive in PortraitResolver.All(cardNode))
                if (inactive != portrait) Remove(Find(inactive));

            TextureRect overlay = Find(portrait) ?? Create(portrait);
            var material = overlay.Material as ShaderMaterial ?? new ShaderMaterial();
            material.Shader = shader;
            material.SetShaderParameter("use_normalized_source_uv", textures.NormalizedSourceUv);
            material.SetShaderParameter("background_texture", textures.Background);
            material.SetShaderParameter("foreground_texture", textures.Foreground);
            material.SetShaderParameter("foreground_mask_texture", textures.ForegroundMask ?? textures.Foreground);
            material.SetShaderParameter("use_foreground_mask", textures.ForegroundMask is not null);
            material.SetShaderParameter("foreground_mask_use_alpha", entry.Layers!.ForegroundMaskChannel == "alpha");
            material.SetShaderParameter("depth", decision.Depth);
            material.SetShaderParameter("background_shift", decision.BackgroundShift);
            material.SetShaderParameter("foreground_shift", decision.ForegroundShift);
            material.SetShaderParameter("foreground_scale", decision.ForegroundScale);
            material.SetShaderParameter("subject_scale", decision.SubjectScale);
            material.SetShaderParameter("subject_offset_x", decision.SubjectOffsetX);
            material.SetShaderParameter("subject_offset_y", decision.SubjectOffsetY);
            material.SetShaderParameter("shadow_strength", decision.ShadowStrength);
            material.SetShaderParameter("frame_inset", decision.FrameInset);
            overlay.Material = material;
            // KeepAspectCovered derives UV from the original source, even
            // when the registered clean plate differs by integer rounding.
            overlay.Texture = textures.NormalizedSourceUv ? textures.Foreground : textures.Background;
            overlay.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            overlay.StretchMode = portrait.StretchMode;
            overlay.Visible = portrait.Visible;
            UpdatePointer(overlay, portrait);
        }
        catch (Exception ex)
        {
            Clear(cardNode);
            WarnOnce(cardNode.Model?.GetType().FullName ?? "unmapped", $"Layered art disabled: {ex.Message}");
        }
    }

    internal static bool HasActiveLayer(NCard cardNode)
        => PortraitResolver.All(cardNode).Any(portrait => Find(portrait) is not null);

    internal static void Clear(NCard cardNode)
    {
        foreach (TextureRect portrait in PortraitResolver.All(cardNode)) Remove(Find(portrait));
    }

    private static Shader? LoadShader()
    {
        if (_shaderAttempted) return _shader;
        _shaderAttempted = true;
        if (!FullArtPackRegistry.TryResolveModPath("shaders/CardLayerShader.gdshader", out string path)
            || !File.Exists(path))
        {
            WarnOnce("shader", "Layered shader missing; preserving flat art.");
            return null;
        }
        _shader = EffectRenderer.TryLoadResource("layered shader", path,
            () => new Shader { Code = File.ReadAllText(path) });
        return _shader;
    }

    private static LayerTextures? LoadTextures(FullArtCardEntry entry)
    {
        FullArtLayerMetadata layers = entry.Layers!;
        if (!LayerPolicy.TryGetRegistration(layers, out var registration)
            || !FullArtPackRegistry.TryResolveModPath(layers.Background, out string backgroundPath)
            || !FullArtPackRegistry.TryResolveModPath(layers.Foreground, out string foregroundPath))
            return null;
        // Identity precedes every positive cache shortcut. A duplicate file
        // with equal bytes is not the original driving the native crop.
        if (registration is not null && (!FullArtPackRegistry.TryResolveModPath(entry.Art, out string sourcePath)
            || !string.Equals(sourcePath, foregroundPath, StringComparison.OrdinalIgnoreCase))) return null;
        bool useMask = layers.ForegroundMask.Length != 0;
        string maskPath = "";
        if (useMask && !FullArtPackRegistry.TryResolveModPath(layers.ForegroundMask, out maskPath))
            return null;
        string key = backgroundPath + "\n" + foregroundPath + "\n" + maskPath + "\n" + layers.ForegroundMaskChannel;
        if (!TextureCache.TryLoadPath(layers.Background, out Texture2D? backgroundTexture)
            || !TextureCache.TryLoadPath(layers.Foreground, out Texture2D? foregroundTexture))
            return null;
        Texture2D? maskTexture = null;
        if (useMask && !TextureCache.TryLoadPath(layers.ForegroundMask, out maskTexture))
            return null;
        // Existing generated RGBA and masked 2:3 packs keep their contract.
        // A newly admitted original canvas must use the very same source as
        // the flat portrait, so both TextureRects inherit an identical crop.
        if (foregroundTexture!.GetWidth() * 3L != foregroundTexture.GetHeight() * 2L
            && (!useMask || !FullArtPackRegistry.TryResolveModPath(entry.Art, out string artPath)
                || !string.Equals(artPath, foregroundPath, StringComparison.OrdinalIgnoreCase)))
            return null;
        var backgroundSize = new LayerCanvas(backgroundTexture!.GetWidth(), backgroundTexture.GetHeight());
        var foregroundSize = new LayerCanvas(foregroundTexture!.GetWidth(), foregroundTexture.GetHeight());
        var maskSize = maskTexture is null ? default : new LayerCanvas(maskTexture.GetWidth(), maskTexture.GetHeight());
        var validationKey = new ValidationKey(backgroundTexture.GetInstanceId(), foregroundTexture.GetInstanceId(),
            maskTexture?.GetInstanceId() ?? 0, layers.ForegroundMaskChannel, registration, backgroundSize, foregroundSize, maskSize);
        var textures = new LayerTextures(backgroundTexture, foregroundTexture, maskTexture, registration is not null);
        if (ValidationCache.TryGetValue(validationKey, out bool valid)) return valid ? textures : null;
        try
        {
            using Image background = backgroundTexture.GetImage();
            using Image foreground = foregroundTexture.GetImage();
            using Image? mask = maskTexture?.GetImage();
            if (useMask && mask is null)
                throw new InvalidDataException("A required foreground mask could not be decoded.");
            if (registration is { } declared && (declared.Source != foregroundSize || declared.Background != backgroundSize
                || declared.Mask != maskSize))
                throw new InvalidDataException("Normalized source UV registration dimensions differ from actual textures.");
            if (background.IsEmpty() || foreground.IsEmpty()
                || (registration is null && background.GetSize() != foreground.GetSize())
                || !LayerPolicy.IsValidCanvas(background.GetWidth(), background.GetHeight(), useMask)
                || !LayerPolicy.IsValidCanvas(foreground.GetWidth(), foreground.GetHeight(), useMask)
                || background.DetectAlpha() != Image.AlphaMode.None
                || (!useMask && foreground.DetectAlpha() == Image.AlphaMode.None) || foreground.IsInvisible())
                throw new InvalidDataException("Layers require matching bounded portrait canvases, an opaque background, and visible coverage; unmasked RGBA layers require 2:3.");
            if (mask is not null)
            {
                if (mask.IsEmpty() || !LayerPolicy.IsValidCanvas(mask.GetWidth(), mask.GetHeight(), true)
                    || (registration is null && mask.GetSize() != foreground.GetSize()))
                    throw new InvalidDataException("Foreground mask must match the source canvas.");
                // Conversion is in memory only. The original source PNG is never rewritten.
                mask.Convert(Image.Format.Rgba8);
                foreground.Convert(Image.Format.Rgba8);
                if (!LayerPolicy.IsValidForegroundMask(mask.GetData(), foreground.GetData(), mask.GetWidth(), mask.GetHeight(),
                    foreground.GetWidth(), foreground.GetHeight(), layers.ForegroundMaskChannel))
                    throw new InvalidDataException("Foreground mask needs visible and hidden regions in its selected red/alpha channel; red masks must be opaque near-grayscale.");
            }
            ValidationCache.Set(validationKey, true);
            return textures;
        }
        catch (Exception ex)
        {
            WarnOnce(key, $"Could not load layered card {layers.Background}: {ex.Message}");
        }
        ValidationCache.Set(validationKey, false);
        return null;
    }

    private static TextureRect Create(TextureRect portrait)
    {
        var overlay = new TextureRect
        {
            Name = OverlayName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Stay inside the native portrait CanvasGroup, beneath card UI.
            ZIndex = 0,
            SelfModulate = portrait.SelfModulate,
        };
        portrait.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddToGroup(OverlayGroup);
        if (Engine.GetMainLoop() is SceneTree tree && tree.GetInstanceId() != _processTreeId)
        {
            tree.ProcessFrame += UpdateVisibleLayers;
            _processTreeId = tree.GetInstanceId();
        }
        return overlay;
    }

    private static void UpdateVisibleLayers()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        foreach (Node node in tree.GetNodesInGroup(OverlayGroup))
        {
            if (node is not TextureRect overlay || !GodotObject.IsInstanceValid(overlay)
                || overlay.IsQueuedForDeletion()
                || overlay.GetParent() is not TextureRect portrait || !portrait.IsVisibleInTree())
                continue;
            NCard? card = null;
            for (Node? ancestor = portrait; ancestor is not null; ancestor = ancestor.GetParent())
                if (ancestor is NCard candidate) { card = candidate; break; }
            if (card is null || card.Visibility != ModelVisibility.Visible)
            {
                Remove(overlay);
                continue;
            }
            UpdatePointer(overlay, portrait);
        }
    }

    private static void UpdatePointer(TextureRect overlay, TextureRect portrait)
    {
        // Modulate is already inherited from the card. SelfModulate is not.
        overlay.SelfModulate = portrait.SelfModulate;
        // A partial-alpha child over a partially faded base would increase
        // opacity. During native fades show the untouched flat card instead;
        // no native texture/material/tween state is modified to hide the base.
        float opacity = portrait.SelfModulate.A;
        for (CanvasItem? item = portrait; item is not null; item = item.GetParent() as CanvasItem)
        {
            opacity *= item.Modulate.A;
            if (item.TopLevel) break;
        }
        overlay.Visible = portrait.Visible && LayerPolicy.CanComposite(opacity);
        if (overlay.Material is not ShaderMaterial material) return;
        Vector2 size = portrait.Size;
        Vector2 position = portrait.GetLocalMousePosition();
        var pointer = size.X > 0f && size.Y > 0f
            ? LayerPolicy.Pointer(position.X / size.X * 2f - 1f, position.Y / size.Y * 2f - 1f,
                FullArtModConfig.ReducedMotion)
            : (X: 0f, Y: 0f);
        material.SetShaderParameter("pointer", new Vector2(pointer.X, pointer.Y));
    }

    private static TextureRect? Find(TextureRect portrait)
    {
        TextureRect? overlay = portrait.GetNodeOrNull<TextureRect>(OverlayName);
        return overlay is not null && !overlay.IsQueuedForDeletion() ? overlay : null;
    }

    private static void Remove(TextureRect? overlay)
    {
        if (overlay is null) return;
        // Detach synchronously: a refresh in the same frame must not reuse a
        // queued node or create an automatically renamed duplicate overlay.
        overlay.GetParent()?.RemoveChild(overlay);
        overlay.QueueFree();
    }

    private static void WarnOnce(string key, string message)
    {
        if (Warnings.TryGetValue(key, out _)) return;
        Warnings.Set(key, true);
        GD.PushWarning($"[FullArtCardPacks] {message}");
    }
}
