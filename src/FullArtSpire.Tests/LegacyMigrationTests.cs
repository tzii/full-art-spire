using MegaCrit.Sts2.Core.Modding;

namespace FullArtSpire.Tests;

internal static class LegacyMigrationTests
{
    internal static void Run()
    {
        DetectsOnlyAnOldModThatPatches();
        ImportsBaseLibSettingsOnce();
        TurnsTheOldAllowListIntoADenyList();
        RejectsUntrustworthyOldConfigs();
        AppliesAndPersistsTheSelection();
        Console.WriteLine("Legacy mod detection, settings import and pack selection carry-over tests passed");
    }

    private static void DetectsOnlyAnOldModThatPatches()
    {
        static Mod ModWith(string id, ModLoadState state) => new()
        {
            path = "mods/" + id,
            state = state,
            manifest = new ModManifest { id = id },
        };

        Require(LegacyMigration.FindActiveLegacyMod(new[] { ModWith(ModIdentity.ModId, ModLoadState.None) }) is null,
            "the new mod alone is not a conflict");
        foreach (ModLoadState state in new[] { ModLoadState.None, ModLoadState.Loaded })
        {
            Require(LegacyMigration.FindActiveLegacyMod(new[] { ModWith(ModIdentity.LegacyModId, state) }) is not null,
                $"an old mod in state {state} patches cards this session");
        }

        foreach (ModLoadState state in new[] { ModLoadState.Disabled, ModLoadState.Failed, ModLoadState.DisabledDuplicate, ModLoadState.AddedAtRuntime })
        {
            Require(LegacyMigration.FindActiveLegacyMod(new[] { ModWith(ModIdentity.LegacyModId, state) }) is null,
                $"an old mod in state {state} never patches, so the new one runs");
        }

        Mod active = ModWith(ModIdentity.LegacyModId, ModLoadState.Loaded);
        Require(ReferenceEquals(active, LegacyMigration.FindActiveLegacyMod(new[]
            {
                ModWith(ModIdentity.LegacyModId, ModLoadState.DisabledDuplicate), active,
            })), "a disabled duplicate does not hide the loaded copy");
        Require(LegacyMigration.FindActiveLegacyMod(new[] { ModWith("FULL_ART_CARD_PACKS", ModLoadState.Loaded) }) is null,
            "mod ids compare exactly, as the game compares them");
        Require(LegacyMigration.FindActiveLegacyMod(new[] { new Mod { path = "broken", state = ModLoadState.None } }) is null,
            "a mod without a manifest is ignored");
    }

    private static void ImportsBaseLibSettingsOnce()
    {
        string directory = TempDirectory();
        try
        {
            string legacy = Path.Combine(directory, ModIdentity.LegacyBaseLibConfigFileName);
            string current = Path.Combine(directory, ModIdentity.BaseLibConfigFileName);
            Require(!LegacyMigration.ImportBaseLibSettings(directory), "nothing to import without old settings");
            Require(!File.Exists(current), "no settings file is invented");

            File.WriteAllText(legacy, "{\"LayerDepth\": \"0.4\"}");
            Require(LegacyMigration.ImportBaseLibSettings(directory), "old settings are imported");
            Require(File.ReadAllText(current) == "{\"LayerDepth\": \"0.4\"}", "the import is a verbatim copy");
            Require(File.Exists(legacy), "the old settings file is left in place");

            File.WriteAllText(current, "{\"LayerDepth\": \"0.9\"}");
            Require(!LegacyMigration.ImportBaseLibSettings(directory), "existing settings are never overwritten");
            Require(File.ReadAllText(current) == "{\"LayerDepth\": \"0.9\"}", "the player's newer settings survive");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TurnsTheOldAllowListIntoADenyList()
    {
        string folder = LegacyFolder("{\"enabled\": true, \"enabled_packs\": [\"ironclad\", \"silent\", \"fool\", \"not_shipped\", \"\"]}",
            "ironclad", "silent", "defect", "fool", "curse", "Bad-Name");
        try
        {
            LegacyPackSelection? selection = LegacyMigration.ReadLegacyPackSelection(folder);
            Require(selection is not null, "a readable old cfg is imported");
            Require(selection!.DisabledPacks.SequenceEqual(new[] { "curse", "defect" }),
                $"only shipped, valid, unlisted packs are switched off; got {string.Join(",", selection.DisabledPacks)}");
            Require(selection.LegacyEnabled, "the old global switch is recorded");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        folder = LegacyFolder("{\"enabled\": false, \"enabled_packs\": [\"silent\"]}", "silent");
        try
        {
            LegacyPackSelection? selection = LegacyMigration.ReadLegacyPackSelection(folder);
            Require(selection is { LegacyEnabled: false, DisabledPacks.Count: 0 }, "a globally disabled install is recorded, not obeyed");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void RejectsUntrustworthyOldConfigs()
    {
        string folder = LegacyFolder(null, "silent");
        try
        {
            Require(LegacyMigration.ReadLegacyPackSelection(folder) is null, "a missing cfg imports nothing");
            foreach (string config in new[] { "{ not json", "{\"enabled_packs\": null}", "null" })
            {
                File.WriteAllText(Path.Combine(folder, ModIdentity.LegacyPackConfigFileName), config);
                Require(LegacyMigration.ReadLegacyPackSelection(folder) is null, $"cfg {config} imports nothing");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void AppliesAndPersistsTheSelection()
    {
        var selection = new LegacyPackSelection { Source = "test", DisabledPacks = new List<string> { "silent", "defect", "Bad-Name" } };
        Require(LegacyMigration.DisabledPackIds(selection).SequenceEqual(new[] { "megacrit.silent", "megacrit.defect" }),
            "0.3.x pools become the ids of their v1 packs; invalid pool names are dropped");
        Require(!LegacyMigration.DisabledPackIds(null).Any(), "no selection switches nothing off");

        string directory = TempDirectory();
        try
        {
            string path = Path.Combine(directory, "nested", LegacyMigration.PackSelectionFileName);
            Require(LegacyMigration.LoadPackSelection(path) is null, "a missing selection file loads as none");
            LegacyMigration.SavePackSelection(path, selection);
            LegacyPackSelection? loaded = LegacyMigration.LoadPackSelection(path);
            Require(loaded is not null && loaded.DisabledPacks.SequenceEqual(selection.DisabledPacks) && loaded.Source == "test",
                "the selection round-trips");
            Require(!File.Exists(path + ".new"), "the temporary file is replaced");
            File.WriteAllText(path, "{ broken");
            Require(LegacyMigration.LoadPackSelection(path) is null, "a broken selection file loads as none");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string LegacyFolder(string? config, params string[] packs)
    {
        string folder = TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder, "packs"));
        foreach (string pack in packs)
        {
            File.WriteAllText(Path.Combine(folder, "packs", pack + ".pack"), "{}");
        }

        if (config is not null)
        {
            File.WriteAllText(Path.Combine(folder, ModIdentity.LegacyPackConfigFileName), config);
        }

        return folder;
    }

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "full-art-spire-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
