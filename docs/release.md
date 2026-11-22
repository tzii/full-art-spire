# Release checklist

How a Full Art Spire release is assembled and checked. Artwork comes from the
art repository; this repository adds the runtime and assembles the mod folder.

## 1. Inputs

- **Runtime:** a clean, committed `main` (the receipt records the commit and
  warns about uncommitted changes).
- **Packs:** one or more pack sources built by the art repository: mod-shaped
  folders (`packs/*.pack` with `art/`, `layers/`, `effects/`, `gpu/` and
  `gpu-textures.audit`) or v2 pack folders (`pack.json`). A mod folder the art
  repository's packs were installed into works too. For packs of another
  mod's characters, that mod author's written permission.
- **Versions:** set `version` in `mod/full_art_spire.json` and add the
  `CHANGELOG.md` entry; its bullets become the Workshop change note.
  `min_game_version` and the BaseLib `min_version` must match what was tested,
  and the BaseLib package version in `src/FullArtSpire/FullArtSpire.csproj`
  must equal that `min_version`.
- **Store text:** card counts, game versions and features in
  `mod/full_art_spire.json` (shown in game), `store/workshop-description.bbcode`
  and `store/nexus-description.bbcode` match the release.

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
python tools/package_mod.py --build --zip --workshop --optimize-png --strip-content-credentials \
  --pack-source <pack source> [--pack-source <more>] \
  --output dist/<version>
```

- Writes `dist/<version>/full_art_spire/`, `FullArtSpire-<version>.zip` (one
  top-level `full_art_spire/` folder) and `package-receipt.json` (counts,
  runtime commit, DLL and archive hashes, every file's hash).
- `--workshop` also writes `dist/<version>/workshop/`, the workspace Mega
  Crit's uploader takes: `content/` (the mod folder's files, hard-linked),
  `workshop.json` (`store/workshop.json` plus the description from
  `store/workshop-description.bbcode` and this version's changelog as the change
  note), `image.png` (`store/workshop-image.png`, under 1 MB) and, once the item
  exists, `mod_id.txt` from `store/workshop-item-id.txt`. Without an item ID the
  upload creates a new, private item; with one it updates that item and leaves
  its visibility alone. The store inputs are checked before the build starts.
- Copies only what cards reference, hash-checks every GPU variant and merges
  the GPU indexes. A later `--pack-source` replaces an earlier one's pack of the
  same name; conflicting files fail the build.
- The DLL ships as `full_art_spire.dll` (the build output is
  `FullArtSpire.dll`), shaders under `shaders/`. There is no pack cfg.
- `--optimize-png` (needs Pillow) re-encodes every 8-bit PNG outside v2 packs
  losslessly and keeps the result only when it is smaller and decodes to the
  same pixels. Only files made of bare image data qualify: 16-bit files and
  files with colour chunks (gAMA, iCCP, sRGB, tRNS) or other metadata keep
  their bytes.
- `--strip-content-credentials` also re-encodes files whose only metadata is
  a C2PA Content Credentials manifest (`caBX`), which removes it. Most
  generated art and backgrounds carry OpenAI's; re-encoding them saves about a
  fifth of their installed size (211 MB on the QA build, 49 MB of the zip). Decided 2026-11-20 by the maintainer, on the condition
  that the README and the mod page say plainly that the art is AI-generated.
- `png-reencode.audit` maps each rewritten file to the hash it had before (and
  lists `removed_chunks`), so the art repo's recorded hashes (effect audits,
  `layer_audit`) still check out; the validator reads it, and packaging an
  optimized folder again carries it over. The art repo's own files are never
  touched, even with `--hardlink`.
- `dist/` is ignored by git. The script refuses to write into a game install.

Then validate the result; warnings are worth reading, issues block the release:

```bash
python tools/validate_pack.py --mod-folder dist/<version>/full_art_spire
```

## 4. Check in the game

Done by a person on a real install. Only then may release notes claim in-game
QA.

1. Back up `mods/`. Copy `full_art_spire/` in; BaseLib must be installed.
2. **With the old mod still installed:** the main menu shows the Full Art
   Spire warning, cards look as with 0.3.x, and there is no Full Art Spire
   settings page.
3. **Old mod removed:** settings carried over (compare a changed slider),
   packs switched off in the old cfg are off, and the log lists every pack.
4. **Card packs section:** every pack has a switch. Switching one off removes
   its art at once, including from cards already on screen (open a run's deck
   view or the Compendium behind the settings); switching it on brings the art
   back. Restore defaults switches all on. After a restart the choice holds.
5. Compendium and a run: full art, depth, finishes and Reduced Motion on a
   sample of every pool.
6. With a pack from another mod or the player folder (when one exists): it
   loads, and its overrides appear in the log.
7. **From the Workshop** (after the private upload in step 5): remove the
   copied `full_art_spire/` from `mods/`, subscribe to the private item, and
   check that Steam pulls in BaseLib, the mod loads from the Workshop folder,
   and steps 3 to 5 still hold.

## 5. Publish

The Workshop item goes up first, private, so step 4.7 can run on it before
anything is public.

**Steam Workshop**

1. Get `ModUploader-win-x64.zip` from
   [megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader/releases)
   (outside this repository). Steam must be running and signed in.
2. `ModUploader.exe upload -w <repo>/dist/<version>/workshop`. Its log is
   `mod-uploader.log` next to the executable.
3. First upload only: the uploader writes the new item's ID to
   `dist/<version>/workshop/mod_id.txt`. Copy it to `store/workshop-item-id.txt`
   and commit it, or the next release creates a second item.
4. On the item's Steam page: check the text, image and BaseLib under Required
   items; add gallery images; run step 4.7; then set it public.

**Nexus Mods**

1. Upload the zip to the Full Art Spire page and paste
   `store/nexus-description.bbcode`; BaseLib (mods/103) is a requirement.
2. Choose the "AI-Generated Content" label: the full-art extensions and
   backgrounds are AI-generated from the original card art.
3. Permissions: asset use, uploading to other sites, commercial use and
   conversion to other games all off; point modders to the MIT code on GitHub.
4. The old Full Art Card Packs page links to the new one and tells players to
   delete the `full_art_card_packs` folder.

**GitHub**

1. Tag the release commit `v<version>` and push the tag.
2. `gh release create v<version> dist/<version>/FullArtSpire-<version>.zip
   --title "Full Art Spire <version>" --notes-file <this version's changelog section>`.
   Release files may be up to 2 GiB each.

Everywhere: credit Mega Crit and BaseLib, describe preview quality honestly,
and say that the full-art extensions and backgrounds are AI-generated from the
original card art and that the release strips the images' C2PA Content
Credentials to save space. The store files in `store/` already do.

When a release ships art extended with Google's image model (the beta v0.113.1
cards made with Nano Banana 2.1 in Google Flow), name Google's model next to
OpenAI's in the README, the store text and the release notes. Re-encoding
removes the C2PA manifest, but Google's SynthID watermark stays in the pixels.
