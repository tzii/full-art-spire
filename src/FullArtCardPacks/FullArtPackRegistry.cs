using System.Reflection;
using System.Text.Json;
using Godot;

namespace FullArtCardPacks;

internal static class FullArtPackRegistry
{
    private const string ModId = "FullArtCardPacks";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, FullArtCardEntry> CardsByRuntimeModel = new(StringComparer.OrdinalIgnoreCase);

    internal static FullArtConfig Config { get; private set; } = new() { EnabledPacks = new List<string> { "silent" } };
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

        foreach (string pool in Config.EnabledPacks.Where(pool => !string.IsNullOrWhiteSpace(pool)))
        {
            LoadPack(pool);
        }

        GD.Print($"[{ModId}] loaded {CardsByRuntimeModel.Count} card art mappings.");
    }

    internal static bool HasReplacement(string? runtimeModel)
    {
        return !string.IsNullOrWhiteSpace(runtimeModel)
            && CardsByRuntimeModel.TryGetValue(runtimeModel, out FullArtCardEntry? entry)
            && !string.IsNullOrWhiteSpace(entry.Art)
            && File.Exists(ResolveModPath(entry.Art));
    }

    internal static bool TryGetCard(string? runtimeModel, out FullArtCardEntry? entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(runtimeModel)
            && CardsByRuntimeModel.TryGetValue(runtimeModel, out entry);
    }

    internal static string ResolveModPath(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(ModDirectory, normalized);
    }

    private static FullArtConfig LoadConfig()
    {
        string configPath = ResolveModPath("full_art_card_packs.json");
        if (!File.Exists(configPath))
        {
            GD.PushWarning($"[{ModId}] Missing config: {configPath}; defaulting to Silent.");
            return DefaultConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<FullArtConfig>(File.ReadAllText(configPath), JsonOptions)
                ?? DefaultConfig();
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
            EnableEffects = false,
            EffectIntensity = 0.3f,
            RareFirstEffects = true,
        };
    }

    private static void LoadPack(string pool)
    {
        string packPath = ResolveModPath($"packs/{pool}.json");
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

        foreach (FullArtCardEntry card in pack.Cards)
        {
            if (string.IsNullOrWhiteSpace(card.RuntimeModel))
            {
                GD.PushWarning($"[{ModId}] Skipping {card.CanonicalAssetName}: missing runtime_model");
                continue;
            }

            CardsByRuntimeModel[card.RuntimeModel] = card;
        }
    }
}
