using System.Text.Json;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Debug;

namespace FullArtSpire;

// Where a pack came from. Also the tie-break when two packs claim the same card
// at equal priority: a later source wins.
internal enum PackSource
{
    Legacy,
    Bundled,
    Mod,
    Player,
}

// A mod that is loaded, or still to load, in this session.
internal sealed record InstalledMod(string Id, string? Version, string Path);

internal sealed record PackDiscoveryInput(
    string ModDirectory,
    string? PlayerPacksDirectory,
    IReadOnlyList<InstalledMod> OtherMods,
    SemanticVersion? GameVersion);

internal sealed record DiscoveredPack
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public required PackSource Source { get; init; }
    public string? SourceModId { get; init; }

    // Every card path of this pack resolves inside Root.
    public required string Root { get; init; }
    public required string ManifestPath { get; init; }
    public int Priority { get; init; }
    public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();
    public string Credits { get; init; } = "";
    public IReadOnlyList<PackRequirement> Requires { get; init; } = Array.Empty<PackRequirement>();
    public string? MinGameVersion { get; init; }
    public IReadOnlyList<FullArtCardEntry> Cards { get; init; } = Array.Empty<FullArtCardEntry>();

    // Discovery order, the final tie-break.
    public int Order { get; init; }

    // Set when the pack is found but not used this session.
    public string? SkipReason { get; init; }
}

internal sealed class PackDiscoveryResult
{
    public List<DiscoveredPack> Packs { get; } = new();
    public List<string> Notes { get; } = new();
    public List<string> Warnings { get; } = new();
}

