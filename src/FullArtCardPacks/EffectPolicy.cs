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
            || entry.Effects is not { } effects)
        {
            return Disabled;
        }

        EffectSurface surface = EffectSurfaceParser.Parse(effects.Surface);
        bool depthFinish = surface == EffectSurface.None
            && string.Equals(effects.Surface, "none", StringComparison.OrdinalIgnoreCase)
            && FullArtModConfig.LayerSubjectFinishes
            && entry.Layers is { FinishMask.Length: > 0 };
        if (depthFinish) surface = EffectSurface.Foil;
        if (surface == EffectSurface.None)
        {
            return Disabled;
        }

        // Cached Compendium reloads still need their finish. Read the true rarity
        // without allowing the temporary Ancient frame spoof into finish policy.
        CardRarity rarity = CardRenderRaritySpoofPatch.ReadRealRarity(model);
        if (string.Equals(effects.RarityPolicy, "rare_first", StringComparison.OrdinalIgnoreCase)
            && FullArtModConfig.RareFirstEffects
            && rarity != CardRarity.Rare)
        {
            return Disabled;
        }

        string permissionMask = depthFinish ? entry.Layers!.FinishMask.Trim() : effects.PermissionMask.Trim();
        if (permissionMask.Length == 0)
        {
            return Disabled;
        }

        float intensity = EffectSettings.EffectiveIntensity(depthFinish ? 0.12f : effects.Intensity, FullArtModConfig.EffectIntensity);
        surface = EffectFinishSelector.Select(surface, FullArtModConfig.FinishAssignment,
            FullArtModConfig.SingleFinish, rarity.ToString(), entry.RuntimeModel, FullArtModConfig.FinishSeed);
        float phase = EffectSettings.PhaseFor(entry.RuntimeModel, effects.PhaseSeed);
        return new(intensity > 0f, surface, intensity, phase, permissionMask);
    }
}
