using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class LayeredCardRenderer
{
    private const string OverlayName = "_full_art_layered_card";
    private const string OverlayGroup = "full_art_card_packs_layered_cards";
    private static readonly Dictionary<string, LayerTextures?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Warnings = new(StringComparer.OrdinalIgnoreCase);
    private static Shader? _shader;
    private static bool _shaderAttempted;
    private static ulong _processTreeId;

    private sealed record LayerTextures(Texture2D Background, Texture2D Foreground, Texture2D? ForegroundMask);

    internal static void ApplyOrUpdate(NCard cardNode)
    {
        try
        {
            if (!FullArtPackRegistry.TryGetCard(cardNode.Model?.GetType().FullName, out var entry)
                || entry is null
                || !LayerPolicy.TryDecide(entry, FullArtModConfig.EnableLayeredCards,
                    cardNode.Visibility == ModelVisibility.Visible, FullArtModConfig.LayerDepth, out var decision)
                || PortraitResolver.Resolve(cardNode) is not { Texture: not null } portrait
                || LoadTextures(entry.Layers!) is not { } textures
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
            overlay.Texture = textures.Background;
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

    private static LayerTextures? LoadTextures(FullArtLayerMetadata layers)
    {
        if (!FullArtPackRegistry.TryResolveModPath(layers.Background, out string backgroundPath)
            || !FullArtPackRegistry.TryResolveModPath(layers.Foreground, out string foregroundPath))
            return null;
        bool useMask = layers.ForegroundMask.Length != 0;
        string maskPath = "";
        if (useMask && !FullArtPackRegistry.TryResolveModPath(layers.ForegroundMask, out maskPath))
            return null;
        string key = backgroundPath + "\n" + foregroundPath + "\n" + maskPath + "\n" + layers.ForegroundMaskChannel;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        LayerTextures? result = null;
        try
        {
            if (!File.Exists(backgroundPath) || !File.Exists(foregroundPath) || (useMask && !File.Exists(maskPath)))
                throw new InvalidDataException("A required clean plate, foreground, or foreground mask is missing.");
            using Image background = Image.LoadFromFile(backgroundPath);
            using Image foreground = Image.LoadFromFile(foregroundPath);
            using Image? mask = useMask ? Image.LoadFromFile(maskPath) : null;
            if (useMask && mask is null)
                throw new InvalidDataException("A required foreground mask could not be decoded.");
            if (background.IsEmpty() || foreground.IsEmpty()
                || background.GetSize() != foreground.GetSize()
                || background.GetWidth() * 3 != background.GetHeight() * 2
                || background.GetWidth() < 64 || background.GetHeight() > 4096
                || background.DetectAlpha() != Image.AlphaMode.None
                || (!useMask && foreground.DetectAlpha() == Image.AlphaMode.None) || foreground.IsInvisible())
                throw new InvalidDataException("Layers require matching 2:3 images, an opaque background, and a visible transparent foreground.");
            if (mask is not null)
            {
                if (mask.IsEmpty() || mask.GetSize() != foreground.GetSize())
                    throw new InvalidDataException("Foreground mask must match the source canvas.");
                // Conversion is in memory only. The original source PNG is never rewritten.
                mask.Convert(Image.Format.Rgba8);
                foreground.Convert(Image.Format.Rgba8);
                if (!LayerPolicy.IsValidForegroundMask(mask.GetData(), foreground.GetData(), mask.GetWidth(), mask.GetHeight(),
                    layers.ForegroundMaskChannel))
                    throw new InvalidDataException("Foreground mask needs visible and hidden regions in its selected red/alpha channel; red masks must be opaque near-grayscale.");
            }
            result = new(ImageTexture.CreateFromImage(background), ImageTexture.CreateFromImage(foreground),
                mask is not null ? ImageTexture.CreateFromImage(mask) : null);
        }
        catch (Exception ex)
        {
            WarnOnce(key, $"Could not load layered card {layers.Background}: {ex.Message}");
        }
        Cache[key] = result;
        return result;
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
        if (Warnings.Add(key)) GD.PushWarning($"[FullArtCardPacks] {message}");
    }
}
