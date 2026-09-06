using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace FullArtCardPacks;

internal readonly record struct EffectDecision(
    bool Enabled,
    EffectSurface Surface,
    float Intensity,
    float Phase,
    string PermissionMask);

internal static class EffectPolicy
{
    private static readonly EffectDecision Disabled = new(false, EffectSurface.None, 0f, 0f, "");

    internal static EffectDecision Decide(CardModel model, FullArtCardEntry entry)
    {
        if (!FullArtModConfig.EnableEffects
            || entry.Effects is not { } effects
            || CardRenderRaritySpoofPatch.IsSpoofingForCardReload)
        {
            return Disabled;
        }

        EffectSurface surface = EffectSurfaceParser.Parse(effects.Surface);
        if (surface == EffectSurface.None)
        {
            return Disabled;
        }

        if (string.Equals(effects.RarityPolicy, "rare_first", StringComparison.OrdinalIgnoreCase)
            && FullArtModConfig.RareFirstEffects
            && model.Rarity != CardRarity.Rare)
        {
            return Disabled;
        }

        string permissionMask = effects.PermissionMask.Trim();
        if (permissionMask.Length == 0)
        {
            return Disabled;
        }

        float intensity = EffectSettings.EffectiveIntensity(effects.Intensity, FullArtModConfig.EffectIntensity);
        surface = EffectFinishSelector.Select(surface, FullArtModConfig.FinishAssignment,
            FullArtModConfig.SingleFinish, model.Rarity.ToString(), entry.RuntimeModel, FullArtModConfig.FinishSeed);
        float phase = EffectSettings.PhaseFor(entry.RuntimeModel, effects.PhaseSeed);
        return new(intensity > 0f, surface, intensity, phase, permissionMask);
    }
}
