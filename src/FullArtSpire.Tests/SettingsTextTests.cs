using System.Reflection;
using BaseLib.Config;
using BaseLib.Extensions;
using FullArtSpire;

namespace FullArtSpire.Tests;

// The settings page reads every label and hover tip from SettingsText by key.
// A missing entry shows a raw property name in game, so check them here.
internal static class SettingsTextTests
{
    // Saved settings are keyed by these names; renaming one resets it for players.
    private static readonly string[] SavedNames =
    {
        "DisabledPacks", "LegacyPackSelectionImported", "EnableLayeredCards", "LayerDepth", "PortraitParallax",
        "SubjectPop", "EnableEffects", "FinishAssignment", "SingleFinish", "FinishSeed", "RareFirstEffects",
        "LayerSubjectFinishes", "EffectIntensity", "GridIntensity", "HandIntensity", "FocusedIntensity",
        "RewardIntensity", "ReducedMotion", "EffectFollowPointer", "GridMotion", "FocusedMotion",
        "BackgroundTextureLoading", "GpuCompressedTextures", "EnableCardReports",
    };

    internal static void Run()
    {
        Require(typeof(FullArtModConfig).GetPrefix() == SettingsText.Prefix,
            "SettingsText keys use BaseLib's prefix for the config class");
        Require(SettingsText.Entries.ContainsKey("FULLARTSPIRE.mod_title"), "the mod list has a title");

        PropertyInfo[] settings = typeof(FullArtModConfig).GetProperties(BindingFlags.Public | BindingFlags.Static);
        Require(settings.Select(p => p.Name).OrderBy(n => n).SequenceEqual(SavedNames.OrderBy(n => n)),
            "saved setting names are unchanged: " + string.Join(",", settings.Select(p => p.Name)));

        foreach (PropertyInfo property in settings)
        {
            bool hidden = property.GetCustomAttribute<ConfigHideInUI>() is not null;
            if (hidden && !FullArtModConfig.AdvancedSettings.Contains(property.Name)) continue;
            string key = SettingsText.Key(property.Name);
            Require(SettingsText.Entries.ContainsKey(key + ".title"), $"{property.Name} has a label");
            Require(SettingsText.Entries.ContainsKey(key + ".hover.desc"), $"{property.Name} has a hover tip");

            if (property.GetCustomAttribute<ConfigSectionAttribute>() is { } section)
                Require(SettingsText.Entries.ContainsKey(SettingsText.Key(section.Name) + ".title"),
                    $"section {section.Name} has a title");

            if (property.GetCustomAttribute<ConfigVisibleIfAttribute>() is { } visibleIf)
            {
                PropertyInfo? target = typeof(FullArtModConfig).GetProperty(visibleIf.TargetName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo? method = typeof(FullArtModConfig).GetMethod(visibleIf.TargetName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Require(target?.PropertyType == typeof(bool) || (method?.ReturnType == typeof(bool)
                        && method.GetParameters().Length == 0),
                    $"{property.Name} shows or hides on a bool setting or check");
            }

            if (property.PropertyType.IsEnum)
                foreach (object value in Enum.GetValues(property.PropertyType))
                    Require(SettingsText.Entries.ContainsKey($"{key}.{value}"), $"{property.Name}.{value} has a label");
        }

        foreach (string section in new[] { SettingsText.PacksSection, SettingsText.AdvancedSection })
            Require(SettingsText.Entries.ContainsKey(SettingsText.Key(section) + ".title"), $"{section} has a title");
        foreach (string name in FullArtModConfig.AdvancedSettings)
            Require(typeof(FullArtModConfig).GetProperty(name)?.GetCustomAttribute<ConfigHideInUI>() is not null,
                $"{name} is built only in the Advanced group, not twice");

        foreach (var (key, text) in SettingsText.Entries)
            Require(!text.Contains('{') && !text.Contains('}') && text.Trim().Length > 0,
                $"{key} is plain text SmartFormat can read");

        Require(SettingsText.Words("BrushedSilver") == "Brushed silver", "finish names read as words");
        Require(SettingsText.Safe("[b]{x}[/b]") == "[lb]b](x)[lb]/b]", "pack text cannot open tags or fields");

        var pack = new DiscoveredPack
        {
            Id = "a.pack", Name = "A", Source = PackSource.Mod, SourceModId = "other_mod", Root = "r", ManifestPath = "m",
            Authors = new[] { "someone" }, Credits = "Art (c) Mega Crit.",
        };
        string description = SettingsText.PackDescription(pack);
        Require(description == "0 cards.\nComes with the mod other_mod.\nBy someone.\nArt (c) Mega Crit.",
            "pack hover tip: " + description);
        Console.WriteLine("Settings page labels, hover tips, groups and saved names tests passed");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
