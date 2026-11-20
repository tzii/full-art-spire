"""Validate Full Art Spire packs.

  --pack <pool>.pack     a v1 pack, checked strictly (the art repository's release gate)
                         against the mod folder that holds its art (--release-root)
  --pack <folder>        a v2 pack folder (or its pack.json), checked against what
                         the runtime accepts
  --mod-folder <folder>  an assembled mod folder: every pack in it, its GPU index,
                         which pack wins each card, and files no card uses

Art may ship as a PNG or only as the GPU variant gpu-textures.audit lists for it.
Image content checks on effect masks need Pillow; everything else is stdlib.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any


EXPECTED_FORMAT = "sts2_full_art_standalone_pack"
EXPECTED_VERSION = 1
CANONICAL_ID_RE = re.compile(r"^[a-z0-9]+(?:_[a-z0-9]+)*$")
REQUIRED_CARD_FIELDS = [
    "canonical_asset_name",
    "display_name",
    "pool",
    "status",
    "source_paths",
    "art",
    "repo_final_png",
    "runtime_model",
    "runtime_model_source",
    "effects",
]
REQUIRED_CARD_SCALAR_FIELDS = [
    "canonical_asset_name",
    "display_name",
    "pool",
    "status",
    "art",
    "repo_final_png",
    "runtime_model",
    "runtime_model_source",
]
OPTIONAL_CARD_SCALAR_FIELDS = [
    "id",
    "name",
    "rarity",
]
REQUIRED_TOP_LEVEL_FIELDS = {
    "format": str,
    "version": int,
    "pool": str,
    "card_count": int,
    "cards": list,
}
REQUIRED_EFFECT_FIELDS = {
    "rarity_flame": str,
    "surface": str,
    "rarity_policy": str,
    "effect_mask": str,
    "protection_mask": str,
    "permission_mask": str,
    "intensity": (int, float),
}
EXPECTED_EFFECT_VALUES = {
    "rarity_flame": "real_rarity",
    "rarity_policy": "rare_first",
}
ALLOWED_EFFECT_SURFACES = {
    "none",
    "shimmer",
    "foil",
    "holo",
    "prismatic",
    "rainbow",
}


def add_issue(issues: list[str], message: str) -> None:
    issues.append(message)


def is_absolute_path(value: str) -> bool:
    windows_path = PureWindowsPath(value)
    return (
        Path(value).is_absolute()
        or windows_path.is_absolute()
        or PurePosixPath(value).is_absolute()
        or bool(windows_path.drive)
        or bool(windows_path.root)
    )


def resolve_under(base: Path, relative_path: str) -> Path | None:
    if is_absolute_path(relative_path):
        return None
    resolved_base = base.resolve()
    resolved_path = (resolved_base / relative_path).resolve(strict=False)
    try:
        resolved_path.relative_to(resolved_base)
    except ValueError:
        return None
    return resolved_path


def resolve_under_named_subdir(base: Path, subdir: str, relative_path: str) -> Path | None:
    resolved_path = resolve_under(base, relative_path)
    if resolved_path is None:
        return None
    resolved_subdir = (base.resolve() / subdir).resolve(strict=False)
    try:
        resolved_path.relative_to(resolved_subdir)
    except ValueError:
        return None
    return resolved_path


def is_png_path(value: str) -> bool:
    return PureWindowsPath(value).suffix.lower() == ".png" or PurePosixPath(value).suffix.lower() == ".png"


def path_parts(value: str) -> tuple[str, ...]:
    return tuple(part for part in PurePosixPath(value.replace("\\", "/")).parts if part not in ("", "."))


def starts_with_path_segment(value: str, expected_segment: str) -> bool:
    parts = path_parts(value)
    return bool(parts) and parts[0] == expected_segment


def card_label(card: dict[str, Any], index: int) -> str:
    return str(card.get("canonical_asset_name") or card.get("display_name") or f"card #{index}")


def has_required_value(value: Any) -> bool:
    return value not in (None, "", [])


def load_pack(path: Path, issues: list[str]) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add_issue(issues, f"could not read pack: {exc}")
        return {}
    if not isinstance(data, dict):
        add_issue(issues, "pack JSON must be an object")
        return {}
    return data


def type_name(expected_type: type | tuple[type, ...]) -> str:
    if expected_type is int:
        return "an integer"
    if expected_type is str:
        return "a string"
    if expected_type is list:
        return "a list"
    if expected_type == (int, float):
        return "numeric"
    return "the expected type"


def is_expected_type(value: Any, expected_type: type | tuple[type, ...]) -> bool:
    if expected_type is int:
        return isinstance(value, int) and not isinstance(value, bool)
    if expected_type == (int, float):
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    return isinstance(value, expected_type)


def string_field(card: dict[str, Any], field: str) -> str:
    value = card.get(field)
    return value.strip() if isinstance(value, str) else ""


def validate_card_scalar_fields(label: str, card: dict[str, Any], issues: list[str]) -> None:
    if "preserve_original_layout" in card and not isinstance(card["preserve_original_layout"], bool):
        add_issue(issues, f"{label}: preserve_original_layout must be a boolean")
    for field in REQUIRED_CARD_SCALAR_FIELDS:
        value = card.get(field)
        if not isinstance(value, str) or not value.strip():
            add_issue(issues, f"{label}: {field} must be a non-empty string")

    for field in OPTIONAL_CARD_SCALAR_FIELDS:
        if field not in card or card[field] is None:
            continue
        value = card[field]
        if not isinstance(value, str) or not value.strip():
            add_issue(issues, f"{label}: {field} must be a non-empty string")


def validate_top_level_schema(pack: dict[str, Any], issues: list[str]) -> list[dict[str, Any]]:
    for field, expected_type in REQUIRED_TOP_LEVEL_FIELDS.items():
        if field not in pack:
            add_issue(issues, f"missing top-level field {field}")
            continue
        if not is_expected_type(pack[field], expected_type):
            add_issue(issues, f"{field} must be {type_name(expected_type)}")

    pack_format = pack.get("format")
    if isinstance(pack_format, str) and pack_format != EXPECTED_FORMAT:
        add_issue(issues, f"format must be {EXPECTED_FORMAT!r}")

    version = pack.get("version")
    if isinstance(version, int) and not isinstance(version, bool) and version != EXPECTED_VERSION:
        add_issue(issues, f"version must be {EXPECTED_VERSION}")

    pool = pack.get("pool")
    if isinstance(pool, str):
        if not pool.strip():
            add_issue(issues, "pool must be a non-empty string")
        elif not CANONICAL_ID_RE.fullmatch(pool.strip()):
            add_issue(issues, "pool must use lowercase snake_case")

    cards = pack.get("cards")
    return cards if isinstance(cards, list) else []


def validate_effects_shape(label: str, effects: dict[str, Any], issues: list[str]) -> None:
    for field, expected_type in REQUIRED_EFFECT_FIELDS.items():
        if field not in effects:
            add_issue(issues, f"{label}: effects missing required field {field}")
            continue
        value = effects[field]
        if not is_expected_type(value, expected_type):
            add_issue(issues, f"{label}: effects.{field} must be {type_name(expected_type)}")
            continue
        if expected_type is str and field not in {"effect_mask", "protection_mask", "permission_mask"} and not value.strip():
            add_issue(issues, f"{label}: effects.{field} must be a non-empty string")

    for field, expected_value in EXPECTED_EFFECT_VALUES.items():
        value = effects.get(field)
        if isinstance(value, str) and value.strip() and value != expected_value:
            add_issue(issues, f"{label}: effects.{field} must be {expected_value!r}")

    surface = effects.get("surface")
    if isinstance(surface, str) and surface.strip() and surface not in ALLOWED_EFFECT_SURFACES:
        allowed = ", ".join(sorted(repr(value) for value in ALLOWED_EFFECT_SURFACES))
        add_issue(issues, f"{label}: effects.surface must be one of {allowed}")


def is_effect_surface_enabled(effects: dict[str, Any]) -> bool:
    surface = effects.get("surface")
    return isinstance(surface, str) and surface.strip().lower() != "none"


def validate_mask_contents(label: str, mask_path: Path, mask: str, issues: list[str]) -> None:
    try:
        from PIL import Image

        with Image.open(mask_path) as image:
            extrema = image.convert("L").getextrema()
    except Exception as exc:
        add_issue(issues, f"{label}: could not inspect mask {mask}: {exc}")
        return

    if extrema[0] == extrema[1]:
        add_issue(issues, f"{label}: uniform mask {mask} ({extrema[0]})")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_enabled_effect(
    label: str,
    effects: dict[str, Any],
    release_root: Path,
    release_art_path: Path | None,
    allow_missing_masks: bool,
    issues: list[str],
    warnings: list[str],
    art_source_sha256: str | None = None,
) -> int:
    missing_masks = 0
    audit = effects.get("audit")
    if not isinstance(audit, dict):
        add_issue(issues, f"{label}: enabled effects require an audit object")
        audit = {}
    if audit.get("review_status") != "approved":
        add_issue(issues, f"{label}: enabled effects require audit.review_status 'approved'")
    if audit.get("automated_status") not in {"candidate", "approved"}:
        add_issue(issues, f"{label}: enabled effects require a passing automated_status")
    coverage = audit.get("permitted_coverage")
    if isinstance(coverage, bool) or not isinstance(coverage, (int, float)) or not 0 <= float(coverage) <= 0.75:
        add_issue(issues, f"{label}: audit.permitted_coverage must be between 0 and 0.75")
    phase_seed = effects.get("phase_seed")
    if phase_seed is not None and (isinstance(phase_seed, bool) or not isinstance(phase_seed, (int, float))):
        add_issue(issues, f"{label}: effects.phase_seed must be numeric or null")

    dimensions = audit.get("dimensions")
    if (
        not isinstance(dimensions, list)
        or len(dimensions) != 2
        or any(isinstance(value, bool) or not isinstance(value, int) or value <= 0 for value in dimensions)
    ):
        add_issue(issues, f"{label}: audit.dimensions must contain two positive integers")
        dimensions = None

    mask_specs = (
        ("effect_mask", True),
        ("protection_mask", False),
        ("permission_mask", True),
    )
    loaded_masks: dict[str, Any] = {}
    for field, required in mask_specs:
        mask = string_field(effects, field)
        if required and not mask:
            add_issue(issues, f"{label}: enabled effects require effects.{field}")
            continue
        if not mask:
            continue
        if not is_png_path(mask):
            add_issue(issues, f"{label}: effects.{field} must point to a .png file")
        mask_path = resolve_under_named_subdir(release_root, "effects", mask)
        if mask_path is None:
            missing_masks += 1
            add_issue(issues, f"{label}: effects.{field} must be a relative path under effects/")
            continue
        if not mask_path.is_file():
            missing_masks += 1
            message = f"{label}: missing {field} {mask}"
            add_issue(warnings if allow_missing_masks else issues, message)
            continue
        expected_hash = audit.get(f"{field}_sha256")
        if not isinstance(expected_hash, str) or sha256_file(mask_path) != expected_hash:
            add_issue(issues, f"{label}: audit hash does not match {field} {mask}")
        try:
            from PIL import Image

            with Image.open(mask_path) as image:
                loaded_masks[field] = image.convert("L")
                if dimensions is not None and [image.width, image.height] != dimensions:
                    add_issue(issues, f"{label}: {field} dimensions differ from audit")
        except Exception as exc:
            add_issue(issues, f"{label}: could not inspect {field} {mask}: {exc}")
        if field == "permission_mask":
            validate_mask_contents(label, mask_path, mask, issues)

    if "effect_mask" in loaded_masks and "permission_mask" in loaded_masks:
        from PIL import Image, ImageChops

        effect_mask = loaded_masks["effect_mask"]
        protection_mask = loaded_masks.get("protection_mask") or Image.new("L", effect_mask.size, 0)
        permission_mask = loaded_masks["permission_mask"]
        if protection_mask.size == effect_mask.size and permission_mask.size == effect_mask.size:
            expected_permission = ImageChops.multiply(effect_mask, ImageChops.invert(protection_mask))
            if ImageChops.difference(expected_permission, permission_mask).getbbox() is not None:
                add_issue(issues, f"{label}: permission_mask does not equal effect_mask × inverse protection_mask")
            pixel_count = permission_mask.width * permission_mask.height
            measured_coverage = sum(
                value >= 128 for value in permission_mask.get_flattened_data()
            ) / pixel_count
            if measured_coverage > 0.75:
                add_issue(issues, f"{label}: measured permission coverage exceeds 0.75")
            if isinstance(coverage, (int, float)) and not isinstance(coverage, bool):
                if abs(measured_coverage - float(coverage)) > 1 / pixel_count:
                    add_issue(
                        issues,
                        f"{label}: measured permission coverage does not match audit.permitted_coverage",
                    )

    if release_art_path is not None and release_art_path.is_file():
        expected_art_hash = audit.get("final_art_sha256")
        if not isinstance(expected_art_hash, str) or sha256_file(release_art_path) != expected_art_hash:
            add_issue(issues, f"{label}: audit final_art_sha256 does not match released art")
        try:
            from PIL import Image

            with Image.open(release_art_path) as image:
                if dimensions is not None and [image.width, image.height] != dimensions:
                    add_issue(issues, f"{label}: released art dimensions differ from audit")
        except Exception as exc:
            add_issue(issues, f"{label}: could not inspect released art: {exc}")
    elif art_source_sha256 is not None:
        # The art ships only as a GPU variant; its audit row records the PNG's hash.
        if audit.get("final_art_sha256") != art_source_sha256.lower():
            add_issue(issues, f"{label}: audit final_art_sha256 does not match the art's GPU variant source")
    return missing_masks


def validate_card(
    card: dict[str, Any],
    index: int,
    root: Path | None,
    release_root: Path,
    allow_missing_masks: bool,
    allow_missing_release_art: bool,
    issues: list[str],
    warnings: list[str],
    ctx: "Context | None" = None,
) -> tuple[int, int, int]:
    label = card_label(card, index)
    ctx = ctx or Context(release_root)
    missing_repo_final_art = 0
    missing_release_art = 0
    missing_masks = 0

    for field in REQUIRED_CARD_FIELDS:
        if not has_required_value(card.get(field)):
            add_issue(issues, f"{label}: missing required field {field}")

    validate_card_scalar_fields(label, card, issues)

    canonical_asset_name = string_field(card, "canonical_asset_name")
    if canonical_asset_name and not CANONICAL_ID_RE.fullmatch(canonical_asset_name):
        add_issue(issues, f"{label}: canonical_asset_name must use lowercase snake_case")

    source_paths = card.get("source_paths")
    if source_paths is not None and not isinstance(source_paths, list):
        add_issue(issues, f"{label}: source_paths must be a list")

    repo_final = string_field(card, "repo_final_png")
    if repo_final:
        if not is_png_path(repo_final):
            add_issue(issues, f"{label}: repo_final_png must point to a .png file")
        repo_final_path = resolve_under(root, repo_final) if root is not None else None
        if root is None:
            pass  # No art repository given: only the path shape is checked.
        elif repo_final_path is None:
            missing_repo_final_art += 1
            add_issue(issues, f"{label}: repo_final_png must be a relative path under --root")
        elif not repo_final_path.is_file():
            missing_repo_final_art += 1
            add_issue(issues, f"{label}: missing repo_final_png {repo_final}")

    art = string_field(card, "art")
    release_art_path: Path | None = None
    art_source_sha256: str | None = None
    ctx.use(card_file_paths(card))
    if art:
        if not starts_with_path_segment(art, "art"):
            add_issue(issues, f"{label}: art must start with art/")
        if not is_png_path(art):
            add_issue(issues, f"{label}: art must point to a .png file")
        release_art_path = resolve_under(release_root, art)
        if release_art_path is None:
            missing_release_art += 1
            add_issue(issues, f"{label}: art must be a relative path under --release-root")
        elif resolve_under_named_subdir(release_root, "art", art) is None:
            missing_release_art += 1
            add_issue(issues, f"{label}: art must resolve under art/")
        elif not release_art_path.is_file() and ctx.gpu.has(art):
            ctx.gpu_only_art += 1
            ctx.check_variant(art, label, issues, warnings)
            art_source_sha256 = ctx.gpu.row(art)["source_sha256"]
        elif not release_art_path.is_file():
            missing_release_art += 1
            message = f"{label}: missing release art {art}"
            if allow_missing_release_art:
                add_issue(warnings, message)
            else:
                add_issue(issues, message)

    check_layers(label, card, ctx, issues, warnings)

    effects = card.get("effects")
    if not isinstance(effects, dict):
        add_issue(issues, f"{label}: effects must be an object")
        return missing_repo_final_art, missing_release_art, missing_masks
    validate_effects_shape(label, effects, issues)
    surface_enabled = is_effect_surface_enabled(effects)
    if not surface_enabled:
        # Enabled finishes check their masks below; these still ship and must exist.
        for name in EFFECT_PATH_FIELDS:
            if isinstance(effects.get(name), str) and effects[name].strip():
                ctx.check_texture(label, f"effects.{name}", effects[name], issues, warnings)

    if surface_enabled:
        missing_masks += validate_enabled_effect(
            label,
            effects,
            release_root,
            release_art_path,
            allow_missing_masks,
            issues,
            warnings,
            art_source_sha256,
        )

    return missing_repo_final_art, missing_release_art, missing_masks


def validate_pack(
    pack: dict[str, Any],
    root: Path | None,
    release_root: Path,
    allow_missing_masks: bool,
    allow_missing_release_art: bool,
    ctx: "Context | None" = None,
) -> tuple[list[str], list[str], int, int, int, int]:
    ctx = ctx or Context(release_root)
    issues: list[str] = []
    warnings: list[str] = []
    # Validate roster provenance if present
    if "roster" in pack:
        roster = pack["roster"]
        if not isinstance(roster, dict):
            add_issue(issues, "roster must be an object")
        else:
            if not isinstance(roster.get("game_version"), str) or not roster["game_version"].strip():
                add_issue(issues, "roster.game_version must be a non-empty string")
            if not isinstance(roster.get("snapshot"), str) or not roster["snapshot"].strip():
                add_issue(issues, "roster.snapshot must be a non-empty string")
        for index, card in enumerate(pack.get("cards", []), start=1):
            if isinstance(card, dict) and card.get("runtime_model_source") != "verified_game_roster":
                add_issue(
                    issues,
                    f"{card_label(card, index)}: roster-backed packs require runtime_model_source 'verified_game_roster'",
                )
    # Retired check
    for idx, card in enumerate(pack.get("cards", []), start=1):
        roster_status = str(card.get("roster_status", "")).strip() if isinstance(card, dict) else ""
        if roster_status == "retired":
            add_issue(issues, f"{card_label(card, idx)}: retired runtime model")
        elif roster_status == "unverified":
            add_issue(issues, f"{card_label(card, idx)}: runtime model is not in the pinned roster snapshot")

    cards = validate_top_level_schema(pack, issues)

    missing_repo_final_art = 0
    missing_release_art = 0
    missing_masks = 0
    runtime_models: dict[str, int] = {}
    canonical_asset_names: dict[str, int] = {}
    for index, card in enumerate(cards, start=1):
        if not isinstance(card, dict):
            add_issue(issues, f"card #{index}: card entry must be an object")
            continue

        card_missing_repo_final_art, card_missing_release_art, card_missing_masks = validate_card(
            card,
            index,
            root,
            release_root,
            allow_missing_masks,
            allow_missing_release_art,
            issues,
            warnings,
            ctx,
        )
        missing_repo_final_art += card_missing_repo_final_art
        missing_release_art += card_missing_release_art
        missing_masks += card_missing_masks

        runtime_model = str(card.get("runtime_model", "")).strip()
        if runtime_model:
            if runtime_model in runtime_models:
                first_index = runtime_models[runtime_model]
                add_issue(
                    issues,
                    f"{card_label(card, index)}: duplicate runtime_model {runtime_model!r} also used by card #{first_index}",
                )
            else:
                runtime_models[runtime_model] = index

        canonical_asset_name = str(card.get("canonical_asset_name", "")).strip()
        if canonical_asset_name:
            if canonical_asset_name in canonical_asset_names:
                first_index = canonical_asset_names[canonical_asset_name]
                add_issue(
                    issues,
                    f"{card_label(card, index)}: duplicate canonical_asset_name {canonical_asset_name!r} also used by card #{first_index}",
                )
            else:
                canonical_asset_names[canonical_asset_name] = index

    declared_count = pack.get("card_count")
    if isinstance(declared_count, int) and declared_count != len(cards):
        add_issue(issues, f"card_count is {declared_count}, but cards contains {len(cards)} entries")

    return issues, warnings, len(cards), missing_repo_final_art, missing_release_art, missing_masks


def format_count(label: str, count: int, allowed: bool = False) -> str:
    suffix = " (allowed)" if allowed and count else ""
    return f"{label}: {count}{suffix}"


# ---------------------------------------------------------------------------
# Runtime rules. These mirror src/FullArtSpire (PackDiscovery, LayerPolicy,
# EffectSurfaceParser, GpuTextureVariants): a pack that passes loads every card
# with the art, layers and finish it declares. Pack JSON is read with
# System.Text.Json, so a value of the wrong type makes the whole pack unreadable.

V2_FORMAT = "full_art_pack"
V2_FORMAT_VERSION = 2
GPU_AUDIT = "gpu-textures.audit"
PACK_ID_RE = re.compile(r"^[a-z0-9_]+(\.[a-z0-9_]+)+$")
POOL_NAME_RE = re.compile(r"^[a-z0-9_]+$")
VERSION_RE = re.compile(r"^v?\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")
SHA256_RE = re.compile(r"^[0-9a-fA-F]{64}$")
GPU_FORMATS = {"BC1_UNORM", "BC3_UNORM", "BC7_UNORM"}
RUNTIME_SURFACES = {"none", "shimmer", "foil", "holo", "prismatic", "rainbow"}
LAYER_PATH_FIELDS = ("background", "foreground", "foreground_mask", "finish_mask")
EFFECT_PATH_FIELDS = ("mask", "effect_mask", "protection_mask", "permission_mask")
LAYER_RANGES = {
    "depth": (0.0, 1.0),
    "background_shift": (0.0, 0.025),
    "foreground_shift": (0.0, 0.04),
    "foreground_scale": (1.0, 1.10),
    "subject_scale": (0.25, 1.5),
    "subject_offset_x": (-0.5, 0.5),
    "subject_offset_y": (-0.5, 0.5),
    "shadow_strength": (0.0, 0.5),
    "frame_inset": (0.025, 0.10),
}
REGISTRATION_FIELDS = {"mode", "source_dimensions", "mask_dimensions", "background_dimensions"}
V2_FIELDS = {"$schema", "format", "format_version", "id", "name", "version", "authors", "credits", "requires",
             "min_game_version", "priority", "cards"}
CARD_FIELDS = {"runtime_model", "art", "preserve_original_layout", "layers", "effects",
               "canonical_asset_name", "display_name", "pool",
               # Written by the art repository's builder; informational.
               "id", "name", "rarity", "status", "source_paths", "repo_final_png", "runtime_model_source",
               "runtime_card_id", "layer_audit", "roster_status"}
LAYER_FIELDS = set(LAYER_PATH_FIELDS) | {"foreground_mask_channel", "registration"} | set(LAYER_RANGES)
EFFECT_FIELDS = set(EFFECT_PATH_FIELDS) | {"surface", "rarity_policy", "intensity", "phase_seed",
                                           "rarity_flame", "audit"}
UNREADABLE = "(the runtime cannot read the pack otherwise)"


def is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def is_integer(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def path_problem(value: Any, png: bool = True) -> str | None:
    """Why the runtime would refuse this pack path (LayerPolicy.IsRelativeImagePath), or None."""
    if not isinstance(value, str) or not value.strip():
        return "must be a non-empty path"
    if value != value.strip():
        return "has leading or trailing spaces"
    if value.startswith(("/", "\\")) or ":" in value:
        return "must be relative to the pack"
    if "\\" in value:
        return "must use / as the separator"
    if png and not value.lower().endswith(".png"):
        return "must be a .png file"
    for part in value.split("/"):
        if part in ("", ".", ".."):
            return "must not contain empty, '.' or '..' segments"
        if part != part.rstrip(" ."):
            return "has a folder or file name ending in a space or dot"
        if any(ord(character) < 32 or character in '<>:"|?*' for character in part):
            return "contains a character Windows does not allow"
    return None


def same_path(first: str, second: str) -> bool:
    return first.replace("\\", "/").lower() == second.replace("\\", "/").lower()


def valid_canvas(width: int, height: int) -> bool:
    return 64 <= width <= 2730 and width < height <= 4096 and width * height <= 2730 * 4095


def compatible_aspect(source: tuple[int, int], other: tuple[int, int]) -> bool:
    return abs(other[0] * source[1] / (other[1] * source[0]) - 1.0) <= 0.001


def card_file_paths(card: dict[str, Any]) -> list[str]:
    paths = [card.get("art")]
    for group, fields in ((card.get("layers"), LAYER_PATH_FIELDS), (card.get("effects"), EFFECT_PATH_FIELDS)):
        if isinstance(group, dict):
            paths.extend(group.get(field) for field in fields)
    return [path for path in paths if isinstance(path, str) and path.strip()]


def dds_problem(path: Path, row: dict[str, Any]) -> str | None:
    """Checks the DDS header the way the runtime's loader will see it."""
    try:
        with path.open("rb") as stream:
            header = stream.read(128)
    except OSError as exc:
        return f"cannot be read ({exc})"
    if len(header) < 128 or header[:4] != b"DDS ":
        return "is not a DDS file"
    height, width = struct.unpack_from("<II", header, 12)
    mipmaps = struct.unpack_from("<I", header, 28)[0]
    if (width, height) != (row.get("width"), row.get("height")):
        return f"is {width}x{height}, but the index says {row.get('width')}x{row.get('height')}"
    if mipmaps < 2:
        return "has no mipmaps"
    return None


