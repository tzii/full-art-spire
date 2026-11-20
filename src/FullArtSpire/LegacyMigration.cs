using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Modding;

namespace FullArtSpire;

// Which packs a player had switched off in a 0.3.x install, kept as a deny-list
// so packs that 0.3.x never shipped stay on.
internal sealed record LegacyPackSelection
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("disabled_packs")]
    public List<string> DisabledPacks { get; init; } = new();

    // Recorded for diagnostics only: a globally disabled 0.3.x install is not carried over.
    [JsonPropertyName("legacy_enabled")]
    public bool LegacyEnabled { get; init; } = true;
}

// Moving from Full Art Card Packs (full_art_card_packs, 0.3.x) to Full Art Spire.
// Both mods patch the same cards, so this mod stays inactive while the old one
// is installed and enabled, and carries the player's choices over.
internal static class LegacyMigration
{
    internal const string PackSelectionFileName = "legacy-pack-selection.json";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // Returns the old mod when it has loaded or will load in this session. Mods load
    // in the player's order, so a pending (None) old mod still loads after this one;
    // Disabled, Failed and duplicate copies never patch anything.
    internal static Mod? FindActiveLegacyMod(IEnumerable<Mod> mods)
    {
        return mods.FirstOrDefault(mod =>
            string.Equals(mod.manifest?.id, ModIdentity.LegacyModId, StringComparison.Ordinal)
            && mod.state is ModLoadState.None or ModLoadState.Loaded);
    }

    // Copies the old BaseLib settings once, before BaseLib creates defaults for the
    // new name. Property names are unchanged, so BaseLib reads the copy as its own.
    internal static bool ImportBaseLibSettings(string modConfigsDirectory)
    {
        string target = Path.Combine(modConfigsDirectory, ModIdentity.BaseLibConfigFileName);
        string source = Path.Combine(modConfigsDirectory, ModIdentity.LegacyBaseLibConfigFileName);
        if (File.Exists(target) || !File.Exists(source))
        {
            return false;
        }

        File.Copy(source, target, overwrite: false);
        return true;
    }

    // Reads the old mod folder's cfg allow-list and turns it into the packs that
    // folder shipped but the player did not enable. Null when there is nothing
    // trustworthy to import (no cfg, unreadable cfg, or no pack list).
    internal static LegacyPackSelection? ReadLegacyPackSelection(string legacyModFolder)
    {
        string configPath = Path.Combine(legacyModFolder, ModIdentity.LegacyPackConfigFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }

        FullArtConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<FullArtConfig>(File.ReadAllText(configPath), ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (config?.EnabledPacks is null)
        {
            return null;
        }

        var enabled = new HashSet<string>(
            config.EnabledPacks.Where(pool => !string.IsNullOrWhiteSpace(pool)),
            StringComparer.Ordinal);
        string packsDirectory = Path.Combine(legacyModFolder, "packs");
        IEnumerable<string> shipped = Directory.Exists(packsDirectory)
            ? Directory.EnumerateFiles(packsDirectory, "*.pack").Select(path => Path.GetFileNameWithoutExtension(path))
            : Enumerable.Empty<string>();

        return new LegacyPackSelection
        {
            Source = configPath,
            DisabledPacks = shipped
                .Where(pool => PackDiscovery.IsValidPoolName(pool) && !enabled.Contains(pool))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(pool => pool, StringComparer.Ordinal)
                .ToList(),
            LegacyEnabled = config.Enabled,
        };
    }

    internal static void SavePackSelection(string path, LegacyPackSelection selection)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(selection, WriteOptions));
        File.Move(temporary, path, overwrite: true);
    }

    internal static LegacyPackSelection? LoadPackSelection(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LegacyPackSelection>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The pack ids the selection switches off: 0.3.x pools are the v1 packs
    // megacrit.<pool>.
    internal static IEnumerable<string> DisabledPackIds(LegacyPackSelection? selection)
    {
        return selection?.DisabledPacks
            .Where(PackDiscovery.IsValidPoolName)
            .Select(PackDiscovery.LegacyPackId) ?? Enumerable.Empty<string>();
    }
}
