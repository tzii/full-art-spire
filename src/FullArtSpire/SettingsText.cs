using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;

namespace FullArtSpire;

// English text for the settings page. BaseLib reads labels, group titles, dropdown
// entries and hover tips from the game's settings_ui table, which mods normally
// fill from a .pck. This mod ships none, so the entries are merged into the loaded
// table instead, again whenever the game reloads its text (a language change).
// Every language shows the English text. SmartFormat reads these strings: no braces.
internal static class SettingsText
{
    internal const string Prefix = "FULLARTSPIRE-";
    internal const string PacksSection = "PacksSection";
    internal const string AdvancedSection = "AdvancedSection";
    internal const string PacksIntro = "Switch a pack off to show the game's own art for its cards. Changes apply right away.";

    private static readonly Dictionary<string, string> PackTips = new(StringComparer.Ordinal);
    private static bool _subscribed;

    internal static readonly IReadOnlyDictionary<string, string> Entries = Build();

    private static Dictionary<string, string> Build()
    {
        var e = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FULLARTSPIRE.mod_title"] = ModIdentity.DisplayName,
        };

        void Section(string name, string title) => e[Key(name) + ".title"] = title;
        void Option(string property, string title, string tip)
        {
            e[Key(property) + ".title"] = title;
            e[Key(property) + ".hover.desc"] = tip;
        }

        Section(PacksSection, "Card packs");
        Section("DepthSection", "Depth");
        Section("FinishesSection", "Finishes");
        Section("FinishStrengthSection", "Finish strength by screen");
        Section("MotionSection", "Motion");
        Section(AdvancedSection, "Advanced");

        Option(nameof(FullArtModConfig.EnableLayeredCards), "Depth layers",
            "Cards with a depth layer show the character in front of a separate background, which shifts as you move "
            + "the pointer over the card.\nOff: every card shows its full art flat.");
        Option(nameof(FullArtModConfig.LayerDepth), "Background depth",
            "How far the background shifts behind the character as the pointer moves. 0% keeps it still.");
        Option(nameof(FullArtModConfig.PortraitParallax), "Character follows pointer",
            "How far the character itself follows the pointer. 0% keeps it in place. Reduced motion turns this off.");
        Option(nameof(FullArtModConfig.SubjectPop), "Character pop",
            "A soft shadow, a rim light and a slightly receding background that lift the character off the card. "
            + "0% shows the art as painted.");

        Option(nameof(FullArtModConfig.EnableEffects), "Card finishes",
            "A sheen over the card art, like a foil trading card.\nOff: no finishes on any card.");
        Option(nameof(FullArtModConfig.FinishAssignment), "Finish style",
            "Which finish each card gets:\n"
            + "[b]Rarity showcase[/b]: satin on starters and commons, pearl holo on uncommons, gold foil with glints on "
            + "rares, prismatic on Ancients.\n"
            + "[b]By rarity[/b]: shimmer on commons, foil on uncommons, prismatic on rares.\n"
            + "[b]Pack default[/b]: the finish each pack chose.\n"
            + "[b]Random per card[/b]: one of seven classic finishes, always the same for a card.\n"
            + "[b]Experimental mix[/b]: the same, from eleven newer finishes.\n"
            + "[b]One finish for all[/b]: pick it below.");
        Option(nameof(FullArtModConfig.SingleFinish), "Finish for every card",
            "The finish every card gets with the style One finish for all.");
        Option(nameof(FullArtModConfig.FinishSeed), "Shuffle number",
            "Change this number to deal the random finishes out differently. The same number always gives the same cards "
            + "the same finishes.");
        Option(nameof(FullArtModConfig.RareFirstEffects), "Rare cards only",
            "Some packs ask for their finishes on rare cards only. Off: those finishes show on every rarity.");
        Option(nameof(FullArtModConfig.LayerSubjectFinishes), "Finishes on depth cards",
            "Lets cards with depth layers take a finish, on the character, even where their pack sets none.\n"
            + "Off: those cards show no finish.");
        Option(nameof(FullArtModConfig.EffectIntensity), "Overall strength",
            "Strength of every finish together. 100% is the designed look.");

        Option(nameof(FullArtModConfig.GridIntensity), "Card grids",
            "Finish strength on small cards: your deck, draw and discard piles, the Compendium and other card lists.");
        Option(nameof(FullArtModConfig.HandIntensity), "Hand",
            "Finish strength on cards resting in your hand.");
        Option(nameof(FullArtModConfig.FocusedIntensity), "Hovered and inspected",
            "Finish strength on the card you hover, drag or play, and on cards shown full size.");
        Option(nameof(FullArtModConfig.RewardIntensity), "Card rewards",
            "Finish strength on cards offered as rewards.");

