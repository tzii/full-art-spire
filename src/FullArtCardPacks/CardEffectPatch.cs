using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class CardEffectPatch
{
    internal static void ApplyOrUpdate(NCard cardNode, EffectContext context)
    {
        LayeredCardRenderer.ApplyOrUpdate(cardNode);
        EffectOverlayManager.ApplyOrUpdate(cardNode, context);
    }
}
