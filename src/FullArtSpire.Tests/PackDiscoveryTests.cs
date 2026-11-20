using System.Text.Json;
using MegaCrit.Sts2.Core.Debug;

namespace FullArtSpire.Tests;

internal static class PackDiscoveryTests
{
    private const string Strike = "MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad";
    private const string KiWhip = "MegaCrit.Sts2.Core.Models.Cards.KiWhip";
    private const string ModdedCard = "SomeCharacter.Cards.Lantern";

    internal static void Run()
    {
        ResolvesPathsInsideTheirOwnRoot();
        FindsPacksInEveryLocation();
        RejectsMalformedPacks();
        ChecksRequirements();
        ResolvesConflictsAndDuplicates();
        AppliesSwitchesAndPrefixes();
        PackSwitchesReadAndWriteTheSetting();
        InstalledModFolder();
        Console.WriteLine("Pack v2 discovery, requirements, conflicts, path confinement and pack switch tests passed");
    }

    private static void ResolvesPathsInsideTheirOwnRoot()
    {
        string mod = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fas-mod"));
        string other = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fas-other"));
        string[] roots = { mod, other };

        Require(PackPaths.TryResolve(roots, "shaders/a.gdshader", out string shader) && shader == Path.Combine(mod, "shaders", "a.gdshader"),
            "unprefixed paths resolve in the mod folder");
        Require(PackPaths.TryResolve(roots, "@1/art/x.png", out string art) && art == Path.Combine(other, "art", "x.png"),
            "a prefix selects its pack root");
        Require(PackPaths.TryResolve(roots, "@0/art/x.png", out string own) && own == Path.Combine(mod, "art", "x.png"),
            "root 0 is the mod folder");
        foreach (string bad in new[] { "@1/../fas-mod/art/x.png", "@2/art/x.png", "@/art/x.png", "@1x/art.png", "@-1/a.png",
                     "@1/", "@1", "../outside.png", "@1/C:/Windows/x.png", "@1//etc/x.png", "", "   " })
        {
            Require(!PackPaths.TryResolve(roots, bad, out _), $"\"{bad}\" must not resolve");
        }

        // A manifest that writes a prefix itself stays inside its own root.
        FullArtCardEntry rebased = PackPaths.Rebase(new FullArtCardEntry { RuntimeModel = Strike, Art = "@1/art/x.png" }, PackPaths.Prefix(0));
        Require(PackPaths.TryResolve(roots, rebased.Art, out string trapped) && trapped.StartsWith(mod + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "a forged prefix cannot reach another pack's root");

        var entry = new FullArtCardEntry
        {
            RuntimeModel = Strike,
            Art = "art/strike.png",
            Layers = new FullArtLayerMetadata { Background = "layers/s/background.png", Foreground = "art/strike.png", ForegroundMask = "", FinishMask = "layers/s/finish.png" },
            Effects = new FullArtEffectMetadata { PermissionMask = " masks/p.png", Mask = "" },
        };
        FullArtCardEntry moved = PackPaths.Rebase(entry, PackPaths.Prefix(3));
        Require(moved.Art == "@3/art/strike.png" && moved.Layers!.Background == "@3/layers/s/background.png"
            && moved.Layers.Foreground == "@3/art/strike.png" && moved.Layers.FinishMask == "@3/layers/s/finish.png",
            "every layer path gets the prefix");
        Require(moved.Layers!.ForegroundMask == "" && moved.Effects!.Mask == "", "empty paths stay empty");
        Require(moved.Effects!.PermissionMask == "@3/masks/p.png", "effect masks get the prefix");
        Require(LayerPolicy.IsRelativeImagePath(moved.Art), "prefixed paths still pass the layer path rules");
        Require(entry.Art == "art/strike.png", "rebasing does not mutate the loaded entry");
    }

    private static void FindsPacksInEveryLocation()
    {
        using var temp = new TempTree();
        string mod = temp.Folder("mods/full_art_spire");
        temp.Json("mods/full_art_spire/packs/ironclad.pack", V1(Strike));
        temp.Json("mods/full_art_spire/packs/fool/pack.json", V2("megacrit.fool", KiWhip));
        temp.Json("mods/some_character/full_art/lantern/pack.json", V2("someone.lantern", ModdedCard));
        temp.Json("player/packs/mine/pack.json", V2("player.mine", Strike));
        temp.Folder("player/packs/empty");

        PackDiscoveryResult result = PackDiscovery.Discover(new PackDiscoveryInput(mod, temp.Path("player/packs"),
            new[] { new InstalledMod("some_character", "1.0.0", temp.Path("mods/some_character")) }, null));

        Require(result.Warnings.Count == 0, "valid packs raise no warnings: " + string.Join("; ", result.Warnings));
        Require(result.Packs.Select(pack => (pack.Id, pack.Source)).SequenceEqual(new[]
            {
                ("megacrit.ironclad", PackSource.Legacy), ("megacrit.fool", PackSource.Bundled),
                ("someone.lantern", PackSource.Mod), ("player.mine", PackSource.Player),
            }), "discovery visits v1, bundled, other mods, then player packs");
        Require(result.Packs.All(pack => pack.SkipReason is null), "nothing is skipped");
        DiscoveredPack legacy = result.Packs[0], fool = result.Packs[1], lantern = result.Packs[2];
        Require(legacy.Root == mod && legacy.Name == "Ironclad", "v1 packs resolve under the mod folder and are named after their pool");
        Require(fool.Root == temp.Path("mods/full_art_spire/packs/fool") && fool.Name == "Test megacrit.fool" && fool.Version == "1.2.3",
            "v2 packs resolve under their own folder");
        Require(lantern.SourceModId == "some_character", "mod packs remember their mod");
        Require(result.Notes.Any(note => note.Contains("empty", StringComparison.Ordinal)), "a folder without pack.json is noted");
        Require(result.Packs.Select(pack => pack.Order).SequenceEqual(new[] { 0, 1, 2, 3 }), "discovery order is recorded");
    }

    private static void RejectsMalformedPacks()
    {
        using var temp = new TempTree();
        string mod = temp.Folder("mod");
        temp.Write("mod/packs/broken/pack.json", "{ not json");
        temp.Json("mod/packs/wrong_format/pack.json", V2("a.wrong", Strike) with { Format = "sts2_full_art_standalone_pack" });
        temp.Json("mod/packs/future/pack.json", V2("a.future", Strike) with { FormatVersion = 3 });
        temp.Json("mod/packs/bad_id/pack.json", V2("NoNamespace", Strike));
        temp.Json("mod/packs/no_cards/pack.json", V2("a.no_cards", Strike) with { Cards = null });
        temp.Json("mod/packs/Bad-Pool.pack", V1(Strike));
        temp.Json("mod/packs/cards/pack.json", V2("a.cards", Strike) with
        {
            Cards = new List<FullArtCardEntry?>
            {
                null,
                new() { RuntimeModel = "", Art = "art/x.png" },
                new() { RuntimeModel = Strike, Art = "../escape.png" },
                new() { RuntimeModel = KiWhip, Art = "C:/abs.png" },
                new() { RuntimeModel = ModdedCard, Art = "art/ok.png" },
            },
        });

        PackDiscoveryResult result = PackDiscovery.Discover(new PackDiscoveryInput(mod, null, Array.Empty<InstalledMod>(), null));
        Require(result.Packs.Select(pack => pack.Id).SequenceEqual(new[] { "a.cards" }), "only the well-formed pack is found: "
            + string.Join(",", result.Packs.Select(pack => pack.Id)));
        Require(result.Packs[0].Cards.Select(card => card.RuntimeModel).SequenceEqual(new[] { ModdedCard }),
            "null, unnamed and escaping cards are dropped");
        Require(result.Warnings.Count == 10, $"each rejection is reported once; got {result.Warnings.Count}: {string.Join(" | ", result.Warnings)}");
    }

    private static void ChecksRequirements()
    {
        using var temp = new TempTree();
        string mod = temp.Folder("mod");
        PackRequirement Needs(string id, string? min = null) => new() { Mod = id, MinVersion = min };
        temp.Json("mod/packs/plain/pack.json", V2("t.plain", Strike));
        temp.Json("mod/packs/needs_loaded/pack.json", V2("t.needs_loaded", Strike) with { Requires = new() { Needs("character", "1.2.0") } });
        temp.Json("mod/packs/needs_newer/pack.json", V2("t.needs_newer", Strike) with { Requires = new() { Needs("character", "2.0.0") } });
        temp.Json("mod/packs/needs_missing/pack.json", V2("t.needs_missing", Strike) with { Requires = new() { Needs("absent") } });
        temp.Json("mod/packs/needs_unversioned/pack.json", V2("t.needs_unversioned", Strike) with { Requires = new() { Needs("unversioned", "1.0.0") } });
        temp.Json("mod/packs/bad_min/pack.json", V2("t.bad_min", Strike) with { Requires = new() { Needs("character", "soon") } });
        temp.Json("mod/packs/new_game/pack.json", V2("t.new_game", Strike) with { MinGameVersion = "0.200.0" });
        temp.Json("mod/packs/old_game/pack.json", V2("t.old_game", Strike) with { MinGameVersion = "0.107.1" });

        var mods = new[]
        {
            new InstalledMod("character", "1.4.0", temp.Path("character")),
            new InstalledMod("unversioned", null, temp.Path("unversioned")),
        };
        PackDiscoveryResult result = PackDiscovery.Discover(new PackDiscoveryInput(mod, null, mods, SemanticVersion.FromString("0.113.1")));
        string[] loaded = result.Packs.Where(pack => pack.SkipReason is null).Select(pack => pack.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Require(loaded.SequenceEqual(new[] { "t.needs_loaded", "t.old_game", "t.plain" }), "only packs whose requirements hold load: " + string.Join(",", loaded));
        Require(result.Packs.Single(pack => pack.Id == "t.needs_newer").SkipReason!.Contains("2.0.0", StringComparison.Ordinal), "the reason names the version");

        PackDiscoveryResult unknownGame = PackDiscovery.Discover(new PackDiscoveryInput(mod, null, mods, null));
        Require(unknownGame.Packs.Single(pack => pack.Id == "t.new_game").SkipReason is null,
            "without a game version, min_game_version is not enforced (as the game does for mods)");
    }

    private static void ResolvesConflictsAndDuplicates()
    {
        using var temp = new TempTree();
        string mod = temp.Folder("mod");
        temp.Json("mod/packs/fool.pack", V1(KiWhip, Strike));
        temp.Json("mod/packs/fool_v2/pack.json", V2("megacrit.fool", KiWhip));
        temp.Json("character/full_art/alt/pack.json", V2("someone.alt", KiWhip));
        temp.Json("player/packs/override/pack.json", V2("player.override", Strike));
        temp.Json("player/packs/humble/pack.json", V2("player.humble", KiWhip) with { Priority = -1 });
        temp.Json("player/packs/again/pack.json", V2("someone.alt", KiWhip));

        PackDiscoveryResult result = PackDiscovery.Discover(new PackDiscoveryInput(mod, temp.Path("player/packs"),
            new[] { new InstalledMod("character", "1.0.0", temp.Path("character")) }, null));
        DiscoveredPack v1 = result.Packs.Single(pack => pack.Source == PackSource.Legacy);
        Require(v1.SkipReason is not null, "a v2 pack replaces the v1 file with the same id");
        Require(result.Packs.Single(pack => pack.Id == "someone.alt" && pack.Source == PackSource.Mod).SkipReason is not null,
            "for one id installed twice, the player copy wins");

        PackLoadPlan plan = FullArtPackRegistry.Plan(mod, result.Packs, new HashSet<string>());
        Require(plan.CardOwners[KiWhip] == "someone.alt", "at equal priority a later source wins; lower priority never does");
        Require(plan.Cards[KiWhip].Art.StartsWith("@", StringComparison.Ordinal), "card paths carry their pack root");
        Require(plan.CardOwners[Strike] == "player.override", "the player pack overrides the bundled card");
        Require(plan.Overrides.Count > 0 && plan.Overrides.All(line => line.Contains(" overrides ", StringComparison.Ordinal)), "overrides are reported");

        temp.Json("mod/packs/strong/pack.json", V2("megacrit.strong", KiWhip) with { Priority = 5 });
        PackDiscoveryResult strong = PackDiscovery.Discover(new PackDiscoveryInput(mod, temp.Path("player/packs"),
            new[] { new InstalledMod("character", "1.0.0", temp.Path("character")) }, null));
        Require(FullArtPackRegistry.Plan(mod, strong.Packs, new HashSet<string>()).CardOwners[KiWhip] == "megacrit.strong",
            "higher priority beats a later source");
    }

    private static void AppliesSwitchesAndPrefixes()
    {
        using var temp = new TempTree();
        string mod = temp.Folder("mod");
        temp.Json("mod/packs/ironclad.pack", V1(Strike));
        temp.Json("mod/packs/fool/pack.json", V2("megacrit.fool", KiWhip));
        temp.Json("other/full_art/lantern/pack.json", V2("someone.lantern", ModdedCard));
        PackDiscoveryResult result = PackDiscovery.Discover(new PackDiscoveryInput(mod, null,
            new[] { new InstalledMod("other", "1.0.0", temp.Path("other")) }, null));

        PackLoadPlan all = FullArtPackRegistry.Plan(mod, result.Packs, new HashSet<string>());
        Require(all.Roots.Count == 3 && all.Roots[0] == mod, "the mod folder is root 0 and each v2 pack adds one root");
        Require(all.Cards[Strike].Art == "@0/art/strike.png", "v1 cards resolve under the mod folder");
        Require(PackPaths.TryResolve(all.Roots, all.Cards[ModdedCard].Art, out string lantern)
            && lantern == Path.Combine(temp.Path("other/full_art/lantern"), "art", "lantern.png"), "mod pack art resolves inside that mod");

        PackLoadPlan without = FullArtPackRegistry.Plan(mod, result.Packs, new HashSet<string> { "megacrit.fool", "megacrit.ironclad" });
        Require(without.Cards.Keys.SequenceEqual(new[] { ModdedCard }), "switched-off packs contribute no cards");
        Require(all.Cards.ContainsKey(Strike.ToUpperInvariant()), "runtime models stay case-insensitive");
    }

    private static void PackSwitchesReadAndWriteTheSetting()
    {
        string saved = FullArtModConfig.DisabledPacks;
        try
        {
            FullArtModConfig.DisabledPacks = " megacrit.silent, ,Bad Id,megacrit.fool ";
            Require(FullArtModConfig.DisabledPackIds().SetEquals(new[] { "megacrit.silent", "megacrit.fool" }), "the setting parses, ignoring junk");
            var fool = new PackToggleProperty("megacrit.fool");
            var defect = new PackToggleProperty("megacrit.defect");
            Require(fool.PropertyType == typeof(bool) && fool.Name == "Pack_megacrit_fool", "the switch looks like a bool setting with a node-safe name");
            Require(fool.GetValue(null) is false && defect.GetValue(null) is true, "listed packs read as off, others as on");
            fool.SetValue(null, true);
            defect.SetValue(null, false);
            Require(FullArtModConfig.DisabledPacks == "megacrit.defect,megacrit.silent", "switches rewrite the sorted list: " + FullArtModConfig.DisabledPacks);
            Require(fool.GetValue(null) is true && defect.GetValue(null) is false, "reads follow writes");
            Require(fool.GetCustomAttributes(typeof(Attribute), true).Length == 0 && !fool.IsDefined(typeof(Attribute), true), "the switch has no attributes");
        }
        finally
        {
            FullArtModConfig.DisabledPacks = saved;
        }
    }

    // Optional: FULL_ART_SPIRE_TEST_MOD_FOLDER=<an installed mod folder> runs discovery
    // and the load plan over real packs and checks every art path resolves to a PNG
    // or to a GPU variant source listed in that folder's gpu-textures.audit.
    private static void InstalledModFolder()
    {
        string? folder = Environment.GetEnvironmentVariable("FULL_ART_SPIRE_TEST_MOD_FOLDER");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        PackDiscoveryResult result = PackDiscovery.Discover(new PackDiscoveryInput(folder, null, Array.Empty<InstalledMod>(), null));
        Require(result.Warnings.Count == 0, "installed packs raise no warnings: " + string.Join("; ", result.Warnings.Take(5)));
        PackLoadPlan plan = FullArtPackRegistry.Plan(folder, result.Packs, new HashSet<string>());
        var audited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string audit = System.IO.Path.Combine(folder, "gpu-textures.audit");
        if (File.Exists(audit))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(audit));
            foreach (JsonElement row in document.RootElement.GetProperty("textures").EnumerateArray())
            {
                if (PackPaths.TryResolveUnder(folder, row.GetProperty("source").GetString(), out string source)) audited.Add(source);
            }
        }

        int missing = 0;
        foreach (FullArtCardEntry card in plan.Cards.Values)
        {
            if (!PackPaths.TryResolve(plan.Roots, card.Art, out string art) || !(File.Exists(art) || audited.Contains(art)))
            {
                missing++;
            }
        }

        Console.WriteLine($"Installed folder {folder}: {result.Packs.Count} packs, {plan.Cards.Count} cards, "
            + $"{plan.Overrides.Count} overrides, {missing} cards without art");
        Require(plan.Overrides.Count == 0, "base-game pools do not overlap");
        Require(missing == 0, "every installed card resolves to art");
    }

