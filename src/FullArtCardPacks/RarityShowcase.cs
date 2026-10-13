namespace FullArtCardPacks;

// One escalating look per true rarity: silver satin on starters and commons,
// pearl holo on uncommons, gold foil with glints and an aura on rares, and full
// prismatic with a rainbow aura and the densest sparkle on Ancients.
internal readonly record struct ShowcaseTier(
    EffectSurface Surface,
    float Strength,
    float Sparkle,
    float Backdrop,
    float Aura,
    float AuraSpectral,
    float Red,
    float Green,
    float Blue,
    float AlphaCap);

internal static class RarityShowcase
{
    internal const float DefaultAlphaCap = 0.32f;

    private static readonly ShowcaseTier Starter = new(EffectSurface.Shimmer, 0.40f, 0f, 0f, 0f, 0f, 0.80f, 0.86f, 0.95f, 0.24f);
    private static readonly ShowcaseTier Common = new(EffectSurface.Shimmer, 0.55f, 0f, 0f, 0f, 0f, 0.80f, 0.86f, 0.95f, 0.26f);
    private static readonly ShowcaseTier Uncommon = new(EffectSurface.Holo, 0.75f, 0.18f, 0f, 0f, 0f, 0.70f, 0.92f, 1.00f, 0.30f);
    private static readonly ShowcaseTier Rare = new(EffectSurface.Foil, 0.95f, 0.75f, 0.50f, 0.45f, 0.15f, 1.00f, 0.80f, 0.42f, 0.38f);
    private static readonly ShowcaseTier Ancient = new(EffectSurface.Prismatic, 1.00f, 1.00f, 0.80f, 0.75f, 1.00f, 1.00f, 0.90f, 0.70f, 0.44f);
    private static readonly ShowcaseTier Event = new(EffectSurface.Pearlescent, 0.70f, 0.20f, 0.15f, 0f, 0f, 0.60f, 1.00f, 0.80f, 0.30f);
    private static readonly ShowcaseTier Quest = new(EffectSurface.Aurora, 0.75f, 0.30f, 0.20f, 0.25f, 0f, 1.00f, 0.75f, 0.40f, 0.32f);
    private static readonly ShowcaseTier Token = new(EffectSurface.Shimmer, 0.40f, 0f, 0f, 0f, 0f, 0.85f, 0.90f, 1.00f, 0.24f);
    private static readonly ShowcaseTier Status = new(EffectSurface.Frost, 0.35f, 0f, 0f, 0f, 0f, 0.75f, 0.85f, 0.90f, 0.22f);
    private static readonly ShowcaseTier Curse = new(EffectSurface.OilSlick, 0.35f, 0f, 0f, 0.30f, 0f, 0.70f, 0.35f, 1.00f, 0.22f);

    // Rarity is passed by name so the table stays testable without the game.
    internal static ShowcaseTier For(string rarity) => rarity switch
    {
        "Basic" => Starter,
        "Common" => Common,
        "Uncommon" => Uncommon,
        "Rare" => Rare,
        "Ancient" => Ancient,
        "Event" => Event,
        "Quest" => Quest,
        "Token" => Token,
        "Status" => Status,
        "Curse" => Curse,
        _ => Starter,
    };
}