class Files:
    """Finds pack files as a case-sensitive file system would, noting letter-case mismatches."""

    def __init__(self, root: Path) -> None:
        self.root = root
        self._folders: dict[Path, tuple[set[str], dict[str, str]]] = {}

    def _names(self, folder: Path) -> tuple[set[str], dict[str, str]]:
        if folder not in self._folders:
            try:
                names = os.listdir(folder)
            except OSError:
                names = []
            self._folders[folder] = (set(names), {name.lower(): name for name in names})
        return self._folders[folder]

    def find(self, relative: str) -> tuple[Path | None, bool]:
        current, exact = self.root, True
        for part in relative.split("/"):
            names, lowered = self._names(current)
            if part in names:
                current = current / part
                continue
            actual = lowered.get(part.lower())
            if actual is None:
                return None, False
            current, exact = current / actual, False
        return (current, exact) if current.is_file() else (None, False)


@dataclass
class GpuIndex:
    rows: dict[str, dict[str, Any]] = field(default_factory=dict)  # lower-case source path -> row

    def has(self, relative: str) -> bool:
        return relative.lower() in self.rows

    def row(self, relative: str) -> dict[str, Any]:
        return self.rows[relative.lower()]


def read_gpu_index(root: Path, issues: list[str], warnings: list[str]) -> GpuIndex:
    """Reads <root>/gpu-textures.audit like GpuTextureVariants.ReadIndex: one bad row voids the index."""
    path = root / GPU_AUDIT
    if not path.is_file():
        return GpuIndex()

    def void(message: str) -> GpuIndex:
        add_issue(issues, f"{GPU_AUDIT}: {message}; the runtime ignores every GPU variant in it")
        return GpuIndex()

    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        return void(f"cannot be read ({exc})")
    if not isinstance(data, dict):
        return void("must be a JSON object")
    if not is_integer(data.get("schema_version")) or data["schema_version"] not in (1, 2):
        return void("schema_version must be 1 or 2")
    textures = data.get("textures")
    if not isinstance(textures, list):
        return void("textures must be a list")
    rows: dict[str, dict[str, Any]] = {}
    for number, row in enumerate(textures, start=1):
        where = f"row {number}"
        if not isinstance(row, dict):
            return void(f"{where} is not an object")
        if row.get("format") not in GPU_FORMATS:
            add_issue(warnings, f"{GPU_AUDIT}: {where} has format {row.get('format')!r}, which the runtime skips")
            continue
        source, variant = row.get("source"), row.get("path")
        problem = path_problem(source)
        if problem:
            return void(f"{where}: source {problem}")
        problem = path_problem(variant, png=False)
        if problem:
            return void(f"{where}: path {problem}")
        if not (source.startswith("art/") or source.startswith("layers/") and source.endswith("/background.png")):
            return void(f"{where}: {source} is neither art nor a layer background")
        for name in ("sha256", "source_sha256"):
            if not isinstance(row.get(name), str) or not SHA256_RE.fullmatch(row[name]):
                return void(f"{where}: {name} must be 64 hex digits")
        for name in ("width", "height"):
            if not is_integer(row.get(name)) or row[name] <= 0:
                return void(f"{where}: {name} must be a positive integer")
        if source.lower() in rows:
            return void(f"{where}: {source} is listed twice")
        rows[source.lower()] = row
    return GpuIndex(rows)


