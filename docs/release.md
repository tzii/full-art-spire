# Release checklist

How a Full Art Spire release is assembled and checked. Artwork comes from the
art repository; this repository adds the runtime and assembles the mod folder.

## 1. Inputs

- **Runtime:** a clean, committed `main` (the receipt records the commit and
  warns about uncommitted changes).
- **Packs:** one or more pack sources built by the art repository: mod-shaped
  folders (`packs/*.pack` with `art/`, `layers/`, `effects/`, `gpu/` and
  `gpu-textures.audit`) or v2 pack folders (`pack.json`). For packs of another
  mod's characters, that mod author's written permission.
- **Versions:** set `version` in `mod/full_art_spire.json` and add the
  `CHANGELOG.md` entry. `min_game_version` and the BaseLib `min_version` must
  match what was tested.

## 2. Build and test

```bash
dotnet build src/FullArtSpire -c Release
dotnet run --project src/FullArtSpire.Tests
python -m unittest discover -s tools/tests
```

Optionally check real packs with the runtime's own discovery:
`FULL_ART_SPIRE_TEST_MOD_FOLDER=<pack source> dotnet run --project src/FullArtSpire.Tests`.

## 3. Package

```bash
python tools/package_mod.py --build --zip \
  --pack-source <art repo release folder> [--pack-source <more>] \
  --output dist/<version>
```

- Writes `dist/<version>/full_art_spire/`, `FullArtSpire-<version>.zip` (one
  top-level `full_art_spire/` folder) and `package-receipt.json` (counts,
  runtime commit, DLL and archive hashes, every file's hash).
- Copies only what cards reference, hash-checks every GPU variant and merges
  the GPU indexes. A later `--pack-source` replaces an earlier one's pack of the
  same name; conflicting files fail the build.
- The DLL ships as `full_art_spire.dll` (the build output is
  `FullArtSpire.dll`), shaders under `shaders/`. There is no pack cfg.
- `dist/` is ignored by git. The script refuses to write into a game install.

## 4. Check in the game

Done by a person on a real install. Only then may release notes claim in-game
QA.

1. Back up `mods/`. Copy `full_art_spire/` in; BaseLib must be installed.
2. **With the old mod still installed:** the main menu shows the Full Art
   Spire warning, cards look as with 0.3.x, and there is no Full Art Spire
   settings page.
3. **Old mod removed:** settings carried over (compare a changed slider),
   packs switched off in the old cfg are off, and the log lists every pack.
4. **Card packs section:** every pack has a switch; switching one off and
   restarting removes its art; restore defaults switches all on.
5. Compendium and a run: full art, depth, finishes and Reduced Motion on a
   sample of every pool.
6. With a pack from another mod or the player folder (when one exists): it
   loads, and its overrides appear in the log.

## 5. Publish

- Upload the zip to the Nexus page; the old Full Art Card Packs page links to
  it and tells players to delete the `full_art_card_packs` folder.
- Credit Mega Crit and BaseLib, and describe preview quality honestly.
- Tag the commit `v<version>`.
