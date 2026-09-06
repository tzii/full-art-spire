using System.Reflection;
using System.Text.Json;
using Godot;

namespace FullArtCardPacks;

internal static class FullArtPackRegistry
{
    private const string ModId = "FullArtCardPacks";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, FullArtCardEntry> CardsByRuntimeModel = new(StringComparer.OrdinalIgnoreCase);

    internal static FullArtConfig Config { get; set; } = new() { EnabledPacks = new List<string> { "silent" } };
    internal static string ModDirectory { get; private set; } = "";

    internal static void Load()
    {
        ModDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        CardsByRuntimeModel.Clear();
        Config = LoadConfig();

        if (!Config.Enabled)
        {
            GD.Print($"[{ModId}] disabled by config.");
            return;
        }

        IEnumerable<string> enabledPacks = Config.EnabledPacks ?? Enumerable.Empty<string>();
        foreach (string pool in enabledPacks.Where(pool => !string.IsNullOrWhiteSpace(pool)))
        {
            if (!IsValidPoolName(pool))
            {
                GD.PushWarning($"[{ModId}] Skipping invalid configured pool name: {pool}");
                continue;
            }

            LoadPack(pool);
        }

        GD.Print($"[{ModId}] loaded {CardsByRuntimeModel.Count} card art mappings.");
    }

    internal static bool HasReplacement(string? runtimeModel)
    {
        return !string.IsNullOrWhiteSpace(runtimeModel)
            && CardsByRuntimeModel.TryGetValue(runtimeModel, out FullArtCardEntry? entry)
            && !entry.PreserveOriginalLayout
            && !string.IsNullOrWhiteSpace(entry.Art)
            && TryResolveModPath(entry.Art, out string artPath)
            && File.Exists(artPath);
    }

    internal static bool TryGetCard(string? runtimeModel, out FullArtCardEntry? entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(runtimeModel)
            && CardsByRuntimeModel.TryGetValue(runtimeModel, out entry);
    }

    internal static bool TryResolveModPath(string? relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        string modRoot = Path.GetFullPath(ModDirectory);
        string modRootTrimmed = Path.TrimEndingDirectorySeparator(modRoot);
        string modRootWithSeparator = modRootTrimmed + Path.DirectorySeparatorChar;
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        fullPath = Path.GetFullPath(Path.Combine(ModDirectory, normalized));
        return fullPath.Equals(modRootTrimmed, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(modRootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolveModPath(string relativePath)
    {
        return TryResolveModPath(relativePath, out string fullPath)
            ? fullPath
            : Path.Combine(ModDirectory, "__invalid__");
    }

    private static FullArtConfig LoadConfig()
    {
        string configPath = ResolveModPath("full_art_card_packs.cfg");
        if (!File.Exists(configPath))
        {
            GD.PushWarning($"[{ModId}] Missing config: {configPath}; defaulting to Silent.");
            return DefaultConfig();
        }

        try
        {
            FullArtConfig config = JsonSerializer.Deserialize<FullArtConfig>(File.ReadAllText(configPath), JsonOptions)
                ?? DefaultConfig();
            WarnIgnoredLegacyEffectSettings(config);
            return NormalizeConfig(config);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Failed to load config {configPath}: {ex.Message}; defaulting to Silent.");
            return DefaultConfig();
        }
    }

    private static FullArtConfig DefaultConfig()
    {
        return new FullArtConfig
        {
            Enabled = true,
            EnabledPacks = new List<string> { "silent" },
        };
    }

    private static void WarnIgnoredLegacyEffectSettings(FullArtConfig config)
    {
        string[] legacyKeys = { "enable_effects", "effect_intensity", "rare_first_effects", "grid_motion", "focused_motion" };
        string[] present = legacyKeys
            .Where(key => config.AdditionalSettings?.ContainsKey(key) == true)
            .ToArray();
        if (present.Length > 0)
        {
            GD.PushWarning($"[{ModId}] Ignoring legacy effect settings in full_art_card_packs.cfg ({string.Join(", ", present)}). BaseLib is the single runtime effect configuration source.");
        }
    }

    private static FullArtConfig NormalizeConfig(FullArtConfig config)
    {
        if (config.EnabledPacks is not null)
        {
            return config;
        }

        GD.PushWarning($"[{ModId}] Config enabled_packs is null; no packs will be loaded.");
        return config with
        {
            EnabledPacks = new List<string>(),
        };
    }

    private static void LoadPack(string pool)
    {
        if (!IsValidPoolName(pool))
        {
            GD.PushWarning($"[{ModId}] Skipping invalid pack name: {pool}");
            return;
        }

        if (!TryResolveModPath($"packs/{pool}.pack", out string packPath))
        {
            GD.PushWarning($"[{ModId}] Skipping pack outside mod directory: {pool}");
            return;
        }

        if (!File.Exists(packPath))
        {
            GD.PushWarning($"[{ModId}] Missing pack manifest: {packPath}");
            return;
        }

        FullArtPackManifest? pack;
        try
        {
            pack = JsonSerializer.Deserialize<FullArtPackManifest>(File.ReadAllText(packPath), JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Failed to load pack manifest {packPath}: {ex.Message}");
            return;
        }

        if (pack is null)
        {
            GD.PushWarning($"[{ModId}] Could not parse pack manifest: {packPath}");
            return;
        }

        if (pack.Cards is null)
        {
            GD.PushWarning($"[{ModId}] Skipping pack manifest with missing cards: {packPath}");
            return;
        }

        foreach (FullArtCardEntry? card in pack.Cards)
        {
            if (card is null)
            {
                GD.PushWarning($"[{ModId}] Skipping null card entry in {packPath}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.RuntimeModel))
            {
                GD.PushWarning($"[{ModId}] Skipping {card.CanonicalAssetName}: missing runtime_model");
                continue;
            }

            if (!TryResolveModPath(card.Art, out string artPath))
            {
                GD.PushWarning($"[{ModId}] Skipping {card.CanonicalAssetName}: art path must stay under mod directory");
                continue;
            }

            CardsByRuntimeModel[card.RuntimeModel] = card;
        }
    }

    private static bool IsValidPoolName(string pool)
    {
        return !string.IsNullOrWhiteSpace(pool)
            && pool.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_');
    }
}
