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
    // Per-refresh logging flooded godot.log; report each card type once.
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissedLayout = new(StringComparer.Ordinal);

    static PortraitPatch()
    {
        GD.Print($"[FullArtCardPacks] PortraitField found: {PortraitField != null}");
        GD.Print($"[FullArtCardPacks] AncientPortraitField found: {AncientPortraitField != null}");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "UpdateVisuals")]
    private static void UpdateVisualsPostfix(NCard __instance, PileType pileType, CardPreviewMode previewMode)
    {
        TryReplacePortrait(__instance, EffectContext.From(__instance, pileType, previewMode));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "_EnterTree")]
    private static void EnterTreePostfix(NCard __instance)
    {
        TryReplacePortrait(__instance, new EffectContext(EffectPresentation.Unknown));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "Reload")]
    private static void ReloadPostfix(NCard __instance)
    {
        // Reload also runs when locked/unknown visibility changes, without an
        // UpdateVisuals call. Refresh the child before the next frame is drawn.
        string? cardId = __instance.Model?.GetType().FullName;
        if (FullArtPackRegistry.HasReplacement(cardId)
            && PortraitField?.GetValue(__instance) is TextureRect { Visible: true }
            && MissedLayout.Add(cardId!))
            GD.PushWarning($"[FullArtCardPacks] Full-art layout was not applied to {cardId}; it shows the standard frame.");
        TryReplacePortrait(__instance, new EffectContext(EffectPresentation.Unknown));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "OnFreedToPool")]
    private static void FreedToPoolPostfix(NCard __instance)
    {
        PendingArtRefresh.Forget(__instance);
        LayeredCardRenderer.Clear(__instance);
    }

    /// <summary>Reapplies art once background loading delivers it.</summary>
    internal static void Refresh(NCard cardNode)
        => TryReplacePortrait(cardNode, EffectOverlayManager.StoredContext(cardNode));

    private static void TryReplacePortrait(NCard cardNode, EffectContext context)
    {
        CardModel? model = cardNode.Model;
        if (model is not null)
        {
            string cardId = model.GetType().FullName ?? model.GetType().Name;
            if (TryGetReplacementTexture(cardNode, cardId, out Texture2D? texture) && texture is not null)
            {
                if (Reported.Add(cardId))
                    GD.Print($"[FullArtCardPacks] Replacing portrait for {cardId}");
                if (PortraitField?.GetValue(cardNode) is TextureRect portrait)
                {
                    portrait.Texture = texture;
                    portrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                    portrait.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
                }
                if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancientPortrait)
                {
                    ancientPortrait.Texture = texture;
                    ancientPortrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                    ancientPortrait.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
                    ancientPortrait.Visible = true;
                }
            }
        }

        CardEffectPatch.ApplyOrUpdate(cardNode, context);
    }

    // While the art loads in the background the native portrait stays.
    private static bool TryGetReplacementTexture(NCard cardNode, string cardId, out Texture2D? texture)
    {
        texture = null;
        if (!(FullArtPackRegistry.TryGetCard(cardId, out FullArtCardEntry? entry)
            && entry is not null
            && !entry.PreserveOriginalLayout))
            return false;
        if (TextureCache.BackgroundLoading && LayerPolicy.TryDecide(entry, FullArtModConfig.EnableLayeredCards,
            cardNode.Visibility == MegaCrit.Sts2.Core.Entities.UI.ModelVisibility.Visible,
            FullArtModConfig.LayerDepth, out _))
            LayeredCardRenderer.Preload(entry);
        TextureState state = TextureCache.Request(entry.Art, out texture);
        if (state == TextureState.Pending) PendingArtRefresh.Track(cardNode);
        return state == TextureState.Ready;
    }
}

[HarmonyPatch(typeof(CardModel), "Rarity", MethodType.Getter)]
internal static class CardRenderRaritySpoofPatch
{
    [ThreadStatic]
    private static bool _spoofForCardReload;

    [ThreadStatic]
    private static bool _treatMappedBasicCardsAsCommonInLibrary;

    internal static bool IsSpoofingForCardReload => _spoofForCardReload;

    [HarmonyPostfix]
    private static void Postfix(CardModel __instance, ref CardRarity __result)
    {
        if (__result == CardRarity.Ancient || (!_spoofForCardReload && !_treatMappedBasicCardsAsCommonInLibrary))
        {
            return;
        }

        string cardId = __instance.GetType().FullName ?? __instance.GetType().Name;
        __result = Decide(__result, _spoofForCardReload, _treatMappedBasicCardsAsCommonInLibrary,
            FullArtPackRegistry.HasReplacement(cardId));
    }

    // The Compendium reloads its card nodes inside FilterCards, so both scopes
    // can be active at once. A reload must win: otherwise mapped Basic cards
    // (Bash, Strikes, Defends) read Common and keep the standard frame.
    internal static CardRarity Decide(CardRarity rarity, bool reloading, bool libraryFilter, bool hasReplacement)
    {
        if (rarity == CardRarity.Ancient || (!reloading && !libraryFilter) || !hasReplacement)
        {
            return rarity;
        }

        if (reloading)
        {
            return CardRarity.Ancient;
        }
        return rarity == CardRarity.Basic ? CardRarity.Common : rarity;
    }

    internal static bool BeginSpoofFor(NCard cardNode)
    {
        bool previous = _spoofForCardReload;
        CardModel? model = cardNode.Model;
        if (model is null)
        {
            return previous;
        }

        string cardId = model.GetType().FullName ?? model.GetType().Name;
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

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        CardRenderRaritySpoofPatch.EndSpoof(__state);
        return __exception;
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

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        CardRenderRaritySpoofPatch.EndLibraryRarityOverride(__state);
        return __exception;
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

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        CardRenderRaritySpoofPatch.EndLibraryRarityOverride(__state);
        return __exception;
    }
}
