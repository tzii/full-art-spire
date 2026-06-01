using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace FullArtCardPacks;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    private const string ModId = "FullArtCardPacks";

    public static void Initialize()
    {
        FullArtPackRegistry.Load();
        new Harmony(ModId).PatchAll();
        GD.Print($"[{ModId}] initialized.");
    }
}
