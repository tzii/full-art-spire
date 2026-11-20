using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Debug;

namespace FullArtSpire;

// Cards from every enabled pack, plus the pack roots their paths resolve under.
internal sealed record PackLoadPlan(
    IReadOnlyList<string> Roots,
    IReadOnlyDictionary<string, FullArtCardEntry> Cards,
    IReadOnlyDictionary<string, string> CardOwners,
    IReadOnlyList<string> Overrides);

internal static class FullArtPackRegistry
{
    private const string ModId = "FullArtSpire";

    private static Dictionary<string, FullArtCardEntry> CardsByRuntimeModel = new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, string> CardOwners = new Dictionary<string, string>();
    // The Rarity getter asks this during every card reload; avoid a disk check per call.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> ReplacementCache = new(StringComparer.OrdinalIgnoreCase);
    // Index 0 is the mod folder. Replaced, never mutated, so readers need no lock.
    private static IReadOnlyList<string> _roots = Array.Empty<string>();

    internal static string ModDirectory { get; private set; } = "";

    // Every pack found at the last load, including disabled and skipped ones.
    internal static IReadOnlyList<DiscoveredPack> Packs { get; private set; } = Array.Empty<DiscoveredPack>();

    internal static string PlayerPacksDirectory()
        => Path.Combine(OS.GetUserDataDir(), ModIdentity.UserDataFolder, "packs");

    internal static void Load(IReadOnlyList<InstalledMod> otherMods, SemanticVersion? gameVersion)
    {
        // Explicit reload is the boundary for replaced/recovered files.
        // Drop cache ownership only; active native nodes keep their resources.
        TextureCache.Invalidate();
        LayeredCardRenderer.InvalidateResources();
        EffectRenderer.InvalidateResources();
        ModDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        _roots = new[] { ModDirectory };

        PackDiscoveryResult discovery = PackDiscovery.Discover(
            new PackDiscoveryInput(ModDirectory, PlayerPacksDirectory(), otherMods, gameVersion));
        foreach (string note in discovery.Notes) GD.Print($"[{ModId}] {note}");
        foreach (string warning in discovery.Warnings) GD.PushWarning($"[{ModId}] {warning}");
        Packs = discovery.Packs;

        ISet<string> disabled = FullArtModConfig.DisabledPackIds();
        PackLoadPlan plan = Plan(ModDirectory, discovery.Packs, disabled);
        foreach (string line in plan.Overrides) GD.Print($"[{ModId}] {line}");

        _roots = plan.Roots;
        GpuTextureVariants.Load(plan.Roots);
        CardsByRuntimeModel = new Dictionary<string, FullArtCardEntry>(plan.Cards, StringComparer.OrdinalIgnoreCase);
        CardOwners = plan.CardOwners;
        ReplacementCache.Clear();

        foreach (DiscoveredPack pack in discovery.Packs)
        {
            string state = pack.SkipReason is not null ? "skipped"
                : disabled.Contains(pack.Id) ? "switched off"
                : $"{plan.CardOwners.Values.Count(owner => owner == pack.Id)} of {pack.Cards.Count} cards";
            GD.Print($"[{ModId}] Pack {PackDiscovery.Describe(pack)}: {state}");
        }

        GD.Print($"[{ModId}] loaded {CardsByRuntimeModel.Count} card art mappings from {discovery.Packs.Count} packs.");
    }

    // Pure: no Godot, no globals. Packs are applied in PackDiscovery.ApplicationOrder,
    // so the winner of a card is the highest-priority, latest-source pack.
    internal static PackLoadPlan Plan(string modDirectory, IEnumerable<DiscoveredPack> packs, ISet<string> disabledIds)
    {
        var roots = new List<string> { modDirectory };
        var rootIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.TrimEndingDirectorySeparator(Path.GetFullPath(modDirectory))] = 0,
        };
        var cards = new Dictionary<string, FullArtCardEntry>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new List<string>();

        IEnumerable<DiscoveredPack> active = packs.Where(pack => pack.SkipReason is null && !disabledIds.Contains(pack.Id));
        foreach (DiscoveredPack pack in PackDiscovery.ApplicationOrder(active))
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pack.Root));
            if (!rootIndexes.TryGetValue(root, out int index))
            {
                index = roots.Count;
                roots.Add(root);
                rootIndexes[root] = index;
            }

            string prefix = PackPaths.Prefix(index);
            foreach (FullArtCardEntry card in pack.Cards)
            {
                if (owners.TryGetValue(card.RuntimeModel, out string? owner) && owner != pack.Id)
                {
                    overrides.Add($"{card.RuntimeModel}: {PackDiscovery.Describe(pack)} overrides {owner}");
                }

                cards[card.RuntimeModel] = PackPaths.Rebase(card, prefix);
                owners[card.RuntimeModel] = pack.Id;
            }
        }

        return new PackLoadPlan(roots, cards, owners, overrides);
    }

    internal static bool HasReplacement(string? runtimeModel)
    {
        if (string.IsNullOrWhiteSpace(runtimeModel)) return false;
        return ReplacementCache.GetOrAdd(runtimeModel, model =>
            CardsByRuntimeModel.TryGetValue(model, out FullArtCardEntry? entry)
            && !entry.PreserveOriginalLayout
            && !string.IsNullOrWhiteSpace(entry.Art)
            && TryResolveModPath(entry.Art, out string artPath)
            && (File.Exists(artPath) || GpuTextureVariants.Has(artPath)));
    }

    internal static bool TryGetCard(string? runtimeModel, out FullArtCardEntry? entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(runtimeModel)
            && CardsByRuntimeModel.TryGetValue(runtimeModel, out entry);
    }

    // The id of the pack that supplied this card's art.
    internal static string? PackFor(string? runtimeModel)
        => runtimeModel is not null && CardOwners.TryGetValue(runtimeModel, out string? owner) ? owner : null;

    // Resolves a card path (prefixed with its pack root) or a mod-folder path, and
    // refuses anything that leaves that root.
    internal static bool TryResolveModPath(string? relativePath, out string fullPath)
        => PackPaths.TryResolve(_roots.Count == 0 ? new[] { ModDirectory } : _roots, relativePath, out fullPath);

    internal static string ImportedPackSelectionPath()
    {
        return Path.Combine(OS.GetUserDataDir(), ModIdentity.UserDataFolder, LegacyMigration.PackSelectionFileName);
    }
}
