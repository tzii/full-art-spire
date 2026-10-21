using FullArtCardPacks;

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

static void Near(float expected, float actual, float tolerance, string message)
{
    if (MathF.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

try
{
    FullArtCardPacks.Tests.LruCacheTests.Run();
    FullArtCardPacks.Tests.BackgroundLoaderTests.Run();
    LayerPolicyTests.Run();
    RaritySpoofTests.Run();
    Equal(EffectSurface.Shimmer, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.ByRarity, TrialFinish.Foil, "Common", "card", 0), "common receives satin");
    Equal(EffectSurface.Foil, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.ByRarity, TrialFinish.Foil, "Uncommon", "card", 0), "uncommon receives foil");
    Equal(EffectSurface.Prismatic, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.ByRarity, TrialFinish.Foil, "Rare", "card", 0), "rare receives prismatic");
    Equal(EffectSurface.Holo, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.ByRarity, TrialFinish.Foil, "Ancient", "card", 0), "special rarity preserves authored finish");
    Equal(EffectSurface.None, EffectFinishSelector.Select(EffectSurface.None, FinishAssignment.SingleFinish, TrialFinish.Ember, "Rare", "card", 0), "override cannot enable disabled art");
    Equal(EffectSurface.Ember, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.SingleFinish, TrialFinish.Ember, "Rare", "card", 0), "single finish override");
    Equal(EffectSurface.Holo, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.PackDefault, TrialFinish.Ember, "Rare", "card", 0), "authored finish preserved");
    var randomFinishes = new HashSet<EffectSurface>();
    for (int seed = 0; seed < 100; seed++)
    {
        var chosen = EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.StableRandom, TrialFinish.Foil, "Rare", "card", seed);
        Equal(chosen, EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.StableRandom, TrialFinish.Foil, "Common", "card", seed), "random is stable independent of view or rarity");
        if ((int)chosen < 1 || (int)chosen > 7) throw new Exception("random finish out of range");
        randomFinishes.Add(chosen);
    }
    Equal(7, randomFinishes.Count, "seeds explore all finishes");
    var experimentalFinishes = new HashSet<EffectSurface>();
    for (int seed = 0; seed < 256; seed++)
    {
        var chosen = EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.ExperimentalMix,
            TrialFinish.Foil, "Rare", "card", seed);
        Equal(chosen, EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.ExperimentalMix,
            TrialFinish.Ember, "Common", "card", seed), "experimental mix is deterministic");
        if ((int)chosen < 8 || (int)chosen > 18) throw new Exception("experimental finish out of range");
        experimentalFinishes.Add(chosen);
    }
    Equal(11, experimentalFinishes.Count, "experimental seeds cover every new material");
    foreach (TrialFinish finish in Enum.GetValues<TrialFinish>())
    {
        Equal((EffectSurface)finish, EffectFinishSelector.Select(EffectSurface.Holo,
            FinishAssignment.SingleFinish, finish, "Rare", "card", 0), "all selectable finishes route");
        Equal(EffectSurface.None, EffectFinishSelector.Select(EffectSurface.None,
            FinishAssignment.SingleFinish, finish, "Rare", "card", 0), "new finishes cannot bypass permission");
    }
    Equal(EffectSurface.None, EffectSurfaceParser.Parse("DiffractiveFoil"), "experiments stay opt-in runtime overrides");
    Near(0.5f, new EffectContext(EffectPresentation.Grid).IntensityScale, 0.0001f, "grid intensity is independent");
    Near(0.8f, new EffectContext(EffectPresentation.Hand).IntensityScale, 0.0001f, "hand intensity is independent");
    FullArtModConfig.HandIntensity = 0.5f;
    FullArtModConfig.FocusedIntensity = 1f;
    Near(0.5f, EffectContext.HandIntensityAt(0f), 0.0001f, "resting hand cards use the hand strength");
    Near(0.75f, EffectContext.HandIntensityAt(0.5f), 0.0001f, "hovered hand cards ease toward focus");
    Near(1f, EffectContext.HandIntensityAt(1f), 0.0001f, "hovered, dragged and played cards reach focus strength");
    FullArtModConfig.HandIntensity = 0.8f;
    Near(0.75f, new EffectContext(EffectPresentation.Reward).IntensityScale, 0.0001f, "reward intensity is independent");
    Near(1f, new EffectContext(EffectPresentation.Focused).IntensityScale, 0.0001f, "focus intensity is independent");
    Near(0.5f, new EffectContext(EffectPresentation.Unknown).IntensityScale, 0.0001f, "unknown context uses the grid strength");
    Equal(FinishAssignment.RarityShowcase, FullArtModConfig.FinishAssignment, "rarity showcase is the default look");
    Equal(true, FullArtModConfig.EnableEffects, "effects are on by default");
    Equal(EffectSurface.Shimmer, EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.RarityShowcase, TrialFinish.Foil, "Common", "card", 0), "showcase commons get satin");
    Equal(EffectSurface.Holo, EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.RarityShowcase, TrialFinish.Foil, "Uncommon", "card", 0), "showcase uncommons get pearl holo");
    Equal(EffectSurface.Foil, EffectFinishSelector.Select(EffectSurface.Holo, FinishAssignment.RarityShowcase, TrialFinish.Holo, "Rare", "card", 0), "showcase rares get gold foil");
    Equal(EffectSurface.Prismatic, EffectFinishSelector.Select(EffectSurface.Foil, FinishAssignment.RarityShowcase, TrialFinish.Foil, "Ancient", "card", 0), "showcase ancients get prismatic");
    Equal(EffectSurface.None, EffectFinishSelector.Select(EffectSurface.None, FinishAssignment.RarityShowcase, TrialFinish.Foil, "Ancient", "card", 0), "showcase cannot enable art without a finish mask");
    string[] ladder = { "Basic", "Common", "Uncommon", "Rare", "Ancient" };
    for (int step = 1; step < ladder.Length; step++)
    {
        ShowcaseTier lower = RarityShowcase.For(ladder[step - 1]), higher = RarityShowcase.For(ladder[step]);
        if (higher.Strength < lower.Strength || higher.Sparkle < lower.Sparkle || higher.Backdrop < lower.Backdrop
            || higher.Aura < lower.Aura || higher.AlphaCap < lower.AlphaCap)
            throw new InvalidOperationException($"{ladder[step]} must not look quieter than {ladder[step - 1]}");
    }
    foreach (string rarity in new[] { "None", "Basic", "Common", "Uncommon", "Rare", "Ancient", "Event", "Token", "Status", "Curse", "Quest", "Unknown" })
    {
        ShowcaseTier tier = RarityShowcase.For(rarity);
        if (tier.Surface == EffectSurface.None || tier.Strength is <= 0f or > 1f || tier.AlphaCap is <= 0f or > 0.5f)
            throw new InvalidOperationException($"{rarity} showcase tier is out of range");
    }
    Equal(0f, RarityShowcase.For("Common").Sparkle + RarityShowcase.For("Common").Backdrop + RarityShowcase.For("Common").Aura, "commons stay simple");
    Equal(EffectSurface.None, EffectSurfaceParser.Parse("Ember"), "experimental finishes do not expand pack approval contract");
    Equal(EffectSurface.Foil, EffectFinishSelector.Select(EffectSurface.Foil, (FinishAssignment)999, (TrialFinish)999, "Rare", "card", 0), "invalid settings preserve authored finish");
    Equal(EffectSurface.None, EffectSurfaceParser.Parse("unknown"), "unknown surfaces fail closed");
    Equal(EffectSurface.Holo, EffectSurfaceParser.Parse("HOLO"), "surface parsing is case-insensitive");
    Equal(3, EffectSurfaceParser.ShaderId(EffectSurface.Holo), "surface shader IDs are stable");
    Equal(EffectSurface.Prismatic, EffectSurfaceParser.Parse("rainbow"), "rainbow aliases to prismatic");
    Near(0.1f, EffectSettings.EffectiveIntensity(0.5f, 0.2f), 0.0001f, "intensities multiply");
    Near(0f, EffectSettings.EffectiveIntensity(-1f, 0.2f), 0.0001f, "negative intensity clamps");
    Near(1f, EffectSettings.EffectiveIntensity(2f, 2f), 0.0001f, "combined intensity clamps");
    float phaseA = EffectSettings.PhaseFor("MegaCrit.Sts2.Core.Models.Cards.Nightmare", null);
    float phaseB = EffectSettings.PhaseFor("MegaCrit.Sts2.Core.Models.Cards.Nightmare", null);
    float phaseC = EffectSettings.PhaseFor("MegaCrit.Sts2.Core.Models.Cards.WraithForm", null);
    Near(phaseA, phaseB, 0.000001f, "runtime phase is deterministic");
    if (MathF.Abs(phaseA - phaseC) < 0.000001f)
        throw new InvalidOperationException("different runtime models need different phases");
    Near(1.25f, EffectSettings.PhaseFor("ignored", 1.25f), 0.000001f, "explicit phase wins");
    int resourceWarnings = 0;
    object? failedResource = EffectRenderer.TryLoadResource<object>(
        "test resource",
        "corrupt.mask",
        () => throw new InvalidDataException("corrupt test resource"),
        _ => resourceWarnings++);
    Equal<object?>(null, failedResource, "resource failures return null");
    Equal(1, resourceWarnings, "resource failure warns once");
    Console.WriteLine("Effect contract and finish assignment tests passed");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
