using BaseLib.Config;

namespace FullArtCardPacks;

public class FullArtModConfig : SimpleModConfig
{
    public static bool EnableLayeredCards { get; set; } = true;

    // Decode card art off the main thread; the native portrait shows until it is ready.
    public static bool BackgroundTextureLoading { get; set; } = true;

    // Use verified DDS variants. Turning this off prefers original PNGs where a
    // package still ships them; release packages carry only the DDS copy.
    public static bool GpuCompressedTextures { get; set; } = true;

    // F8 records the hovered card and current visual settings for local QA.
    public static bool EnableCardReports { get; set; } = true;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float LayerDepth { get; set; } = 1f;

    // How far the character follows the pointer; zero keeps it fixed.
    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float PortraitParallax { get; set; } = 0.5f;

    // Shadow, rim light and background recession that separate the character.
    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float SubjectPop { get; set; } = 0.5f;

    public static bool EnableEffects { get; set; } = true;

    // Finishes on the reviewed depth subject of every layered card.
    public static bool LayerSubjectFinishes { get; set; } = true;

    // 1.0 is the designed look; lower values fade every finish together.
    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float EffectIntensity { get; set; } = 1f;

    // Applies to PackDefault only; the rarity showcase already tiers every card.
    public static bool RareFirstEffects { get; set; } = true;

    // Subtle satin on commons, escalating to prismatic glints and an aura on the rarest.
    public static FinishAssignment FinishAssignment { get; set; } = FinishAssignment.RarityShowcase;
    public static TrialFinish SingleFinish { get; set; } = TrialFinish.Foil;
    public static bool EffectFollowPointer { get; set; } = true;

    [ConfigSlider(0, 999, 1, Format = "{0:F0}")]
    public static int FinishSeed { get; set; } = 0;

    public static bool ReducedMotion { get; set; } = false;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float GridIntensity { get; set; } = 0.5f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float HandIntensity { get; set; } = 0.8f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float RewardIntensity { get; set; } = 0.75f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float FocusedIntensity { get; set; } = 1f;

    [ConfigSlider(0, 0.25, 0.01, Format = "{0:F2}")]
    public static float GridMotion { get; set; } = 0.05f;

    [ConfigSlider(0, 1, 0.05, Format = "{0:F2}")]
    public static float FocusedMotion { get; set; } = 1f;
}
