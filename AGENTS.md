# AGENTS.md

Short map for agents. Keep it compact; deeper material goes in `docs/`.

## Intent

Runtime mod and pack format for full-art Slay the Spire 2 cards: every base-game
character, and characters from other mods through packs. Visual-only: no
gameplay, balance or card-data changes.

## Boundaries

- This repo holds code, the pack format, tooling and small test fixtures only.
  Artwork, prompts, candidates and review evidence live in the art repo
  `tzii/sts2-full-art-card-pack`; packs arrive here as build inputs, not commits.
- Cards are matched by `runtime_model`, the card model's full .NET type name.
  Never key by display name or portrait path.
- Keep reading v1 `.pack` files until a release explicitly drops them.
- Pack paths must stay confined to their own pack root.
- Do not install into the game folder or launch the game unless asked.
- Do not claim in-game QA unless a human or local game session performed it.
- Always credit Mega Crit; keep docs honest about preview quality.
- Commit/push only when asked. Preserve uncommitted work.

## Commands

```bash
dotnet build src/FullArtSpire -c Release
dotnet run --project src/FullArtSpire.Tests
python -m unittest discover -s tools/tests
python tools/package_mod.py --build --pack-source <art release folder> --output dist/<name>
python tools/validate_pack.py --mod-folder <mod-folder>   # or --pack <v2 folder | v1 .pack>
```

The build needs an installed game (see `src/Sts2PathDiscovery.props`).

## Docs

- `docs/architecture.md` — how the runtime works
- `docs/pack-format.md` — v1 (still read) and v2 (implemented)
- `docs/roadmap.md` — phases and open decisions
- `docs/release.md` — packaging and the release checklist