    private static FullArtPackManifest V1(params string[] models) => new()
    {
        Format = "sts2_full_art_standalone_pack",
        Cards = models.Select(model => (FullArtCardEntry?)new FullArtCardEntry { RuntimeModel = model, Art = $"art/{Short(model)}.png" }).ToList(),
    };

    private static FullArtPackManifestV2 V2(string id, params string[] models) => new()
    {
        Format = PackDiscovery.FormatName,
        FormatVersion = PackDiscovery.FormatVersion,
        Id = id,
        Name = "Test " + id,
        Version = "1.2.3",
        Cards = models.Select(model => (FullArtCardEntry?)new FullArtCardEntry { RuntimeModel = model, Art = $"art/{Short(model)}.png" }).ToList(),
    };

    private static string Short(string model) => model[(model.LastIndexOf('.') + 1)..].Replace("Ironclad", "").ToLowerInvariant();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TempTree : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "full-art-spire-packs-" + Guid.NewGuid().ToString("N"));

        internal string Path(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relative));

        internal string Folder(string relative)
        {
            string path = Path(relative);
            Directory.CreateDirectory(path);
            return path;
        }

        internal void Write(string relative, string text)
        {
            string path = Path(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        internal void Json<T>(string relative, T value) => Write(relative, JsonSerializer.Serialize(value));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
