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
    Console.WriteLine("Effect contract tests passed: 12 assertions");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
