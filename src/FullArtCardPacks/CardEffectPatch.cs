using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class CardEffectPatch
{
    private static readonly FieldInfo? PortraitField = typeof(NCard).GetField("_portrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? AncientPortraitField = typeof(NCard).GetField("_ancientPortrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly Dictionary<NCard, ShaderMaterial> EffectMaterials = new();

    internal static void TryPatch(Harmony harmony)
    {
        try
        {
            MethodInfo? processMethod = AccessTools.Method(typeof(NCard), "_Process");
            MethodInfo? processPostfix = AccessTools.Method(typeof(CardEffectPatch), nameof(ProcessPostfix));
            MethodInfo? exitTreeMethod = AccessTools.Method(typeof(NCard), "_ExitTree");
            MethodInfo? cleanupPostfix = AccessTools.Method(typeof(CardEffectPatch), nameof(CleanupPostfix));

            if (exitTreeMethod is not null && cleanupPostfix is not null)
            {
                harmony.Patch(exitTreeMethod, postfix: new HarmonyMethod(cleanupPostfix));
            }
            else
            {
                GD.PushWarning("[FullArtCardPacks] NCard._ExitTree not found; effect cleanup patch is disabled.");
            }

            if (processMethod is null || processPostfix is null)
            {
                GD.PushWarning("[FullArtCardPacks] NCard._Process not found; animated card effects are disabled for this game version.");
                return;
            }

            harmony.Patch(processMethod, postfix: new HarmonyMethod(processPostfix));
            GD.Print("[FullArtCardPacks] Effect tick patch applied.");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[FullArtCardPacks] Failed to patch animated effects; full-art replacements will continue without effects. {ex.Message}");
        }
    }

    internal static void ApplyOrUpdate(NCard cardNode)
    {
        var model = cardNode.Model;
        if (model == null)
        {
            ClearEffectMaterial(cardNode);
            return;
        }

        var cardId = model.GetType().FullName ?? model.GetType().Name;

        if (!FullArtPackRegistry.TryGetCard(cardId, out var entry) || entry == null)
        {
            ClearEffectMaterial(cardNode);
            return;
        }

        if (!EffectPolicy.ShouldApplyEffect(model, entry))
        {
            ClearEffectMaterial(cardNode);
            return;
        }

        if (!EffectMaterials.TryGetValue(cardNode, out var material))
        {
            material = CreateEffectMaterial(cardNode, model, entry);
            if (material == null)
            {
                return;
            }
            EffectMaterials[cardNode] = material;
        }

        float intensity = Math.Clamp(FullArtModConfig.EffectIntensity, 0f, 1f);
        EffectRenderer.UpdateIntensity(material, intensity);
        ApplyMaterialToPortraits(cardNode, material);
    }

    private static void ProcessPostfix(NCard __instance, double delta)
    {
        ApplyOrUpdate(__instance);
    }

    private static ShaderMaterial? CreateEffectMaterial(NCard cardNode, CardModel model, FullArtCardEntry entry)
    {
        if (PortraitField?.GetValue(cardNode) is not TextureRect
            && AncientPortraitField?.GetValue(cardNode) is not TextureRect)
        {
            GD.Print($"[FullArtCardPacks] No portrait fields for {entry.CanonicalAssetName}");
            return null;
        }

        int rarityType = model.Rarity switch
        {
            CardRarity.Common => 0,
            CardRarity.Uncommon => 1,
            CardRarity.Rare => 2,
            _ => 0
        };

        float intensity = Math.Clamp(FullArtModConfig.EffectIntensity, 0f, 1f);

        var material = EffectRenderer.CreateEffectMaterial(entry, rarityType, intensity);
        if (material == null)
        {
            GD.Print($"[FullArtCardPacks] Failed to create material for {entry.CanonicalAssetName}");
            return null;
        }

        ApplyMaterialToPortraits(cardNode, material);

        GD.Print($"[FullArtCardPacks] Applied effect to portrait for {entry.CanonicalAssetName} (rarity: {rarityType}, intensity: {intensity})");

        return material;
    }

    private static void ApplyMaterialToPortraits(NCard cardNode, ShaderMaterial material)
    {
        if (PortraitField?.GetValue(cardNode) is TextureRect portrait)
        {
            portrait.Material = material;
        }

        if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancientPortrait)
        {
            ancientPortrait.Material = material;
        }
    }

    private static void ClearEffectMaterial(NCard cardNode)
    {
        if (!EffectMaterials.TryGetValue(cardNode, out var material))
        {
            return;
        }

        if (PortraitField?.GetValue(cardNode) is TextureRect portrait && portrait.Material == material)
        {
            portrait.Material = null;
        }

        if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancientPortrait && ancientPortrait.Material == material)
        {
            ancientPortrait.Material = null;
        }

        EffectMaterials.Remove(cardNode);
    }

    private static void CleanupPostfix(NCard __instance)
    {
        ClearEffectMaterial(__instance);
    }
}
