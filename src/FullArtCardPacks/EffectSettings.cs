using System.Text;

namespace FullArtCardPacks;

internal static class EffectSettings
{
    private const float Tau = MathF.PI * 2f;

    internal static float EffectiveIntensity(float perCard, float global)
    {
        return Math.Clamp(Math.Clamp(perCard, 0f, 1f) * Math.Clamp(global, 0f, 1f), 0f, 1f);
    }

    internal static float PhaseFor(string runtimeModel, float? explicitSeed)
    {
        if (explicitSeed is { } seed)
        {
            return seed;
        }

        uint hash = 2166136261;
        foreach (byte value in Encoding.UTF8.GetBytes(runtimeModel))
        {
            hash ^= value;
            hash *= 16777619;
        }
        return (hash & 0x00ffffff) / 16777215f * Tau;
    }
}
