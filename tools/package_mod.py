"""Assemble the Full Art Spire mod folder from this repository's build and pack sources.

Output: <output>/full_art_spire/ (the folder players put in the game's mods/),
<output>/package-receipt.json, and with --zip <output>/FullArtSpire-<version>.zip.

A pack source is either
  * a mod-shaped folder, as the art repository builds it (packs/*.pack plus the
    art/, layers/, effects/, gpu/ files those packs reference and its
    gpu-textures.audit), optionally with v2 pack folders under packs/; or
  * one v2 pack folder (it contains pack.json).

Only files a card references are copied: the PNG if the source has it, otherwise
the GPU variant its gpu-textures.audit lists for it (hash-checked). The audits
of all sources are merged into one. When two sources supply the same pack
(same packs/ entry name), the later --pack-source wins. Two packs that need the
same path with different bytes are an error.

Never writes into a game install. Standard library only.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

REPO = Path(__file__).resolve().parents[1]
MOD_ID = "full_art_spire"
DLL_NAME = "FullArtSpire.dll"
DEFAULT_DLL = REPO / "src" / "FullArtSpire" / "bin" / "Release" / "net9.0" / DLL_NAME
SHADERS = REPO / "src" / "FullArtSpire" / "Shaders"
MANIFEST = REPO / "mod" / f"{MOD_ID}.json"
CHANGELOG = REPO / "CHANGELOG.md"
AUDIT = "gpu-textures.audit"
V2_FORMAT = "full_art_pack"
V2_VERSION = 2
POOL_RE = re.compile(r"^[a-z0-9_]+$")
PACK_ID_RE = re.compile(r"^[a-z0-9_]+(\.[a-z0-9_]+)+$")
LAYER_PATHS = ("background", "foreground", "foreground_mask", "finish_mask")
EFFECT_PATHS = ("mask", "effect_mask", "protection_mask", "permission_mask")
ZIP_TIME = (2026, 1, 1, 0, 0, 0)


class PackageError(Exception):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def read_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise PackageError(f"cannot read {path}: {error}") from error


def safe_relative(value: object, where: str) -> str:
    """A path a pack may reference: relative, '/'-separated, no '..'."""
    if not isinstance(value, str) or not value.strip():
        raise PackageError(f"{where}: empty path")
    text = value.strip().replace("\\", "/")
    path = PurePosixPath(text)
    if text.startswith("/") or ":" in text or any(part in ("", ".", "..") for part in text.split("/")):
        raise PackageError(f"{where}: unsafe path {value!r}")
    return path.as_posix()


def card_paths(card: dict, where: str) -> list[str]:
    paths = [safe_relative(card.get("art"), f"{where} art")]
    layers = card.get("layers") or {}
    effects = card.get("effects") or {}
    for group, keys, name in ((layers, LAYER_PATHS, "layers"), (effects, EFFECT_PATHS, "effects")):
        if not isinstance(group, dict):
            raise PackageError(f"{where}: {name} must be an object")
        for key in keys:
            if group.get(key):
                paths.append(safe_relative(group[key], f"{where} {name}.{key}"))
    return paths


@dataclass
class Audit:
    schema_version: int = 0
    rows: dict[str, dict] = field(default_factory=dict)  # source path -> row
    formats: set[str] = field(default_factory=set)

    @staticmethod
    def read(root: Path) -> "Audit":
        audit = Audit()
        path = root / AUDIT
        if not path.is_file():
            return audit
        data = read_json(path)
        audit.schema_version = int(data.get("schema_version", 0))
        if audit.schema_version not in (1, 2):
            raise PackageError(f"{path}: unsupported schema_version {audit.schema_version}")
        audit.formats = set(data.get("formats") or [])
        for row in data.get("textures") or []:
            source = safe_relative(row.get("source"), f"{path} source")
            safe_relative(row.get("path"), f"{path} path")
            audit.rows[source] = row
        return audit


@dataclass
class Pack:
    key: str            # entry name under packs/: "<pool>.pack" or "<folder>"
    kind: str           # "v1" or "v2"
    pack_id: str
    source_root: Path   # where the pack's paths resolve
    manifest: Path
    cards: int
    files: dict[str, Path]           # output relative path -> source file
    audit_rows: dict[str, dict]      # v1 only: rows for the mod-root audit
    audit_schema: int = 0
    audit_formats: frozenset[str] = frozenset()


def collect_v1(source: Path, pack_file: Path, audit: Audit) -> Pack:
    pool = pack_file.stem
    if not POOL_RE.match(pool):
        raise PackageError(f"{pack_file}: invalid pool name")
    data = read_json(pack_file)
    cards = data.get("cards")
    if not isinstance(cards, list):
        raise PackageError(f"{pack_file}: no cards list")
    files = {f"packs/{pack_file.name}": pack_file}
    rows: dict[str, dict] = {}
    missing = []
    for index, card in enumerate(cards):
        if not isinstance(card, dict) or not card.get("runtime_model"):
            raise PackageError(f"{pack_file}: card {index} has no runtime_model")
        for relative in card_paths(card, f"{pack_file.name} {card['runtime_model']}"):
            if (source / relative).is_file():
                files[relative] = source / relative
            elif relative in audit.rows:
                row = audit.rows[relative]
                files[row["path"]] = source / row["path"]
                rows[relative] = row
            else:
                missing.append(relative)
    if missing:
        raise PackageError(f"{pack_file}: {len(missing)} referenced files missing, e.g. {missing[:3]}")
    for row in rows.values():
        variant = source / row["path"]
        if not variant.is_file():
            raise PackageError(f"{pack_file}: GPU variant missing: {row['path']}")
    return Pack(f"{pool}.pack", "v1", f"megacrit.{pool}", source, pack_file, len(cards), files, rows,
                audit.schema_version, frozenset(audit.formats))


def collect_v2(folder: Path, key: str) -> Pack:
    manifest = folder / "pack.json"
    data = read_json(manifest)
    if data.get("format") != V2_FORMAT or data.get("format_version") != V2_VERSION:
        raise PackageError(f"{manifest}: format must be {V2_FORMAT!r} with format_version {V2_VERSION}")
    pack_id = data.get("id")
    if not isinstance(pack_id, str) or not PACK_ID_RE.match(pack_id):
        raise PackageError(f"{manifest}: invalid id {pack_id!r}")
    cards = data.get("cards")
    if not isinstance(cards, list):
        raise PackageError(f"{manifest}: no cards list")
    audit = Audit.read(folder)
    missing = []
    for index, card in enumerate(cards):
        if not isinstance(card, dict) or not card.get("runtime_model"):
            raise PackageError(f"{manifest}: card {index} has no runtime_model")
        for relative in card_paths(card, f"{key} {card['runtime_model']}"):
            if not (folder / relative).is_file() and not (relative in audit.rows and (folder / audit.rows[relative]["path"]).is_file()):
                missing.append(relative)
    if missing:
        raise PackageError(f"{manifest}: {len(missing)} referenced files missing, e.g. {missing[:3]}")
    # A v2 pack ships as its author made it, including readmes and its own audit.
    files = {}
    for path in sorted(folder.rglob("*")):
        if path.is_file():
            files[f"packs/{key}/{path.relative_to(folder).as_posix()}"] = path
    return Pack(key, "v2", pack_id, folder, manifest, len(cards), files, {})


def collect_source(source: Path) -> list[Pack]:
    if not source.is_dir():
        raise PackageError(f"pack source is not a folder: {source}")
    if (source / "pack.json").is_file():
        if not POOL_RE.match(source.name):
            raise PackageError(f"{source}: a v2 pack folder name must use a-z, 0-9 and _")
        return [collect_v2(source, source.name)]
    packs_dir = source / "packs"
    if not packs_dir.is_dir():
        raise PackageError(f"{source}: neither pack.json nor a packs/ folder")
    audit = Audit.read(source)
    packs = [collect_v1(source, path, audit) for path in sorted(packs_dir.glob("*.pack"))]
    for folder in sorted(path for path in packs_dir.iterdir() if path.is_dir()):
        if (folder / "pack.json").is_file():
            if not POOL_RE.match(folder.name):
                raise PackageError(f"{folder}: a v2 pack folder name must use a-z, 0-9 and _")
            packs.append(collect_v2(folder, folder.name))
    if not packs:
        raise PackageError(f"{source}: no packs found")
    return packs


def guard_output(output: Path) -> None:
    for parent in [output, *output.parents]:
        if (parent / "SlayTheSpire2.exe").exists() or parent.name.lower() == "steamapps":
            raise PackageError(f"refusing to write inside a game install: {output}")
    if output.exists():
        raise PackageError(f"output folder already exists: {output}")


def check_dll(dll: Path) -> None:
    if not dll.is_file():
        raise PackageError(f"build output missing: {dll} (run with --build, or dotnet build src/FullArtSpire -c Release)")
    newest = max(path.stat().st_mtime for path in (REPO / "src" / "FullArtSpire").rglob("*")
                 if path.is_file() and path.suffix in (".cs", ".gdshader", ".csproj") and "obj" not in path.parts and "bin" not in path.parts)
    if dll.stat().st_mtime < newest:
        print(f"warning: {dll.name} is older than the newest runtime source; rebuild with --build", file=sys.stderr)


def git_state() -> dict:
    def run(*args: str) -> str:
        return subprocess.run(["git", *args], cwd=REPO, capture_output=True, text=True, check=True).stdout.strip()
    try:
        return {"commit": run("rev-parse", "HEAD"), "dirty": bool(run("status", "--porcelain"))}
    except (OSError, subprocess.CalledProcessError):
        return {"commit": None, "dirty": None}


def place(files: dict[str, Path], relative: str, source: Path, owner: str, owners: dict[str, str]) -> None:
    existing = files.get(relative)
    if existing is not None and existing != source and sha256(existing) != sha256(source):
        raise PackageError(f"{relative}: {owner} and {owners[relative]} ship different files at the same path")
    if existing is None:
        files[relative] = source
        owners[relative] = owner


def assemble(sources: list[Path], dll: Path, version: str | None, output: Path, hardlink: bool) -> dict:
    guard_output(output)
    check_dll(dll)
    chosen: dict[str, Pack] = {}
    for source in sources:
        for pack in collect_source(source.resolve()):
            if pack.key in chosen:
                print(f"note: {pack.key} from {source} replaces the copy from {chosen[pack.key].source_root}")
            chosen[pack.key] = pack
    ids: dict[str, str] = {}
    for pack in chosen.values():
        if pack.pack_id in ids and pack.kind == "v2" and chosen[ids[pack.pack_id]].kind == "v2":
            raise PackageError(f"pack id {pack.pack_id} is used by both packs/{ids[pack.pack_id]} and packs/{pack.key}")
        ids.setdefault(pack.pack_id, pack.key)

    files: dict[str, Path] = {}
    owners: dict[str, str] = {}
    rows: dict[str, dict] = {}
    schema = 0
    formats: set[str] = set()
    for pack in sorted(chosen.values(), key=lambda item: item.key):
        for relative, path in pack.files.items():
            place(files, relative, path, pack.key, owners)
        for source_path, row in pack.audit_rows.items():
            if source_path in rows and rows[source_path]["sha256"] != row["sha256"]:
                raise PackageError(f"{source_path}: two packs carry different GPU variants")
            rows[source_path] = row
        if pack.audit_rows:
            schema = max(schema, pack.audit_schema)
            formats |= pack.audit_formats

    manifest = read_json(MANIFEST)
    if manifest.get("id") != MOD_ID:
        raise PackageError(f"{MANIFEST}: id must be {MOD_ID}")
    if version:
        manifest["version"] = version
    version = manifest["version"]

    stage = output / MOD_ID
    stage.mkdir(parents=True)
    for relative, path in sorted(files.items()):
        target = stage / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if hardlink:
            try:
                os.link(path, target)
                continue
            except OSError:
                pass
        shutil.copyfile(path, target)
    for relative, row in sorted(rows.items()):
        if sha256(stage / row["path"]) != row["sha256"]:
            raise PackageError(f"GPU variant does not match its audit hash: {row['path']}")
    if rows:
        audit = {"schema_version": schema, "formats": sorted(formats),
                 "textures": [rows[key] for key in sorted(rows)]}
        (stage / AUDIT).write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8", newline="\n")
    shutil.copyfile(dll, stage / f"{MOD_ID}.dll")
    (stage / "shaders").mkdir()
    for shader in sorted(SHADERS.glob("*.gdshader")):
        shutil.copyfile(shader, stage / "shaders" / shader.name)
    (stage / f"{MOD_ID}.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    shutil.copyfile(CHANGELOG, stage / "CHANGELOG.md")

    shipped = sorted(path for path in stage.rglob("*") if path.is_file())
    return {
        "mod_id": MOD_ID,
        "version": version,
        "runtime": {**git_state(), "dll_sha256": sha256(stage / f"{MOD_ID}.dll")},
        "packs": [{"entry": f"packs/{pack.key}", "id": pack.pack_id, "format": pack.kind, "cards": pack.cards}
                  for pack in sorted(chosen.values(), key=lambda item: item.key)],
        "cards": sum(pack.cards for pack in chosen.values()),
        "gpu_variants": len(rows),
        "file_count": len(shipped),
        "installed_bytes": sum(path.stat().st_size for path in shipped),
        "files": {path.relative_to(stage).as_posix(): sha256(path) for path in shipped},
    }


def zip_folder(stage: Path, archive: Path) -> None:
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        files = {item.relative_to(stage).as_posix(): item for item in stage.rglob("*") if item.is_file()}
        for relative in sorted(files):
            path = files[relative]
            info = zipfile.ZipInfo(f"{MOD_ID}/{relative}", date_time=ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with path.open("rb") as stream, bundle.open(info, "w") as target:
                shutil.copyfileobj(stream, target, 1 << 20)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pack-source", type=Path, action="append", required=True,
                        help="mod-shaped folder or v2 pack folder; repeat, later sources win")
    parser.add_argument("--output", type=Path, required=True, help="new folder to create (must not exist)")
    parser.add_argument("--dll", type=Path, default=DEFAULT_DLL, help=f"built runtime (default: {DEFAULT_DLL.relative_to(REPO)})")
    parser.add_argument("--build", action="store_true", help="run dotnet build -c Release first")
    parser.add_argument("--version", help="override the manifest version")
    parser.add_argument("--zip", action="store_true", help="also write FullArtSpire-<version>.zip")
    parser.add_argument("--hardlink", action="store_true", help="hard-link pack files instead of copying, where possible")
    args = parser.parse_args(argv)
    try:
        if args.build:
            subprocess.run(["dotnet", "build", str(REPO / "src" / "FullArtSpire"), "-c", "Release", "-nologo", "-v", "q"], check=True)
        output = args.output.resolve()
        receipt = assemble(args.pack_source, args.dll.resolve(), args.version, output, args.hardlink)
        if args.zip:
            archive = output / f"FullArtSpire-{receipt['version']}.zip"
            zip_folder(output / MOD_ID, archive)
            receipt["archive"] = {"name": archive.name, "bytes": archive.stat().st_size, "sha256": sha256(archive)}
        (output / "package-receipt.json").write_text(json.dumps(receipt, indent=1) + "\n", encoding="utf-8", newline="\n")
    except (PackageError, subprocess.CalledProcessError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(f"{receipt['mod_id']} {receipt['version']}: {len(receipt['packs'])} packs, {receipt['cards']} cards, "
          f"{receipt['gpu_variants']} GPU variants, {receipt['file_count']} files, "
          f"{receipt['installed_bytes'] / 1e9:.2f} GB -> {output / MOD_ID}")
    if receipt["runtime"]["dirty"]:
        print("warning: built from a working tree with uncommitted changes", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
