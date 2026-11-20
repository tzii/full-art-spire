using System.Diagnostics;
using Godot;

namespace FullArtSpire;

internal enum TextureState { Ready, Failed, Pending }

internal static class TextureCache
{
    private const string ModId = "FullArtSpire";

    // Image count, not card count. Includes negative entries. Native controls
    // may retain live textures outside this bounded set after LRU eviction.
    // 96 images is about 32 layered cards, roughly 600 MB of mipmapped VRAM.
    internal const int Capacity = 96;
    private static readonly LruCache<string, Texture2D?> Cache = new(Capacity, StringComparer.OrdinalIgnoreCase);

    // PNG decoding and mipmap generation run on two background threads, which
    // leaves cores for the game's own loading. Only the GPU upload in
    // ImageTexture.CreateFromImage stays on the main thread, within a budget.
    private const int Workers = 2;
    private const double UploadBudgetMilliseconds = 4;
    private static readonly BackgroundLoader<string, DecodedImages> Loader = new(Workers, StringComparer.OrdinalIgnoreCase);
    // Paths that a pending layer-set job will deliver; single requests wait for it.
    private static readonly Dictionary<string, int> Claimed = new(StringComparer.OrdinalIgnoreCase);
    private static ulong _pumpTreeId;
    private static bool _gpuSetting = FullArtModConfig.GpuCompressedTextures;

    internal static void SettingsChanged()
    {
        if (_gpuSetting == FullArtModConfig.GpuCompressedTextures) return;
        _gpuSetting = FullArtModConfig.GpuCompressedTextures;
        Invalidate();
        LayeredCardRenderer.InvalidateResources();
    }

    /// <summary>Raised on the main thread after each frame that delivered
    /// background results, and on idle frames, so waiting cards can refresh.</summary>
    internal static event Action? Delivered;

    internal static bool BackgroundLoading => FullArtModConfig.BackgroundTextureLoading;
    internal static int PendingCount => Loader.PendingCount;
    // Main-thread cost of background results: texture uploads and verdicts.
    internal static double UploadMilliseconds { get; private set; }

    internal sealed class DecodedImages : IDisposable
    {
        internal readonly List<(string Path, Image? Image, string? Error)> Images = new();
        internal string? ValidationError;
        public void Dispose()
        {
            foreach (var item in Images) item.Image?.Dispose();
        }
    }

    internal static void Invalidate()
    {
        Cache.Clear();
        Loader.Invalidate();
        Claimed.Clear();
    }

    internal static bool TryGetTexture(FullArtCardEntry entry, out Texture2D? texture)
        => TryLoadPath(entry.Art, out texture);

    /// <summary>Loads synchronously on the calling (main) thread.</summary>
    internal static bool TryLoadPath(string relativePath, out Texture2D? texture)
    {
        texture = null;
        if (!FullArtPackRegistry.TryResolveModPath(relativePath, out string artPath))
        {
            GD.PushWarning($"[{ModId}] Invalid texture path: {relativePath}");
            return false;
        }
        return LoadResolved(artPath, out texture);
    }

    /// <summary>Returns a cached texture or queues a background decode. A
    /// pending path completes in a later frame and raises <see cref="Delivered"/>.</summary>
    internal static TextureState Request(string relativePath, out Texture2D? texture)
    {
        texture = null;
        if (!FullArtPackRegistry.TryResolveModPath(relativePath, out string artPath))
        {
            GD.PushWarning($"[{ModId}] Invalid texture path: {relativePath}");
            return TextureState.Failed;
        }
        return RequestResolved(artPath, out texture);
    }

    internal static TextureState RequestResolved(string artPath, out Texture2D? texture)
    {
        if (Cache.TryGetValue(artPath, out texture))
            return texture is null ? TextureState.Failed : TextureState.Ready;
        if (!BackgroundLoading || !EnsurePump())
            return LoadResolved(artPath, out texture) ? TextureState.Ready : TextureState.Failed;
        if (!Claimed.ContainsKey(artPath))
        {
            string[] paths = { artPath };
            Loader.Request(artPath, () => Decode(paths, null), (images, error) => Deliver(paths, images, error, null));
        }
        return TextureState.Pending;
    }

    /// <summary>Cache lookup only. True with a null texture for a cached failure.</summary>
    internal static bool TryGetCached(string artPath, out Texture2D? texture)
        => Cache.TryGetValue(artPath, out texture);

