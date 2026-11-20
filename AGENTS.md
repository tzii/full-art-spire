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
dotnet build src/FullArtCardPacks -c Release
dotnet run --project src/FullArtCardPacks.Tests
python tools/validate_pack.py --pack <pack> --release-root <mod-folder>
```

The build needs an installed game (see `src/Sts2PathDiscovery.props`).

## Docs

- `docs/architecture.md` — how the runtime works
- `docs/pack-format.md` — v1 (implemented) and v2 (draft)
- `docs/roadmap.md` — phases and open decisions
