using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace FullArtCardPacks;

// Immutable lookup snapshots are published on the main thread and read by workers.
// Masks never appear in this map; their exact coverage bytes remain uncompressed.
internal static class GpuTextureVariants
{
    private sealed record Binding(string Path, string Sha256, string SourceSha256, int Width, int Height);
    private static IReadOnlyDictionary<string, Binding> _bindings = new Dictionary<string, Binding>();

    internal static void Load()
    {
        var bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        bool supported = RenderingServer.HasOsFeature("s3tc");
        if (supported && FullArtPackRegistry.TryResolveModPath("gpu-textures.audit", out string index) && File.Exists(index))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(index));
                if (document.RootElement.GetProperty("schema_version").GetInt32() != 1)
                    throw new InvalidDataException("Unsupported GPU texture index.");
                foreach (var row in document.RootElement.GetProperty("textures").EnumerateArray())
                {
                    if (row.GetProperty("format").GetString() != "BC3_UNORM") continue;
                    if (!FullArtPackRegistry.TryResolveModPath(row.GetProperty("source").GetString(), out string source)
                        || !FullArtPackRegistry.TryResolveModPath(row.GetProperty("path").GetString(), out string path))
                        throw new InvalidDataException("GPU texture path escapes the mod.");
                    // Admit only art and layer color textures, never a permission/cutout mask.
                    string relative = Path.GetRelativePath(FullArtPackRegistry.ModDirectory, source).Replace('\\', '/');
                    if (!(relative.StartsWith("art/", StringComparison.Ordinal)
                        || relative.StartsWith("layers/", StringComparison.Ordinal) && relative.EndsWith("/background.png", StringComparison.Ordinal)))
                        throw new InvalidDataException("GPU variant is not an opaque art texture.");
                    string sha = row.GetProperty("sha256").GetString()!;
                    string sourceSha = row.GetProperty("source_sha256").GetString()!;
                    if (sha.Length != 64 || sourceSha.Length != 64
                        || !sha.All(Uri.IsHexDigit) || !sourceSha.All(Uri.IsHexDigit))
                        throw new InvalidDataException("Invalid GPU texture hash.");
                    bindings.Add(source, new(path, sha, sourceSha,
                        row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32()));
                }
            }
            catch (Exception ex)
            {
                bindings.Clear();
                GD.PushWarning($"[FullArtCardPacks] GPU variants disabled: {ex.Message}");
            }
        }
        _bindings = bindings;
    }

    internal static Image? TryLoad(string source)
    {
        if (!FullArtModConfig.GpuCompressedTextures || !_bindings.TryGetValue(source, out var binding)) return null;
        Image? image = null;
        try
        {
            using var input = File.OpenRead(source);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(binding.SourceSha256, StringComparison.OrdinalIgnoreCase))
                return null;
            byte[] bytes = File.ReadAllBytes(binding.Path);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(binding.Sha256, StringComparison.OrdinalIgnoreCase))
                return null;
            image = new Image();
            if (image.LoadDdsFromBuffer(bytes) != Error.Ok || image.IsEmpty()
                || image.GetFormat() != Image.Format.Dxt5 || !image.HasMipmaps()
                || image.GetWidth() != binding.Width || image.GetHeight() != binding.Height)
                throw new InvalidDataException("Invalid compressed image.");
            return image;
        }
        catch
        {
            image?.Dispose();
            return null; // Missing, corrupt or stale variants preserve the lossless path.
        }
    }
}
