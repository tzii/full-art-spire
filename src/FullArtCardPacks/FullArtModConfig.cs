using BaseLib.Config;

namespace FullArtCardPacks;

public class FullArtModConfig : SimpleModConfig
{
    public static bool EnableEffects { get; set; } = true;

    [ConfigSlider(0, 1, 0.1, Format = "{0:F1}")]
    public static float EffectIntensity { get; set; } = 0.5f;

    public static bool RareFirstEffects { get; set; } = false;
}
