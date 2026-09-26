using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace FullArtCardPacks;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    private const string ModId = "full_art_card_packs";

    public static void Initialize()
    {
        GD.Print($"[{ModId}] Starting initialization...");
        
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
}
