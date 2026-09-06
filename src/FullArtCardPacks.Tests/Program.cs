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
    Near(0.25f, new EffectContext(EffectPresentation.Grid).IntensityScale, 0.0001f, "grid intensity is independent");
    Near(0.5f, new EffectContext(EffectPresentation.Hand).IntensityScale, 0.0001f, "hand intensity is independent");
    Near(0.75f, new EffectContext(EffectPresentation.Reward).IntensityScale, 0.0001f, "reward intensity is independent");
    Near(1f, new EffectContext(EffectPresentation.Focused).IntensityScale, 0.0001f, "focus intensity is independent");
    Near(0.25f, new EffectContext(EffectPresentation.Unknown).IntensityScale, 0.0001f, "unknown context stays conservative");
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
