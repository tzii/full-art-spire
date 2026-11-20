using BaseLib.Config;
using BaseLib.Extensions;
using Godot;

namespace FullArtSpire;

public class FullArtModConfig : SimpleModConfig
{
    // Ids of packs the player switched off, comma-separated and sorted. Packs are
    // on unless listed, so newly installed packs appear switched on. Edited
    // through the pack switches in SetupConfigUI.
    [ConfigHideInUI]
    public static string DisabledPacks { get; set; } = "";

    // Set once the packs a 0.3.x player had switched off have been copied into
    // DisabledPacks, so later launches respect the player's own switches.
    [ConfigHideInUI]
    [ConfigIgnoreRestoreDefaults]
    public static bool LegacyPackSelectionImported { get; set; } = false;

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

    private static string _parsedDisabledPacks = "";
    private static HashSet<string> _disabledPackIds = new(StringComparer.Ordinal);

    internal static ISet<string> DisabledPackIds()
    {
        string current = DisabledPacks ?? "";
        if (!ReferenceEquals(current, _parsedDisabledPacks))
        {
            _disabledPackIds = ParsePackIds(current);
            _parsedDisabledPacks = current;
        }

        return _disabledPackIds;
    }

    internal static HashSet<string> ParsePackIds(string? list)
    {
        return new HashSet<string>(
            (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(PackDiscovery.IsValidPackId),
            StringComparer.Ordinal);
    }

    internal static string FormatPackIds(IEnumerable<string> ids)
        => string.Join(",", ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));

    internal static void SetPackEnabled(string packId, bool enabled)
    {
        var ids = new HashSet<string>(DisabledPackIds(), StringComparer.Ordinal);
        if (enabled ? ids.Remove(packId) : ids.Add(packId))
        {
            DisabledPacks = FormatPackIds(ids);
        }
    }

    public override void SetupConfigUI(Control optionContainer)
    {
        GenerateOptionsForAllProperties(optionContainer);
        try
        {
            AddPackSection(optionContainer);
        }
        catch (Exception ex)
        {
            // The rest of the page still works without the pack switches.
            GD.PushWarning($"[FullArtSpire] Could not add the pack switches: {ex}");
        }

        AddRestoreDefaultsButton(optionContainer);
        SetupFocusNeighbors(optionContainer);
    }

    private void AddPackSection(Control optionContainer)
    {
        DiscoveredPack[] packs = FullArtPackRegistry.Packs
            .Where(pack => pack.SkipReason is null)
            .OrderBy(pack => pack.Source)
            .ThenBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packs.Length == 0)
        {
            return;
        }

        optionContainer.AddChild(CreateSectionHeader("Card packs"));
        optionContainer.AddChild(CreateRawLabelControl("Switching a pack on or off applies right away.", 24));
        for (int i = 0; i < packs.Length; i++)
        {
            if (i > 0)
            {
                optionContainer.AddChild(CreateDividerControl());
            }

            var property = new PackToggleProperty(packs[i].Id);
            AddPackRow(optionContainer, property, PackLabel(packs[i]).Replace("[", "[lb]"));
        }
    }

    private void AddPackRow(Control optionContainer, PackToggleProperty property, string label)
    {
        var tickbox = CreateRawTickboxControl(property);
        var row = new BaseLib.Config.UI.NConfigOptionRow(ModPrefix, property.Name, CreateRawLabelControl(label, 28), tickbox);
        tickbox.ClearFocusNeighbors();
        optionContainer.AddChild(row);
    }

    internal static string PackLabel(DiscoveredPack pack)
    {
        string name = pack.Version.Length == 0 ? pack.Name : $"{pack.Name} {pack.Version}";
        return pack.Source switch
        {
            PackSource.Mod => $"{name} (from {pack.SourceModId})",
            PackSource.Player => $"{name} (your pack)",
            _ => name,
        };
    }
}