class Context:
    """What one pack root shares across its cards: its files, its GPU index and which files cards use."""

    def __init__(self, root: Path) -> None:
        self.root = root
        self.files = Files(root)
        self.issues: list[str] = []
        self.warnings: list[str] = []
        self.gpu = read_gpu_index(root, self.issues, self.warnings)
        self.used: set[str] = set()
        self.gpu_only_art = 0
        self._variants: dict[str, tuple[str | None, str | None]] = {}

    def use(self, paths: list[str]) -> None:
        self.used.update(path.lower() for path in paths)

    def check_variant(self, relative: str, label: str, issues: list[str], warnings: list[str]) -> None:
        """Hash- and header-checks the GPU variant for a source, once per source."""
        row = self.gpu.row(relative)
        self.used.add(str(row["path"]).lower())
        key = relative.lower()
        if key not in self._variants:
            problem = stale = None
            variant, _ = self.files.find(row["path"])
            if variant is None:
                problem = f"GPU variant {row['path']} is missing"
            elif sha256_file(variant) != row["sha256"].lower():
                problem = f"GPU variant {row['path']} does not match its sha256"
            else:
                header = dds_problem(variant, row)
                problem = f"GPU variant {row['path']} {header}" if header else None
            png, _ = self.files.find(relative)
            if png is not None and sha256_file(png) != row["source_sha256"].lower():
                stale = f"GPU variant {row['path']} was made from a different {relative}; the runtime uses the PNG"
            self._variants[key] = (problem, stale)
            png_exists = png is not None
            if problem:
                # With the PNG present the runtime falls back to it; without, the card loses its art.
                add_issue(warnings if png_exists else issues, f"{label}: {problem}")
            if stale:
                add_issue(warnings, f"{label}: {stale}")

    def check_texture(self, label: str, name: str, value: Any, issues: list[str], warnings: list[str]) -> None:
        problem = path_problem(value)
        if problem:
            add_issue(issues, f"{label}: {name} {problem}")
            return
        found, exact = self.files.find(value)
        if found is not None and not exact:
            add_issue(warnings, f"{label}: {name} {value} differs in letter case from the file; "
                                "it will not load on case-sensitive systems")
        if self.gpu.has(value):
            self.check_variant(value, label, issues, warnings)
        elif found is None:
            add_issue(issues, f"{label}: {name} file missing: {value}")

    def unused_files(self, folders: set[str] | None = None) -> list[str]:
        unused = []
        for path in sorted(self.root.rglob("*")):
            if not path.is_file():
                continue
            relative = path.relative_to(self.root).as_posix()
            if folders is not None and relative.split("/")[0] not in folders:
                continue
            if relative.lower() in self.used or relative in ("pack.json", GPU_AUDIT):
                continue
            if path.suffix.lower() in (".md", ".txt") or path.name.upper().startswith(("LICENSE", "README")):
                continue
            unused.append(relative)
        return unused