// Finds packs in the four v2 locations (pack-format.md, Discovery) without
// touching Godot, so it runs in unit tests against temporary folders.
internal static partial class PackDiscovery
{
    internal const string ManifestFileName = "pack.json";
    internal const string FormatName = "full_art_pack";
    internal const int FormatVersion = 2;
    internal const string ModPackFolder = "full_art";
    internal const string LegacyIdNamespace = "megacrit.";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex("^[a-z0-9_]+(\\.[a-z0-9_]+)+$")]
    private static partial Regex PackIdPattern();

    internal static bool IsValidPackId(string? id) => id is not null && id.Length <= 128 && PackIdPattern().IsMatch(id);

    internal static bool IsValidPoolName(string pool)
    {
        return !string.IsNullOrWhiteSpace(pool)
            && pool.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_');
    }

    internal static string LegacyPackId(string pool) => LegacyIdNamespace + pool;

    internal static PackDiscoveryResult Discover(PackDiscoveryInput input)
    {
        var result = new PackDiscoveryResult();
        string bundled = Path.Combine(input.ModDirectory, "packs");

        foreach (string file in SortedFiles(bundled, "*.pack"))
        {
            Add(result, Guarded(result, file, () => ReadLegacy(file, input.ModDirectory, result)));
        }

        foreach (string folder in SortedDirectories(bundled))
        {
            Add(result, Guarded(result, folder, () => ReadPackFolder(folder, PackSource.Bundled, null, result)));
        }

        foreach (InstalledMod mod in input.OtherMods.OrderBy(mod => mod.Id, StringComparer.Ordinal))
        {
            foreach (string folder in SortedDirectories(Path.Combine(mod.Path, ModPackFolder)))
            {
                Add(result, Guarded(result, folder, () => ReadPackFolder(folder, PackSource.Mod, mod.Id, result)));
            }
        }

        if (!string.IsNullOrEmpty(input.PlayerPacksDirectory))
        {
            foreach (string folder in SortedDirectories(input.PlayerPacksDirectory))
            {
                Add(result, Guarded(result, folder, () => ReadPackFolder(folder, PackSource.Player, null, result)));
            }
        }

        CheckRequirements(result, input);
        SkipDuplicateIds(result);
        return result;
    }

    // Packs in the order their cards are applied: a later pack overrides an earlier
    // one for the same card. Highest priority wins, then the later source, then the
    // later discovery.
    internal static IEnumerable<DiscoveredPack> ApplicationOrder(IEnumerable<DiscoveredPack> packs)
    {
        return packs
            .OrderBy(pack => pack.Priority)
            .ThenBy(pack => pack.Source)
            .ThenBy(pack => pack.Order);
    }

    internal static string Describe(DiscoveredPack pack) => pack.Source switch
    {
        PackSource.Mod => $"{pack.Id} (from mod {pack.SourceModId})",
        PackSource.Player => $"{pack.Id} (player pack)",
        PackSource.Legacy => $"{pack.Id} (v1 {Path.GetFileName(pack.ManifestPath)})",
        _ => pack.Id,
    };

    // One pack, however malformed, must never stop the others from loading.
    private static DiscoveredPack? Guarded(PackDiscoveryResult result, string path, Func<DiscoveredPack?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Skipping {path}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void Add(PackDiscoveryResult result, DiscoveredPack? pack)
    {
        if (pack is not null)
        {
            result.Packs.Add(pack with { Order = result.Packs.Count });
        }
    }

    private static DiscoveredPack? ReadLegacy(string file, string modDirectory, PackDiscoveryResult result)
    {
        string pool = Path.GetFileNameWithoutExtension(file);
        if (!IsValidPoolName(pool))
        {
            result.Warnings.Add($"Skipping v1 pack with an invalid pool name: {file}");
            return null;
        }

        FullArtPackManifest? manifest = Parse<FullArtPackManifest>(file, result);
        if (manifest?.Cards is null)
        {
            if (manifest is not null) result.Warnings.Add($"Skipping v1 pack without cards: {file}");
            return null;
        }

        return new DiscoveredPack
        {
            Id = LegacyPackId(pool),
            Name = char.ToUpperInvariant(pool[0]) + pool[1..].Replace('_', ' '),
            Source = PackSource.Legacy,
            Root = modDirectory,
            ManifestPath = file,
            Cards = AcceptedCards(manifest.Cards, modDirectory, file, result),
        };
    }

    private static DiscoveredPack? ReadPackFolder(string folder, PackSource source, string? sourceModId, PackDiscoveryResult result)
    {
        string file = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(file))
        {
            result.Notes.Add($"No {ManifestFileName} in {folder}; not a pack.");
            return null;
        }

        FullArtPackManifestV2? manifest = Parse<FullArtPackManifestV2>(file, result);
        if (manifest is null)
        {
            return null;
        }

        if (!string.Equals(manifest.Format, FormatName, StringComparison.Ordinal))
        {
            result.Warnings.Add($"Skipping {file}: format must be \"{FormatName}\".");
            return null;
        }

        if (manifest.FormatVersion != FormatVersion)
        {
            result.Warnings.Add($"Skipping {file}: format_version {manifest.FormatVersion} is not supported (expected {FormatVersion}).");
            return null;
        }

        if (!IsValidPackId(manifest.Id))
        {
            result.Warnings.Add($"Skipping {file}: id \"{manifest.Id}\" must look like namespace.name (a-z, 0-9, _).");
            return null;
        }

        if (manifest.Cards is null)
        {
            result.Warnings.Add($"Skipping {file}: no cards.");
            return null;
        }

        return new DiscoveredPack
        {
            Id = manifest.Id,
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name.Trim(),
            Version = manifest.Version?.Trim() ?? "",
            Source = source,
            SourceModId = sourceModId,
            Root = Path.GetFullPath(folder),
            ManifestPath = file,
            Priority = manifest.Priority,
            Authors = manifest.Authors?.Where(author => !string.IsNullOrWhiteSpace(author)).ToArray() ?? Array.Empty<string>(),
            Credits = manifest.Credits ?? "",
            Requires = manifest.Requires?.Where(requirement => requirement is not null).Select(requirement => requirement!).ToArray()
                ?? Array.Empty<PackRequirement>(),
            MinGameVersion = manifest.MinGameVersion,
            Cards = AcceptedCards(manifest.Cards, folder, file, result),
        };
    }

    private static IReadOnlyList<FullArtCardEntry> AcceptedCards(
        IEnumerable<FullArtCardEntry?> cards, string root, string file, PackDiscoveryResult result)
    {
        var accepted = new List<FullArtCardEntry>();
        foreach (FullArtCardEntry? card in cards)
        {
            if (card is null)
            {
                result.Warnings.Add($"Skipping a null card entry in {file}");
            }
            else if (string.IsNullOrWhiteSpace(card.RuntimeModel))
            {
                result.Warnings.Add($"Skipping {card.CanonicalAssetName} in {file}: missing runtime_model");
            }
            else if (!PackPaths.TryResolveUnder(root, card.Art, out _))
            {
                result.Warnings.Add($"Skipping {card.RuntimeModel} in {file}: art path must stay inside the pack");
            }
            else
            {
                accepted.Add(card);
            }
        }

        return accepted;
    }

    private static void CheckRequirements(PackDiscoveryResult result, PackDiscoveryInput input)
    {
        var mods = new Dictionary<string, InstalledMod>(StringComparer.Ordinal);
        foreach (InstalledMod mod in input.OtherMods)
        {
            mods.TryAdd(mod.Id, mod);
        }

        for (int i = 0; i < result.Packs.Count; i++)
        {
            DiscoveredPack pack = result.Packs[i];
            string? reason = UnmetRequirement(pack, mods, input.GameVersion);
            if (reason is not null)
            {
                result.Packs[i] = pack with { SkipReason = reason };
                result.Notes.Add($"Not loading {Describe(pack)}: {reason}.");
            }
        }
    }

    private static string? UnmetRequirement(DiscoveredPack pack, IReadOnlyDictionary<string, InstalledMod> mods, SemanticVersion? game)
    {
        if (!string.IsNullOrWhiteSpace(pack.MinGameVersion) && game is not null)
        {
            if (!SemanticVersion.TryFromString(pack.MinGameVersion.Trim(), out SemanticVersion? minimum) || minimum is null)
                return $"min_game_version \"{pack.MinGameVersion}\" is not a version";
            if (game.CompareTo(minimum) < 0)
                return $"needs game {minimum} or later (running {game})";
        }

        foreach (PackRequirement requirement in pack.Requires)
        {
            if (string.IsNullOrWhiteSpace(requirement.Mod))
                return "a requires entry has no mod id";
            if (!mods.TryGetValue(requirement.Mod, out InstalledMod? mod))
                return $"needs mod {requirement.Mod}, which is not loaded";
            if (string.IsNullOrWhiteSpace(requirement.MinVersion))
                continue;
            if (!SemanticVersion.TryFromString(requirement.MinVersion.Trim(), out SemanticVersion? minimum) || minimum is null)
                return $"min_version \"{requirement.MinVersion}\" for {requirement.Mod} is not a version";
            if (mod.Version is null || !SemanticVersion.TryFromString(mod.Version.Trim(), out SemanticVersion? installed) || installed is null)
                return $"needs {requirement.Mod} {minimum} or later, but its version \"{mod.Version}\" cannot be read";
            if (installed.CompareTo(minimum) < 0)
                return $"needs {requirement.Mod} {minimum} or later (installed {installed})";
        }

        return null;
    }

    // Two packs with one id are the same pack installed twice (or a v2 pack that
    // replaces its v1 file). Keep the one that would win a card conflict.
    private static void SkipDuplicateIds(PackDiscoveryResult result)
    {
        var winners = ApplicationOrder(result.Packs.Where(pack => pack.SkipReason is null))
            .GroupBy(pack => pack.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        for (int i = 0; i < result.Packs.Count; i++)
        {
            DiscoveredPack pack = result.Packs[i];
            if (pack.SkipReason is null && winners.TryGetValue(pack.Id, out DiscoveredPack? winner) && !ReferenceEquals(pack, winner))
            {
                string reason = $"another copy of {pack.Id} is used instead ({winner.ManifestPath})";
                result.Packs[i] = pack with { SkipReason = reason };
                result.Notes.Add($"Not loading {Describe(pack)}: {reason}.");
            }
        }
    }

    private static T? Parse<T>(string file, PackDiscoveryResult result) where T : class
    {
        try
        {
            T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(file), JsonOptions);
            if (value is null) result.Warnings.Add($"Skipping empty pack manifest: {file}");
            return value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add($"Skipping unreadable pack manifest {file}: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> SortedFiles(string directory, string pattern)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern).OrderBy(path => path, StringComparer.Ordinal)
            : Enumerable.Empty<string>();
    }

    private static IEnumerable<string> SortedDirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.Ordinal)
                : Enumerable.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<string>();
        }
    }
}
