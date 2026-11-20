using System.Runtime.CompilerServices;
using BaseLib.Config;
using Godot;
using HarmonyLib;
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
            var modConfig = new FullArtModConfig();
            GD.Print($"[{ModId}] Created FullArtModConfig instance");
            modConfig.ConfigChanged += (_, _) => EffectOverlayManager.RefreshTrackedCards();
            modConfig.OnConfigReloaded += EffectOverlayManager.RefreshTrackedCards;

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
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[{ModId}] Failed to register config: {ex}");
        }

        FullArtPackRegistry.Load();
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
