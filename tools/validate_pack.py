from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RELEASE_ROOT = ROOT / "release" / "standalone" / "full_art_card_packs"
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


def add_issue(issues: list[str], message: str) -> None:
    issues.append(message)


def is_absolute_path(value: str) -> bool:
    return Path(value).is_absolute() or PureWindowsPath(value).is_absolute() or PurePosixPath(value).is_absolute()


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


def is_png_path(value: str) -> bool:
    return PureWindowsPath(value).suffix.lower() == ".png" or PurePosixPath(value).suffix.lower() == ".png"


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


def validate_card(
    card: dict[str, Any],
    index: int,
    root: Path,
    release_root: Path,
    allow_missing_masks: bool,
    allow_missing_release_art: bool,
    issues: list[str],
    warnings: list[str],
) -> tuple[int, int, int]:
    label = card_label(card, index)
    missing_repo_final_art = 0
    missing_release_art = 0
    missing_masks = 0

    for field in REQUIRED_CARD_FIELDS:
        if not has_required_value(card.get(field)):
            add_issue(issues, f"{label}: missing required field {field}")

    source_paths = card.get("source_paths")
    if source_paths is not None and not isinstance(source_paths, list):
        add_issue(issues, f"{label}: source_paths must be a list")

    repo_final = str(card.get("repo_final_png", "")).strip()
    if repo_final:
        if not is_png_path(repo_final):
            add_issue(issues, f"{label}: repo_final_png must point to a .png file")
        repo_final_path = resolve_under(root, repo_final)
        if repo_final_path is None:
            missing_repo_final_art += 1
            add_issue(issues, f"{label}: repo_final_png must be a relative path under --root")
        elif not repo_final_path.is_file():
            missing_repo_final_art += 1
            add_issue(issues, f"{label}: missing repo_final_png {repo_final}")

    art = str(card.get("art", "")).strip()
    if art:
        if not is_png_path(art):
            add_issue(issues, f"{label}: art must point to a .png file")
        release_art_path = resolve_under(release_root, art)
        if release_art_path is None:
            missing_release_art += 1
            add_issue(issues, f"{label}: art must be a relative path under --release-root")
        elif not release_art_path.is_file():
            missing_release_art += 1
            message = f"{label}: missing release art {art}"
            if allow_missing_release_art:
                add_issue(warnings, message)
            else:
                add_issue(issues, message)

    effects = card.get("effects")
    if not isinstance(effects, dict):
        add_issue(issues, f"{label}: effects must be an object")
        return missing_repo_final_art, missing_release_art, missing_masks

    mask = str(effects.get("mask", "")).strip()
    if mask:
        if not is_png_path(mask):
            add_issue(issues, f"{label}: mask must point to a .png file")
        mask_path = resolve_under(release_root, mask)
        if mask_path is None:
            missing_masks += 1
            add_issue(issues, f"{label}: mask must be a relative path under --release-root")
        elif not mask_path.is_file():
            missing_masks += 1
            message = f"{label}: missing mask {mask}"
            if allow_missing_masks:
                add_issue(warnings, message)
            else:
                add_issue(issues, message)

    return missing_repo_final_art, missing_release_art, missing_masks


def validate_pack(
    pack: dict[str, Any],
    root: Path,
    release_root: Path,
    allow_missing_masks: bool,
    allow_missing_release_art: bool,
) -> tuple[list[str], list[str], int, int, int, int]:
    issues: list[str] = []
    warnings: list[str] = []
    cards = pack.get("cards")
    if not isinstance(cards, list):
        add_issue(issues, "pack must contain a top-level cards list")
        return issues, warnings, 0, 0, 0, 0

    missing_repo_final_art = 0
    missing_release_art = 0
    missing_masks = 0
    runtime_models: dict[str, int] = {}
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

    declared_count = pack.get("card_count")
    if isinstance(declared_count, int) and declared_count != len(cards):
        add_issue(issues, f"card_count is {declared_count}, but cards contains {len(cards)} entries")

    return issues, warnings, len(cards), missing_repo_final_art, missing_release_art, missing_masks


def format_count(label: str, count: int, allowed: bool = False) -> str:
    suffix = " (allowed)" if allowed and count else ""
    return f"{label}: {count}{suffix}"


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate a standalone full-art pack manifest.")
    parser.add_argument("--pack", type=Path, required=True, help="Standalone pack JSON to validate.")
    parser.add_argument("--root", type=Path, default=ROOT, help="Repository root for repo_final_png paths.")
    parser.add_argument(
        "--release-root",
        type=Path,
        default=DEFAULT_RELEASE_ROOT,
        help="Standalone release root for art and effects paths.",
    )
    parser.add_argument("--allow-missing-masks", action="store_true", help="Report but do not fail on missing masks.")
    parser.add_argument(
        "--allow-missing-release-art",
        action="store_true",
        help="Report but do not fail when art paths are absent from --release-root.",
    )
    args = parser.parse_args()

    issues: list[str] = []
    pack_path = args.pack if args.pack.is_absolute() else Path.cwd() / args.pack
    pack = load_pack(pack_path, issues)
    validation_issues, warnings, cards, missing_repo_final_art, missing_release_art, missing_masks = validate_pack(
        pack,
        args.root if args.root.is_absolute() else Path.cwd() / args.root,
        args.release_root if args.release_root.is_absolute() else Path.cwd() / args.release_root,
        args.allow_missing_masks,
        args.allow_missing_release_art,
    )
    issues.extend(validation_issues)
    missing_art = missing_repo_final_art + missing_release_art

    print("Standalone pack validation summary")
    print(f"Pack: {pack_path}")
    print(f"Cards: {cards}")
    print(format_count("Missing repo final art", missing_repo_final_art))
    print(format_count("Missing release art", missing_release_art, args.allow_missing_release_art))
    print(f"Missing art: {missing_art}")
    print(format_count("Missing masks", missing_masks, args.allow_missing_masks))

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


if __name__ == "__main__":
    sys.exit(main())
