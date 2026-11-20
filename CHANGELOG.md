# Changelog

## 0.4.0 - unreleased

- Renamed to Full Art Spire (mod id `full_art_spire`). Delete the old `full_art_card_packs` folder after installing.
- While the old mod is still installed and enabled, Full Art Spire stays inactive and shows a warning on the main menu, so the two never patch the same cards.
- Packs can now come from other mods (a `full_art/` folder inside the mod) and from your own `full_art_spire/packs/` folder in the game's user data, using the new pack format (`pack.json`). Old `.pack` files still load.
- Every pack has its own switch in the mod's settings, under Card packs; changes apply after a restart. The `full_art_card_packs.cfg` pack list is no longer used.
- Your Full Art Card Packs settings are imported on first start. Packs you had switched off in `full_art_card_packs.cfg` stay off if the old folder was still installed when you first started 0.4.0.

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
