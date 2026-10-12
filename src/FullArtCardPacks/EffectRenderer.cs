using Godot;

namespace FullArtCardPacks;

internal static class EffectRenderer
{
    private static Shader? _effectShader;
    private static readonly LruCache<string, bool> EmittedWarnings = new(128, StringComparer.OrdinalIgnoreCase);

    internal static void InvalidateResources()
    {
        _effectShader = null;
        EmittedWarnings.Clear();
    }

    internal static void Initialize()
    {
        if (!FullArtPackRegistry.TryResolveModPath("shaders/CardEffectShader.gdshader", out string shaderPath)
            || !File.Exists(shaderPath))
        {
            GD.PushWarning($"[FullArtCardPacks] Effect shader not found: {shaderPath}");
            return;
        }

        _effectShader = TryLoadResource(
            "effect shader",
            shaderPath,
            () => new Shader { Code = File.ReadAllText(shaderPath) });
        if (_effectShader is not null)
        {
            GD.Print($"[FullArtCardPacks] Loaded effect shader: {shaderPath}");
        }
    }

    internal static ShaderMaterial? CreateOrUpdateEffectMaterial(
        ShaderMaterial? existingMaterial,
        EffectDecision decision,
        EffectContext context,
        TextureRect portrait)
    {
        try
        {
            if (_effectShader is null)
            {
                Initialize();
            }
            Texture2D? permissionMask = LoadMask(decision.PermissionMask);
            if (_effectShader is null || permissionMask is null)
            {
                return null;
            }

            float motionScale = context.MotionScale;
            if (motionScale < 0f)
            {
                Transform2D globalTransform = portrait.GetGlobalTransformWithCanvas();
                float inferredScale = MathF.Max(globalTransform.X.Length(), globalTransform.Y.Length());
                float focus = Math.Clamp((inferredScale - 0.72f) / 0.24f, 0f, 1f);
                float gridMotion = Math.Clamp(FullArtModConfig.GridMotion, 0f, 0.25f);
                float focusedMotion = Math.Clamp(FullArtModConfig.FocusedMotion, 0f, 1f);
                motionScale = gridMotion + (focusedMotion - gridMotion) * focus;
            }

            ShaderMaterial material = existingMaterial ?? new ShaderMaterial();
            material.Shader = _effectShader;
            material.SetShaderParameter("permission_mask_texture", permissionMask);
            material.SetShaderParameter("surface_type", EffectSurfaceParser.ShaderId(decision.Surface));
            material.SetShaderParameter("intensity", decision.Intensity * context.IntensityScale);
            material.SetShaderParameter("phase", decision.Phase);
            material.SetShaderParameter("motion_scale", FullArtModConfig.ReducedMotion ? 0f : motionScale);
            // Pooled portraits start neutral. The tree callback supplies live local tilt.
            material.SetShaderParameter("view_tilt", Vector2.Zero);
            return material;
        }
        catch (Exception ex)
        {
            WarnOnce(
                $"effect-material:{decision.PermissionMask}",
                $"Could not create effect overlay material for {decision.PermissionMask}: {ex.Message}");
            return null;
        }
    }

    private static Texture2D? LoadMask(string maskPath)
    {
        // The sampler's shader hint determines color/data interpretation.
        // Sharing ImageTexture objects does not add a source_color hint.
        return TextureCache.Request(maskPath, out Texture2D? texture) == TextureState.Ready ? texture : null;
    }

    internal static T? TryLoadResource<T>(
        string resourceKind,
        string path,
        Func<T> loader,
        Action<string>? warningSink = null)
        where T : class
    {
        try
        {
            return loader();
        }
        catch (Exception ex)
        {
            WarnOnce(
                $"{resourceKind}:{path}",
                $"Could not load {resourceKind} {path}: {ex.Message}",
                warningSink);
            return null;
        }
    }

    private static void WarnOnce(string key, string message, Action<string>? warningSink = null)
    {
        lock (EmittedWarnings)
        {
            if (EmittedWarnings.TryGetValue(key, out _))
            {
                return;
            }
            EmittedWarnings.Set(key, true);
        }
        if (warningSink is not null)
        {
            warningSink(message);
        }
        else
        {
            GD.PushWarning($"[FullArtCardPacks] {message}");
        }
    }
}
