using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;

namespace FullArtCardPacks;

[HarmonyPatch]
internal static class PortraitPatch
{
    private static readonly FieldInfo? PortraitField = typeof(NCard).GetField("_portrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? AncientPortraitField = typeof(NCard).GetField("_ancientPortrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "UpdateVisuals")]
    private static void UpdateVisualsPostfix(NCard __instance, PileType pileType, CardPreviewMode previewMode)
    {
        TryReplacePortrait(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "_EnterTree")]
    private static void EnterTreePostfix(NCard __instance)
    {
        TryReplacePortrait(__instance);
    }

    private static void TryReplacePortrait(NCard cardNode)
    {
        CardModel? model = cardNode.Model;
        if (model is null)
        {
            return;
        }

        string cardId = model.GetType().FullName ?? model.GetType().Name;
        if (!TryGetReplacementTexture(cardId, out Texture2D? texture) || texture is null)
        {
            return;
        }

        if (PortraitField?.GetValue(cardNode) is TextureRect portrait)
        {
            portrait.Texture = texture;
            portrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        }

        if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancientPortrait)
        {
            ancientPortrait.Texture = texture;
            ancientPortrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            ancientPortrait.Visible = true;
        }
    }

    private static bool TryGetReplacementTexture(string cardId, out Texture2D? texture)
    {
        texture = null;
        return FullArtPackRegistry.TryGetCard(cardId, out FullArtCardEntry? entry)
            && entry is not null
            && TextureCache.TryGetTexture(entry, out texture);
    }
}

[HarmonyPatch(typeof(CardModel), "Rarity", MethodType.Getter)]
internal static class CardRenderRaritySpoofPatch
{
    [ThreadStatic]
    private static bool _spoofForCardReload;

    [ThreadStatic]
    private static bool _treatMappedBasicCardsAsCommonInLibrary;

    [HarmonyPostfix]
    private static void Postfix(CardModel __instance, ref CardRarity __result)
    {
        if ((int)__result == 5)
        {
            return;
        }

        string cardId = __instance.GetType().FullName ?? __instance.GetType().Name;
        if (_treatMappedBasicCardsAsCommonInLibrary
            && __result == CardRarity.Basic
            && FullArtPackRegistry.HasReplacement(cardId))
        {
            __result = CardRarity.Common;
            return;
        }

        if (_spoofForCardReload && FullArtPackRegistry.HasReplacement(cardId))
        {
            __result = (CardRarity)5;
        }
    }

    internal static bool BeginSpoofFor(NCard cardNode)
    {
        CardModel? model = cardNode.Model;
        if (model is null)
        {
            return false;
        }

        string cardId = model.GetType().FullName ?? model.GetType().Name;
        bool previous = _spoofForCardReload;
        _spoofForCardReload = previous || FullArtPackRegistry.HasReplacement(cardId);
        return previous;
    }

    internal static void EndSpoof(bool previous)
    {
        _spoofForCardReload = previous;
    }

    internal static bool BeginLibraryRarityOverride()
    {
        bool previous = _treatMappedBasicCardsAsCommonInLibrary;
        _treatMappedBasicCardsAsCommonInLibrary = true;
        return previous;
    }

    internal static void EndLibraryRarityOverride(bool previous)
    {
        _treatMappedBasicCardsAsCommonInLibrary = previous;
    }
}

[HarmonyPatch(typeof(NCard), "Reload")]
internal static class CardReloadSpoofScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(NCard __instance, out bool __state)
    {
        __state = CardRenderRaritySpoofPatch.BeginSpoofFor(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(bool __state)
    {
        CardRenderRaritySpoofPatch.EndSpoof(__state);
    }
}

[HarmonyPatch(typeof(NCardLibraryGrid), "FilterCards", new Type[] { typeof(Func<CardModel, bool>) })]
internal static class CardLibraryGridFilterPatch
{
    [HarmonyPrefix]
    private static void Prefix(out bool __state)
    {
        __state = CardRenderRaritySpoofPatch.BeginLibraryRarityOverride();
    }

    [HarmonyPostfix]
    private static void Postfix(bool __state)
    {
        CardRenderRaritySpoofPatch.EndLibraryRarityOverride(__state);
    }
}

[HarmonyPatch(typeof(NCardLibraryGrid), "FilterCards", new Type[] { typeof(Func<CardModel, bool>), typeof(List<SortingOrders>) })]
internal static class CardLibraryGridFilterWithSortPatch
{
    [HarmonyPrefix]
    private static void Prefix(out bool __state)
    {
        __state = CardRenderRaritySpoofPatch.BeginLibraryRarityOverride();
    }

    [HarmonyPostfix]
    private static void Postfix(bool __state)
    {
        CardRenderRaritySpoofPatch.EndLibraryRarityOverride(__state);
    }
}