def registration_problem(layers: dict[str, Any]) -> str | None:
    value = layers["registration"]
    if not isinstance(value, dict):
        return "registration must be an object"
    if not layers.get("foreground_mask"):
        return "registration needs a foreground_mask"
    if set(value) != REGISTRATION_FIELDS:
        return f"registration must have exactly the fields {', '.join(sorted(REGISTRATION_FIELDS))}"
    if value["mode"] != "normalized_source_uv":
        return "registration.mode must be 'normalized_source_uv'"
    canvases = {}
    for name in ("source_dimensions", "mask_dimensions", "background_dimensions"):
        dimensions = value[name]
        if not (isinstance(dimensions, list) and len(dimensions) == 2 and all(is_integer(item) for item in dimensions)):
            return f"registration.{name} must be [width, height]"
        if not valid_canvas(*dimensions):
            return f"registration.{name} {dimensions} is not a supported portrait canvas"
        canvases[name] = tuple(dimensions)
    for name in ("mask_dimensions", "background_dimensions"):
        if not compatible_aspect(canvases["source_dimensions"], canvases[name]):
            return f"registration.{name} does not match the source aspect ratio"
    return None


def check_layers(label: str, card: dict[str, Any], ctx: Context, issues: list[str], warnings: list[str]) -> None:
    layers = card.get("layers")
    if layers is None:
        return
    if not isinstance(layers, dict):
        add_issue(issues, f"{label}: layers must be an object {UNREADABLE}")
        return
    unknown = sorted(set(layers) - LAYER_FIELDS)
    if unknown:
        add_issue(warnings, f"{label}: layers fields the runtime ignores: {', '.join(unknown)}")
    wrong_types = [name for name in (*LAYER_PATH_FIELDS, "foreground_mask_channel")
                   if name in layers and not isinstance(layers[name], str)]
    wrong_types += [name for name in LAYER_RANGES if name in layers and not is_number(layers[name])]
    for name in wrong_types:
        add_issue(issues, f"{label}: layers.{name} has the wrong type {UNREADABLE}")
    if wrong_types:
        return

    reasons = []
    background, foreground = layers.get("background", ""), layers.get("foreground", "")
    for name, value in (("background", background), ("foreground", foreground)):
        problem = path_problem(value)
        if problem:
            reasons.append(f"layers.{name} {problem}")
    if not reasons and same_path(background, foreground):
        reasons.append("background and foreground are the same file")
    if layers.get("foreground_mask_channel", "red") not in ("red", "alpha"):
        reasons.append("foreground_mask_channel must be 'red' or 'alpha'")
    mask = layers.get("foreground_mask", "")
    if mask:
        problem = path_problem(mask)
        if problem:
            reasons.append(f"layers.foreground_mask {problem}")
        elif same_path(mask, background) or same_path(mask, foreground):
            reasons.append("foreground_mask must differ from background and foreground")
    if "registration" in layers:
        problem = registration_problem(layers)
        if problem:
            reasons.append(problem)
        elif not isinstance(card.get("art"), str) or not same_path(card["art"], foreground):
            reasons.append("with a registration, foreground must be the card's art")
    for name, (low, high) in LAYER_RANGES.items():
        if name in layers and not low <= layers[name] <= high:
            reasons.append(f"layers.{name} must be between {low} and {high}")
    for reason in reasons:
        add_issue(issues, f"{label}: layers are ignored and the card shows flat art: {reason}")

    art = card.get("art")
    for name in LAYER_PATH_FIELDS:
        value = layers.get(name)
        if not value or (name == "foreground" and isinstance(art, str) and same_path(value, art)):
            continue  # The art is checked on its own.
        if name != "finish_mask" and path_problem(value):
            continue  # Reported above.
        ctx.check_texture(label, f"layers.{name}", value, issues, warnings)


