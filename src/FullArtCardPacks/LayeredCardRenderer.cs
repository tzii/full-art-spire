using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class LayeredCardRenderer
{
    private const string OverlayName = "_full_art_layered_card";
    private const string OverlayGroup = "full_art_card_packs_layered_cards";
    private static readonly StringName LayerKeyMeta = "full_art_card_packs_layer_key";
    // Validation caches no textures. New instances reuse a file-set verdict
    // when the paths, file sizes and write times are unchanged.
    private readonly record struct ValidationKey(ulong Background, ulong Foreground, ulong Mask, string Channel,
        LayerRegistration? Registration, LayerCanvas BackgroundSize, LayerCanvas ForegroundSize, LayerCanvas MaskSize);
    private static readonly LruCache<ValidationKey, bool> ValidationCache = new(64);
    // Survives texture eviction: an unchanged file set (same paths, sizes and
    // write times) needs no second GPU readback and pixel scan.
    private static readonly LruCache<string, bool> FileValidationCache = new(1024, StringComparer.Ordinal);
    // Seconds^-1 for easing layer motion toward the pointer target.
    private const float PointerResponse = 10f;
    private static readonly LruCache<string, bool> Warnings = new(128, StringComparer.OrdinalIgnoreCase);
    private static Shader? _shader;
    private static bool _shaderAttempted;
    private static ulong _processTreeId;

    private readonly record struct LayerTextures(Texture2D Background, Texture2D Foreground, Texture2D? ForegroundMask,
        bool NormalizedSourceUv);

    internal static void InvalidateResources()
    {
        ValidationCache.Clear();
        FileValidationCache.Clear();
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
                || PortraitResolver.Resolve(cardNode) is not { Texture: not null } portrait)
            {
                Clear(cardNode);
                return;
            }
            LayerTextures? loaded = LoadTextures(entry, out string layerKey, out bool pending);
            if (pending)
            {
                PendingArtRefresh.Track(cardNode);
                // An overlay already showing these layers keeps its material's
                // textures; anything else falls back to flat until delivery.
                if (Find(portrait) is { } current && current.GetMeta(LayerKeyMeta, "").AsString() == layerKey)
                    UpdatePointer(current, portrait);
                else
                    Clear(cardNode);
                return;
            }
            if (loaded is not { } textures || LoadShader() is not { } shader)
            {
                Clear(cardNode);
                return;
            }

            decision = LayerPolicy.WithPortraitParallax(decision,
                FullArtModConfig.ReducedMotion ? 0f : FullArtModConfig.PortraitParallax);

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
            material.SetShaderParameter("pop_strength", Math.Clamp(FullArtModConfig.SubjectPop, 0f, 1f));
            overlay.Material = material;
            overlay.SetMeta(LayerKeyMeta, layerKey);
            overlay.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
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

    internal static ShaderMaterial? ActiveMaterial(TextureRect portrait)
        => Find(portrait)?.Material as ShaderMaterial;

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

    private readonly record struct LayerSpec(LayerRegistration? Registration, bool UseMask, bool SourceIsArt, string Channel);

    // Queue a complete layer set before the flat path requests the same source.
    internal static void Preload(FullArtCardEntry entry) => LoadTextures(entry, out _, out _);

    private static LayerTextures? LoadTextures(FullArtCardEntry entry, out string key, out bool pending)
    {
        key = "";
        pending = false;
        FullArtLayerMetadata layers = entry.Layers!;
        if (!LayerPolicy.TryGetRegistration(layers, out var registration)
            || !FullArtPackRegistry.TryResolveModPath(layers.Background, out string backgroundPath)
            || !FullArtPackRegistry.TryResolveModPath(layers.Foreground, out string foregroundPath))
            return null;
        // Identity precedes every positive cache shortcut. A duplicate file
        // with equal bytes is not the original driving the native crop.
        bool sourceIsArt = FullArtPackRegistry.TryResolveModPath(entry.Art, out string artPath)
            && string.Equals(artPath, foregroundPath, StringComparison.OrdinalIgnoreCase);
        if (registration is not null && !sourceIsArt) return null;
        bool useMask = layers.ForegroundMask.Length != 0;
        string maskPath = "";
        if (useMask && !FullArtPackRegistry.TryResolveModPath(layers.ForegroundMask, out maskPath))
            return null;
        key = backgroundPath + "\n" + foregroundPath + "\n" + maskPath + "\n" + layers.ForegroundMaskChannel;
        var spec = new LayerSpec(registration, useMask, sourceIsArt, layers.ForegroundMaskChannel);
        return TextureCache.BackgroundLoading
            ? LoadInBackground(spec, key, backgroundPath, foregroundPath, maskPath, out pending)
            : LoadNow(spec, key, backgroundPath, foregroundPath, maskPath, layers);
    }

    // Existing generated RGBA and masked 2:3 packs keep their contract. A
    // newly admitted original canvas must use the very same source as the
    // flat portrait, so both TextureRects inherit an identical crop.
    private static bool AdmitsForeground(LayerSpec spec, int width, int height)
        => width * 3L == height * 2L || (spec.UseMask && spec.SourceIsArt);

    private static LayerTextures? Admit(LayerSpec spec, Texture2D background, Texture2D foreground, Texture2D? mask,
        out ValidationKey validationKey)
    {
        var backgroundSize = new LayerCanvas(background.GetWidth(), background.GetHeight());
        var foregroundSize = new LayerCanvas(foreground.GetWidth(), foreground.GetHeight());
        var maskSize = mask is null ? default : new LayerCanvas(mask.GetWidth(), mask.GetHeight());
        validationKey = new ValidationKey(background.GetInstanceId(), foreground.GetInstanceId(),
            mask?.GetInstanceId() ?? 0, spec.Channel, spec.Registration, backgroundSize, foregroundSize, maskSize);
        return AdmitsForeground(spec, foregroundSize.Width, foregroundSize.Height)
            ? new LayerTextures(background, foreground, mask, spec.Registration is not null)
            : null;
    }

    // Main thread: loads now and validates by GPU readback when needed.
    private static LayerTextures? LoadNow(LayerSpec spec, string key, string backgroundPath, string foregroundPath,
        string maskPath, FullArtLayerMetadata layers)
    {
        if (!TextureCache.TryLoadPath(layers.Background, out Texture2D? backgroundTexture)
            || !TextureCache.TryLoadPath(layers.Foreground, out Texture2D? foregroundTexture))
            return null;
        Texture2D? maskTexture = null;
        if (spec.UseMask && !TextureCache.TryLoadPath(layers.ForegroundMask, out maskTexture))
            return null;
        if (Admit(spec, backgroundTexture!, foregroundTexture!, maskTexture, out var validationKey) is not { } textures)
            return null;
        if (ValidationCache.TryGetValue(validationKey, out bool valid)) return valid ? textures : null;
        string? fileKey = FileValidationKey(spec, backgroundPath, foregroundPath, spec.UseMask ? maskPath : null);
        if (fileKey is not null && FileValidationCache.TryGetValue(fileKey, out valid))
        {
            ValidationCache.Set(validationKey, valid);
            return valid ? textures : null;
        }
        try
        {
            using Image background = backgroundTexture!.GetImage();
            using Image foreground = foregroundTexture!.GetImage();
            using Image? mask = maskTexture?.GetImage();
            ValidateImages(background, foreground, mask, spec);
            valid = true;
        }
        catch (Exception ex)
        {
            WarnOnce(key, $"Could not load layered card {layers.Background}: {ex.Message}");
            valid = false;
        }
        ValidationCache.Set(validationKey, valid);
        if (fileKey is not null) FileValidationCache.Set(fileKey, valid);
        return valid ? textures : null;
    }

    // Cached textures with a known verdict return at once. Otherwise a worker
    // decodes and validates a new file set, or the texture cache reloads an
    // evicted one, and the card is refreshed when the results arrive.
    private static LayerTextures? LoadInBackground(LayerSpec spec, string key, string backgroundPath,
        string foregroundPath, string maskPath, out bool pending)
    {
        pending = false;
        Texture2D? background = null, foreground = null, mask = null;
        if (TextureCache.TryGetCached(backgroundPath, out background) && TextureCache.TryGetCached(foregroundPath, out foreground)
            && (!spec.UseMask || TextureCache.TryGetCached(maskPath, out mask)))
        {
            if (background is null || foreground is null || (spec.UseMask && mask is null)) return null;
            if (Admit(spec, background, foreground, mask, out var cachedKey) is not { } cached) return null;
            if (ValidationCache.TryGetValue(cachedKey, out bool known)) return known ? cached : null;
        }
        string? fileKey = FileValidationKey(spec, backgroundPath, foregroundPath, spec.UseMask ? maskPath : null);
        if (fileKey is null)
        {
            WarnOnce(key, $"Layered card file missing: {key.Replace('\n', ' ')}");
            return null;
        }
        if (!FileValidationCache.TryGetValue(fileKey, out bool valid))
        {
            string[] paths = spec.UseMask
                ? new[] { backgroundPath, foregroundPath, maskPath }
                : new[] { backgroundPath, foregroundPath };
            string warningKey = key;
            pending = TextureCache.RequestSet("layers\n" + fileKey, paths, images =>
                {
                    ValidateImages(images[0]!, images[1]!, spec.UseMask ? images[2] : null, spec);
                    return null;
                },
                error =>
                {
                    FileValidationCache.Set(fileKey, error is null);
                    if (error is not null) WarnOnce(warningKey, $"Could not load layered card {backgroundPath}: {error}");
                });
            return null;
        }
        if (!valid) return null;
        TextureState[] states =
        {
            TextureCache.RequestResolved(backgroundPath, out background),
            TextureCache.RequestResolved(foregroundPath, out foreground),
            spec.UseMask ? TextureCache.RequestResolved(maskPath, out mask) : TextureState.Ready,
        };
        if (states.Contains(TextureState.Failed)) return null;
        if (states.Contains(TextureState.Pending))
        {
            pending = true;
            return null;
        }
        if (Admit(spec, background!, foreground!, mask, out var validationKey) is not { } textures) return null;
        ValidationCache.Set(validationKey, true);
        return textures;
    }

    // Throws for a rejected layer set. Safe on a worker thread: it only reads
    // the given images and converts private copies.
    private static void ValidateImages(Image background, Image foreground, Image? mask, LayerSpec spec)
    {
        // GPU variants retain their compressed upload buffer. Validation reads
        // decompressed private copies, with the same alpha/coverage gates.
        if (background.IsCompressed() || foreground.IsCompressed())
        {
            using Image plainBackground = BaseLevelRgba8(background);
            using Image plainForeground = BaseLevelRgba8(foreground);
            ValidateImages(plainBackground, plainForeground, mask, spec);
            return;
        }
        if (spec.UseMask && mask is null)
            throw new InvalidDataException("A required foreground mask could not be decoded.");
        var backgroundSize = new LayerCanvas(background.GetWidth(), background.GetHeight());
        var foregroundSize = new LayerCanvas(foreground.GetWidth(), foreground.GetHeight());
        var maskSize = mask is null ? default : new LayerCanvas(mask.GetWidth(), mask.GetHeight());
        if (!AdmitsForeground(spec, foregroundSize.Width, foregroundSize.Height))
            throw new InvalidDataException("Generated or unmasked foregrounds must be 2:3 unless they are the card's own source art.");
        if (spec.Registration is { } declared && (declared.Source != foregroundSize || declared.Background != backgroundSize
            || declared.Mask != maskSize))
            throw new InvalidDataException("Normalized source UV registration dimensions differ from actual textures.");
        if (background.IsEmpty() || foreground.IsEmpty()
            || (spec.Registration is null && background.GetSize() != foreground.GetSize())
            || !LayerPolicy.IsValidCanvas(background.GetWidth(), background.GetHeight(), spec.UseMask)
            || !LayerPolicy.IsValidCanvas(foreground.GetWidth(), foreground.GetHeight(), spec.UseMask)
            || background.DetectAlpha() != Image.AlphaMode.None
            || (!spec.UseMask && foreground.DetectAlpha() == Image.AlphaMode.None) || foreground.IsInvisible())
            throw new InvalidDataException("Layers require matching bounded portrait canvases, an opaque background, and visible coverage; unmasked RGBA layers require 2:3.");
        if (mask is null) return;
        if (mask.IsEmpty() || !LayerPolicy.IsValidCanvas(mask.GetWidth(), mask.GetHeight(), true)
            || (spec.Registration is null && mask.GetSize() != foreground.GetSize()))
            throw new InvalidDataException("Foreground mask must match the source canvas.");
        // Conversion is in memory only, on copies; the source PNG is never
        // rewritten. Validation reads only the base level.
        using Image maskData = BaseLevelRgba8(mask);
        using Image foregroundData = BaseLevelRgba8(foreground);
        if (!LayerPolicy.IsValidForegroundMask(maskData.GetData(), foregroundData.GetData(), maskData.GetWidth(),
            maskData.GetHeight(), foregroundData.GetWidth(), foregroundData.GetHeight(), spec.Channel))
            throw new InvalidDataException("Foreground mask needs visible and hidden regions in its selected red/alpha channel; red masks must be opaque near-grayscale.");
    }

    private static Image BaseLevelRgba8(Image source)
    {
        var copy = new Image();
        copy.CopyFrom(source);
        if (copy.IsCompressed() && copy.Decompress() != Error.Ok)
            throw new InvalidDataException("Compressed layer could not be decoded for validation.");
        copy.ClearMipmaps();
        copy.Convert(Image.Format.Rgba8);
        return copy;
    }

    // Survives texture eviction: an unchanged file set needs no second decode
    // and pixel scan. Sizes follow from the bytes that path, length and write
    // time identify.
    private static string? FileValidationKey(LayerSpec spec, string background, string foreground, string? mask)
    {
        var builder = new System.Text.StringBuilder();
        foreach (string? path in new[] { background, foreground, mask })
        {
            if (path is null) { builder.Append("-|"); continue; }
            string? textureKey = GpuTextureVariants.FileValidationKey(path);
            if (textureKey is null) return null;
            builder.Append(textureKey).Append('|');
        }
        return builder.Append(spec).ToString();
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
        // Pooled cards are refreshed before they enter a SceneTree. Godot's
        // local mouse query requires a viewport, including when motion would
        // otherwise be discarded. Clear any previous pose until both nodes
        // are attached again; do not cache attachment or transformed positions.
        Vector2 pointer = Vector2.Zero;
        Vector2 size = portrait.Size;
        float depth = material.GetShaderParameter("depth").AsSingle();
        bool attached = portrait.IsInsideTree() && overlay.IsInsideTree();
        if (!FullArtModConfig.ReducedMotion && float.IsFinite(depth) && depth > 0f
            && float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0f && size.Y > 0f
            && portrait.IsInsideTree() && overlay.IsInsideTree() && overlay.GetParent() == portrait
            && portrait.GetViewport() is { } viewport && GodotObject.IsInstanceValid(viewport)
            && overlay.GetViewport() == viewport)
        {
            Vector2 position = portrait.GetLocalMousePosition();
            var normalized = LayerPolicy.Pointer(position.X / size.X * 2f - 1f,
                position.Y / size.Y * 2f - 1f, false);
            pointer = new Vector2(normalized.X, normalized.Y);
        }
        material.SetShaderParameter("pointer", pointer);
        // Ease toward the target so layers glide in and settle back; detached,
        // pooled or reduced-motion cards snap so no stale pose survives.
        Vector2 previous = material.GetShaderParameter("view_pointer").AsVector2();
        float blend = !attached || FullArtModConfig.ReducedMotion || !float.IsFinite(previous.X) || !float.IsFinite(previous.Y)
            ? 1f : 1f - MathF.Exp(-PointerResponse * (float)overlay.GetProcessDeltaTime());
        material.SetShaderParameter("view_pointer", previous.Lerp(pointer, blend));
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
