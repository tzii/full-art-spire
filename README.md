# Full Art Spire

Runtime mod, pack format and pack-author tooling for full-art Slay the Spire 2
cards with layered parallax depth and rarity finishes. Visual-only: no gameplay
or balance changes.

Ships today as **Full Art Card Packs** (`full_art_card_packs` 0.3.4, requires
BaseLib). From 0.4.0 it becomes **Full Art Spire** (`full_art_spire`), and this
repository is its code home. The artwork, prompts and review evidence are
produced in the separate art repository
[`tzii/sts2-full-art-card-pack`](https://github.com/tzii/sts2-full-art-card-pack),
which builds the character packs this runtime loads.

Status: private, pre-0.4.0. The runtime is the 0.3.4 code (unchanged since the
`v0.3.4-rc` tag) with its history carried over from the art repository. Pack
format v2, the rename and support for other mods' characters are designs, not
implemented yet. See [docs/roadmap.md](docs/roadmap.md).

## Layout

```text
FullArtSpire.sln              solution for both projects
src/FullArtCardPacks/         runtime mod (C#, Harmony, Godot shaders)
src/FullArtCardPacks.Tests/   unit tests (custom runner; needs the game's DLLs)
src/Sts2PathDiscovery.props   finds the Slay the Spire 2 install for builds
tools/validate_pack.py        v1 pack validator (stdlib Python)
mod/                          mod manifest shipped next to the DLL (0.3.4 copy)
docs/                         architecture, pack format, roadmap
```

## Build and test

Requires the .NET 9 SDK and an installed copy of Slay the Spire 2. The build
references `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` from the game folder,
so it cannot run on hosted CI. The game path is found from the Steam registry;
override with `/p:Sts2Path=...` or `/p:Sts2DataDir=...`.

```bash
dotnet build src/FullArtCardPacks -c Release
dotnet run --project src/FullArtCardPacks.Tests
python tools/validate_pack.py --pack <mod>/packs/<pool>.pack
```

The validator checks art and mask files against the mod folder that contains
`packs/`. Add `--root <art repo>` to also check each card's `repo_final_png`.

Building never installs anything into the game folder.

## Credits

Original Slay the Spire 2 artwork, characters and designs © Mega Crit. This is
an unofficial fan project. BaseLib by Alchyr.