def check_effects(label: str, card: dict[str, Any], ctx: Context, issues: list[str], warnings: list[str]) -> None:
    effects = card.get("effects")
    if effects is None:
        return
    if not isinstance(effects, dict):
        add_issue(issues, f"{label}: effects must be an object {UNREADABLE}")
        return
    unknown = sorted(set(effects) - EFFECT_FIELDS)
    if unknown:
        add_issue(warnings, f"{label}: effects fields the runtime ignores: {', '.join(unknown)}")
    wrong_types = [name for name in (*EFFECT_PATH_FIELDS, "surface", "rarity_policy")
                   if name in effects and not isinstance(effects[name], str)]
    if "intensity" in effects and not is_number(effects["intensity"]):
        wrong_types.append("intensity")
    if effects.get("phase_seed") is not None and not is_number(effects["phase_seed"]):
        wrong_types.append("phase_seed")
    for name in wrong_types:
        add_issue(issues, f"{label}: effects.{name} has the wrong type {UNREADABLE}")
    if wrong_types:
        return

    surface = effects.get("surface", "none").strip().lower()
    if surface not in RUNTIME_SURFACES:
        add_issue(issues, f"{label}: effects.surface {effects['surface']!r} is not one of "
                          f"{', '.join(sorted(RUNTIME_SURFACES))}; the card gets no finish")
    elif surface != "none" and not effects.get("permission_mask", "").strip():
        layers = card.get("layers")
        if not (isinstance(layers, dict) and layers.get("finish_mask")):
            add_issue(warnings, f"{label}: effects.surface is {surface!r} but there is no permission_mask, "
                                "so the finish never shows")
    for name in EFFECT_PATH_FIELDS:
        if effects.get(name):
            ctx.check_texture(label, f"effects.{name}", effects[name], issues, warnings)


