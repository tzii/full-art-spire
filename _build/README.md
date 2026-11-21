# Building & Updating the Full Art Spire Showcase Site

This folder holds the media regeneration pipeline for the static site on the `gh-pages` branch.

- `cards.json` — the curated card lineup for the hero, before/after sliders, depth demo, rarity finishes, The Fool, character gallery and press images.
- `render_showcase_lab.gd` — headless Godot script that renders layered cards with the mod's real `CardLayerShader.gdshader` and `CardEffectShader.gdshader`.
- `build_media.py` — regenerates every file under `../assets/` and `../press/` in one command.
- `out/` — temporary raw renders, frames and diagnostic verification sheets (gitignored via `../.gitignore`).

## Prerequisites

- Python 3.12+ with `Pillow`, `numpy` and `opencv-python` (`cv2`)
- A built `full_art_spire` mod folder (e.g., from `dist/<version>/full_art_spire`)
- The game roster JSON containing `runtime_model` and `rarity`
- Exported original game portraits organized as `<references>/<pool>/<canonical_asset_name>.png`
- An installed copy of Slay the Spire 2 (used only in headless `--script` mode; never writes to the game folder)

## Regenerate All Media

Run one command from the site root (or pass paths via environment variables `FULL_ART_STAGE`, `FULL_ART_ROSTER`, `FULL_ART_REFERENCES`, `STS2_EXE`):

```bash
python _build/build_media.py \
  --stage <path-to-built-full_art_spire> \
  --roster <path-to-roster.json> \
  --references <path-to-exported-portraits> \
  --mod-src <path-to-src/FullArtSpire> \
  --game "<path-to-SlayTheSpire2.exe>"
```

Pass `--skip-godot` to re-encode WebP/JPG/PNG outputs using the already-rendered frames in `_build/out/renders/`.

After running, check `_build/out/verify_before_after.jpg` to visually verify pixel alignment across the 50/50 split seam of each Before/After card.

## Filling In Store Links & Trailer

Edit the single `SITE_CONFIG` object at the top of `../script.js`:

```js
const SITE_CONFIG = {
  steamWorkshopUrl: "https://steamcommunity.com/sharedfiles/filedetails/?id=...",
  nexusModsUrl: "https://www.nexusmods.com/slaythespire2/mods/...",
  youtubeVideoId: "YOUR_11_CHAR_ID",
};
```

- Leaving `steamWorkshopUrl` or `nexusModsUrl` blank keeps that button disabled with a **Coming soon** badge.
- Leaving `youtubeVideoId` blank keeps the **Trailer** section hidden and loads nothing from YouTube. When filled in, the page embeds `https://www.youtube-nocookie.com/embed/<id>`.

## In-Game Captures

Real v0.113.1 captures sit under `../assets/ingame-*.webp` (the BaseLib Card packs settings, the live pack-on/pack-off combat comparison pair, and the six-shot in-game gallery) alongside the silent 720p gameplay reel at `../assets/ingame-reel.mp4` (poster `../assets/ingame-reel-poster.webp`). `build_media.py` does not touch `ingame-*` files.

## Press / Store Images

The Workshop thumbnail, the social card and the banner are drawn by `press.html`
(the site's fonts, stickers and card art) and screenshotted by `render_press.mjs`
with a local headless Chrome or Edge. Node 22+ is enough; nothing is downloaded.

```bash
node _build/render_press.mjs            # all three
node _build/render_press.mjs --only square
```

- `press/workshop-preview.png`: 864×864 PNG, Steam Workshop preview, kept under 1,000,000 bytes; it includes a Fool card
- `assets/og-card.jpg`: 1200×630 Open Graph / Twitter card
- `press/hero-1920x1080.jpg`: 1920×1080 banner (Nexus header, video thumbnail)

`build_media.py` also writes these three files in its older style, so run
`render_press.mjs` after it. Open `press.html?layout=square|og|banner` in a
browser to preview a layout while editing it.

`press/gallery-1-common.jpg` … `press/gallery-4-ancient.jpg` (1600×900 tier
strips) still come from `build_media.py`. Nothing under `press/` is linked from the page.

## Fonts

`fonts/mona-sans.woff2` is Mona Sans (variable weight and width) under the SIL
Open Font License; the licence sits next to it in `fonts/OFL.txt`.
