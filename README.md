# Full Art Spire

Runtime mod, pack format and pack-author tooling for full-art Slay the Spire 2
cards with layered parallax depth and rarity finishes. Visual-only: no gameplay
or balance changes.

Ships today as **Full Art Card Packs** (`full_art_card_packs` 0.3.4, requires
BaseLib). From 0.4.0 it is **Full Art Spire** (`full_art_spire`), and this
repository is its code home. The artwork, prompts and review evidence are
produced in the separate art repository
[`tzii/sts2-full-art-card-pack`](https://github.com/tzii/sts2-full-art-card-pack),
which builds the character packs this runtime loads.

Status: private, pre-0.4.0. Done in code but not yet tried in a game session:
the rename (with detection of an installed 0.3.x and import of its settings),
pack format v2 with discovery in this mod, other mods and the player's folder,
and a switch per pack in the settings. Whether other mods' cards render like
the base game's still needs a pilot. The renderer is the 0.3.4 code, with its
history carried over from the art repository. See [docs/roadmap.md](docs/roadmap.md).

## Layout

```text
FullArtSpire.sln              solution for both projects
src/FullArtSpire/             runtime mod (C#, Harmony, Godot shaders)
src/FullArtSpire.Tests/       unit tests (custom runner; needs the game's DLLs)
src/Sts2PathDiscovery.props   finds the Slay the Spire 2 install for builds
tools/validate_pack.py        v1 pack validator (stdlib Python)
mod/                          mod manifest shipped next to the DLL
docs/                         architecture, pack format, roadmap
```

## Build and test

Requires the .NET 9 SDK and an installed copy of Slay the Spire 2. The build
references `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` from the game folder,
so it cannot run on hosted CI. The game path is found from the Steam registry;
override with `/p:Sts2Path=...` or `/p:Sts2DataDir=...`.

```bash
dotnet build src/FullArtSpire -c Release
dotnet run --project src/FullArtSpire.Tests
python tools/validate_pack.py --pack <mod>/packs/<pool>.pack
```

The validator checks art and mask files against the mod folder that contains
`packs/`. Add `--root <art repo>` to also check each card's `repo_final_png`.

Building never installs anything into the game folder.

## Credits

Original Slay the Spire 2 artwork, characters and designs © Mega Crit. This is
an unofficial fan project. BaseLib by Alchyr.
