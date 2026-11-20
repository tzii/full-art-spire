using FullArtSpire;
using MegaCrit.Sts2.Core.Entities.Cards;

internal static class RaritySpoofTests
{
    internal static void Run()
    {
        // Compendium regression: the grid reloads card nodes inside FilterCards.
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Basic, true, true, true) == CardRarity.Ancient,
            "a mapped Basic card reloaded during Compendium filtering gets the full-art layout");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Rare, true, true, true) == CardRarity.Ancient,
            "a mapped Rare card reloaded during filtering gets the full-art layout");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Basic, true, false, true) == CardRarity.Ancient,
            "a mapped Basic card reloaded elsewhere gets the full-art layout");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Basic, false, true, true) == CardRarity.Common,
            "Compendium filtering still sorts mapped Basic cards with Common");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Uncommon, false, true, true) == CardRarity.Uncommon,
            "Compendium filtering leaves other rarities alone");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Basic, true, true, false) == CardRarity.Basic,
            "unmapped cards keep their real rarity");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Basic, false, false, true) == CardRarity.Basic,
            "gameplay outside both scopes sees the real rarity");
        Require(CardRenderRaritySpoofPatch.Decide(CardRarity.Ancient, false, true, true) == CardRarity.Ancient,
            "real Ancient cards stay Ancient");
        Console.WriteLine("Full-art rarity spoof tests passed");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
