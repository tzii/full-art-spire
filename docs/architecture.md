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
  `user://full_art_spire/legacy-pack-selection.json`. On the first active
  launch those pools are switched off as `megacrit.<pool>` and
  `LegacyPackSelectionImported` is set, so this happens once. Packs the old
  mod never had stay on. The old global
  `"enabled": false` is recorded but not carried over. A player who deletes the
  old folder before ever starting 0.4.0 keeps every pack on.
- **Settings:** before BaseLib reads its settings, `user://mod_configs/
  FullArtCardPacks.cfg` is copied to `FullArtSpire.cfg` if the latter does not
  exist yet. Property names are unchanged, so BaseLib reads the copy as its
  own. The old file is left in place.

## Pack loading — `PackDiscovery`, `FullArtPackRegistry`

- `PackDiscovery.Discover` finds v1 and v2 packs in this mod's `packs/`, in
  every other loaded (or still to load) mod's `full_art/`, and in the player's
  `user://full_art_spire/packs/` (see `pack-format.md`). It checks `requires`
  and `min_game_version`, and drops duplicate ids. It makes no Godot calls, so
  it runs in unit tests.
- `FullArtPackRegistry.Plan` applies the packs that are on, in conflict order,
  and indexes cards by `runtime_model` (case-insensitive).
- **Path roots:** card paths stay relative. Each pack gets a root index (0 is
  this mod's folder, which v1 packs use), and its card paths are rewritten to
  `@<index>/<path>`. `TryResolveModPath` maps the prefix back to that root and
  refuses anything outside it; unprefixed paths (shaders, the GPU index)
  resolve in the mod folder. Every manifest path is prefixed, so a manifest
  cannot forge a prefix into another pack's root. `PackPaths` holds this logic.
- `GpuTextureVariants` reads a `gpu-textures.audit` from each pack root.
- `HasReplacement(model)` is true when the entry exists, is not
  `preserve_original_layout`, and its art file (or a GPU variant) exists.
- Mods later in the load order have not loaded when this one initialises, so
  their packs are found from the game's mod list before those mods load.

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

Pack switches: `FullArtModConfig` overrides `SetupConfigUI` to add a **Card
packs** section after the generated options. Each switch is BaseLib's own
`NConfigTickbox` bound to a `PackToggleProperty`, a synthetic bool
`PropertyInfo` that reads and writes the hidden `DisabledPacks` setting
(comma-separated ids). BaseLib saves it on change.

Switches apply while the game runs. On BaseLib's `ConfigChanged` (a tickbox)
or `OnConfigReloaded` (Restore defaults), `FullArtPackRegistry.ApplySwitches`
re-plans the card table from the packs found at startup, without reading disk,
and bumps `Generation`. Pack roots are numbered from every usable pack, on or
off, so path prefixes, cached textures, layer verdicts and GPU variants stay
valid. `LiveCardRefresh` then calls the private `NCard.Reload()` on every card
node in the scene tree shown under an older generation: Reload rebuilds the
frame from the (spoofed) rarity and resets the native portrait, and the Reload
postfix applies the new art, layers and finish. Nodes outside the tree catch up
in their `_EnterTree` or `UpdateVisuals` postfix. When a card loses its art,
the portrait's native stretch mode and filter (kept as metadata on the
`TextureRect`) are restored.

Per-frame work (layer pointer easing, finish tilt and dimming) passes cached
`ShaderNames` rather than strings: each string argument would allocate a new
`StringName` per call, per visible card, per frame.

## Diagnostics

`CardReports`: a hotkey appends the hovered card's model, pack id, pack entry, layers
and current settings to `user://full_art_spire/card-reports.jsonl`. It is
the natural base for an exporter of every loaded card (roadmap phase 2).

## Tests

`src/FullArtSpire.Tests` is a console runner (`Program.cs`) covering the
background loader, legacy-mod migration, pack discovery and path roots,
game API shape (`GameApiTests`,
checks the patched members exist in the installed game), layer policy, LRU cache
and rarity spoofing.
Native rendering tests that need prepared artwork stay in the art repository.
