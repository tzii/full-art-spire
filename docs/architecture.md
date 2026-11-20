# Runtime architecture

State of the code in this repository: the 0.3.4 runtime, renamed to Full Art
Spire for 0.4.0.

## Startup

The game loads `<mod folder>/full_art_spire.dll` (the file name must match the
manifest id; the build produces `FullArtSpire.dll`, which packaging renames).

`MainFile.Initialize` (a `[ModInitializer]`) first checks for the old mod (see
Migration below). Then it registers the BaseLib settings (`FullArtModConfig`),
loads packs (`FullArtPackRegistry.Load`), starts the card report hotkey
(`CardReports`), initialises the effect renderer when effects are on, and
applies all Harmony patches under the id `full_art_spire`.

Fixed names (mod id, cfg and settings file names, user-data folder, and their
0.3.x counterparts) live in `ModIdentity`.

## Migration from 0.3.x — `LegacyMigration`

0.3.x was Full Art Card Packs (`full_art_card_packs`, assembly
`FullArtCardPacks`). Both mods patch the same cards.

- **Old mod present:** if the game's mod list (`ModManager.Mods`) has
  `full_art_card_packs` as loaded or still to load, this mod registers no
  settings and applies no patches. It logs a warning and queues a BaseLib
  main-menu popup asking the player to delete or disable the old folder.
  Disabled, failed or duplicate copies of the old mod do not count.
- **Pack choices:** on each such launch, the old folder's
  `full_art_card_packs.cfg` allow-list is turned into the packs that folder
  shipped but the player had not enabled, saved to
  `user://full_art_spire/legacy-pack-selection.json`. Once active, the registry
  keeps those packs off. Packs the old mod never had stay on. The old global
  `"enabled": false` is recorded but not carried over. A player who deletes the
  old folder before ever starting 0.4.0 keeps every pack on.
- **Settings:** before BaseLib reads its settings, `user://mod_configs/
  FullArtCardPacks.cfg` is copied to `FullArtSpire.cfg` if the latter does not
  exist yet. Property names are unchanged, so BaseLib reads the copy as its
  own. The old file is left in place.

## Pack loading — `FullArtPackRegistry`

- Reads `full_art_spire.cfg` (JSON) from the mod folder. `enabled_packs`
  is an explicit allow-list; a missing or unreadable cfg falls back to `silent`.
  Packs switched off in a 0.3.x install are then removed (see Migration).
- For each listed pool, reads `packs/<pool>.pack` and indexes cards by
  `runtime_model` (case-insensitive). A later pool overwrites an earlier one
  for the same model.
- All paths are relative and must resolve inside the mod folder.
- `HasReplacement(model)` is true when the entry exists, is not
  `preserve_original_layout`, and its art file (or a GPU variant) exists.

This is the main limit for other mods' characters: packs can only come from this
mod's own folder and must be named in its cfg. See `pack-format.md` for the v2
discovery design.

## Card hooks — `CardVisualPatches`

Harmony patches on the game's `NCard` (`UpdateVisuals`, `_EnterTree`, `Reload`,
`OnFreedToPool`) and on `CardModel.Rarity`. The card id is always
`Model.GetType().FullName`, so any card built on `CardModel`, including modded
ones, reaches the same code path. The rarity getter is briefly spoofed during
reloads and library filtering so mapped cards use the full-art frame.

`PortraitResolver` finds the portrait `TextureRect` (normal or ancient) and
skips effects inside known external viewports.

## Rendering

- `LayeredCardRenderer` + `Shaders/CardLayerShader.gdshader`: background plate,
  foreground cutout and mask with depth, parallax and subject lift, validated by
  `LayerPolicy` (registration, aspect checks; malformed cards fail flat).
- `EffectRenderer`, `EffectOverlayManager`, `EffectPolicy`,
  `EffectFinishSelector`, `RarityShowcase` + `CardEffectShader.gdshader`:
  rarity finishes (satin → prismatic), pointer follow, Reduced Motion.
- `TextureCache` (LRU), `BackgroundLoader`, `PendingArtRefresh`: texture loading
  off the main thread and refresh when it lands.
- `GpuTextureVariants`: optional `gpu-textures.audit` at the mod root binds
  pre-compressed DDS variants to source PNG hashes.

## Settings

All user settings are BaseLib `SimpleModConfig` properties in
`FullArtModConfig` (layers, depth, parallax, effects, finish assignment,
Reduced Motion, background loading, GPU textures, card reports).

## Diagnostics

`CardReports`: a hotkey appends the hovered card's model, pack entry, layers
and current settings to `user://full_art_spire/card-reports.jsonl`. It is
the natural base for an exporter of every loaded card (roadmap phase 2).

## Tests

`src/FullArtSpire.Tests` is a console runner (`Program.cs`) covering the
background loader, legacy-mod migration, game API shape (`GameApiTests`,
checks the patched members exist in the installed game), layer policy, LRU cache
and rarity spoofing.
Native rendering tests that need prepared artwork stay in the art repository.