    /// <summary>Decodes all paths once on a worker, runs <paramref name="validate"/>
    /// on their base-level images there, then delivers uncached textures and
    /// the verdict (null for valid) together on the main thread.</summary>
    /// <returns>False when background loading is unavailable.</returns>
    internal static bool RequestSet(string key, string[] paths, Func<Image?[], string?> validate, Action<string?> verdict)
    {
        if (!BackgroundLoading || !EnsurePump()) return false;
        string[] claimed = paths.Where(path => !Cache.ContainsKey(path)).ToArray();
        if (Loader.Request(key, () => Decode(paths, validate), (images, error) =>
            {
                Release(claimed);
                Deliver(paths, images, error, verdict);
            }))
        {
            foreach (string path in claimed) Claimed[path] = Claimed.GetValueOrDefault(path) + 1;
        }
        return true;
    }

    private static void Release(string[] paths)
    {
        foreach (string path in paths)
        {
            if (!Claimed.TryGetValue(path, out int count)) continue;
            if (count <= 1) Claimed.Remove(path);
            else Claimed[path] = count - 1;
        }
    }

    private static bool LoadResolved(string artPath, out Texture2D? texture)
    {
        if (Cache.TryGetValue(artPath, out texture))
            return texture is not null;
        var (image, error) = DecodeOne(artPath);
        using (image)
        {
            Install(artPath, image, error);
        }
        Cache.TryGetValue(artPath, out texture);
        return texture is not null;
    }

    // Worker thread: Image decoding is safe off the main thread; no node,
    // texture or cache state is touched here.
    private static DecodedImages Decode(string[] paths, Func<Image?[], string?>? validate)
    {
        var decoded = new DecodedImages();
        foreach (string path in paths)
        {
            var (image, error) = DecodeOne(path, mipmaps: validate is null);
            decoded.Images.Add((path, image, error));
        }
        if (validate is null) return decoded;
        if (decoded.Images.FirstOrDefault(item => item.Error is not null).Error is { } failed)
            decoded.ValidationError = failed;
        else
        {
            try { decoded.ValidationError = validate(decoded.Images.Select(item => item.Image).ToArray()); }
            catch (Exception ex) { decoded.ValidationError = ex.Message; }
        }
        // Validation reads the base level; mipmaps are added afterwards.
        foreach (var item in decoded.Images)
            if (item.Image is { } image) AddMipmaps(image);
        return decoded;
    }

    private static (Image? Image, string? Error) DecodeOne(string path, bool mipmaps = true)
    {
        Image? image = null;
        try
        {
            // Release packages may carry only the compressed variant of a PNG path.
            image = GpuTextureVariants.TryLoad(path);
            if (image is null && !File.Exists(path)) return (null, $"Missing texture file: {path}");
            image ??= Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
                throw new InvalidDataException("Texture could not be decoded.");
            if (mipmaps) AddMipmaps(image);
            return (image, null);
        }
        catch (Exception ex)
        {
            image?.Dispose();
            return (null, $"Failed to load {path}: {ex.Message}");
        }
    }

    // Cards render far below 1024 px in hands and grids; mipmaps keep them
    // from aliasing. Compressed images keep their own levels.
    private static void AddMipmaps(Image image)
    {
        if (!image.HasMipmaps() && !image.IsCompressed())
            image.GenerateMipmaps();
    }

    private static void Install(string artPath, Image? image, string? error)
    {
        // A synchronous load may have filled the entry first; keep that instance.
        if (Cache.ContainsKey(artPath)) return;
        if (image is null)
        {
            GD.PushWarning($"[{ModId}] {error}");
            Cache.Set(artPath, null);
            return;
        }
        try
        {
            Cache.Set(artPath, ImageTexture.CreateFromImage(image));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Failed to load {artPath}: {ex.Message}");
            Cache.Set(artPath, null);
        }
    }

    private static void Deliver(string[] paths, DecodedImages? images, Exception? error, Action<string?>? verdict)
    {
        using (images)
        {
            if (images is null)
            {
                foreach (string path in paths) Install(path, null, $"Failed to load {path}: {error?.Message}");
                verdict?.Invoke(error?.Message ?? "Layer images could not be decoded.");
                return;
            }
            foreach (var (path, image, message) in images.Images) Install(path, image, message);
            verdict?.Invoke(images.ValidationError);
        }
    }

    private static bool EnsurePump()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return false;
        if (tree.GetInstanceId() != _pumpTreeId)
        {
            tree.ProcessFrame += Pump;
            _pumpTreeId = tree.GetInstanceId();
        }
        return true;
    }

    private static void Pump()
    {
        var clock = Stopwatch.StartNew();
        int delivered = Loader.Drain(() => clock.Elapsed.TotalMilliseconds < UploadBudgetMilliseconds);
        if (delivered > 0) UploadMilliseconds += clock.Elapsed.TotalMilliseconds;
        // Idle frames also notify, so a card left waiting by Invalidate retries.
        if (delivered > 0 || Loader.PendingCount == 0) Delivered?.Invoke();
    }
}
