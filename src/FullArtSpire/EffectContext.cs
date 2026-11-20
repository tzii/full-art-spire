using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Godot;

namespace FullArtSpire;

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
        if (FromTree(cardNode) is { } placed)
        {
            return placed;
        }

        // A played card is shown on its own at full size before it resolves.
        if (pileType is PileType.Hand or PileType.Play)
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

    /// <summary>Context implied by where the card sits, for refreshes without a pile.</summary>
    internal static EffectContext? FromTree(NCard cardNode)
    {
        for (Node? ancestor = cardNode; ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is NHandCardHolder)
            {
                return new(EffectPresentation.Hand);
            }
            string typeName = ancestor.GetType().FullName ?? ancestor.GetType().Name;
            if (ancestor.Name.ToString().Contains("Reward", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("Reward", StringComparison.OrdinalIgnoreCase))
            {
                return new(EffectPresentation.Reward);
            }
        }
        return null;
    }

    // Hand cards rest at the hand strength and rise to the focused strength
    // while hovered, dragged, or drawn/played outside a resting hand slot.
    internal static float HandFocusTarget(NCard cardNode)
    {
        for (Node? ancestor = cardNode.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is NHandCardHolder holder)
            {
                // A dragged holder leaves the hand's slot container for the hand itself.
                return holder == NPlayerHand.Instance?.FocusedHolder || holder.GetParent() is NPlayerHand ? 1f : 0f;
            }
        }
        return 1f;
    }

    internal static float HandIntensityAt(float focus)
    {
        float resting = Math.Clamp(FullArtModConfig.HandIntensity, 0f, 1f);
        float focused = Math.Clamp(FullArtModConfig.FocusedIntensity, 0f, 1f);
        return resting + (focused - resting) * Math.Clamp(focus, 0f, 1f);
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
