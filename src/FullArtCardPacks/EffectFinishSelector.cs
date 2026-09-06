using System.Text;

namespace FullArtCardPacks;

public enum FinishAssignment { PackDefault, ByRarity, StableRandom, SingleFinish }

// Values match shader IDs; existing pack IDs must remain stable.
public enum TrialFinish { Shimmer = 1, Foil = 2, Holo = 3, Prismatic = 4, BrushedSilver = 5, Ember = 6, Frost = 7 }

internal static class EffectFinishSelector
{
    internal static EffectSurface Select(EffectSurface authored, FinishAssignment assignment,
        TrialFinish single, string rarity, string runtimeModel, int seed)
    {
        if (authored == EffectSurface.None || !Enum.IsDefined(authored)) return EffectSurface.None;
        return assignment switch
        {
            FinishAssignment.SingleFinish => Enum.IsDefined(single) ? (EffectSurface)single : authored,
            FinishAssignment.ByRarity => rarity switch
            {
                "Basic" or "Common" => EffectSurface.Shimmer,
                "Uncommon" => EffectSurface.Foil,
                "Rare" => EffectSurface.Prismatic,
                _ => authored,
            },
            FinishAssignment.StableRandom => RandomFinish(runtimeModel, seed),
            _ => authored,
        };
    }

    private static EffectSurface RandomFinish(string runtimeModel, int seed)
    {
        uint hash = 2166136261;
        foreach (byte value in Encoding.UTF8.GetBytes(runtimeModel))
            hash = unchecked((hash ^ value) * 16777619);
        // Mix integer bytes explicitly so results agree across platforms/cultures.
        for (int shift = 0; shift < 32; shift += 8)
            hash = unchecked((hash ^ (byte)((uint)seed >> shift)) * 16777619);
        return (EffectSurface)(1 + hash % 7);
    }
}
