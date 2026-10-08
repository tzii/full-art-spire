using BaseLib.Config;

namespace FullArtCardPacks;

public class FullArtModConfig : SimpleModConfig
{
    public static bool EnableLayeredCards { get; set; } = true;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float LayerDepth { get; set; } = 1f;

    // How far the character follows the pointer; zero keeps it fixed.
    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float PortraitParallax { get; set; } = 0.5f;

    public static bool EnableEffects { get; set; } = false;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float EffectIntensity { get; set; } = 0.2f;

    public static bool RareFirstEffects { get; set; } = true;

    public static FinishAssignment FinishAssignment { get; set; } = FinishAssignment.PackDefault;
    public static TrialFinish SingleFinish { get; set; } = TrialFinish.Foil;
    public static bool EffectFollowPointer { get; set; } = true;

    [ConfigSlider(0, 999, 1, Format = "{0:F0}")]
    public static int FinishSeed { get; set; } = 0;

    public static bool ReducedMotion { get; set; } = false;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float GridIntensity { get; set; } = 0.25f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float HandIntensity { get; set; } = 0.5f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float RewardIntensity { get; set; } = 0.75f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float FocusedIntensity { get; set; } = 1f;

    [ConfigSlider(0, 0.25, 0.01, Format = "{0:F2}")]
    public static float GridMotion { get; set; } = 0.05f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float FocusedMotion { get; set; } = 1f;
}
