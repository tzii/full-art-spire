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
            
            ModConfigRegistry.Register(ModId, modConfig);
            GD.Print($"[{ModId}] Registered config with BaseLib (ModId: {ModId})");
            GD.Print($"[{ModId}] Config has visible settings: {modConfig.HasVisibleSettings()}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[{ModId}] Failed to register config: {ex}");
        }

        FullArtPackRegistry.Load();

        GD.Print($"[{ModId}] EnableEffects={FullArtModConfig.EnableEffects}, EffectIntensity={FullArtModConfig.EffectIntensity}, RareFirstEffects={FullArtModConfig.RareFirstEffects}");
        
        if (FullArtModConfig.EnableEffects)
        {
            EffectRenderer.Initialize();
            GD.Print($"[{ModId}] Effect renderer initialized");
        }

        new Harmony(ModId).PatchAll();
        GD.Print($"[{ModId}] initialized.");
    }
}
