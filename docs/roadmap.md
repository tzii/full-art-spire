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
  `0.4.0-qa2` (commit `764fc6f`) built from the installed 0.3.4 packs:
  - with the old mod installed, the main-menu warning showed and the old mod
    kept working; the pack selection was recorded;
  - with it removed, settings were imported and all 12 packs (667 cards)
    loaded with no errors in the log;
  - Compendium and a run looked right;
  - switching a character's pack off and on worked after a restart.
  Not checked: Restore defaults switching every pack back on. BaseLib titles
  the warning popup "Mod configuration error", which overstates it.
- The Fool's pack (90 cards, public beta v0.113) ships in 0.4.0. The
  maintainer played a full run with it (reported 2026-11-20). Its art is
  plain PNG, without GPU variants; the art repo can add them later.
- Steam Workshop from day one, alongside Nexus and GitHub Releases.
  **Done in code (2026-11-20), nothing uploaded yet:** `package_mod.py
  --workshop` stages the workspace for Mega Crit's uploader from `store/`
  (text, settings with BaseLib's item 3737335127 as a dependency, an 864 px
  preview at 402 KB). How the game loads the mod from the Workshop folder is
  untried (`docs/release.md`, step 4.7).

## Art coverage

Measured 2026-11-20 against the art repo's v0.113.1 roster export (717 runtime
models, 716 public): the 0.4.0 packs cover 666. `Scare` (in the packs) no
longer exists in v0.113.1; its entry is inert. The other 50 cards ship in
0.4.1 (built 2026-11-24, agent-reviewed, in-game check pending), as the v2 pack
`megacrit.beta_v0113`:

- token (25): the 22 Major Arcana tarot cards (`TheFool` to `TheWorld`, with
  `Death`, `Judgement`, `Justice`, `Strength`, `Temperance` and
  `WheelOfFortune`), plus `Amnesia`, `Joy` and `TheShot`;
- event (5): `Abundance`, `DingyDagger`, `GodByrd`, `MightyShield`,
  `MightyWeapon`;
- silent (4): `BladeSymphony`, `Concoct`, `Fade`, `Sidestep`;
- defect, ironclad, necrobinder and regent (3 each): `Hibernate`,
  `ImitationLearning`, `OneForAll`; `Blaze`, `Midnight`, `Outrage`;
  `Cacophony`, `Soulbound`, `Underworld`; `Constellation`, `Plot`, `Tutor`;
- colorless (2): `MyShinies`, `TheBall`;
- quest (1): `Dowsing`; status (1): `Sadness`.

No curse is missing. Not checked here: portraits Mega Crit changed for cards
that already have full art, whose extensions may need a redo.

## Phase 2 — pack authors and modded characters

- In-game exporter: dump every loaded card's `runtime_model`, character/pool,
  source mod and portrait PNG (extends `CardReports`).
- `tools/` CLI: `init`, `validate`, `build` for v2 packs, plus a JSON schema.
  **Validate done (2026-11-20):** `validate_pack.py` checks v2 folders, v1
  packs and whole mod folders against the runtime's rules, including GPU-only
  art (45 cards in the 0.3.4 packs). **Schema done (2026-11-20):**
  `docs/pack.schema.json`, tested against the validator's rules. `init` and
  `build` remain.
- Apply pack switches without a restart. **Done (2026-11-20); verified in game
  (2026-11-21) on v0.113.1 beta:** the card table is re-planned from the packs
  found at startup and cards on screen reload mid-combat (`docs/architecture.md`,
  Settings).
- Pack-author guide.
- Pilot: one modded character, with its author's permission.

## Phase 3 — art repo cleanup

- Art repo builders emit v2 pack folders; stop committing release copies.
- Lossless PNG re-encoding. **Done (2026-11-20)** as `package_mod.py
  --optimize-png`, with `png-reencode.audit` keeping the art repo's recorded
  hashes checkable. On the QA build it rewrote the 259 PNGs that hold only
  image data: 8.5 MB saved (zip 1,129 → 1,120 MB). 708 art and background PNGs
  (1.0 GB) carry OpenAI's signed C2PA Content Credentials (`caBX`), which
  record that the image is AI-generated. **Decided 2026-11-20:** releases strip
  them (`--strip-content-credentials`) and say on the README and mod page that
  the art is AI-generated. Measured on the QA build: 971 PNGs re-encoded (712
  of them losing credentials), installed size 1,689 → 1,478 MB, zip 1,120 →
  1,072 MB; the zip gains less because its deflate pass already recovered part
  of the slack. All 971 decode to the original pixels (checked independently).
- Remove the runtime from the art repo once this repo is the source of truth
  (its native rendering tests move or point here).

## Decided

- **Name and in-game id (2026-11-20):** the mod becomes **Full Art Spire**,
  id `full_art_spire`, assembly/namespace `FullArtSpire`, in 0.4.0, before any
  other mod depends on the id. Required with it: detect an installed
  `full_art_card_packs`, warn and stay inactive until it is removed (both
  would patch the same cards); import the old BaseLib settings and cfg.
- **Distribution (updated 2026-11-20):** from 0.4.0, the Steam Workshop, a new
  Nexus page and GitHub Releases, all carrying every card. The old Nexus page
  links to the new one and tells users to delete the old
  `full_art_card_packs` folder.
- **Repositories (2026-11-20):** this repository goes public; the art repo
  stays private under its current name. It holds art extracted from the game
  (including beta exports) and about 9 GB of LFS objects whose downloads
  would count against the owner's quota. Card art never enters this
  repository's git history; complete builds are attached to GitHub Releases.
- **License (2026-11-20):** MIT for the code, docs, pack format and schema,
  with an explicit note that it covers no artwork.
- **BaseLib (2026-11-20):** referenced from NuGet (`Alchyr.Sts2.BaseLib`,
  MIT) at the manifest's minimum version, instead of committed binaries.
- **Commit identity (2026-11-20):** every commit is authored as `tzii` with
  the GitHub noreply address; the history was rewritten once to make it so.

## Open decisions

- ~~When the 50 new v0.113 cards (Art coverage) ship~~: decided 2026-11-24,
  0.4.1.