@dataclass
class Report:
    title: str
    kind: str                 # "v1" or "v2"
    pack_id: str | None = None
    cards: int = 0
    priority: int = 0
    models: list[str] = field(default_factory=list)
    issues: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    gpu_only_art: int = 0


def v2_card_label(card: dict[str, Any], number: int) -> str:
    for name in ("display_name", "canonical_asset_name"):
        if isinstance(card.get(name), str) and card[name].strip():
            return card[name].strip()
    model = card.get("runtime_model")
    return model.rsplit(".", 1)[-1] if isinstance(model, str) and model.strip() else f"card #{number}"


def check_v2_card(card: Any, number: int, ctx: Context, issues: list[str], warnings: list[str]) -> str | None:
    if not isinstance(card, dict):
        add_issue(issues, f"card #{number}: must be an object")
        return None
    label = v2_card_label(card, number)
    unknown = sorted(set(card) - CARD_FIELDS)
    if unknown:
        add_issue(warnings, f"{label}: fields the runtime ignores: {', '.join(unknown)}")
    wrong_types = [name for name in ("runtime_model", "art", "canonical_asset_name", "display_name", "pool")
                   if name in card and not isinstance(card[name], str)]
    if "preserve_original_layout" in card and not isinstance(card["preserve_original_layout"], bool):
        wrong_types.append("preserve_original_layout")
    for name in wrong_types:
        add_issue(issues, f"{label}: {name} has the wrong type {UNREADABLE}")
    if wrong_types:
        return None

    model = card.get("runtime_model", "")
    if not model.strip():
        add_issue(issues, f"{label}: runtime_model is required; the runtime drops the card")
        model = None
    elif model != model.strip() or any(character.isspace() for character in model):
        add_issue(issues, f"{label}: runtime_model must not contain spaces")
    elif "." not in model:
        add_issue(warnings, f"{label}: runtime_model should be the card model's full .NET type name, "
                            "e.g. MegaCrit.Sts2.Core.Models.Cards.KiWhip")
    ctx.use(card_file_paths(card))
    if "art" not in card:
        add_issue(issues, f"{label}: art is required")
    else:
        ctx.check_texture(label, "art", card["art"], issues, warnings)
        if isinstance(card["art"], str) and card["art"] and not path_problem(card["art"]) \
                and ctx.files.find(card["art"])[0] is None and ctx.gpu.has(card["art"]):
            ctx.gpu_only_art += 1
    check_layers(label, card, ctx, issues, warnings)
    check_effects(label, card, ctx, issues, warnings)
    return model


