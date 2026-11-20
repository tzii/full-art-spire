# Pack format

## v1 — implemented (0.3.x)

Location: `<mod folder>/packs/<pool>.pack`, loaded only when `<pool>` appears in
`full_art_card_packs.cfg`:

```json
{ "enabled": true, "enabled_packs": ["ironclad", "silent", "fool"] }
```

Pool names use `a-z`, `0-9` and `_` only. Listed order matters: for the same
`runtime_model`, the later pool wins.

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

## v2 — draft, not implemented

Goal: any installed mod, or the player, can supply packs, including for other
mods' characters, without editing this mod's folder or cfg.

### Pack folder

```text
<pack root>/
  pack.json
  art/…  layers/…
```

All paths in `pack.json` resolve inside `<pack root>`.

```json
{
  "format": "full_art_pack",
  "format_version": 2,
  "id": "megacrit.fool",
  "name": "The Fool",
  "version": "1.0.0",
  "authors": ["tzii"],
  "credits": "Original art © Mega Crit",
  "requires": [{ "mod": "some_character_mod", "version": ">=1.2.0" }],
  "game": { "min": "0.113.1" },
  "priority": 0,
  "cards": [ { "runtime_model": "…", "art": "art/ki_whip.png", "layers": { } } ]
}
```

- `id`: `<namespace>.<name>`, lowercase. Base-game packs use `megacrit.<pool>`;
  third-party packs use their author's or mod's namespace.
- `requires`: the pack is skipped (with a log line) unless every listed mod is
  loaded. Base-game packs list nothing.
- Per-card fields are unchanged from v1.

### Discovery

1. Bundled packs: `<this mod>/packs/*/pack.json`.
2. Packs inside other loaded mods: `<that mod's folder>/full_art/*/pack.json`.
   Use the game's list of loaded mods, not a scan of `mods/`, because Workshop
   installs live outside `mods/`.
3. Player packs: `user://<mod id>/packs/*/pack.json`.
4. Legacy v1: `<this mod>/packs/<pool>.pack` as today.

### Enabling and conflicts

- Discovered packs are on by default; each pack gets an on/off toggle in the
  BaseLib settings, saved by pack `id`. The cfg allow-list goes away.
- One `runtime_model` claimed by several packs: highest `priority` wins, then
  discovery order (player > other mods > bundled); every override is logged.

### Open questions (verify before implementing)

- Which game/BaseLib API lists loaded mods and their folders.
- Whether modded cards' portraits behave like the base game's in `NCard`
  (needs a pilot with one modded character).
- Pack-author permission: art derived from another mod's portraits needs that
  mod author's consent before distribution.
