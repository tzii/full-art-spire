using Godot;

namespace FullArtCardPacks;

internal static class TextureCache
{
    private const string ModId = "FullArtCardPacks";

    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryGetTexture(FullArtCardEntry entry, out Texture2D? texture)
    {
        texture = null;
        if (!FullArtPackRegistry.TryResolveModPath(entry.Art, out string artPath))
        {
            GD.PushWarning($"[{ModId}] Invalid art path for {entry.CanonicalAssetName}: {entry.Art}");
            return false;
        }

        if (Cache.TryGetValue(artPath, out texture))
        {
            return texture is not null;
        }

        if (!File.Exists(artPath))
        {
            GD.PushWarning($"[{ModId}] Missing art file for {entry.CanonicalAssetName}: {artPath}");
            Cache[artPath] = null;
            return false;
        }

        try
        {
            Image image = Image.LoadFromFile(artPath);
            texture = ImageTexture.CreateFromImage(image);
            Cache[artPath] = texture;
            GD.Print($"[{ModId}] Loaded full art: {entry.Art}");
            return texture is not null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Failed to load {artPath}: {ex.Message}");
            Cache[artPath] = null;
            return false;
        }
    }
}
