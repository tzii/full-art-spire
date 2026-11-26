using BaseLib.Config;
using BaseLib.Extensions;
using Godot;

namespace FullArtSpire;

[ConfigHoverTipsByDefault]
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

    // Rows appear in declaration order; [ConfigSection] starts a group. Labels,
    // group titles and hover tips are in SettingsText. Saved settings are keyed
    // by property name, so names stay fixed whatever the order.

    [ConfigSection("DepthSection")]
    public static bool EnableLayeredCards { get; set; } = true;

    [ConfigVisibleIf(nameof(EnableLayeredCards))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float LayerDepth { get; set; } = 1f;

    // How far the character follows the pointer; zero keeps it fixed.
    [ConfigVisibleIf(nameof(EnableLayeredCards))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float PortraitParallax { get; set; } = 0.5f;

    // Shadow, rim light and background recession that separate the character.
    [ConfigVisibleIf(nameof(EnableLayeredCards))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float SubjectPop { get; set; } = 0.5f;

    [ConfigSection("FinishesSection")]
    public static bool EnableEffects { get; set; } = true;

    // Subtle satin on commons, escalating to prismatic glints and an aura on the rarest.
    [ConfigVisibleIf(nameof(EnableEffects))]
    public static FinishAssignment FinishAssignment { get; set; } = FinishAssignment.RarityShowcase;

    [ConfigVisibleIf(nameof(ShowSingleFinish))]
    public static TrialFinish SingleFinish { get; set; } = TrialFinish.Foil;

    [ConfigVisibleIf(nameof(ShowFinishSeed))]
    [ConfigSlider(0, 999, 1, Format = "{0:F0}")]
    public static int FinishSeed { get; set; } = 0;

    // Applies to PackDefault only; the rarity showcase already tiers every card.
    [ConfigVisibleIf(nameof(ShowRareFirst))]
    public static bool RareFirstEffects { get; set; } = true;

    // Finishes on the reviewed depth subject of every layered card.
    [ConfigVisibleIf(nameof(EnableEffects))]
    public static bool LayerSubjectFinishes { get; set; } = true;

    // 1.0 is the designed look; lower values fade every finish together.
    [ConfigVisibleIf(nameof(EnableEffects))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float EffectIntensity { get; set; } = 1f;

    [ConfigSection("FinishStrengthSection")]
    [ConfigVisibleIf(nameof(EnableEffects))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float GridIntensity { get; set; } = 0.5f;

    [ConfigVisibleIf(nameof(EnableEffects))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float HandIntensity { get; set; } = 0.8f;

    [ConfigVisibleIf(nameof(EnableEffects))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float FocusedIntensity { get; set; } = 1f;

    [ConfigVisibleIf(nameof(EnableEffects))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float RewardIntensity { get; set; } = 0.75f;

    [ConfigSection("MotionSection")]
    public static bool ReducedMotion { get; set; } = false;

    [ConfigVisibleIf(nameof(ShowFinishMotion))]
    public static bool EffectFollowPointer { get; set; } = true;

    [ConfigVisibleIf(nameof(ShowFinishMotion))]
    [ConfigSlider(0, 0.25, 0.01, Format = "{0:P0}")]
    public static float GridMotion { get; set; } = 0.05f;

    [ConfigVisibleIf(nameof(ShowFinishMotion))]
    [ConfigSlider(0, 1, 0.05, Format = "{0:P0}")]
    public static float FocusedMotion { get; set; } = 1f;

    // Advanced: built by hand in a section that starts collapsed (SetupConfigUI).

    // Decode card art off the main thread; the native portrait shows until it is ready.
    [ConfigHideInUI]
    public static bool BackgroundTextureLoading { get; set; } = true;

    // Use verified DDS variants. Turning this off prefers original PNGs where a
    // package still ships them; release packages carry only the DDS copy.
    [ConfigHideInUI]
    public static bool GpuCompressedTextures { get; set; } = true;

    // F8 records the hovered card and current visual settings for local QA.
    [ConfigHideInUI]
    public static bool EnableCardReports { get; set; } = true;

    internal static readonly string[] AdvancedSettings =
        { nameof(BackgroundTextureLoading), nameof(GpuCompressedTextures), nameof(EnableCardReports) };

    private static bool ShowSingleFinish() => EnableEffects && FinishAssignment == FinishAssignment.SingleFinish;

    private static bool ShowFinishSeed() => EnableEffects
        && FinishAssignment is FinishAssignment.StableRandom or FinishAssignment.ExperimentalMix;

    private static bool ShowRareFirst() => EnableEffects && FinishAssignment == FinishAssignment.PackDefault;

    private static bool ShowFinishMotion() => EnableEffects && !ReducedMotion;

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
        // Labels and hover tips come from the game's settings_ui table.
        SettingsText.Install();
        AddVersionLine(optionContainer);
        try
        {
            AddPackSection(optionContainer);
        }
        catch (Exception ex)
        {
            // The rest of the page still works without the pack switches.
            GD.PushWarning($"[FullArtSpire] Could not add the pack switches: {ex}");
        }

        GenerateOptionsForAllProperties(optionContainer);
        AddAdvancedSection(optionContainer);
        AddRestoreDefaultsButton(optionContainer);
        SetupFocusNeighbors(optionContainer);
    }

    private void AddVersionLine(Control optionContainer)
    {
        var label = CreateRawLabelControl(VersionLine(), 24);
        label.Name = "VersionLine";
        // The card count follows the pack switches while the page is open.
        EventHandler update = (_, _) =>
        {
            if (GodotObject.IsInstanceValid(label)) label.Text = VersionLine();
        };
        ConfigChanged += update;
        label.TreeExiting += () => ConfigChanged -= update;
        optionContainer.AddChild(label);
    }

    internal static string VersionLine()
    {
        int packs = FullArtPackRegistry.Packs.Count(pack => pack.SkipReason is null && !DisabledPackIds().Contains(pack.Id));
        return $"Version {ModVersion.Current} · {FullArtPackRegistry.ActiveCardCount} cards with full art "
            + $"from {packs} {(packs == 1 ? "pack" : "packs")}";
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

        var section = CreateCollapsibleSection(SettingsText.PacksSection);
        optionContainer.AddChild(section);
        Control content = section.ContentContainer;
        content.AddChild(CreateRawLabelControl(SettingsText.PacksIntro, 24));
        for (int i = 0; i < packs.Length; i++)
        {
            if (i > 0)
            {
                content.AddChild(CreateDividerControl());
            }

            var property = new PackToggleProperty(packs[i].Id);
            AddPackRow(content, property, packs[i]);
        }
    }

    private void AddPackRow(Control container, PackToggleProperty property, DiscoveredPack pack)
    {
        var tickbox = CreateRawTickboxControl(property);
        string label = PackLabel(pack).Replace("[", "[lb]");
        var row = new BaseLib.Config.UI.NConfigOptionRow(ModPrefix, property.Name, CreateRawLabelControl(label, 28), tickbox);
        tickbox.ClearFocusNeighbors();
        try
        {
            var (title, description) = SettingsText.AddPackTip(ModPrefix, pack);
            row.AddCustomHoverTip(title, description);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[FullArtSpire] No hover tip for pack {pack.Id}: {ex.Message}");
        }
        container.AddChild(row);
    }

    // Rarely needed switches, in a group that starts collapsed.
    private void AddAdvancedSection(Control optionContainer)
    {
        var section = CreateCollapsibleSection(SettingsText.AdvancedSection, collapsedByDefault: true);
        optionContainer.AddChild(section);
        Control content = section.ContentContainer;
        for (int i = 0; i < AdvancedSettings.Length; i++)
        {
            if (i > 0)
            {
                content.AddChild(CreateDividerControl());
            }

            content.AddChild(GenerateOptionFromProperty(typeof(FullArtModConfig).GetProperty(AdvancedSettings[i])!));
        }
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
