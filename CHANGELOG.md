# Changelog

## 0.4.1 - every beta card

- Full art with depth layers for the 50 new cards of the v0.113.1 public beta that kept their original art in 0.4.0 (the 22 tarot cards, Amnesia, Joy and The Shot, five event cards, and the new cards of every character plus colorless and status), plus the two Fool cards added in v0.114.0 (Twin Shot and Optimism). Every card of the main game and the v0.113.1–v0.114.0 public beta now has full art.
- Fix: full-art Quest cards (Lantern Key, Byrdonis Egg, Spoils Map, Dowsing) no longer show the game's placeholder "NOPE" text-box graphic behind their description.
- Clearer settings page. Options sit in groups (Card packs, Depth, Finishes, Finish strength by screen, Motion, and a collapsed Advanced group), and every option explains itself when you hover it. Options that do nothing with the current choices are hidden, such as the single-finish picker unless Finish style is One finish for all. The page shows the mod version and how many cards have full art. Saved settings carry over unchanged.
- The 52 beta cards are agent-reviewed only and not yet checked in game.
- The new cards' extensions and backgrounds are AI-generated with Google's Nano Banana 2.1 in Google Flow and other image models on public websites, starting from Mega Crit's original card art.

## 0.4.0

- Full art for all 90 cards of the Fool (public beta). On the beta, 50 other new cards keep their original art for now.
- Now on the Steam Workshop too, with BaseLib as a required item.
- Renamed to Full Art Spire (mod id `full_art_spire`). Delete the old `full_art_card_packs` folder after installing.
- While the old mod is still installed and enabled, Full Art Spire stays inactive and shows a warning on the main menu, so the two never patch the same cards.
- Packs can now come from other mods (a `full_art/` folder inside the mod) and from your own `full_art_spire/packs/` folder in the game's user data, using the new pack format (`pack.json`). Old `.pack` files still load.
- Every pack has its own switch in the mod's settings, under Card packs. Switching applies right away, including to cards already on screen. The `full_art_card_packs.cfg` pack list is no longer used.
- A card whose pack is switched off gets the game's own portrait layout back.
- Less work per frame for layered cards and finishes.
- Smaller download: PNGs are compressed by type in the zip and re-encoded losslessly (identical pixels). The re-encode removes the C2PA Content Credentials the image generator embedded; the full-art extensions and backgrounds are AI-generated from Mega Crit's original card art.
- Your Full Art Card Packs settings are imported on first start. Packs you had switched off in `full_art_card_packs.cfg` stay off if the old folder was still installed when you first started 0.4.0.
- A malformed pack (for example one with `"version": null`) is skipped with a log line instead of stopping every pack from loading.
- For pack authors: `tools/validate_pack.py` checks the new pack format and whole mod folders, including art shipped only as compressed GPU textures. `docs/pack.schema.json` gives editors completion and inline errors for `pack.json`.

## 0.3.4 - complete layered release candidate

- Compressed textures must preserve full opacity as well as meeting the existing RGB quality limits. Textures that fail keep their original PNG, preventing affected cards from falling back to flat art.
- Package verification checks decoded opacity, and the native release check validates all 577 decoded layer sets against the renderer's admission rules.
- Includes the 0.3.3 combat finish, 0.3.2 rare finish, and 0.3.1 loading and Compendium fixes in one complete package.

## 0.3.3 - finishes in combat

- Hand cards ease up to the full inspect-strength finish while hovered, dragged, drawn or played, then settle back to HandIntensity in the resting hand.
- Drawn cards keep their hand finish after the game reloads them, instead of dropping to the faint grid strength.
- HandIntensity now defaults to 0.8 (was 0.65); existing saved settings are kept.

## 0.3.2 - stronger rare finishes

- Rare gold foil has a steadier sheen, larger glints and a clearer subject rim at normal grid/hand sizes; Ancients retain the stronger prismatic tier.
- Rare/Ancient showcase animation remains visible at grid size while respecting zero motion and Reduced Motion.

## 0.3.1 - first public release

- Fix: finishes on Compendium grid cards no longer draw over the enlarged card when you inspect one.
- Fix: DDS-only art and background layers now pass layer file validation, restoring depth/parallax with background texture loading enabled. Cached layer verdicts track both PNG and DDS files.

## 0.3.0 - release candidate (not published)

- All 577 cards across 11 pools have full art with layered parallax depth.
- New default rarity showcase: satin on starters, commons, tokens and statuses; pearl holo on uncommons; gold foil with glints, backdrop sparkle and an aura on rares; full prismatic with a rainbow aura on Ancients; themed finishes for event, quest and curse cards.
- Each texture ships once, as BC1 where it is visually lossless and BC7 otherwise, cutting the install from 3.7 GB to under 2 GB.
- Background loading on bounded worker threads, with mipmaps for small cards.
- F8 card reports for bug reports.
