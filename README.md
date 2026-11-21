# Full Art Spire

Runtime mod, pack format and pack-author tooling for full-art Slay the Spire 2
cards with layered parallax depth and rarity finishes. Visual-only: no gameplay
or balance changes.

Players: get the mod from the Steam Workshop or Nexus Mods, or download
`FullArtSpire-<version>.zip` from this repository's
[Releases](https://github.com/tzii/full-art-spire/releases). Interactive card
previews, in-game footage and screenshots are on the
[showcase site](https://tzii.github.io/full-art-spire/). It needs
[BaseLib](https://github.com/Alchyr/BaseLib-StS2). 0.4.0 covers all 577 cards of
the main game and the Fool's 90 from the public beta.

Until 0.3.4 the mod was **Full Art Card Packs** (`full_art_card_packs`). From
0.4.0 it is **Full Art Spire** (`full_art_spire`), and this repository is its
code home. The artwork is made in a separate, private art repository; packs
arrive here as build inputs, so this repository holds no card art. Releases
attach the complete mod, every card included.

Status: 0.4.0 release candidate. Tried in game (v0.113.1 beta): the rename, with
detection of an installed 0.3.x and import of its settings, every pack
(including the Fool's), and switching packs live without a restart. Not yet
tried in game: loading from the Steam Workshop folder, and packs from other
mods or the player's folder (pack format v2). Whether other mods' cards render
like the base game's still needs a pilot. See [docs/roadmap.md](docs/roadmap.md).

## Layout

```text
FullArtSpire.sln              solution for both projects
src/FullArtSpire/             runtime mod (C#, Harmony, Godot shaders)
src/FullArtSpire.Tests/       unit tests (custom runner; needs the game's DLLs)
src/Sts2PathDiscovery.props   finds the Slay the Spire 2 install for builds
tools/package_mod.py          assembles the mod folder and zip (stdlib Python; Pillow for --optimize-png)
tools/validate_pack.py        pack and mod folder validator (stdlib Python)
tools/tests/                  tests for the Python tools
mod/                          mod manifest shipped next to the DLL
store/                        Workshop and Nexus page text, Workshop settings and preview image
docs/                         architecture, pack format and its JSON schema, roadmap, release checklist
```

## Build and test

Requires the .NET 9 SDK and an installed copy of Slay the Spire 2. The build
references `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` from the game folder,
so it cannot run on hosted CI. The game path is found from the Steam registry;
override with `/p:Sts2Path=...` or `/p:Sts2DataDir=...`. BaseLib comes from
NuGet (`Alchyr.Sts2.BaseLib`), compiled against the oldest version the mod
accepts.

```bash
dotnet build src/FullArtSpire -c Release
dotnet run --project src/FullArtSpire.Tests
python -m unittest discover -s tools/tests
python tools/validate_pack.py --mod-folder <mod folder>
```

To assemble the mod folder, the zip and the Steam Workshop upload folder from
the build and a pack source, see [docs/release.md](docs/release.md):

```bash
python tools/package_mod.py --build --zip --workshop --optimize-png --strip-content-credentials \
  --pack-source <pack source> --output dist/<version>
```

The validator takes one of:

- `--pack <folder>`: a v2 pack folder, checked against what the runtime
  accepts (types, paths, files, layer and finish rules, its GPU index);
- `--pack <mod>/packs/<pool>.pack`: a v1 pack, checked strictly against the mod
  folder that contains `packs/` (add `--root <art repo>` to also check each
  card's `repo_final_png`);
- `--mod-folder <mod folder>`: every pack in an assembled mod folder, plus which
  pack wins each card and files no card uses.

Art may ship only as the GPU variant `gpu-textures.audit` lists for it; variants
are hash- and header-checked. Effect mask content checks need Pillow.

Building and packaging never install anything into the game folder.

## Credits

Original Slay the Spire 2 artwork, characters and designs © Mega Crit. This is
an unofficial fan project, not affiliated with or endorsed by Mega Crit.
BaseLib by Alchyr.

The full-art extensions and the depth-layer backgrounds are AI-generated (OpenAI
image models), starting from Mega Crit's original card art. The released PNGs
are re-encoded to save space, which removes the C2PA Content Credentials the
generator embedded; `png-reencode.audit` in the mod folder lists every such
file and the hash it had before.

## License

The code, documentation, pack format and schema are under the [MIT License](LICENSE).
It covers no artwork: Slay the Spire 2's art, characters and designs belong to
Mega Crit, and the full-art images in releases are derived from them. No
permission to reuse those images is granted.