def read_json_object(path: Path, issues: list[str], warnings: list[str]) -> dict[str, Any] | None:
    def no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        seen: dict[str, Any] = {}
        for key, value in pairs:
            if key in seen:
                add_issue(warnings, f"{path.name}: field {key!r} appears twice; the last one is used")
            seen[key] = value
        return seen

    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=no_duplicates)
    except (OSError, ValueError) as exc:
        add_issue(issues, f"{path.name} cannot be read: {exc}")
        return None
    if not isinstance(data, dict):
        add_issue(issues, f"{path.name} must be a JSON object")
        return None
    return data


def validate_v2_pack(folder: Path, title: str | None = None) -> Report:
    report = Report(title or folder.name, "v2")
    issues, warnings = report.issues, report.warnings
    ctx = Context(folder)
    manifest = folder / "pack.json"
    data = read_json_object(manifest, issues, warnings) if manifest.is_file() else None
    if not manifest.is_file():
        add_issue(issues, "no pack.json in the folder")
    if data is None:
        return report

    if data.get("format") != V2_FORMAT:
        add_issue(issues, f"format must be {V2_FORMAT!r}; the runtime skips the pack")
    if not is_integer(data.get("format_version")) or data["format_version"] != V2_FORMAT_VERSION:
        add_issue(issues, f"format_version must be {V2_FORMAT_VERSION}; the runtime skips the pack")
    pack_id = data.get("id")
    if not isinstance(pack_id, str) or len(pack_id) > 128 or not PACK_ID_RE.fullmatch(pack_id):
        add_issue(issues, "id must look like namespace.name (lowercase a-z, 0-9, _); the runtime skips the pack")
    else:
        report.pack_id = pack_id
    unknown = sorted(set(data) - V2_FIELDS)
    if unknown:
        add_issue(warnings, f"pack.json fields the runtime ignores: {', '.join(unknown)}")

    for name in ("name", "version", "credits", "min_game_version"):
        if data.get(name) is not None and not isinstance(data[name], str):
            add_issue(issues, f"{name} must be a string {UNREADABLE}")
    if not isinstance(data.get("name"), str) or not data["name"].strip():
        add_issue(warnings, "name is empty; the settings show the id instead")
    if not isinstance(data.get("credits"), str) or not data["credits"].strip():
        add_issue(warnings, "credits is empty; credit the original art and its artists")
    authors = data.get("authors")
    if authors is not None and not (isinstance(authors, list) and all(isinstance(item, str) for item in authors)):
        add_issue(issues, f"authors must be a list of strings {UNREADABLE}")
    if "priority" in data:
        if not is_integer(data["priority"]) or not -2**31 <= data["priority"] < 2**31:
            add_issue(issues, f"priority must be a whole number {UNREADABLE}")
        else:
            report.priority = data["priority"]
    minimum = data.get("min_game_version")
    if isinstance(minimum, str) and minimum.strip() and not VERSION_RE.fullmatch(minimum.strip()):
        add_issue(issues, f"min_game_version {minimum!r} is not a version like 0.113.1; the runtime skips the pack")
    requires = data.get("requires")
    if requires is not None:
        if not isinstance(requires, list):
            add_issue(issues, f"requires must be a list {UNREADABLE}")
        else:
            for number, requirement in enumerate(requires, start=1):
                where = f"requires #{number}"
                if requirement is None:
                    continue
                if not isinstance(requirement, dict):
                    add_issue(issues, f"{where} must be an object {UNREADABLE}")
                    continue
                if not isinstance(requirement.get("mod"), str) or not requirement["mod"].strip():
                    add_issue(issues, f"{where}: mod is required; the runtime skips the pack")
                version = requirement.get("min_version")
                if version is not None and (not isinstance(version, str) or (version.strip() and not VERSION_RE.fullmatch(version.strip()))):
                    add_issue(issues, f"{where}: min_version {version!r} is not a version like 1.2.0; the runtime skips the pack")
                extra = sorted(set(requirement) - {"mod", "min_version"})
                if extra:
                    add_issue(warnings, f"{where}: fields the runtime ignores: {', '.join(extra)}")

    cards = data.get("cards")
    if not isinstance(cards, list):
        add_issue(issues, "cards must be a list; the runtime skips the pack")
        cards = []
    elif not cards:
        add_issue(warnings, "cards is empty")
    report.cards = len(cards)
    first_seen: dict[str, int] = {}
    for number, card in enumerate(cards, start=1):
        model = check_v2_card(card, number, ctx, issues, warnings)
        if not model:
            continue
        if model.lower() in first_seen:
            add_issue(issues, f"{model} is listed twice (cards #{first_seen[model.lower()]} and #{number}); only one is used")
        else:
            first_seen[model.lower()] = number
            report.models.append(model)
    report.gpu_only_art = ctx.gpu_only_art
    issues[:0] = ctx.issues
    warnings[:0] = ctx.warnings
    unused = ctx.unused_files()
    if unused:
        add_issue(warnings, f"{len(unused)} files no card uses (they still ship with the pack), e.g. {', '.join(unused[:3])}")
    return report


def resolve_overlaps(reports: list[Report]) -> list[str]:
    """Which copy of a pack id and of a card wins, as PackDiscovery and the registry decide it."""
    rank = {"v1": 0, "v2": 1}  # legacy files, then bundled folders

    def order(item: tuple[int, Report]) -> tuple[int, int, int]:
        return item[1].priority, rank[item[1].kind], item[0]

    notes = []
    by_id: dict[str, list[tuple[int, Report]]] = {}
    for item in enumerate(reports):
        if item[1].pack_id and not item[1].issues:
            by_id.setdefault(item[1].pack_id, []).append(item)
    winners = []
    for pack_id, items in by_id.items():
        best = max(items, key=order)
        winners.append(best)
        for item in items:
            if item is not best:
                notes.append(f"pack id {pack_id} is in {item[1].title} and {best[1].title}; only {best[1].title} loads")
    claims: dict[str, tuple[str, list[tuple[int, Report]]]] = {}
    for item in sorted(winners, key=lambda pair: pair[0]):
        for model in item[1].models:
            claims.setdefault(model.lower(), (model, []))[1].append(item)
    for model, items in sorted(claims.values(), key=lambda claim: claim[0]):
        if len(items) > 1:
            best = max(items, key=order)
            titles = ", ".join(item[1].title for item in items)
            notes.append(f"{model} is in {titles}; {best[1].title} wins")
    return notes


