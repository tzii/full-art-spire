using Godot;

namespace FullArtCardPacks;

internal static class TextureCache
{
    private const string ModId = "FullArtCardPacks";

    // Image count, not card count. Includes negative entries. Native controls
    // may retain live textures outside this bounded set after LRU eviction.
    internal const int Capacity = 48;
    private static readonly LruCache<string, Texture2D?> Cache = new(Capacity, StringComparer.OrdinalIgnoreCase);

    internal static void Invalidate() => Cache.Clear();

    internal static bool TryGetTexture(FullArtCardEntry entry, out Texture2D? texture)
        => TryLoadPath(entry.Art, out texture);

    internal static bool TryLoadPath(string relativePath, out Texture2D? texture)
    {
        texture = null;
        if (!FullArtPackRegistry.TryResolveModPath(relativePath, out string artPath))
        {
            GD.PushWarning($"[{ModId}] Invalid texture path: {relativePath}");
            return false;
        }

        if (Cache.TryGetValue(artPath, out texture))
        {
            return texture is not null;
        }

        if (!File.Exists(artPath))
        {
            GD.PushWarning($"[{ModId}] Missing texture file: {artPath}");
            Cache.Set(artPath, null);
            return false;
        }

        try
        {
            using Image? image = Image.LoadFromFile(artPath);
            if (image is null || image.IsEmpty())
                throw new InvalidDataException("Texture could not be decoded.");
            texture = ImageTexture.CreateFromImage(image);
            Cache.Set(artPath, texture);
            GD.Print($"[{ModId}] Loaded texture: {relativePath}");
            return texture is not null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Failed to load {artPath}: {ex.Message}");
            Cache.Set(artPath, null);
            return false;
        }
    }
}
