"""Regenerate all showcase website assets and store press images for Full Art Spire.

Reads:
  - A built Full Art Spire mod folder (--stage)
  - A game roster JSON with runtime_model -> rarity (--roster)
  - Exported original game portraits organized as <pool>/<canonical_asset_name>.png (--references)
  - The mod's Godot shaders and RarityShowcase.cs (--mod-src, defaults to ../../src/FullArtSpire)
  - Slay the Spire 2 executable (--game) for headless shader script rendering

Writes:
  - site/assets/ (WebP loops, stills, before/after pairs, depth layers, gallery thumbs & fulls, OG image)
  - site/press/  (1920x1080 hero JPG, four 1600x900 tier gallery JPGs, square Workshop preview PNG < 1 MB)
  - site/_build/out/ (raw renders, frames, diagnostic sheets; gitignored)

These are headless shader script renders, not in-game play QA.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import subprocess
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BUILD_DIR = Path(__file__).resolve().parent
SITE_DIR = BUILD_DIR.parent
ASSETS_DIR = SITE_DIR / "assets"
PRESS_DIR = SITE_DIR / "press"
OUT_DIR = BUILD_DIR / "out"
CARDS_JSON = BUILD_DIR / "cards.json"
GODOT_SCRIPT = BUILD_DIR / "render_showcase_lab.gd"

SURFACES = {
    "Shimmer": 1, "Foil": 2, "Holo": 3, "Prismatic": 4, "BrushedSilver": 5, "Ember": 6, "Frost": 7,
    "DiffractiveFoil": 8, "EtchedFoil": 9, "GlitterFoil": 10, "CrackedIce": 11, "OilSlick": 12,
    "Pearlescent": 13, "Embossed": 14, "Lenticular": 15, "Aurora": 16, "Starfield": 17, "Circuit": 18,
}
TIERS = ["Basic", "Common", "Uncommon", "Rare", "Ancient", "Event", "Quest", "Token", "Status", "Curse"]
FONTS_DIR = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts"

LOOP_FRAMES = 36
LOOP_DURATION_MS = 85
MAX_LOOP_BYTES = 1_480_000
STEAM_PREVIEW_LIMIT = 1_000_000


def parse_showcase_tiers(rarity_cs: Path) -> dict[str, dict]:
    source = rarity_cs.read_text(encoding="utf-8")
    rows = dict(re.findall(r"ShowcaseTier (\w+) = new\(([^;]+)\);", source))
    aliases = {"Basic": "Starter"}
    tiers: dict[str, dict] = {}
    for rarity in TIERS:
        values = [part.strip() for part in rows[aliases.get(rarity, rarity)].split(",")]
        surface = values[0].removeprefix("EffectSurface.")
        nums = [float(v.rstrip("f")) for v in values[1:]]
        tiers[rarity] = {
            "surface": SURFACES[surface],
            "surface_name": surface,
            "strength": nums[0],
            "sparkle": nums[1],
            "backdrop": nums[2],
            "aura": nums[3],
            "aura_spectral": nums[4],
            "tier_color": nums[5:8],
            "alpha_cap": nums[8],
        }
    return tiers


def load_dds_map(stage: Path) -> dict[str, str]:
    audit = stage / "gpu-textures.audit"
    if not audit.is_file():
        return {}
    rows = json.loads(audit.read_text(encoding="utf-8"))["textures"]
    return {row["source"]: row["path"] for row in rows}


def resolve_shipped(stage: Path, rel: str, dds_map: dict[str, str]) -> Path:
    direct = stage / rel
    if direct.is_file():
        return direct
    if rel in dds_map:
        alt = stage / dds_map[rel]
        if alt.is_file():
            return alt
    raise FileNotFoundError(f"Missing shipped asset: {rel}")


def load_packs(stage: Path) -> dict[str, dict[str, dict]]:
    packs: dict[str, dict[str, dict]] = {}
    for pack_path in sorted((stage / "packs").glob("*.pack")):
        data = json.loads(pack_path.read_text(encoding="utf-8"))
        packs[pack_path.stem] = {c["canonical_asset_name"]: c for c in data["cards"]}
    return packs


def card_layer_uniforms(entry: dict) -> dict:
    layers = entry["layers"]
    defaults = (
        ("background_shift", 0.010),
        ("foreground_shift", 0.022),
        ("foreground_scale", 1.035),
        ("subject_scale", 1.0),
        ("subject_offset_x", 0.0),
        ("subject_offset_y", 0.0),
        ("shadow_strength", 0.28),
        ("frame_inset", 0.055),
    )
    return {k: layers.get(k, d) for k, d in defaults}


def make_render_job(
    job_name: str,
    entry: dict,
    stage: Path,
    dds_map: dict[str, str],
    width: int,
    tilt: list[float],
    tier: dict | None,
    phase: float = 0.25,
    pop_strength: float = 0.5,
) -> dict:
    layers = entry["layers"]
    layer_u = dict(
        card_layer_uniforms(entry),
        depth=1.0,
        pop_strength=pop_strength,
        pointer=tilt,
        view_pointer=tilt,
    )
    job = {
        "name": job_name,
        "art": str(resolve_shipped(stage, entry["art"], dds_map)),
        "background": str(resolve_shipped(stage, layers["background"], dds_map)),
        "mask": str(stage / layers["foreground_mask"]),
        "width": width,
        "layer": layer_u,
    }
    if tier is not None:
        job["effect"] = {
            "surface_type": tier["surface"],
            "intensity": tier["strength"],
            "phase": phase,
            "motion_scale": 0.0,
            "view_tilt": tilt,
            "sparkle_strength": tier["sparkle"],
            "backdrop_strength": tier["backdrop"],
            "aura_strength": tier["aura"],
            "aura_spectral": tier["aura_spectral"],
            "tier_color": tier["tier_color"],
            "alpha_cap": tier["alpha_cap"],
        }
    return job


def run_godot_jobs(jobs: list[dict], render_dir: Path, game_exe: Path, shaders_dir: Path) -> None:
    render_dir.mkdir(parents=True, exist_ok=True)
    plan_path = render_dir / "plan.json"
    plan_path.write_text(json.dumps(jobs, indent=1), encoding="utf-8")
    command = [
        str(game_exe),
        "--display-driver", "windows",
        "--rendering-method", "gl_compatibility",
        "--audio-driver", "Dummy",
        "--windowed",
        "--resolution", "64x64",
        "--position", "-10000,-10000",
        "--script", str(GODOT_SCRIPT),
        "--",
        str(shaders_dir / "CardLayerShader.gdshader"),
        str(shaders_dir / "CardEffectShader.gdshader"),
        str(plan_path),
        str(render_dir),
    ]
    print(f"Running Godot headless renderer for {len(jobs)} frames...")
    result = subprocess.run(command, cwd=game_exe.parent, text=True, capture_output=True, timeout=600)
    (render_dir / "stdout.txt").write_text(result.stdout, encoding="utf-8")
    (render_dir / "stderr.txt").write_text(result.stderr, encoding="utf-8")
    expected = f"SHOWCASE_LAB_COMPLETE {len(jobs)}"
    if (
        result.returncode != 0
        or expected not in result.stdout
        or "SHADER ERROR" in result.stderr
        or "SCRIPT ERROR" in result.stderr
    ):
        raise SystemExit(f"Godot render failed (rc={result.returncode}):\n{result.stdout[-2500:]}\n{result.stderr[-2500:]}")
    print(f"  {expected}")


def save_animated_webp(frame_paths: list[Path], out_path: Path, duration_ms: int = LOOP_DURATION_MS) -> int:
    frames = [Image.open(p).convert("RGB") for p in frame_paths]
    try:
        for quality in (82, 76, 70, 64, 58):
            frames[0].save(
                out_path,
                format="WEBP",
                save_all=True,
                append_images=frames[1:],
                duration=duration_ms,
                loop=0,
                quality=quality,
                method=4,
            )
            size = out_path.stat().st_size
            if size <= MAX_LOOP_BYTES:
                print(f"  wrote {out_path.name} ({frames[0].width}x{frames[0].height}, {len(frames)}f, q={quality}, {size / 1024:.0f} KB)")
                return size
        raise SystemExit(f"Animated WebP {out_path} still exceeds {MAX_LOOP_BYTES} bytes ({out_path.stat().st_size})")
    finally:
        for im in frames:
            im.close()


def save_webp_still(src: Path | Image.Image, out_path: Path, max_side: int | None = None, quality: int = 82) -> int:
    im = Image.open(src).convert("RGB") if isinstance(src, Path) else src
    close_after = isinstance(src, Path)
    try:
        if max_side is not None and max(im.width, im.height) > max_side:
            scale = max_side / max(im.width, im.height)
            im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
        im.save(out_path, format="WEBP", quality=quality, method=6)
        return out_path.stat().st_size
    finally:
        if close_after:
            im.close()


def match_portrait_exact(full_rgb: np.ndarray, ref_rgb: np.ndarray) -> tuple[float, int, int, int, int]:
    """Two-stage multi-scale template matching returning (score, x, y, w, h) in full_rgb coords."""
    fh, fw = full_rgb.shape[:2]
    rh, rw = ref_rgb.shape[:2]
    full_gray = cv2.cvtColor(full_rgb, cv2.COLOR_RGB2GRAY)
    ref_gray = cv2.cvtColor(ref_rgb, cv2.COLOR_RGB2GRAY)

    # Coarse pass at width=320
    c_scale = 320.0 / fw
    full_c = cv2.resize(full_gray, (320, round(fh * c_scale)), interpolation=cv2.INTER_AREA)
    best_c = (-1.0, 1.0, 0, 0)
    for w_frac in np.linspace(0.65, 1.04, 40):
        tw = round(320 * w_frac)
        th = round(tw * rh / rw)
        if tw < 32 or th < 32 or th > full_c.shape[0]:
            continue
        tpl = cv2.resize(ref_gray, (tw, th), interpolation=cv2.INTER_AREA)
        crop_x = max(0, (tw - full_c.shape[1]) // 2)
        tpl_use = tpl[:, crop_x:crop_x + full_c.shape[1]] if tw > full_c.shape[1] else tpl
        res = cv2.matchTemplate(full_c, tpl_use, cv2.TM_CCOEFF_NORMED)
        _, score, _, loc = cv2.minMaxLoc(res)
        if score > best_c[0]:
            best_c = (float(score), float(w_frac), int(loc[0] - crop_x), int(loc[1]))

    # Fine pass at target full_rgb resolution around best coarse scale
    best_f = (-1.0, 0, 0, 0, 0)
    center_w = best_c[1] * fw
    for tw in range(max(64, round(center_w) - 14), round(center_w) + 15, 2):
        th = round(tw * rh / rw)
        if th > fh:
            continue
        tpl = cv2.resize(ref_gray, (tw, th), interpolation=cv2.INTER_AREA)
        crop_x = max(0, (tw - fw) // 2)
        tpl_use = tpl[:, crop_x:crop_x + fw] if tw > fw else tpl
        res = cv2.matchTemplate(full_gray, tpl_use, cv2.TM_CCOEFF_NORMED)
        _, score, _, loc = cv2.minMaxLoc(res)
        if score > best_f[0]:
            best_f = (float(score), int(loc[0] - crop_x), int(loc[1]), int(tw), int(th))
    return best_f


def build_before_after(
    items: list[dict],
    packs: dict[str, dict[str, dict]],
    stage: Path,
    dds_map: dict[str, str],
    refs_dir: Path,
) -> list[dict]:
    results = []
    verify_strip_imgs = []
    target_w, target_h = 640, 960
    for item in items:
        pool, name = item["pool"], item["name"]
        entry = packs[pool][name]
        art = Image.open(resolve_shipped(stage, entry["art"], dds_map)).convert("RGB")
        art_resized = art.resize((target_w, target_h), Image.LANCZOS)
        ref = Image.open(refs_dir / pool / f"{name}.png").convert("RGB")

        score, x0, y0, w0, h0 = match_portrait_exact(np.array(art_resized), np.array(ref))
        if score < 0.90:
            raise SystemExit(f"Before/after template match too low for {pool}/{name}: {score:.3f}")

        # Darken and softly desaturate everything outside the original portrait
        arr = np.array(art_resized).astype(np.float32)
        luma = arr[:, :, 0:1] * 0.299 + arr[:, :, 1:2] * 0.587 + arr[:, :, 2:3] * 0.114
        desat = arr * 0.45 + luma * 0.55
        darkened = np.clip(desat * 0.18, 0, 255).astype(np.uint8)
        before = Image.fromarray(darkened).convert("RGBA")

        # Subtle outer shadow behind the original portrait crop
        shadow_layer = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
        sdraw = ImageDraw.Draw(shadow_layer)
        sdraw.rectangle((x0 - 2, y0 - 2, x0 + w0 + 1, y0 + h0 + 1), fill=(0, 0, 0, 180))
        shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(8))
        before = Image.alpha_composite(before, shadow_layer)

        # Paste original portrait at its exact matched position and scale
        ref_scaled = ref.resize((w0, h0), Image.LANCZOS).convert("RGBA")
        before.paste(ref_scaled, (x0, y0))

        # Crisp warm hairline border delineating the original game portrait
        border_layer = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
        bdraw = ImageDraw.Draw(border_layer)
        bx0 = max(0, x0)
        by0 = max(0, y0)
        bx1 = min(target_w - 1, x0 + w0 - 1)
        by1 = min(target_h - 1, y0 + h0 - 1)
        bdraw.rectangle((bx0, by0, bx1, by1), outline=(238, 206, 138, 210), width=2)
        before = Image.alpha_composite(before, border_layer).convert("RGB")

        before_path = ASSETS_DIR / f"{item['id']}-before.webp"
        after_path = ASSETS_DIR / f"{item['id']}-after.webp"
        sb = save_webp_still(before, before_path, quality=83)
        sa = save_webp_still(art_resized, after_path, quality=83)
        print(f"  before/after {pool}/{name}: score={score:.4f} box=({x0},{y0},{w0},{h0}) before={sb/1024:.0f}KB after={sa/1024:.0f}KB")

        # Build half-and-half split verification image to confirm zero seam drift
        split = before.copy()
        split.paste(art_resized.crop((target_w // 2, 0, target_w, target_h)), (target_w // 2, 0))
        ImageDraw.Draw(split).line((target_w // 2, 0, target_w // 2, target_h), fill="#ffffff", width=2)
        verify_strip_imgs.append((f"{pool}/{name} ({score:.3f})", before, split, art_resized))
        results.append({
            "id": item["id"],
            "pool": pool,
            "name": name,
            "display_name": entry["display_name"],
            "score": round(score, 4),
            "box": [x0, y0, w0, h0],
        })

    # Diagnostic contact sheet in _build/out/
    cw, ch = 260, 390
    sheet = Image.new("RGB", (cw * 3 + 32, len(verify_strip_imgs) * (ch + 36) + 16), "#0d111a")
    draw = ImageDraw.Draw(sheet)
    fnt = load_font("segoeui.ttf", 15)
    for idx, (label, b_im, s_im, a_im) in enumerate(verify_strip_imgs):
        y = 12 + idx * (ch + 36)
        draw.text((12, y), f"{label}   [Before | 50/50 Split Seam Check | After]", font=fnt, fill="#f3ebd8")
        for col, im in enumerate((b_im, s_im, a_im)):
            sheet.paste(im.resize((cw, ch), Image.LANCZOS), (12 + col * (cw + 4), y + 24))
    sheet.save(OUT_DIR / "verify_before_after.jpg", quality=88)
    return results


def build_depth_layers(
    items: list[dict],
    packs: dict[str, dict[str, dict]],
    stage: Path,
    dds_map: dict[str, str],
    render_dir: Path,
) -> list[dict]:
    meta = []
    target_w, target_h = 600, 900
    for item in items:
        pool, name = item["pool"], item["name"]
        entry = packs[pool][name]
        layers = entry["layers"]
        art = Image.open(resolve_shipped(stage, entry["art"], dds_map)).convert("RGB").resize((target_w, target_h), Image.LANCZOS)
        bg = Image.open(resolve_shipped(stage, layers["background"], dds_map)).convert("RGB").resize((target_w, target_h), Image.LANCZOS)
        mask = Image.open(stage / layers["foreground_mask"])
        mask_r = (mask.split()[0] if mask.mode != "L" else mask).resize((target_w, target_h), Image.BILINEAR)

        fg = art.convert("RGBA")
        fg.putalpha(mask_r)

        bg_path = ASSETS_DIR / f"{item['id']}-bg.webp"
        fg_path = ASSETS_DIR / f"{item['id']}-fg.webp"
        still_path = ASSETS_DIR / f"{item['id']}-still.webp"

        bg.save(bg_path, format="WEBP", quality=82, method=6)
        fg.save(fg_path, format="WEBP", quality=85, method=6)
        still_src = render_dir / f"{item['id']}-still.png"
        save_webp_still(still_src, still_path, max_side=target_h, quality=82)
        u = card_layer_uniforms(entry)
        print(
            f"  depth {pool}/{name}: bg={bg_path.stat().st_size/1024:.0f}KB "
            f"fg={fg_path.stat().st_size/1024:.0f}KB still={still_path.stat().st_size/1024:.0f}KB"
        )
        meta.append({
            "id": item["id"],
            "pool": pool,
            "name": name,
            "display_name": entry["display_name"],
            "uniforms": u,
        })
    return meta


def build_gallery_cards(
    spec: dict,
    packs: dict[str, dict[str, dict]],
    rarity_map: dict[str, str],
    stage: Path,
    dds_map: dict[str, str],
) -> dict:
    thumb_dir = ASSETS_DIR / "cards" / "thumb"
    full_dir = ASSETS_DIR / "cards" / "full"
    thumb_dir.mkdir(parents=True, exist_ok=True)
    full_dir.mkdir(parents=True, exist_ok=True)

    needed: list[tuple[str, str]] = []
    for name in spec["fool"]:
        needed.append(("fool", name))
    for pool, names in spec["gallery"].items():
        for name in names:
            needed.append((pool, name))

    seen: set[tuple[str, str]] = set()
    catalog: dict[str, dict] = {}
    for pool, name in needed:
        if (pool, name) in seen:
            continue
        seen.add((pool, name))
        entry = packs[pool][name]
        art = Image.open(resolve_shipped(stage, entry["art"], dds_map)).convert("RGB")
        key = f"{pool}-{name}"
        t_path = thumb_dir / f"{key}.webp"
        f_path = full_dir / f"{key}.webp"
        save_webp_still(art, t_path, max_side=360, quality=80)
        save_webp_still(art, f_path, max_side=960, quality=82)
        catalog[key] = {
            "pool": pool,
            "name": name,
            "display_name": entry["display_name"],
            "rarity": rarity_map.get(entry["runtime_model"], "Unknown"),
            "thumb": f"assets/cards/thumb/{key}.webp",
            "full": f"assets/cards/full/{key}.webp",
        }
    print(f"  exported {len(catalog)} gallery/fool cards (thumbs <=360px, fulls <=960px)")
    return catalog


def load_font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS_DIR / name), size)


def make_backdrop(size: tuple[int, int], accent_rgb: tuple[int, int, int] = (46, 58, 92)) -> Image.Image:
    width, height = size
    base = Image.new("RGB", size, "#090d16")
    glow = Image.new("L", size, 0)
    ImageDraw.Draw(glow).ellipse((width * 0.08, -height * 0.05, width * 0.92, height * 1.05), fill=145)
    glow = glow.filter(ImageFilter.GaussianBlur(min(size) // 4))
    bg = Image.composite(Image.new("RGB", size, accent_rgb), base, glow)

    # Subtle warm gold top ambient bloom
    warm = Image.new("L", size, 0)
    ImageDraw.Draw(warm).ellipse((width * 0.25, -height * 0.35, width * 0.75, height * 0.35), fill=42)
    warm = warm.filter(ImageFilter.GaussianBlur(min(size) // 5))
    return Image.composite(Image.new("RGB", size, (130, 102, 56)), bg, warm)


def framed_card(src_png: Path, height: int, radius: int = 12) -> Image.Image:
    with Image.open(src_png) as im:
        card = im.convert("RGBA")
    width = round(card.width * height / card.height)
    card = card.resize((width, height), Image.LANCZOS)

    # Rounded card mask + crisp 1px rim highlight
    mask = Image.new("L", (width, height), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, width - 1, height - 1), radius=radius, fill=255)
    clipped = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    clipped.paste(card, (0, 0), mask)
    ImageDraw.Draw(clipped).rounded_rectangle(
        (0, 0, width - 1, height - 1), radius=radius, outline=(235, 212, 162, 72), width=2
    )

    pad = 44
    canvas = Image.new("RGBA", (width + pad * 2, height + pad * 2), (0, 0, 0, 0))
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        (pad, pad + 14, pad + width, pad + 14 + height), radius=radius, fill=(0, 0, 0, 195)
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    canvas = Image.alpha_composite(canvas, shadow)
    canvas.alpha_composite(clipped, (pad, pad))
    return canvas


def clean_display_name(raw: str) -> str:
    special = {"Ascenders Bane": "Ascender's Bane"}
    if raw in special:
        return special[raw]
    words = raw.split(" ")
    minor = {"of", "the", "a", "an", "in", "to", "for", "and"}
    out = [words[0]]
    for w in words[1:]:
        out.append(w.lower() if w.lower() in minor else w)
    return " ".join(out)


def build_press_and_og(
    spec: dict,
    packs: dict[str, dict[str, dict]],
    render_dir: Path,
) -> None:
    PRESS_DIR.mkdir(parents=True, exist_ok=True)
    finishes = spec["finishes"]

    # 1. Store Hero 1920x1080 JPG (Nexus header / YouTube thumbnail)
    hero_img = make_backdrop((1920, 1080)).convert("RGBA")
    draw = ImageDraw.Draw(hero_img)
    draw.text((960, 42), "Full Art Spire", font=load_font("georgiab.ttf", 72), fill="#f6efe0", anchor="mt")
    draw.text(
        (960, 130),
        "667 full-art cards for Slay the Spire 2  ·  Layered depth & rarity finishes",
        font=load_font("segoeui.ttf", 28),
        fill="#bcc8dc",
        anchor="mt",
    )
    draw.line((740, 178, 1180, 178), fill=(216, 174, 96, 120), width=2)

    for idx, f in enumerate(finishes):
        entry = packs[f["pool"]][f["name"]]
        card_im = framed_card(render_dir / f"press-{f['id']}-left.png", height=625, radius=14)
        cx = 288 + idx * 448
        hero_img.alpha_composite(card_im, (cx - card_im.width // 2, 185))
        draw.text((cx, 882), clean_display_name(entry["display_name"]), font=load_font("georgiab.ttf", 30), fill="#f4ecd8", anchor="mt")
        tier_line = f"{f['tier']}  ·  {f['finish_label']}"
        draw.text((cx, 924), tier_line, font=load_font("segoeui.ttf", 21), fill="#d9b976" if idx >= 2 else "#9db0cc", anchor="mt")
        if f["pool"] == "fool":
            draw.text((cx, 956), "The Fool (public beta)", font=load_font("segoeui.ttf", 18), fill="#8d9db8", anchor="mt")

    draw.text(
        (960, 1042),
        "Unofficial visual-only fan mod  ·  Slay the Spire 2 art, characters and designs © Mega Crit",
        font=load_font("segoeui.ttf", 20),
        fill="#76849d",
        anchor="mb",
    )
    hero_path = PRESS_DIR / "hero-1920x1080.jpg"
    hero_img.convert("RGB").save(hero_path, quality=90, optimize=True, progressive=True)
    print(f"  wrote press/{hero_path.name} ({hero_path.stat().st_size / 1024:.0f} KB)")

    # 2. Four 1600x900 Tier Gallery JPGs
    tier_accents = {
        "common": (42, 56, 84),
        "uncommon": (36, 62, 88),
        "rare": (76, 56, 36),
        "ancient": (56, 44, 86),
    }
    for idx, f in enumerate(finishes, start=1):
        entry = packs[f["pool"]][f["name"]]
        img = make_backdrop((1600, 900), accent_rgb=tier_accents[f["id"]]).convert("RGBA")
        d = ImageDraw.Draw(img)
        d.text(
            (800, 30),
            f"Full Art Spire  —  {f['tier']}: {f['finish_label']}",
            font=load_font("georgiab.ttf", 44),
            fill="#f6efe0",
            anchor="mt",
        )
        d.text((800, 88), f["finish_desc"], font=load_font("segoeui.ttf", 24), fill="#b8c5da", anchor="mt")
        shots = [("left", "Tilted left"), ("rest", "At rest"), ("right", "Tilted right")]
        for col, (shot_key, shot_lbl) in enumerate(shots):
            card_im = framed_card(render_dir / f"press-{f['id']}-{shot_key}.png", height=595, radius=13)
            cx = 310 + col * 490
            img.alpha_composite(card_im, (cx - card_im.width // 2, 118))
            d.text((cx, 785), shot_lbl, font=load_font("segoeui.ttf", 20), fill="#95a6c2", anchor="mt")

        pool_title = "The Fool" if f["pool"] == "fool" else f["pool"].capitalize()
        d.text(
            (800, 832),
            f"{clean_display_name(entry['display_name'])}  ({pool_title} · {f['tier']})   —   Original card art © Mega Crit",
            font=load_font("segoeui.ttf", 22),
            fill="#d9c698",
            anchor="mt",
        )
        g_path = PRESS_DIR / f"gallery-{idx}-{f['id']}.jpg"
        img.convert("RGB").save(g_path, quality=90, optimize=True, progressive=True)
        print(f"  wrote press/{g_path.name} ({g_path.stat().st_size / 1024:.0f} KB)")

    # 3. Square Workshop Preview PNG (>= 640px, strictly < 1,000,000 bytes, includes The Fool!)
    ws_path = PRESS_DIR / "workshop-preview.png"
    for side in (864, 800, 768, 720, 680, 640):
        s = side / 1024.0
        ws = make_backdrop((side, side)).convert("RGBA")
        wd = ImageDraw.Draw(ws)
        wd.text((side // 2, round(24 * s)), "Full Art Spire", font=load_font("georgiab.ttf", round(72 * s)), fill="#f6efe0", anchor="mt")
        wd.text(
            (side // 2, round(106 * s)),
            "667 full-art cards · Layered depth · Includes The Fool",
            font=load_font("segoeui.ttf", round(26 * s)),
            fill="#bcc8dc",
            anchor="mt",
        )
        card_h = round(354 * s)
        for idx, f in enumerate(finishes):
            card_im = framed_card(render_dir / f"press-{f['id']}-right.png", height=card_h, radius=max(6, round(11 * s)))
            cx = round((292 + (idx % 2) * 440) * s)
            cy = round((166 + (idx // 2) * 422) * s)
            ws.alpha_composite(card_im, (cx - card_im.width // 2, cy - 44))
            lbl = f"{f['tier']} · The Fool" if f["pool"] == "fool" else f["tier"]
            wd.text(
                (cx, cy + card_h + round(14 * s)),
                lbl,
                font=load_font("segoeuib.ttf", round(24 * s)),
                fill="#ebd194" if f["pool"] == "fool" else "#dce3f0",
                anchor="mt",
            )
        ws.convert("RGB").save(ws_path, format="PNG", optimize=True, compress_level=9)
        sz = ws_path.stat().st_size
        if sz < STEAM_PREVIEW_LIMIT:
            print(f"  wrote press/{ws_path.name} ({side}x{side}, {sz:,} bytes)")
            break
    else:
        raise SystemExit(f"Workshop preview stays above {STEAM_PREVIEW_LIMIT} bytes")

    # 4. Open Graph / Twitter Card 1200x630 JPG
    og = make_backdrop((1200, 630), accent_rgb=(42, 54, 88)).convert("RGBA")
    od = ImageDraw.Draw(og)
    od.rectangle((56, 148, 62, 474), fill=(216, 174, 96, 220))
    od.text((82, 150), "SLAY THE SPIRE 2 VISUAL MOD", font=load_font("segoeuib.ttf", 19), fill="#d8b064")
    od.text((82, 190), "Full Art Spire", font=load_font("georgiab.ttf", 58), fill="#f6efe0")
    od.text((82, 274), "667 full-art cards with layered\nparallax depth and rarity finishes.", font=load_font("segoeui.ttf", 26), fill="#c7d2e4", spacing=8)
    od.text((82, 386), "12 packs · Includes The Fool (public beta)\nVisual only · Requires BaseLib", font=load_font("segoeui.ttf", 21), fill="#90a2be", spacing=8)
    od.text((82, 558), "Unofficial fan project · Art © Mega Crit", font=load_font("segoeui.ttf", 18), fill="#6f7d96")

    # Right side: 3 overlapping real shader renders with clean separation from title
    og_cards = [
        (render_dir / "press-common-left.png", 734, 142, 344),
        (render_dir / "press-rare-left.png", 1048, 142, 344),
        (render_dir / "hero-still.png", 891, 98, 424),
    ]
    for src, cx, top, h in og_cards:
        im = framed_card(src, height=h, radius=12)
        og.alpha_composite(im, (cx - im.width // 2, top - 44))

    og_path = ASSETS_DIR / "og-card.jpg"
    og.convert("RGB").save(og_path, quality=88, optimize=True, progressive=True)
    print(f"  wrote assets/{og_path.name} ({og_path.stat().st_size / 1024:.0f} KB)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", type=Path, default=Path(os.environ.get("FULL_ART_STAGE", "")), help="Built full_art_spire mod folder")
    parser.add_argument("--roster", type=Path, default=Path(os.environ.get("FULL_ART_ROSTER", "")), help="Game roster JSON with rarities")
    parser.add_argument("--references", type=Path, default=Path(os.environ.get("FULL_ART_REFERENCES", "")), help="Exported original portrait directory")
    parser.add_argument("--mod-src", type=Path, default=SITE_DIR.parent / "src" / "FullArtSpire", help="Path to src/FullArtSpire")
    parser.add_argument("--game", type=Path, default=Path(os.environ.get("STS2_EXE", r"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe")), help="Path to SlayTheSpire2.exe")
    parser.add_argument("--skip-godot", action="store_true", help="Reuse existing Godot renders in _build/out/renders")
    args = parser.parse_args()

    for flag, path in (("--stage", args.stage), ("--roster", args.roster), ("--references", args.references)):
        if not str(path) or not path.exists():
            raise SystemExit(f"Required input {flag} is missing or does not exist: {path}")

    spec = json.loads(CARDS_JSON.read_text(encoding="utf-8"))
    stage = args.stage.resolve()
    dds_map = load_dds_map(stage)
    packs = load_packs(stage)
    roster = json.loads(args.roster.read_text(encoding="utf-8"))
    rarity_map = {c["runtime_model"]: c["rarity"] for c in roster["cards"]}
    tiers = parse_showcase_tiers(args.mod_src.resolve() / "RarityShowcase.cs")
    shaders_dir = args.mod_src.resolve() / "Shaders"

    ASSETS_DIR.mkdir(parents=True, exist_ok=True)
    PRESS_DIR.mkdir(parents=True, exist_ok=True)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    render_dir = OUT_DIR / "renders"

    # Build the unified Godot job plan
    jobs: list[dict] = []

    def add_loop_jobs(prefix: str, entry: dict, tier: dict, width: int) -> None:
        # Smooth closed elliptical orbit for pointer/tilt + periodic phase oscillation
        # so sin(phase) and spectrum(... + phase) both wrap cleanly with zero seam.
        for i in range(LOOP_FRAMES):
            theta = 2.0 * math.pi * (i / LOOP_FRAMES)
            tilt = [round(0.80 * math.cos(theta), 4), round(0.42 * math.sin(theta), 4)]
            phase = round(0.25 + 0.85 * math.sin(theta - 0.65), 4)
            jobs.append(make_render_job(f"{prefix}-{i:02d}", entry, stage, dds_map, width, tilt, tier, phase=phase, pop_strength=0.55))
        jobs.append(make_render_job(f"{prefix}-still", entry, stage, dds_map, width, [-0.55, 0.22], tier, phase=0.25, pop_strength=0.55))

    # Hero loop + high-res still for OG card
    hero_entry = packs[spec["hero"]["pool"]][spec["hero"]["name"]]
    hero_rarity = rarity_map[hero_entry["runtime_model"]]
    add_loop_jobs("hero", hero_entry, tiers[hero_rarity], width=400)

    # 4 Rarity finish loops + high-res 3-angle press stills (768px)
    for f in spec["finishes"]:
        entry = packs[f["pool"]][f["name"]]
        actual_rarity = rarity_map[entry["runtime_model"]]
        if actual_rarity != f["tier"]:
            raise SystemExit(f"Rarity mismatch for {f['pool']}/{f['name']}: expected {f['tier']}, got {actual_rarity}")
        tier = tiers[actual_rarity]
        add_loop_jobs(f"finish-{f['id']}", entry, tier, width=360)
        for shot_key, tilt in (("rest", [0.0, 0.0]), ("left", [-0.82, 0.30]), ("right", [0.82, -0.30])):
            jobs.append(make_render_job(f"press-{f['id']}-{shot_key}", entry, stage, dds_map, 768, tilt, tier, phase=0.25, pop_strength=0.5))

    # 2 Depth reduced-motion resting renders (no finish overlay, neutral pose)
    for d_item in spec["depth"]:
        entry = packs[d_item["pool"]][d_item["name"]]
        jobs.append(make_render_job(f"{d_item['id']}-still", entry, stage, dds_map, 600, [0.0, 0.0], None, pop_strength=0.0))

    if not args.skip_godot:
        run_godot_jobs(jobs, render_dir, args.game.resolve(), shaders_dir)

    print("Assembling animated WebP loops & stills...")
    save_animated_webp([render_dir / f"hero-{i:02d}.png" for i in range(LOOP_FRAMES)], ASSETS_DIR / "hero.webp")
    save_webp_still(render_dir / "hero-still.png", ASSETS_DIR / "hero-still.webp", quality=84)
    for f in spec["finishes"]:
        save_animated_webp([render_dir / f"finish-{f['id']}-{i:02d}.png" for i in range(LOOP_FRAMES)], ASSETS_DIR / f"finish-{f['id']}.webp")
        save_webp_still(render_dir / f"finish-{f['id']}-still.png", ASSETS_DIR / f"finish-{f['id']}-still.webp", quality=84)

    print("Building Before / After pairs...")
    ba_meta = build_before_after(spec["before_after"], packs, stage, dds_map, args.references.resolve())

    print("Building Depth interactive layers...")
    depth_meta = build_depth_layers(spec["depth"], packs, stage, dds_map, render_dir)

    print("Building Fool & Character gallery cards...")
    catalog = build_gallery_cards(spec, packs, rarity_map, stage, dds_map)

    print("Building Press & Open Graph images...")
    build_press_and_og(spec, packs, render_dir)

    summary = {
        "hero": {
            "pool": spec["hero"]["pool"],
            "name": spec["hero"]["name"],
            "display_name": hero_entry["display_name"],
            "rarity": hero_rarity,
        },
        "before_after": ba_meta,
        "depth": depth_meta,
        "finishes": [
            {**f, "display_name": packs[f["pool"]][f["name"]]["display_name"]}
            for f in spec["finishes"]
        ],
        "catalog": catalog,
    }
    (OUT_DIR / "build_summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print("Done! Build summary written to _build/out/build_summary.json")


if __name__ == "__main__":
    main()
