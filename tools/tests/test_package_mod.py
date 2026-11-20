"""Tests for tools/package_mod.py. Run: python -m unittest discover -s tools/tests"""
from __future__ import annotations

import hashlib
import io
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import package_mod  # noqa: E402

try:
    from PIL import Image
except ImportError:  # --optimize-png needs Pillow.
    Image = None

STRIKE = "MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad"
WHIP = "MegaCrit.Sts2.Core.Models.Cards.KiWhip"


def write(path: Path, data: bytes | str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(data, str):
        data = data.encode("utf-8")
    path.write_bytes(data)
    return path


def write_json(path: Path, value) -> Path:
    return write(path, json.dumps(value))


def v1_card(model: str, name: str, layered: bool = False) -> dict:
    card = {"runtime_model": model, "art": f"art/{name}.png",
            "effects": {"surface": "foil", "permission_mask": f"effects/{name}_permission.png"}}
    if layered:
        card["layers"] = {"background": f"layers/{name}/background.png", "foreground": f"art/{name}.png",
                          "foreground_mask": f"layers/{name}/mask.png"}
    return card


class PackageTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory(prefix="fas-package-")
        self.root = Path(self._temp.name)
        self.dll = write(self.root / "build" / "FullArtSpire.dll", b"MZ fake runtime")
        self.output = self.root / "out"

    def tearDown(self) -> None:
        self._temp.cleanup()

    def mod_source(self, name: str = "source", art: bytes = b"strike-png", variant: bool = False) -> Path:
        source = self.root / name
        write_json(source / "packs" / "ironclad.pack", {"format": "sts2_full_art_standalone_pack",
                                                         "cards": [v1_card(STRIKE, "strike", layered=True)]})
        write(source / "art" / "strike.png", art)
        write(source / "layers" / "strike" / "mask.png", b"mask")
        write(source / "effects" / "strike_permission.png", b"permission")
        write(source / "art" / "unused.png", b"not referenced")
        dds = b"DDS background variant"
        write(source / "gpu" / "layers" / "strike" / "background.dds", dds)
        rows = [{"source": "layers/strike/background.png", "path": "gpu/layers/strike/background.dds",
                 "format": "BC1_UNORM", "sha256": hashlib.sha256(dds).hexdigest(), "source_sha256": "0" * 64,
                 "width": 4, "height": 4}]
        if variant:
            write(source / "layers" / "strike" / "background.png", b"png background")
        write_json(source / "gpu-textures.audit", {"schema_version": 2, "formats": ["BC1_UNORM"], "textures": rows})
        return source

    def v2_folder(self, parent: Path, folder: str, pack_id: str, model: str = WHIP) -> Path:
        pack = parent / folder
        write_json(pack / "pack.json", {"format": "full_art_pack", "format_version": 2, "id": pack_id,
                                        "name": "Test", "cards": [{"runtime_model": model, "art": "art/whip.png"}]})
        write(pack / "art" / "whip.png", b"whip")
        write(pack / "README.txt", "credits")
        return pack

    def package(self, *sources: Path, **options) -> dict:
        return package_mod.assemble(list(sources), self.dll, options.get("version"), self.output, False)

    def test_copies_only_referenced_files_and_runtime(self) -> None:
        receipt = self.package(self.mod_source())
        stage = self.output / "full_art_spire"
        for relative in ("full_art_spire.dll", "full_art_spire.json", "CHANGELOG.md", "packs/ironclad.pack",
                         "art/strike.png", "layers/strike/mask.png", "effects/strike_permission.png",
                         "gpu/layers/strike/background.dds", "gpu-textures.audit",
                         "shaders/CardEffectShader.gdshader", "shaders/CardLayerShader.gdshader"):
            self.assertTrue((stage / relative).is_file(), relative)
        self.assertFalse((stage / "art" / "unused.png").exists(), "unreferenced files stay behind")
        self.assertFalse((stage / "layers" / "strike" / "background.png").exists(), "a DDS-only texture ships once")
        manifest = json.loads((stage / "full_art_spire.json").read_text(encoding="utf-8"))
        self.assertEqual("full_art_spire", manifest["id"])
        self.assertEqual(receipt["version"], manifest["version"])
        self.assertEqual(1, receipt["cards"])
        self.assertEqual(1, receipt["gpu_variants"])
        self.assertEqual(hashlib.sha256(self.dll.read_bytes()).hexdigest(), receipt["runtime"]["dll_sha256"])
        self.assertEqual(receipt["file_count"], len(receipt["files"]))

    def test_version_override(self) -> None:
        receipt = self.package(self.mod_source(), version="0.4.0-rc1")
        manifest = json.loads((self.output / "full_art_spire" / "full_art_spire.json").read_text(encoding="utf-8"))
        self.assertEqual("0.4.0-rc1", manifest["version"])
        self.assertEqual("0.4.0-rc1", receipt["version"])

    def test_png_and_variant_both_present_ship_the_png(self) -> None:
        self.package(self.mod_source(variant=True))
        stage = self.output / "full_art_spire"
        self.assertTrue((stage / "layers" / "strike" / "background.png").is_file())
        self.assertFalse((stage / "gpu-textures.audit").exists(), "no variant is needed when the PNG ships")

    def test_v2_packs_ship_whole(self) -> None:
        source = self.mod_source()
        self.v2_folder(source / "packs", "fool", "megacrit.fool")
        standalone = self.v2_folder(self.root / "player", "lantern", "someone.lantern", model="Mod.Cards.Lantern")
        receipt = self.package(source, standalone)
        stage = self.output / "full_art_spire"
        for relative in ("packs/fool/pack.json", "packs/fool/art/whip.png", "packs/fool/README.txt",
                         "packs/lantern/pack.json", "packs/lantern/art/whip.png"):
            self.assertTrue((stage / relative).is_file(), relative)
        self.assertEqual(["packs/fool", "packs/ironclad.pack", "packs/lantern"], [pack["entry"] for pack in receipt["packs"]])

    def test_later_source_replaces_a_pack(self) -> None:
        first = self.mod_source("first", art=b"old")
        second = self.mod_source("second", art=b"new")
        self.package(first, second)
        self.assertEqual(b"new", (self.output / "full_art_spire" / "art" / "strike.png").read_bytes())

    def test_conflicting_files_between_packs_fail(self) -> None:
        first = self.mod_source("first", art=b"one")
        second = self.root / "second"
        write_json(second / "packs" / "silent.pack", {"cards": [{"runtime_model": WHIP, "art": "art/strike.png"}]})
        write(second / "art" / "strike.png", b"two")
        with self.assertRaisesRegex(package_mod.PackageError, "different files at the same path"):
            self.package(first, second)

    def test_missing_and_unsafe_paths_fail(self) -> None:
        source = self.mod_source()
        (source / "effects" / "strike_permission.png").unlink()
        with self.assertRaisesRegex(package_mod.PackageError, "referenced files missing"):
            self.package(source)
        for bad in ("../outside.png", "/abs.png", "C:/x.png", "art//x.png", "art/./x.png"):
            with self.subTest(bad=bad):
                write_json(source / "packs" / "ironclad.pack", {"cards": [{"runtime_model": STRIKE, "art": bad}]})
                with self.assertRaisesRegex(package_mod.PackageError, "unsafe path"):
                    self.package(source)

    def test_bad_gpu_variant_fails(self) -> None:
        source = self.mod_source()
        write(source / "gpu" / "layers" / "strike" / "background.dds", b"corrupted")
        with self.assertRaisesRegex(package_mod.PackageError, "audit hash"):
            self.package(source)

    def test_invalid_v2_and_duplicate_ids_fail(self) -> None:
        source = self.mod_source()
        pack = self.v2_folder(source / "packs", "fool", "megacrit.fool")
        self.v2_folder(source / "packs", "fool_copy", "megacrit.fool")
        with self.assertRaisesRegex(package_mod.PackageError, "used by both"):
            self.package(source)
        (source / "packs" / "fool_copy" / "pack.json").unlink()
        write_json(pack / "pack.json", {"format": "full_art_pack", "format_version": 3, "id": "megacrit.fool", "cards": []})
        with self.assertRaisesRegex(package_mod.PackageError, "format_version"):
            self.package(source)

    def test_refuses_existing_output_and_game_folders(self) -> None:
        source = self.mod_source()
        self.output.mkdir()
        with self.assertRaisesRegex(package_mod.PackageError, "already exists"):
            self.package(source)
        game = self.root / "Slay the Spire 2"
        write(game / "SlayTheSpire2.exe", b"")
        with self.assertRaisesRegex(package_mod.PackageError, "game install"):
            package_mod.assemble([source], self.dll, None, game / "mods" / "out", False)
        self.assertFalse((game / "mods").exists())

    def test_missing_dll_fails(self) -> None:
        with self.assertRaisesRegex(package_mod.PackageError, "build output missing"):
            package_mod.assemble([self.mod_source()], self.root / "nope.dll", None, self.output, False)

    def test_zip_is_rooted_at_the_mod_folder(self) -> None:
        self.package(self.mod_source())
        archive = self.root / "mod.zip"
        package_mod.zip_folder(self.output / "full_art_spire", archive)
        with zipfile.ZipFile(archive) as bundle:
            names = bundle.namelist()
            self.assertIsNone(bundle.testzip())
            self.assertEqual(b"strike-png", bundle.read("full_art_spire/art/strike.png"))
        self.assertIn("full_art_spire/full_art_spire.dll", names)
        self.assertTrue(all(name.startswith("full_art_spire/") for name in names))
        self.assertEqual(sorted(names), names, "entries are in a stable order")

    def test_zip_levels_by_file_type(self) -> None:
        self.assertEqual(package_mod.PNG_ZIP_LEVEL, package_mod.zip_level("full_art_spire/art/x.PNG"))
        self.assertEqual(package_mod.ZIP_LEVEL, package_mod.zip_level("full_art_spire/gpu/x.dds"))
        info = package_mod.zip_entry("full_art_spire/art/x.png")
        self.assertEqual(package_mod.PNG_ZIP_LEVEL, getattr(info, "compress_level", getattr(info, "_compresslevel", None)))

    @unittest.skipIf(Image is None, "Pillow is not installed")
    def test_optimize_png_keeps_pixels_and_records_hashes(self) -> None:
        def encode(image, **options) -> bytes:
            buffer = io.BytesIO()
            image.save(buffer, "PNG", compress_level=0, **options)
            return buffer.getvalue()

        def sha(data: bytes) -> str:
            return hashlib.sha256(data).hexdigest()

        gradient = Image.linear_gradient("L").resize((64, 96))
        source = self.mod_source(art=encode(Image.merge("RGBA", (gradient, gradient.rotate(90), gradient, gradient))))
        keyed = encode(gradient.convert("RGB"), transparency=(0, 0, 0))   # tRNS: a colour chunk
        deep = encode(Image.new("I;16", (64, 96), 4000))                   # 16-bit
        write(source / "effects" / "strike_permission.png", keyed)
        write(source / "layers" / "strike" / "mask.png", deep)
        original = (source / "art" / "strike.png").read_bytes()

        receipt = package_mod.assemble([source], self.dll, None, self.output, True, True)
        stage = self.output / "full_art_spire"
        shipped = (stage / "art" / "strike.png").read_bytes()
        self.assertLess(len(shipped), len(original))
        self.assertEqual(original, (source / "art" / "strike.png").read_bytes(), "a hard-linked source is not written through")
        with Image.open(io.BytesIO(original)) as before, Image.open(io.BytesIO(shipped)) as after:
            self.assertEqual((before.mode, before.size, before.tobytes()), (after.mode, after.size, after.tobytes()))
        self.assertEqual(keyed, (stage / "effects" / "strike_permission.png").read_bytes(), "colour chunks keep their bytes")
        self.assertEqual(deep, (stage / "layers" / "strike" / "mask.png").read_bytes(), "16-bit PNGs keep their bytes")
        audit = json.loads((stage / package_mod.REENCODE_AUDIT).read_text(encoding="utf-8"))
        self.assertEqual([{"path": "art/strike.png", "sha256": sha(shipped), "source_sha256": sha(original)}], audit["files"])
        self.assertEqual(1, receipt["reencoded_pngs"])
        self.assertEqual(len(original) - len(shipped), receipt["reencode_saved_bytes"])

        again = package_mod.assemble([stage], self.dll, None, self.root / "again", False, True)
        audit = json.loads((self.root / "again" / "full_art_spire" / package_mod.REENCODE_AUDIT).read_text(encoding="utf-8"))
        self.assertEqual(sha(original), audit["files"][0]["source_sha256"], "packaging again keeps the original hash")
        self.assertEqual(1, again["reencoded_pngs"])

    def test_carried_reencode_rows_need_matching_bytes(self) -> None:
        source = self.mod_source()
        write_json(source / package_mod.REENCODE_AUDIT, {"schema_version": 1, "files": [
            {"path": "art/strike.png", "sha256": "0" * 64, "source_sha256": "1" * 64}]})
        receipt = self.package(source)
        self.assertEqual(0, receipt["reencoded_pngs"], "a row for different bytes is dropped")
        self.assertFalse((self.output / "full_art_spire" / package_mod.REENCODE_AUDIT).exists())


if __name__ == "__main__":
    unittest.main()
