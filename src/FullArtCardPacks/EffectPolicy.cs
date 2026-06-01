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

        if (CardRenderRaritySpoofPatch.IsSpoofingForCardReload)
        {
            return false;
        }

        if (entry.Effects is not { } effects
            || string.IsNullOrWhiteSpace(effects.Surface)
            || string.Equals(effects.Surface, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(effects.RarityPolicy, "rare_first", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!FullArtPackRegistry.Config.RareFirstEffects)
        {
            return true;
        }

        return model.Rarity == CardRarity.Rare
            && !string.Equals(effects.Surface, "none", StringComparison.OrdinalIgnoreCase);
    }
}
