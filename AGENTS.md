# AGENTS.md

Short map for agents. Keep it compact; deeper material goes in `docs/`.

## Intent

Runtime mod and pack format for full-art Slay the Spire 2 cards: every base-game
character, and characters from other mods through packs. Visual-only: no
gameplay, balance or card-data changes.

## Boundaries

- This repo holds code, the pack format, tooling, store text and small test
  fixtures only. Artwork, prompts, candidates and review evidence live in the
  art repo `tzii/sts2-full-art-card-pack`; packs arrive here as build inputs,
  not commits. Complete builds go out as GitHub Release assets and Nexus and
  Workshop uploads.
- The art repo stays private (it holds art extracted from the game): public
  docs and store text never link to it.
- Commits are authored as `tzii` with the GitHub noreply address (set in this
  repo's git config), never a personal email.
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
python tools/package_mod.py --build --pack-source <pack source> --output dist/<name>   # release: see docs/release.md
python tools/validate_pack.py --mod-folder <mod-folder>   # or --pack <v2 folder | v1 .pack>
```

The build needs an installed game (see `src/Sts2PathDiscovery.props`); BaseLib
comes from NuGet.

## Docs

- `docs/architecture.md` — how the runtime works
- `docs/pack-format.md` — v1 (still read) and v2 (implemented)
- `docs/roadmap.md` — phases and open decisions
- `docs/release.md` — packaging and the release checklist (GitHub, Nexus, Workshop)
- `store/` — Workshop and Nexus page text, Workshop settings, preview image and
  item ID; `package_mod.py --workshop` reads it
- `site/` (ignored on `main`) — the showcase website, branch `gh-pages`
  (`git worktree add site gh-pages`), served at `tzii.github.io/full-art-spire`;
  its images stay on that branch, never on `main`
