using System.Globalization;

namespace FullArtSpire;

// Card paths stay relative so layer validation and caches keep working on them.
// Each loaded pack gets a root index, and its card paths are rewritten from
// "art/x.png" to "@<index>/art/x.png". Resolution maps the prefix back to that
// pack's root and confines the rest inside it. Every path read from a manifest
// is prefixed, so a manifest that writes "@2/..." itself only reaches a folder
// named "@2" inside its own root.
internal static class PackPaths
{
    internal const char RootMarker = '@';

    internal static string Prefix(int rootIndex) => RootMarker + rootIndex.ToString(CultureInfo.InvariantCulture) + "/";

    // Unprefixed paths (shaders, the GPU index) resolve under roots[0], the mod folder.
    internal static bool TryResolve(IReadOnlyList<string> roots, string? path, out string fullPath)
    {
        fullPath = "";
        if (roots.Count == 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string root = roots[0];
        string relative = path;
        if (path[0] == RootMarker)
        {
            int separator = path.IndexOf('/');
            if (separator < 2
                || !int.TryParse(path.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index >= roots.Count)
            {
                return false;
            }

            root = roots[index];
            relative = path[(separator + 1)..];
        }

        return TryResolveUnder(root, relative, out fullPath);
    }

    internal static bool TryResolveUnder(string root, string? relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || string.IsNullOrEmpty(root))
        {
            return false;
        }

        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string rootWithSeparator = rootFull + Path.DirectorySeparatorChar;
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        fullPath = Path.GetFullPath(Path.Combine(rootFull, normalized));
        return fullPath.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    internal static FullArtCardEntry Rebase(FullArtCardEntry entry, string prefix)
    {
        return entry with
        {
            Art = Rebase(entry.Art, prefix),
            Layers = entry.Layers is null ? null : entry.Layers with
            {
                Background = Rebase(entry.Layers.Background, prefix),
                Foreground = Rebase(entry.Layers.Foreground, prefix),
                ForegroundMask = Rebase(entry.Layers.ForegroundMask, prefix),
                FinishMask = Rebase(entry.Layers.FinishMask, prefix),
            },
            Effects = entry.Effects is null ? null : entry.Effects with
            {
                Mask = Rebase(entry.Effects.Mask, prefix),
                EffectMask = Rebase(entry.Effects.EffectMask, prefix),
                ProtectionMask = Rebase(entry.Effects.ProtectionMask, prefix),
                PermissionMask = Rebase(entry.Effects.PermissionMask, prefix),
            },
        };
    }

    // Empty means "no file" throughout the renderer, so it stays empty.
    private static string Rebase(string? path, string prefix)
        => string.IsNullOrWhiteSpace(path) ? path ?? "" : prefix + path.TrimStart();
}
