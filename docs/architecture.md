# Runtime architecture

State as of the 0.3.4 code extracted into this repository.

## Startup

`MainFile.Initialize` (a `[ModInitializer]`) registers the BaseLib settings
(`FullArtModConfig`), loads packs (`FullArtPackRegistry.Load`), starts the card
report hotkey (`CardReports`), initialises the effect renderer when effects are
on, and applies all Harmony patches under the id `full_art_card_packs`.

## Pack loading — `FullArtPackRegistry`

- Reads `full_art_card_packs.cfg` (JSON) from the mod folder. `enabled_packs`
  is an explicit allow-list; a missing or unreadable cfg falls back to `silent`.
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
and current settings to `user://full_art_card_packs/card-reports.jsonl`. It is
the natural base for an exporter of every loaded card (roadmap phase 2).

## Tests

`src/FullArtCardPacks.Tests` is a console runner (`Program.cs`) covering the
background loader, game API shape (`GameApiTests`, checks the patched members
exist in the installed game), layer policy, LRU cache and rarity spoofing.
Native rendering tests that need prepared artwork stay in the art repository.
