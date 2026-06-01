using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

[HarmonyPatch(typeof(NCard), "_Process")]
internal static class CardEffectPatch
{
    private static readonly FieldInfo? PortraitField = typeof(NCard).GetField("_portrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly Dictionary<NCard, ShaderMaterial?> EffectMaterials = new();

    [HarmonyPostfix]
    private static void Postfix(NCard __instance, double delta)
    {
        if (!FullArtModConfig.EnableEffects)
            return;

        var model = __instance.Model;
        if (model == null)
            return;

        var cardId = model.GetType().FullName ?? model.GetType().Name;
        
        if (!FullArtPackRegistry.TryGetCard(cardId, out var entry) || entry == null)
            return;

        if (!EffectPolicy.ShouldApplyEffect(model, entry))
            return;

        // Get or create effect material
        if (!EffectMaterials.TryGetValue(__instance, out var material))
        {
            material = CreateEffectMaterial(__instance, model, entry);
            if (material == null)
            {
                // Portrait not ready yet or shader/mask not loaded — retry next frame
                return;
            }
            EffectMaterials[__instance] = material;
        }

        // Update time for animation
        var time = (float)Time.GetTicksMsec() / 1000.0f;
        EffectRenderer.UpdateTime(material, time);
    }

    private static ShaderMaterial? CreateEffectMaterial(NCard cardNode, CardModel model, FullArtCardEntry entry)
    {
        // Get the portrait TextureRect
        if (PortraitField?.GetValue(cardNode) is not TextureRect portrait)
        {
            GD.Print($"[FullArtCardPacks] No portrait field for {entry.CanonicalAssetName}");
            return null;
        }

        // Determine rarity type
        int rarityType = model.Rarity switch
        {
            CardRarity.Common => 0,
            CardRarity.Uncommon => 1,
            CardRarity.Rare => 2,
            _ => 0
        };

        // Get intensity from config or entry
        float intensity = entry.Effects?.Intensity ?? FullArtModConfig.EffectIntensity;

        // Create material
        var material = EffectRenderer.CreateEffectMaterial(entry, rarityType, intensity);
        if (material == null)
        {
            GD.Print($"[FullArtCardPacks] Failed to create material for {entry.CanonicalAssetName}");
            return null;
        }

        // Apply material directly to portrait TextureRect
        portrait.Material = material;

        GD.Print($"[FullArtCardPacks] Applied effect to portrait for {entry.CanonicalAssetName} (rarity: {rarityType}, intensity: {intensity})");

        return material;
    }

    [HarmonyPatch(typeof(NCard), "_ExitTree")]
    [HarmonyPostfix]
    private static void CleanupPostfix(NCard __instance)
    {
        if (EffectMaterials.TryGetValue(__instance, out var material))
        {
            // Restore original material if needed
            if (PortraitField?.GetValue(__instance) is TextureRect portrait && portrait.Material == material)
            {
                portrait.Material = null;
            }
            EffectMaterials.Remove(__instance);
        }
    }
}