def validate_mod_folder(folder: Path, root: Path | None, allow_missing_masks: bool,
                        allow_missing_release_art: bool) -> tuple[list[Report], list[str], list[str]]:
    ctx = Context(folder)
    reports: list[Report] = []
    packs = folder / "packs"
    if not packs.is_dir():
        return reports, [f"{folder} has no packs/ folder"], []
    for pack_file in sorted(packs.glob("*.pack")):
        report = Report(f"packs/{pack_file.name}", "v1", f"megacrit.{pack_file.stem}")
        if not POOL_NAME_RE.fullmatch(pack_file.stem):
            add_issue(report.issues, "the file name must use lowercase a-z, 0-9 and _; the runtime skips it")
        pack = load_pack(pack_file, report.issues)
        before = ctx.gpu_only_art
        found, warnings, cards, _, _, _ = validate_pack(
            pack, root, folder, allow_missing_masks, allow_missing_release_art, ctx)
        report.issues += found
        report.warnings += warnings
        report.cards = cards
        report.gpu_only_art = ctx.gpu_only_art - before
        report.models = [card["runtime_model"].strip() for card in pack.get("cards", [])
                         if isinstance(card, dict) and isinstance(card.get("runtime_model"), str)
                         and card["runtime_model"].strip()]
        reports.append(report)
    for sub in sorted(path for path in packs.iterdir() if path.is_dir()):
        if (sub / "pack.json").is_file():
            reports.append(validate_v2_pack(sub, f"packs/{sub.name}"))
    issues = list(ctx.issues)
    warnings = list(ctx.warnings)
    for sub in sorted(path for path in packs.iterdir() if path.is_dir() and not (path / "pack.json").is_file()):
        add_issue(warnings, f"packs/{sub.name} has no pack.json; the runtime ignores it")
    if not reports:
        add_issue(issues, "no packs found")
    warnings += resolve_overlaps(reports)
    unused = ctx.unused_files({"art", "layers", "effects", "gpu"})
    if unused:
        add_issue(warnings, f"{len(unused)} files under art/, layers/, effects/ or gpu/ that no card uses, "
                            f"e.g. {', '.join(unused[:3])}")
    return reports, issues, warnings


def print_findings(warnings: list[str], issues: list[str]) -> int:
    if warnings:
        print("\nWarnings")
        for warning in warnings:
            print(f"- {warning}")
    if issues:
        print("\nIssues")
        for issue in issues:
            print(f"- {issue}")
        return 1
    print("\nValidation passed.")
    return 0


def run_v2(folder: Path) -> int:
    report = validate_v2_pack(folder)
    print("Full Art Spire pack validation (format v2)")
    print(f"Pack: {folder}")
    print(f"Id: {report.pack_id or '(invalid)'}")
    print(f"Cards: {report.cards}")
    print(f"Art shipped only as GPU variants: {report.gpu_only_art}")
    return print_findings(report.warnings, report.issues)


def run_mod_folder(folder: Path, root: Path | None, allow_missing_masks: bool, allow_missing_release_art: bool) -> int:
    reports, issues, warnings = validate_mod_folder(folder, root, allow_missing_masks, allow_missing_release_art)
    print("Full Art Spire mod folder validation")
    print(f"Folder: {folder}")
    for report in reports:
        state = (f"{len(report.issues)} issues" if report.issues else "ok") + (
            f", {len(report.warnings)} warnings" if report.warnings else "")
        print(f"  {report.title:<28} {report.kind}  {report.pack_id or '(invalid id)':<24} "
              f"{report.cards:>4} cards  {state}")
    print(f"Cards: {sum(report.cards for report in reports)}; "
          f"art shipped only as GPU variants: {sum(report.gpu_only_art for report in reports)}")
    for report in reports:
        warnings += [f"{report.title}: {warning}" for warning in report.warnings]
        issues += [f"{report.title}: {issue}" for issue in report.issues]
    return print_findings(warnings, issues)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--pack", type=Path, help="a v1 <pool>.pack file, or a v2 pack folder or its pack.json")
    target.add_argument("--mod-folder", type=Path, help="an assembled mod folder (every pack in it)")
    parser.add_argument("--root", type=Path, default=None,
                        help="v1: art repository root; when given, each repo_final_png must exist under it.")
    parser.add_argument("--release-root", type=Path, default=None,
                        help="v1: mod folder for art and effects paths (default: the folder containing packs/).")
    parser.add_argument("--allow-missing-masks", action="store_true",
                        help="v1, development only: warn instead of fail for missing approved mask files; "
                             "audit failures still fail.")
    parser.add_argument("--allow-missing-release-art", action="store_true",
                        help="v1: report but do not fail when art paths are absent from --release-root.")
    args = parser.parse_args(argv)
    root = None if args.root is None else args.root.resolve()
    if args.mod_folder is not None:
        return run_mod_folder(args.mod_folder.resolve(), root, args.allow_missing_masks,
                              args.allow_missing_release_art)

    pack_path = args.pack.resolve()
    if pack_path.is_dir():
        return run_v2(pack_path)
    if pack_path.name == "pack.json":
        return run_v2(pack_path.parent)

    issues: list[str] = []
    release_root = args.release_root
    if release_root is None:
        release_root = pack_path.parent.parent if pack_path.parent.name == "packs" else pack_path.parent
    release_root = release_root.resolve()
    ctx = Context(release_root)
    pack = load_pack(pack_path, issues)
    validation_issues, warnings, cards, missing_repo_final_art, missing_release_art, missing_masks = validate_pack(
        pack, root, release_root, args.allow_missing_masks, args.allow_missing_release_art, ctx)
    issues.extend(ctx.issues + validation_issues)
    warnings = ctx.warnings + warnings
    missing_art = missing_repo_final_art + missing_release_art

    print("Standalone pack validation summary")
    print(f"Pack: {pack_path}")
    print(f"Cards: {cards}")
    print(format_count("Missing repo final art", missing_repo_final_art))
    print(format_count("Missing release art", missing_release_art, args.allow_missing_release_art))
    print(f"Missing art: {missing_art}")
    print(format_count("Missing masks", missing_masks, args.allow_missing_masks))
    print(f"Art shipped only as GPU variants: {ctx.gpu_only_art}")
    return print_findings(warnings, issues)


if __name__ == "__main__":
    sys.exit(main())
