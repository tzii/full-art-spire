using System.Runtime.CompilerServices;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Modding;

namespace FullArtSpire;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    private const string ModId = ModIdentity.ModId;

    public static void Initialize()
    {
        GD.Print($"[{ModId}] Starting initialization...");

        if (StayInactiveForLegacyMod())
        {
            return;
        }

        ImportLegacyBaseLibSettings();

        try
        {
            // The mod list shows the title from this text, before the page is built.
            SettingsText.Install();
            var modConfig = new FullArtModConfig();
            GD.Print($"[{ModId}] Created FullArtModConfig instance");
            modConfig.ConfigChanged += (_, _) => OnSettingsChanged();
            modConfig.OnConfigReloaded += OnSettingsChanged;

            ModConfigRegistry.Register(ModId, modConfig);

            if (ModConfigRegistry.Get(ModId) == null)
            {
                GD.PushWarning($"[{ModId}] Config registration failed or was skipped. Menu entry will not appear.");
            }
            else if (!modConfig.HasVisibleSettings())
            {
                GD.PushWarning($"[{ModId}] Config registered but HasVisibleSettings() returned false.");
            }
            else
            {
                GD.Print($"[{ModId}] Config registered successfully with visible settings.");
            }

            ImportLegacyPackSelection(modConfig);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[{ModId}] Failed to register config: {ex}");
        }

        FullArtPackRegistry.Load(OtherInstalledMods(), GameVersion());
        CardReports.Initialize();

        GD.Print($"[{ModId}] BaseLib effect settings: enabled={FullArtModConfig.EnableEffects}, intensity={FullArtModConfig.EffectIntensity}, rareFirst={FullArtModConfig.RareFirstEffects}, gridMotion={FullArtModConfig.GridMotion}, focusedMotion={FullArtModConfig.FocusedMotion}");

        if (FullArtModConfig.EnableEffects)
        {
            EffectRenderer.Initialize();
            GD.Print($"[{ModId}] Effect renderer initialized");
        }

        var harmony = new Harmony(ModId);
        harmony.PatchAll();
        GD.Print($"[{ModId}] initialized.");
    }

    // A tickbox change, or Restore defaults. Pack switches apply at once: the
    // card table is rebuilt and cards on screen reload; finishes then refresh.
    private static void OnSettingsChanged()
    {
        try
        {
            if (FullArtPackRegistry.ApplySwitches())
                GD.Print($"[{ModId}] Reloaded {LiveCardRefresh.ReloadStaleCards()} cards on screen.");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not apply pack switches until restart: {ex.Message}");
        }

        EffectOverlayManager.RefreshTrackedCards();
    }

    // Both mods patch the same cards. While the old one is installed and enabled,
    // this one registers nothing and applies no patches, and only records the
    // old install's pack choices so they survive its removal.
    private static bool StayInactiveForLegacyMod()
    {
        Mod? legacy;
        try
        {
            legacy = LegacyMigration.FindActiveLegacyMod(ModManager.Mods);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not check for {ModIdentity.LegacyModId}: {ex.Message}");
            return false;
        }

        if (legacy is null)
        {
            return false;
        }

        SnapshotLegacyPackSelection(legacy.path);
        string message = $"{ModIdentity.DisplayName} is inactive: the older {ModIdentity.LegacyDisplayName} mod "
            + $"({ModIdentity.LegacyModId}) is also installed, at {legacy.path}. Delete that folder, or disable it in "
            + "the mod list, then restart the game. Your settings carry over.";
        GD.PushWarning($"[{ModId}] {message}");
        try
        {
            // A BaseLib without this API fails when the method is compiled, so the
            // call stays in its own method and is guarded here.
            QueueMainMenuWarning(message);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not queue the main-menu warning: {ex.Message}");
        }

        return true;
    }

    private static void SnapshotLegacyPackSelection(string legacyModFolder)
    {
        try
        {
            LegacyPackSelection? selection = LegacyMigration.ReadLegacyPackSelection(legacyModFolder);
            if (selection is null)
            {
                GD.Print($"[{ModId}] No readable pack selection in {legacyModFolder}; nothing to carry over.");
                return;
            }

            LegacyMigration.SavePackSelection(FullArtPackRegistry.ImportedPackSelectionPath(), selection);
            GD.Print($"[{ModId}] Recorded {ModIdentity.LegacyModId} pack selection; switched off: "
                + (selection.DisabledPacks.Count == 0 ? "none" : string.Join(", ", selection.DisabledPacks)));
            if (!selection.LegacyEnabled)
            {
                GD.Print($"[{ModId}] {ModIdentity.LegacyPackConfigFileName} had \"enabled\": false; that switch is not carried over.");
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not record the {ModIdentity.LegacyModId} pack selection: {ex.Message}");
        }
    }

    // Copies the packs a 0.3.x player had switched off into the pack switches, once.
    private static void ImportLegacyPackSelection(FullArtModConfig modConfig)
    {
        if (FullArtModConfig.LegacyPackSelectionImported)
        {
            return;
        }

        LegacyPackSelection? selection = LegacyMigration.LoadPackSelection(FullArtPackRegistry.ImportedPackSelectionPath());
        string[] disabled = LegacyMigration.DisabledPackIds(selection).ToArray();
        foreach (string id in disabled)
        {
            FullArtModConfig.SetPackEnabled(id, false);
        }

        FullArtModConfig.LegacyPackSelectionImported = true;
        modConfig.Save();
        if (disabled.Length > 0)
        {
            GD.Print($"[{ModId}] Kept off the packs switched off in {ModIdentity.LegacyDisplayName}: {string.Join(", ", disabled)}");
        }
    }

    // Packs may live in any mod that is loaded or still to load this session.
    // Mods after this one in the load order have not loaded yet, so the state
    // filter matches FindActiveLegacyMod.
    private static IReadOnlyList<InstalledMod> OtherInstalledMods()
    {
        try
        {
            return ModManager.Mods
                .Where(mod => mod.manifest?.id is { Length: > 0 } id && id != ModId
                    && mod.state is ModLoadState.None or ModLoadState.Loaded
                    && !string.IsNullOrEmpty(mod.path))
                .Select(mod => new InstalledMod(mod.manifest!.id!, mod.manifest.version, mod.path))
                .ToArray();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not list other mods; only bundled and player packs load: {ex.Message}");
            return Array.Empty<InstalledMod>();
        }
    }

    private static SemanticVersion? GameVersion()
    {
        try
        {
            return ReleaseInfoManager.Instance.SemVer;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not read the game version; min_game_version is not checked: {ex.Message}");
            return null;
        }
    }

    private static void ImportLegacyBaseLibSettings()
    {
        string modConfigs = Path.Combine(OS.GetUserDataDir(), "mod_configs");
        try
        {
            if (LegacyMigration.ImportBaseLibSettings(modConfigs))
            {
                GD.Print($"[{ModId}] Imported settings from {ModIdentity.LegacyBaseLibConfigFileName} into {ModIdentity.BaseLibConfigFileName}.");
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[{ModId}] Could not import {ModIdentity.LegacyBaseLibConfigFileName}: {ex.Message}; using defaults.");
        }
    }

    // BaseLib shows queued messages in a popup when the main menu opens.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void QueueMainMenuWarning(string message)
    {
        ModConfig.ModConfigLogger.Warn(message, showInGui: true);
    }
}
