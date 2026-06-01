using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace FullArtCardPacks;

internal static class EffectPolicy
{
    internal static bool ShouldApplyEffect(CardModel model, FullArtCardEntry entry)
    {
        if (!FullArtModConfig.EnableEffects)
        {
            return false;
        }

        if (CardRenderRaritySpoofPatch.IsSpoofingForCardReload)
        {
            return false;
        }

        if (entry.Effects is not { } effects)
        {
            return false;
        }

        // Check if surface is disabled
        if (string.IsNullOrWhiteSpace(effects.Surface)
            || string.Equals(effects.Surface, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check rarity policy
        if (string.Equals(effects.RarityPolicy, "rare_first", StringComparison.OrdinalIgnoreCase))
        {
            if (FullArtModConfig.RareFirstEffects)
            {
                // Only apply to rare cards
                return model.Rarity == CardRarity.Rare;
            }
        }

        return true;
    }
}
