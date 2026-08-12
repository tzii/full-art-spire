using BaseLib.Config;

namespace FullArtCardPacks;

public class FullArtModConfig : SimpleModConfig
{
    public static bool EnableEffects { get; set; } = false;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float EffectIntensity { get; set; } = 0.2f;

    public static bool RareFirstEffects { get; set; } = true;

    [ConfigSlider(0, 0.25, 0.01, Format = "{0:F2}")]
    public static float GridMotion { get; set; } = 0.05f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float FocusedMotion { get; set; } = 1f;
}
