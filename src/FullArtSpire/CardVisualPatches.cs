using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;

namespace FullArtSpire;

[HarmonyPatch]
internal static class PortraitPatch
{
    private static readonly FieldInfo? PortraitField = typeof(NCard).GetField("_portrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? AncientPortraitField = typeof(NCard).GetField("_ancientPortrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    // Per-refresh logging flooded godot.log; report each card type once.
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissedLayout = new(StringComparer.Ordinal);
    // The native portrait's own layout, kept on the TextureRect while full art
    // replaces it, so a card whose pack is switched off looks as the game made it.
    private static readonly StringName NativeStretchMeta = "full_art_spire_native_stretch";
    private static readonly StringName NativeFilterMeta = "full_art_spire_native_filter";

    static PortraitPatch()
    {
        GD.Print($"[FullArtSpire] PortraitField found: {PortraitField != null}");
        GD.Print($"[FullArtSpire] AncientPortraitField found: {AncientPortraitField != null}");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "UpdateVisuals")]
    private static void UpdateVisualsPostfix(NCard __instance, PileType pileType, CardPreviewMode previewMode)
    {
        LiveCardRefresh.ReloadIfStale(__instance);
        TryReplacePortrait(__instance, EffectContext.From(__instance, pileType, previewMode));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "_EnterTree")]
    private static void EnterTreePostfix(NCard __instance)
    {
        CardReports.Initialize();
        if (LiveCardRefresh.ReloadIfStale(__instance))
            return; // Its Reload postfix has applied the current art.
        TryReplacePortrait(__instance, PlacedContext(__instance));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "Reload")]
    private static void ReloadPostfix(NCard __instance)
    {
        // Reload also runs when locked/unknown visibility changes, without an
        // UpdateVisuals call. Refresh the child before the next frame is drawn.
        LiveCardRefresh.MarkCurrent(__instance);
        string? cardId = __instance.Model?.GetType().FullName;
        if (FullArtPackRegistry.HasReplacement(cardId)
            && PortraitField?.GetValue(__instance) is TextureRect { Visible: true }
            && MissedLayout.Add(cardId!))
            GD.PushWarning($"[FullArtSpire] Full-art layout was not applied to {cardId}; it shows the standard frame.");
        TryReplacePortrait(__instance, PlacedContext(__instance));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NCard), "OnFreedToPool")]
    private static void FreedToPoolPostfix(NCard __instance)
    {
        PendingArtRefresh.Forget(__instance);
        EffectOverlayManager.Forget(__instance);
        LayeredCardRenderer.Clear(__instance);
    }

    // Drawn cards reload after UpdateVisuals has classified them for the hand;
    // keep that rather than the scale-inferred grid look. Other screens retain
    // their earlier reload behaviour.
    private static EffectContext PlacedContext(NCard cardNode)
        => EffectContext.FromTree(cardNode)?.Presentation == EffectPresentation.Hand
            || EffectOverlayManager.StoredContext(cardNode).Presentation == EffectPresentation.Hand
            ? new EffectContext(EffectPresentation.Hand)
            : new EffectContext(EffectPresentation.Unknown);

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
                    GD.Print($"[FullArtSpire] Replacing portrait for {cardId}");
                if (PortraitField?.GetValue(cardNode) is TextureRect portrait)
                    ShowFullArt(portrait, texture);
                if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancientPortrait)
                {
                    ShowFullArt(ancientPortrait, texture);
                    ancientPortrait.Visible = true;
                }
            }
            else if (!HasFullArtEntry(cardId))
            {
                RestoreNativeLayout(cardNode);
            }
        }

        CardEffectPatch.ApplyOrUpdate(cardNode, context);
    }

    private static void ShowFullArt(TextureRect portrait, Texture2D texture)
    {
        if (!portrait.HasMeta(NativeStretchMeta))
        {
            portrait.SetMeta(NativeStretchMeta, (int)portrait.StretchMode);
            portrait.SetMeta(NativeFilterMeta, (int)portrait.TextureFilter);
        }
        portrait.Texture = texture;
        portrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        portrait.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
    }

    // A pooled node rebound to a card without art, or a card whose pack was
    // switched off. Reload has already put the native texture back.
    private static void RestoreNativeLayout(NCard cardNode)
    {
        foreach (TextureRect portrait in PortraitResolver.All(cardNode))
        {
            if (!portrait.HasMeta(NativeStretchMeta)) continue;
            portrait.StretchMode = (TextureRect.StretchModeEnum)portrait.GetMeta(NativeStretchMeta).AsInt32();
            portrait.TextureFilter = (CanvasItem.TextureFilterEnum)portrait.GetMeta(NativeFilterMeta).AsInt32();
            portrait.RemoveMeta(NativeStretchMeta);
            portrait.RemoveMeta(NativeFilterMeta);
        }
    }

    private static bool HasFullArtEntry(string cardId)
        => FullArtPackRegistry.TryGetCard(cardId, out FullArtCardEntry? entry) && entry is { PreserveOriginalLayout: false };

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

    internal static CardRarity ReadRealRarity(CardModel model)
    {
        bool reloading = _spoofForCardReload;
        bool filtering = _treatMappedBasicCardsAsCommonInLibrary;
        try
        {
            _spoofForCardReload = false;
            _treatMappedBasicCardsAsCommonInLibrary = false;
            return model.Rarity;
        }
        finally
        {
            _spoofForCardReload = reloading;
            _treatMappedBasicCardsAsCommonInLibrary = filtering;
        }
    }

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

// Vanilla STS2 has no Ancient-rarity Quest cards, so its ancient_text_bg_quest
// sprite is a placeholder reading "NOPE". Status and Curse already fall back to
// the Skill text backing in CardModel.AncientTextBgPath; do the same for Quest
// cards while we temporarily spoof their rarity for the full-art layout.
[HarmonyPatch(typeof(CardModel), "AncientTextBg", MethodType.Getter)]
internal static class QuestAncientTextBgPatch
{
    private const string SkillAncientTextBg = "atlases/compressed_atlas.sprites/ancient_text_bg_skill.tres";

    internal static bool ShouldUseSkillFallback(bool spoofing, CardType type, CardRarity realRarity)
        => spoofing && type == CardType.Quest && realRarity != CardRarity.Ancient;

    [HarmonyPrefix]
    private static bool Prefix(CardModel __instance, ref Texture2D __result)
    {
        if (!ShouldUseSkillFallback(CardRenderRaritySpoofPatch.IsSpoofingForCardReload, __instance.Type,
            CardRenderRaritySpoofPatch.ReadRealRarity(__instance)))
        {
            return true;
        }

        Texture2D? fallback = ResourceLoader.Load<Texture2D>(
            ImageHelper.GetImagePath(SkillAncientTextBg), null, ResourceLoader.CacheMode.Reuse);
        if (fallback is null)
        {
            return true;
        }

        __result = fallback;
        return false;
    }
}
