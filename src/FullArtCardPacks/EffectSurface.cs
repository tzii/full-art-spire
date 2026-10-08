namespace FullArtCardPacks;

internal enum EffectSurface
{
    None = 0,
    Shimmer = 1,
    Foil = 2,
    Holo = 3,
    Prismatic = 4,
    BrushedSilver = 5,
    Ember = 6,
    Frost = 7,
    DiffractiveFoil = 8,
    EtchedFoil = 9,
    GlitterFoil = 10,
    CrackedIce = 11,
    OilSlick = 12,
    Pearlescent = 13,
    Embossed = 14,
    Lenticular = 15,
    Aurora = 16,
    Starfield = 17,
    Circuit = 18,
}

internal static class EffectSurfaceParser
{
    internal static EffectSurface Parse(string? value)
    {
        string normalized = value?.Trim() ?? "";
        if (string.Equals(normalized, "rainbow", StringComparison.OrdinalIgnoreCase))
        {
            return EffectSurface.Prismatic;
        }

        return Enum.TryParse(normalized, true, out EffectSurface parsed)
            && parsed is >= EffectSurface.None and <= EffectSurface.Prismatic
            ? parsed
            : EffectSurface.None;
    }

    internal static int ShaderId(EffectSurface surface) => (int)surface;
}
