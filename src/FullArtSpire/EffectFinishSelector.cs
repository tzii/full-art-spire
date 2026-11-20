using System.Text;

namespace FullArtSpire;

// Saved settings store names; new values are appended. RarityShowcase is the default.
public enum FinishAssignment { PackDefault, ByRarity, StableRandom, SingleFinish, ExperimentalMix, RarityShowcase }

// Values match shader IDs; existing pack IDs must remain stable.
public enum TrialFinish
{
    Shimmer = 1, Foil = 2, Holo = 3, Prismatic = 4, BrushedSilver = 5, Ember = 6, Frost = 7,
    DiffractiveFoil = 8, EtchedFoil = 9, GlitterFoil = 10, CrackedIce = 11,
    OilSlick = 12, Pearlescent = 13, Embossed = 14, Lenticular = 15,
    Aurora = 16, Starfield = 17, Circuit = 18,
}

internal static class EffectFinishSelector
{
    internal static EffectSurface Select(EffectSurface authored, FinishAssignment assignment,
        TrialFinish single, string rarity, string runtimeModel, int seed)
    {
        if (authored == EffectSurface.None || !Enum.IsDefined(authored)) return EffectSurface.None;
        return assignment switch
        {
            FinishAssignment.RarityShowcase => RarityShowcase.For(rarity).Surface,
            FinishAssignment.SingleFinish => Enum.IsDefined(single) ? (EffectSurface)single : authored,
            FinishAssignment.ByRarity => rarity switch
            {
                "Basic" or "Common" => EffectSurface.Shimmer,
                "Uncommon" => EffectSurface.Foil,
                "Rare" => EffectSurface.Prismatic,
                _ => authored,
            },
            FinishAssignment.StableRandom => RandomFinish(runtimeModel, seed),
            FinishAssignment.ExperimentalMix => RandomFinish(runtimeModel, seed, true),
            _ => authored,
        };
    }

    private static EffectSurface RandomFinish(string runtimeModel, int seed, bool experimental = false)
    {
        uint hash = 2166136261;
        foreach (byte value in Encoding.UTF8.GetBytes(runtimeModel))
            hash = unchecked((hash ^ value) * 16777619);
        // Mix integer bytes explicitly so results agree across platforms/cultures.
        for (int shift = 0; shift < 32; shift += 8)
            hash = unchecked((hash ^ (byte)((uint)seed >> shift)) * 16777619);
        // Preserve the original seven-finish assignment for existing settings.
        return (EffectSurface)(experimental ? 8 + hash % 11 : 1 + hash % 7);
    }
}
