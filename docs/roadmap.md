# Roadmap

## Done

- Runtime, unit tests, build discovery, pack validator and changelog extracted
  from the art repo with their history (2026-11-20). Runtime code is unchanged
  since the `v0.3.4-rc` tag; tests include the beta (v0.113.1) compatibility
  update.

## Phase 1 — 0.4.0: packs from anywhere

- Rename to Full Art Spire (`full_art_spire`) with old-mod detection and
  settings import (see Decided). **Done (2026-11-20).**
- Pack format v2 and discovery (`pack-format.md`), still reading v1 `.pack`.
  **Done (2026-11-20).** v2 discovery is covered by unit tests only; no v2
  pack has been loaded in a game session yet.
- Per-pack toggles in the BaseLib settings instead of the cfg allow-list,
  seeded from the 0.3.x pack choices. **Done (2026-11-20).**
- A packaging script that assembles the mod folder from this repo's build plus
  pack folders produced by the art repo, and a release checklist. **Done
  (2026-11-20):** `tools/package_mod.py`, `docs/release.md`. From the
  installed 0.3.4 packs it builds 12 packs, 667 cards, 545 GPU variants,
  1.70 GB (1.15 GB zipped), leaving out 156 unreferenced Silent masks.
- Python tooling tests on hosted CI (the DLL build stays local: it needs the
  game's DLLs). **Done:** `.github/workflows/tools.yml`.
- First in-game pass of 0.4.0 (`docs/release.md`, step 4). **Done
  (2026-11-20), by the maintainer on game v0.113.1 with BaseLib 3.4.8**, using
  `0.4.0-qa2` (commit `9bd8175`) built from the installed 0.3.4 packs:
  - with the old mod installed, the main-menu warning showed and the old mod
    kept working; the pack selection was recorded;
  - with it removed, settings were imported and all 12 packs (667 cards)
    loaded with no errors in the log;
  - Compendium and a run looked right;
  - switching a character's pack off and on worked after a restart.
  Not checked: Restore defaults switching every pack back on. BaseLib titles
  the warning popup "Mod configuration error", which overstates it.

## Phase 2 — pack authors and modded characters

- In-game exporter: dump every loaded card's `runtime_model`, character/pool,
  source mod and portrait PNG (extends `CardReports`).
- `tools/` CLI: `init`, `validate`, `build` for v2 packs, plus a JSON schema.
  **Validate done (2026-11-20):** `validate_pack.py` checks v2 folders, v1
  packs and whole mod folders against the runtime's rules, including GPU-only
  art (45 cards in the 0.3.4 packs). **Schema done (2026-11-20):**
  `docs/pack.schema.json`, tested against the validator's rules. `init` and
  `build` remain.
- Apply pack switches without a restart. **Done in code (2026-11-20), not yet
  tried in game:** the card table is re-planned from the packs found at
  startup and cards on screen reload (`docs/architecture.md`, Settings).
- Pack-author guide.
- Pilot: one modded character, with its author's permission.

## Phase 3 — art repo cleanup

- Art repo builders emit v2 pack folders; stop committing release copies.
- Lossless PNG re-encoding. **Done (2026-11-20)** as `package_mod.py
  --optimize-png`, with `png-reencode.audit` keeping the art repo's recorded
  hashes checkable. On the QA build it rewrote the 259 PNGs that hold only
  image data: 8.5 MB saved (zip 1,129 → 1,120 MB). **Open decision:** 708 art
  and background PNGs (1.0 GB) carry OpenAI's signed C2PA Content Credentials
  (`caBX`), which record that the image is AI-generated. Re-encoding them
  would save about 20% (roughly 200 MB) but strips or invalidates the
  credentials, so they keep their bytes unless the maintainer decides
  otherwise.
- Remove the runtime from the art repo once this repo is the source of truth
  (its native rendering tests move or point here).

## Decided

- **Name and in-game id (2026-11-20):** the mod becomes **Full Art Spire**,
  id `full_art_spire`, assembly/namespace `FullArtSpire`, in 0.4.0, before any
  other mod depends on the id. Required with it: detect an installed
  `full_art_card_packs`, warn and stay inactive until it is removed (both
  would patch the same cards); import the old BaseLib settings and cfg.
- **Distribution:** a new Nexus page carrying everything. The old page links to
  it and tells users to delete the old `full_art_card_packs` folder.

## Open decisions

- **Art repo name** (keep, or e.g. `sts2-full-art-studio`).
- **Before going public:** choose a license, and confirm `lib/BaseLib.*` may be
  redistributed (or fetch BaseLib at build time instead).
