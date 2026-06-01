using Godot;
using System.Collections.Generic;

namespace FullArtCardPacks;

internal static class EffectRenderer
{
    private static Shader? _effectShader;
    private static readonly Dictionary<string, Texture2D?> MaskCache = new();

    internal static void Initialize()
    {
        var shaderPath = FullArtPackRegistry.ModDirectory + "/shaders/CardEffectShader.gdshader";
        if (File.Exists(shaderPath))
        {
            var shaderCode = File.ReadAllText(shaderPath);
            _effectShader = new Shader();
            _effectShader.Code = shaderCode;
            GD.Print($"[FullArtCardPacks] Loaded effect shader from file: {shaderPath}");
        }
        else
        {
            GD.PushWarning($"[FullArtCardPacks] Effect shader not found: {shaderPath}");
        }
    }

    internal static ShaderMaterial? CreateEffectMaterial(FullArtCardEntry entry, int rarityType, float intensity)
    {
        if (_effectShader == null)
            return null;

        if (string.IsNullOrEmpty(entry.Effects?.Mask))
            return null;

        var maskTexture = LoadMask(entry.Effects.Mask);
        if (maskTexture == null)
            return null;

        var material = new ShaderMaterial();
        material.Shader = _effectShader;
        material.SetShaderParameter("mask_texture", maskTexture);
        material.SetShaderParameter("intensity", intensity);
        material.SetShaderParameter("rarity_type", rarityType);
        material.SetShaderParameter("time", 0.0f);

        return material;
    }

    internal static void UpdateTime(ShaderMaterial material, float time)
    {
        material.SetShaderParameter("time", time);
    }

    private static Texture2D? LoadMask(string maskPath)
    {
        if (MaskCache.TryGetValue(maskPath, out var cached))
            return cached;

        var fullPath = FullArtPackRegistry.ModDirectory + "/" + maskPath;
        if (!File.Exists(fullPath))
        {
            GD.PushWarning($"[FullArtCardPacks] Mask not found: {fullPath}");
            MaskCache[maskPath] = null;
            return null;
        }

        var image = Image.LoadFromFile(fullPath);
        var texture = ImageTexture.CreateFromImage(image);
        MaskCache[maskPath] = texture;
        return texture;
    }
}