        Option(nameof(FullArtModConfig.ReducedMotion), "Reduced motion",
            "Stills the finishes and stops the finishes and the character from following the pointer. "
            + "The background still shifts; set Background depth to 0% to stop that too.");
        Option(nameof(FullArtModConfig.EffectFollowPointer), "Light follows pointer",
            "The light in a finish moves with the pointer over the card.\nOff: the light stays centred.");
        Option(nameof(FullArtModConfig.GridMotion), "Finish motion in grids",
            "How much finishes move on small cards in grids. Glints on rares and Ancients keep a little movement so "
            + "they still read, unless this is 0%.");
        Option(nameof(FullArtModConfig.FocusedMotion), "Finish motion up close",
            "How much finishes move on cards in your hand, in rewards and when hovered or inspected.");

        Option(nameof(FullArtModConfig.BackgroundTextureLoading), "Load art in the background",
            "Loads card art without pausing the game, so screens full of cards open smoothly. The game's own art shows "
            + "for a moment until the full art is ready.\nOff: art loads at once, which can stutter.");
        Option(nameof(FullArtModConfig.GpuCompressedTextures), "Compressed textures",
            "Uses the GPU-compressed copies of the art: faster to load and lighter on video memory. Off only matters "
            + "for packs that also ship the original images.");
        Option(nameof(FullArtModConfig.EnableCardReports), "F8 card reports",
            "Press F8 while pointing at a card to save a report on its art and your settings, to attach to a bug "
            + "report. Reports go to full_art_spire/card-reports.jsonl in the game's user folder.");

        string assignment = Key(nameof(FullArtModConfig.FinishAssignment));
        e[$"{assignment}.{FinishAssignment.RarityShowcase}"] = "Rarity showcase";
        e[$"{assignment}.{FinishAssignment.ByRarity}"] = "By rarity";
        e[$"{assignment}.{FinishAssignment.PackDefault}"] = "Pack default";
        e[$"{assignment}.{FinishAssignment.StableRandom}"] = "Random per card";
        e[$"{assignment}.{FinishAssignment.ExperimentalMix}"] = "Experimental mix";
        e[$"{assignment}.{FinishAssignment.SingleFinish}"] = "One finish for all";

        string single = Key(nameof(FullArtModConfig.SingleFinish));
        foreach (TrialFinish finish in Enum.GetValues<TrialFinish>())
            e[$"{single}.{finish}"] = Words(finish.ToString());

        return e;
    }

    internal static string Key(string name) => Prefix + StringHelper.Slugify(name);

    // "BrushedSilver" -> "Brushed silver".
    internal static string Words(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c)) text.Append(' ').Append(char.ToLowerInvariant(c));
            else text.Append(c);
        }
        return text.ToString();
    }

    // Merges the text into the current table and keeps it there across reloads.
    internal static void Install()
    {
        try
        {
            if (LocManager.Instance is not { } manager) return;
            Merge(manager);
            if (!_subscribed)
            {
                manager.SubscribeToLocaleChange(() => Merge(LocManager.Instance));
                _subscribed = true;
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"{ModIdentity.LogPrefix} Settings text unavailable; the page shows setting names: {ex.Message}");
        }
    }

    private static void Merge(LocManager? manager)
    {
        if (manager is null) return;
        LocTable table = manager.GetTable("settings_ui");
        table.MergeWith(new Dictionary<string, string>(Entries));
        lock (PackTips) table.MergeWith(new Dictionary<string, string>(PackTips));
    }

    // A hover tip for one pack switch; returns the title and description keys.
    internal static (string? Title, string Description) AddPackTip(string modPrefix, DiscoveredPack pack)
    {
        string key = modPrefix + "PACK_" + StringHelper.Slugify(pack.Id.Replace('.', '_'));
        var tip = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [key + ".hover.title"] = Safe(FullArtModConfig.PackLabel(pack)),
            [key + ".hover.desc"] = Safe(PackDescription(pack)),
        };
        lock (PackTips)
        {
            foreach (var (k, v) in tip) PackTips[k] = v;
        }
        LocManager.Instance?.GetTable("settings_ui").MergeWith(tip);
        return (key + ".hover.title", key + ".hover.desc");
    }

    internal static string PackDescription(DiscoveredPack pack)
    {
        var lines = new List<string>
        {
            pack.Cards.Count == 1 ? "1 card." : $"{pack.Cards.Count} cards.",
        };
        if (pack.Source == PackSource.Mod && !string.IsNullOrWhiteSpace(pack.SourceModId))
            lines.Add($"Comes with the mod {pack.SourceModId}.");
        else if (pack.Source == PackSource.Player)
            lines.Add("Installed in your own packs folder.");
        if (pack.Authors.Count > 0)
            lines.Add("By " + string.Join(", ", pack.Authors) + ".");
        if (!string.IsNullOrWhiteSpace(pack.Credits))
            lines.Add(pack.Credits.Trim());
        return string.Join("\n", lines);
    }

    // Pack text is not ours: keep it from opening BBCode tags or SmartFormat fields.
    internal static string Safe(string text)
        => text.Replace("[", "[lb]").Replace("{", "(").Replace("}", ")");
}
