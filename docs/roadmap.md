# Roadmap

## Done

- Runtime, unit tests, build discovery, pack validator and changelog extracted
  from the art repo with their history (2026-11-20). Runtime code is unchanged
  since the `v0.3.4-rc` tag; tests include the beta (v0.113.1) compatibility
  update.

## Phase 1 — 0.4.0: packs from anywhere

- Rename to Full Art Spire (`full_art_spire`) with old-mod detection and
  settings import (see Decided).
- Pack format v2 and discovery (`pack-format.md`), still reading v1 `.pack`.
- Per-pack toggles in the BaseLib settings instead of the cfg allow-list.
- A packaging script that assembles the mod folder from this repo's build plus
  pack folders produced by the art repo, and a release checklist.
- Python tooling tests on hosted CI (the DLL build stays local: it needs the
  game's DLLs).

## Phase 2 — pack authors and modded characters

- In-game exporter: dump every loaded card's `runtime_model`, character/pool,
  source mod and portrait PNG (extends `CardReports`).
- `tools/` CLI: `init`, `validate`, `build` for v2 packs, plus a JSON schema.
- Pack-author guide.
- Pilot: one modded character, with its author's permission.

## Phase 3 — art repo cleanup

- Art repo builders emit v2 pack folders; stop committing release copies.
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
