namespace FullArtSpire;

// Names that are part of the mod's public footprint: the game loads <ModId>.dll,
// BaseLib names its settings file after the root namespace, and the legacy names
// are what 0.3.x installs left behind.
internal static class ModIdentity
{
    internal const string ModId = "full_art_spire";
    internal const string DisplayName = "Full Art Spire";
    internal const string LogPrefix = "[FullArtSpire]";

    // BaseLib stores settings at user://mod_configs/<root namespace>.cfg.
    internal const string BaseLibConfigFileName = "FullArtSpire.cfg";

    // Our folder under user://, for reports and migrated state.
    internal const string UserDataFolder = "full_art_spire";

    internal const string LegacyModId = "full_art_card_packs";
    internal const string LegacyDisplayName = "Full Art Card Packs";
    // 0.3.x read its pack allow-list from here; 0.4.0 has per-pack switches instead.
    internal const string LegacyPackConfigFileName = "full_art_card_packs.cfg";
    internal const string LegacyBaseLibConfigFileName = "FullArtCardPacks.cfg";
}
