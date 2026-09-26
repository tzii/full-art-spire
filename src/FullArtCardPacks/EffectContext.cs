using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Godot;

namespace FullArtCardPacks;

internal enum EffectPresentation
{
    Unknown,
    Grid,
    Hand,
    Reward,
    Focused,
}

internal readonly record struct EffectContext(EffectPresentation Presentation)
{
    internal static EffectContext From(NCard cardNode, PileType pileType, CardPreviewMode previewMode)
    {
        for (Node? ancestor = cardNode; ancestor is not null; ancestor = ancestor.GetParent())
        {
            string typeName = ancestor.GetType().FullName ?? ancestor.GetType().Name;
            if (ancestor.Name.ToString().Contains("Reward", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("Reward", StringComparison.OrdinalIgnoreCase))
            {
                return new(EffectPresentation.Reward);
            }
        }

        if (pileType == PileType.Hand)
        {
            return new(EffectPresentation.Hand);
        }

        if (previewMode is CardPreviewMode.Normal
            or CardPreviewMode.Upgrade
            or CardPreviewMode.MultiCreatureTargeting)
        {
            return new(EffectPresentation.Focused);
        }

        if (pileType is PileType.Draw
            or PileType.Discard
            or PileType.Exhaust
            or PileType.Play
            or PileType.Deck)
        {
            return new(EffectPresentation.Grid);
        }

        return new(EffectPresentation.Unknown);
    }

    internal float MotionScale => Presentation switch
    {
        EffectPresentation.Grid => Math.Clamp(FullArtModConfig.GridMotion, 0f, 0.25f),
        EffectPresentation.Hand or EffectPresentation.Reward or EffectPresentation.Focused
            => Math.Clamp(FullArtModConfig.FocusedMotion, 0f, 1f),
        _ => -1f,
    };

    internal float IntensityScale => Math.Clamp(Presentation switch
    {
        EffectPresentation.Hand => FullArtModConfig.HandIntensity,
        EffectPresentation.Reward => FullArtModConfig.RewardIntensity,
        EffectPresentation.Focused => FullArtModConfig.FocusedIntensity,
        _ => FullArtModConfig.GridIntensity,
    }, 0f, 1f);
}
