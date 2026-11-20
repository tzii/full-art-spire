using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace FullArtSpire;

// Immutable lookup snapshots are published on the main thread and read by workers.
// Masks never appear in this map; their exact coverage bytes remain uncompressed.
internal static class GpuTextureVariants
{
    private sealed record Binding(string Path, string Sha256, string SourceSha256, int Width, int Height,
        Image.Format Format);
    private static IReadOnlyDictionary<string, Binding> _bindings = new Dictionary<string, Binding>();
    private static bool _s3tc, _bptc;

    // Index format names map to the Godot formats the DDS loader produces.
    private static readonly Dictionary<string, Image.Format> Formats = new(StringComparer.Ordinal)
    {
        ["BC1_UNORM"] = Image.Format.Dxt1,
        ["BC3_UNORM"] = Image.Format.Dxt5,
        ["BC7_UNORM"] = Image.Format.BptcRgba,
    };

    // Each pack root may carry its own gpu-textures.audit; its paths resolve inside
    // that root. A bad index disables only its own root's variants.
    internal static void Load(IReadOnlyList<string> roots)
    {
        var bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        _s3tc = RenderingServer.HasOsFeature("s3tc");
        _bptc = RenderingServer.HasOsFeature("bptc");
        foreach (string root in roots)
        {
            if (!PackPaths.TryResolveUnder(root, "gpu-textures.audit", out string index) || !File.Exists(index))
                continue;
            try
            {
                foreach (var (source, binding) in ReadIndex(root, index))
                    bindings[source] = binding;
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[FullArtSpire] GPU variants disabled for {root}: {ex.Message}");
            }
        }
        _bindings = bindings;
    }

    private static List<(string Source, Binding Binding)> ReadIndex(string root, string index)
    {
        var rows = new List<(string, Binding)>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(File.ReadAllText(index));
        if (document.RootElement.GetProperty("schema_version").GetInt32() is not (1 or 2))
            throw new InvalidDataException("Unsupported GPU texture index.");
        foreach (var row in document.RootElement.GetProperty("textures").EnumerateArray())
        {
            if (!Formats.TryGetValue(row.GetProperty("format").GetString() ?? "", out Image.Format format)) continue;
            if (!PackPaths.TryResolveUnder(root, row.GetProperty("source").GetString(), out string source)
                || !PackPaths.TryResolveUnder(root, row.GetProperty("path").GetString(), out string path))
                throw new InvalidDataException("GPU texture path escapes its pack.");
            // Admit only art and layer color textures, never a permission/cutout mask.
            string relative = Path.GetRelativePath(root, source).Replace('\\', '/');
            if (!(relative.StartsWith("art/", StringComparison.Ordinal)
                || relative.StartsWith("layers/", StringComparison.Ordinal) && relative.EndsWith("/background.png", StringComparison.Ordinal)))
                throw new InvalidDataException("GPU variant is not an opaque art texture.");
            string sha = row.GetProperty("sha256").GetString()!;
            string sourceSha = row.GetProperty("source_sha256").GetString()!;
            if (sha.Length != 64 || sourceSha.Length != 64
                || !sha.All(Uri.IsHexDigit) || !sourceSha.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid GPU texture hash.");
            if (!sources.Add(source))
                throw new InvalidDataException("Duplicate GPU texture source.");
            rows.Add((source, new(path, sha, sourceSha,
                row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32(), format)));
        }
        return rows;
    }

    /// <summary>True when a compressed variant stands in for this source path.</summary>
    internal static bool Has(string source) => _bindings.ContainsKey(source);

    // Layer verdicts survive texture eviction. Identify both possible inputs:
    // DDS-only packages have no source PNG, while packages retaining the PNG
    // may decode either copy depending on settings, support and hash checks.
    internal static string? FileValidationKey(string source)
    {
        var original = new FileInfo(source);
        var builder = new System.Text.StringBuilder();
        builder.Append(source).Append('|');
        if (original.Exists)
            builder.Append(original.Length).Append('|').Append(original.LastWriteTimeUtc.Ticks);
        builder.Append('|');
        bool available = original.Exists;
        if (_bindings.TryGetValue(source, out var binding))
        {
            var variant = new FileInfo(binding.Path);
            builder.Append(binding.Path).Append('|').Append(binding.Sha256).Append('|')
                .Append(binding.SourceSha256).Append('|').Append(binding.Width).Append('|')
                .Append(binding.Height).Append('|').Append(binding.Format).Append('|');
            if (variant.Exists)
            {
                available = true;
                builder.Append(variant.Length).Append('|').Append(variant.LastWriteTimeUtc.Ticks);
            }
        }
        // This is only a file-set identity. TryLoad still checks DDS integrity,
        // dimensions and format; layer admission still checks the image pixels.
        return available ? builder.ToString() : null;
    }

    internal static Image? TryLoad(string source)
    {
        if (!_bindings.TryGetValue(source, out var binding)) return null;
        // Release packages ship only the compressed copy. When the original PNG
        // is present it stays the lossless path and the stale-variant check.
        bool original = File.Exists(source);
        bool native = binding.Format == Image.Format.BptcRgba ? _bptc : _s3tc;
        if (original && (!FullArtModConfig.GpuCompressedTextures || !native)) return null;
        Image? image = null;
        try
        {
            if (original)
            {
                using var input = File.OpenRead(source);
                if (!Convert.ToHexString(SHA256.HashData(input)).Equals(binding.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    return null;
            }
            byte[] bytes = File.ReadAllBytes(binding.Path);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(binding.Sha256, StringComparison.OrdinalIgnoreCase))
                return null;
            image = new Image();
            if (image.LoadDdsFromBuffer(bytes) != Error.Ok || image.IsEmpty()
                || image.GetFormat() != binding.Format || !image.HasMipmaps()
                || image.GetWidth() != binding.Width || image.GetHeight() != binding.Height)
                throw new InvalidDataException("Invalid compressed image.");
            // Without native support the renderer would decompress at upload;
            // doing it here keeps that cost on the background worker.
            if (!native && image.Decompress() != Error.Ok)
                throw new InvalidDataException("Compressed image could not be decompressed.");
            return image;
        }
        catch
        {
            image?.Dispose();
            return null; // Missing, corrupt or stale variants preserve the lossless path.
        }
    }
}
