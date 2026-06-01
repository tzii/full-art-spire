using MegaCrit.Sts2.Core.Models;

namespace FullArtCardPacks;

internal static class EffectPolicy
{
    internal static bool ShouldApplyEffect(CardModel model, FullArtCardEntry entry)
    {
        if (!FullArtPackRegistry.Config.EnableEffects)
        {
            return false;
        }

        if (!FullArtPackRegistry.Config.RareFirstEffects)
        {
            return !string.Equals(entry.Effects.Surface, "none", StringComparison.OrdinalIgnoreCase);
        }

        return model.Rarity == CardRarity.Rare
            && !string.Equals(entry.Effects.Surface, "none", StringComparison.OrdinalIgnoreCase);
    }
}
