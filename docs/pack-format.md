# Pack format

## v1 — still read (0.3.x format)

Location: `<mod folder>/packs/<pool>.pack`. From 0.4.0 every such file loads as
pack `megacrit.<pool>`, switched on unless the player turns it off; the pack
cfg is gone. (0.3.x loaded only the pools listed in `full_art_card_packs.cfg`,
in listed order.)

Pool names use `a-z`, `0-9` and `_` only.

The runtime reads `cards` (and parses `format`/`pool`). Other top-level fields
written by the art repo's builder (`version`, `display_name`, `roster`,
`card_count`, `intended_mod_version`, …) are informational.

Per card:

| Field | Required | Meaning |
|---|---|---|
| `runtime_model` | yes | Full .NET type name of the card model, e.g. `MegaCrit.Sts2.Core.Models.Cards.KiWhip` |
| `art` | yes | Relative PNG path under the mod folder |
| `canonical_asset_name`, `display_name`, `pool` | no | Labels for logs and reports |
| `preserve_original_layout` | no | `true` keeps the game's normal frame |
| `layers` | no | `background`, `foreground`, `foreground_mask`, `foreground_mask_channel` (`red`), `finish_mask`, `registration`, `depth` (1.0), `background_shift` (0.010), `foreground_shift` (0.022), `foreground_scale` (1.035), `subject_scale` (1), `subject_offset_x`/`_y` (0), `shadow_strength` (0.28), `frame_inset` (0.055) |
| `effects` | no | `surface`, `rarity_policy` (`rare_first`), `mask`, `effect_mask`, `protection_mask`, `permission_mask`, `intensity` (1.0), `phase_seed` |

All paths are relative, use `/`, and must resolve inside the mod folder.

## v2 — implemented (0.4.0)

Any installed mod, or the player, can supply packs, including for other mods'
characters, without editing this mod's folder.

### Pack folder

```text
<pack root>/
  pack.json
  art/…  layers/…
  gpu-textures.audit   (optional, paths relative to the pack root)
```

All card paths in `pack.json` resolve inside `<pack root>`; a card whose `art`
escapes it is dropped.

```json
{
  "format": "full_art_pack",
  "format_version": 2,
  "id": "megacrit.fool",
  "name": "The Fool",
  "version": "1.0.0",
  "authors": ["tzii"],
  "credits": "Original art © Mega Crit",
  "requires": [{ "mod": "some_character_mod", "min_version": "1.2.0" }],
  "min_game_version": "0.113.1",
  "priority": 0,
  "cards": [ { "runtime_model": "…", "art": "art/ki_whip.png", "layers": { } } ]
}
```

- `format` must be `full_art_pack` and `format_version` exactly `2`;
  anything else is skipped with a warning.
- `id`: `<namespace>.<name>`, lowercase `a-z`, `0-9`, `_`, at least one dot.
  Base-game packs use `megacrit.<pool>`, which is also the id a v1
  `<pool>.pack` gets, so a v2 pack with that id replaces the v1 file.
  Third-party packs use their author's or mod's namespace.
- `requires` (optional): the pack is skipped, with a log line, unless every
  listed mod is loaded (or still to load) and, if `min_version` is given, at
  least that version. Same shape as the game's mod `dependencies`.
- `min_game_version` (optional): skipped on older games. Not checked when the
  game version cannot be read, as the game does for mods.
- `name`, `version`, `authors`, `credits`: shown in the settings and logs.
- Per-card fields are unchanged from v1.

### Discovery

In this order (later sources win ties, see below):

1. Legacy v1: `<this mod>/packs/<pool>.pack`.
2. Bundled: `<this mod>/packs/*/pack.json`.
3. Other mods: `<that mod's folder>/full_art/*/pack.json`, for every mod the
   game lists as loaded or still to load (`ModManager.Mods`, which includes
   Workshop installs outside `mods/`).
4. Player packs: `user://full_art_spire/packs/*/pack.json`
   (`%APPDATA%/SlayTheSpire2/full_art_spire/packs` on Windows).

Folders without `pack.json` are noted and ignored.

### Switching packs and conflicts

- Every pack is on unless the player switches it off. Each pack gets a switch
  under **Card packs** in the BaseLib settings; switched-off ids are stored in
  the hidden `DisabledPacks` setting. Changes apply after a restart.
- One id found twice: the copy that would win a conflict is used, the other is
  skipped with a log line.
- One `runtime_model` claimed by several packs: highest `priority` wins, then
  the later source (player > other mods > bundled > v1), then the later
  discovery. Every override is logged.

### Not settled yet

- Whether modded cards' portraits behave like the base game's in `NCard`
  (needs a pilot with one modded character).
- Pack-author permission: art derived from another mod's portraits needs that
  mod author's consent before distribution.
- `tools/validate_pack.py` checks v1 only.
