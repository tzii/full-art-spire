"""Tests for tools/validate_pack.py. Run: python -m unittest discover -s tools/tests"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_pack  # noqa: E402

WHIP = "MegaCrit.Sts2.Core.Models.Cards.KiWhip"
LANTERN = "SomeMod.Cards.Lantern"


def run_cli(argv: list[str]) -> int:
    with contextlib.redirect_stdout(io.StringIO()):
        return validate_pack.main(argv)


def write(path: Path, data: bytes | str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data.encode("utf-8") if isinstance(data, str) else data)
    return path


def write_json(path: Path, value) -> Path:
    return write(path, json.dumps(value))


def dds(width: int = 4, height: int = 6, mipmaps: int = 3) -> bytes:
    header = bytearray(128)
    header[:4] = b"DDS "
    struct.pack_into("<IIII", header, 4, 124, 0x2100F, height, width)
    struct.pack_into("<I", header, 28, mipmaps)
    return bytes(header) + b"\0" * 32


def v2_pack(**overrides) -> dict:
    pack = {"format": "full_art_pack", "format_version": 2, "id": "someone.lantern", "name": "Lantern",
            "version": "1.0.0", "authors": ["someone"], "credits": "Art (c) its authors",
            "cards": [{"runtime_model": LANTERN, "art": "art/lantern.png"}]}
    pack.update(overrides)
    return pack


def layered(**layers) -> dict:
    base = {"background": "layers/lantern/background.png", "foreground": "art/lantern.png",
            "foreground_mask": "layers/lantern/mask.png"}
    base.update(layers)
    return {"runtime_model": LANTERN, "art": "art/lantern.png", "layers": base}


def v1_card(model: str, name: str) -> dict:
    return {"canonical_asset_name": name, "display_name": name.title(), "pool": "colorless", "status": "final",
            "source_paths": [f"sources/{name}.png"], "art": f"art/{name}.png", "repo_final_png": f"final/{name}.png",
            "runtime_model": model, "runtime_model_source": "manual",
            "effects": {"rarity_flame": "real_rarity", "surface": "none", "rarity_policy": "rare_first",
                        "effect_mask": "", "protection_mask": "", "permission_mask": "", "intensity": 1}}


class ValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory(prefix="fas-validate-")
        self.root = Path(self._temp.name)

    def tearDown(self) -> None:
        self._temp.cleanup()

    def pack(self, data: dict, folder: str = "lantern", files: tuple[str, ...] = ("art/lantern.png",)) -> Path:
        pack = self.root / folder
        write_json(pack / "pack.json", data)
        for relative in files:
            write(pack / relative, b"png " + relative.encode())
        return pack

    def gpu(self, pack: Path, source: str, variant: bytes | None = None, **row) -> dict:
        variant = dds() if variant is None else variant
        target = "gpu/" + source.removesuffix(".png") + ".dds"
        write(pack / target, variant)
        entry = {"source": source, "path": target, "format": "BC1_UNORM",
                 "sha256": hashlib.sha256(variant).hexdigest(), "source_sha256": "0" * 64, "width": 4, "height": 6}
        entry.update(row)
        write_json(pack / "gpu-textures.audit", {"schema_version": 2, "formats": ["BC1_UNORM"], "textures": [entry]})
        return entry

    def check(self, pack: Path) -> validate_pack.Report:
        return validate_pack.validate_v2_pack(pack)

    def assertIssue(self, report: validate_pack.Report, text: str) -> None:
        self.assertTrue(any(text in issue for issue in report.issues), f"no issue with {text!r}: {report.issues}")

    def assertWarning(self, report: validate_pack.Report, text: str) -> None:
        self.assertTrue(any(text in warning for warning in report.warnings),
                        f"no warning with {text!r}: {report.warnings}")

    def test_valid_pack_passes(self) -> None:
        report = self.check(self.pack(v2_pack()))
        self.assertEqual([], report.issues)
        self.assertEqual([], report.warnings)
        self.assertEqual(("someone.lantern", 1, [LANTERN]), (report.pack_id, report.cards, report.models))
        self.assertEqual(0, run_cli(["--pack", str(self.root / "lantern")]))
        self.assertEqual(0, run_cli(["--pack", str(self.root / "lantern" / "pack.json")]))

    def test_manifest_rules(self) -> None:
        report = self.check(self.pack(v2_pack(format="sts2_full_art_standalone_pack", format_version=3,
                                              id="NoNamespace", priority="1", min_game_version="soon",
                                              requires=[{"mod": ""}, {"mod": "x", "min_version": "one"}],
                                              extra=True, credits="")))
        for text in ("format must be", "format_version must be 2", "id must look like", "priority must be",
                     "min_game_version 'soon'", "requires #1: mod is required", "requires #2: min_version"):
            self.assertIssue(report, text)
        self.assertWarning(report, "fields the runtime ignores: extra")
        self.assertWarning(report, "credits is empty")
        self.assertEqual(1, run_cli(["--pack", str(self.root / "lantern")]))

    def test_null_optional_fields_are_fine(self) -> None:
        report = self.check(self.pack(v2_pack(version=None, min_game_version=None, requires=None, authors=None)))
        self.assertEqual([], report.issues)

    def test_cards_rules(self) -> None:
        cards = [{"runtime_model": LANTERN, "art": "art/lantern.png"},
                 {"runtime_model": LANTERN.lower(), "art": "art/lantern.png"},
                 {"art": "art/lantern.png"},
                 {"runtime_model": "Lonely", "art": "art/lantern.png", "colour": "red"},
                 {"runtime_model": "A.B", "art": "../escape.png"},
                 {"runtime_model": "A.C", "art": "art\\lantern.png"},
                 {"runtime_model": "A.D", "art": "art/missing.png"},
                 {"runtime_model": "A.E", "art": "art/lantern.png", "preserve_original_layout": "yes"},
                 None]
        report = self.check(self.pack(v2_pack(cards=cards)))
        for text in ("is listed twice", "runtime_model is required", "art must not contain empty, '.' or '..'",
                     "art must use / as the separator", "art file missing: art/missing.png",
                     "preserve_original_layout has the wrong type", "card #9: must be an object"):
            self.assertIssue(report, text)
        self.assertWarning(report, "full .NET type name")
        self.assertWarning(report, "fields the runtime ignores: colour")

    def test_letter_case_and_unused_files(self) -> None:
        pack = self.pack(v2_pack(cards=[{"runtime_model": LANTERN, "art": "art/Lantern.png"}]),
                         files=("art/lantern.png", "art/old.png", "README.md"))
        report = self.check(pack)
        self.assertWarning(report, "differs in letter case")
        self.assertWarning(report, "1 files no card uses")

    def test_layers_follow_the_runtime_policy(self) -> None:
        files = ("art/lantern.png", "layers/lantern/background.png", "layers/lantern/mask.png")
        self.assertEqual([], self.check(self.pack(v2_pack(cards=[layered()]), files=files)).issues)
        bad = [layered(depth=2), layered(foreground_mask_channel="green"),
               layered(background="art/lantern.png"), layered(depth="deep"),
               layered(registration={"mode": "normalized_source_uv", "source_dimensions": [1000, 1500],
                                     "mask_dimensions": [1000, 1400], "background_dimensions": [1000, 1500]}),
               layered(finish_mask="layers/lantern/finish.png")]
        report = self.check(self.pack(v2_pack(cards=bad), files=files))
        for text in ("layers.depth must be between 0.0 and 1.0", "foreground_mask_channel must be",
                     "background and foreground are the same file", "layers.depth has the wrong type",
                     "mask_dimensions does not match the source aspect ratio",
                     "layers.finish_mask file missing"):
            self.assertIssue(report, text)

    def test_registration_needs_the_art_as_foreground(self) -> None:
        registration = {"mode": "normalized_source_uv", "source_dimensions": [1000, 1500],
                        "mask_dimensions": [1000, 1500], "background_dimensions": [1000, 1500]}
        files = ("art/lantern.png", "art/other.png", "layers/lantern/background.png", "layers/lantern/mask.png")
        good = self.check(self.pack(v2_pack(cards=[layered(registration=registration)]), files=files))
        self.assertEqual([], good.issues)
        bad = self.check(self.pack(v2_pack(cards=[layered(registration=registration, foreground="art/other.png")]),
                                   files=files))
        self.assertIssue(bad, "foreground must be the card's art")

    def test_effects(self) -> None:
        cards = [{"runtime_model": "A.A", "art": "art/lantern.png", "effects": {"surface": "glitter"}},
                 {"runtime_model": "A.B", "art": "art/lantern.png", "effects": {"surface": "foil"}},
                 {"runtime_model": "A.C", "art": "art/lantern.png",
                  "effects": {"surface": "foil", "permission_mask": "effects/missing.png"}},
                 {"runtime_model": "A.D", "art": "art/lantern.png", "effects": {"intensity": "high"}}]
        report = self.check(self.pack(v2_pack(cards=cards)))
        self.assertIssue(report, "effects.surface 'glitter' is not one of")
        self.assertWarning(report, "there is no permission_mask")
        self.assertIssue(report, "effects.permission_mask file missing")
        self.assertIssue(report, "effects.intensity has the wrong type")

    def test_gpu_only_art(self) -> None:
        pack = self.pack(v2_pack(), files=())
        self.gpu(pack, "art/lantern.png")
        report = self.check(pack)
        self.assertEqual([], report.issues)
        self.assertEqual(1, report.gpu_only_art)

    def test_broken_gpu_variants(self) -> None:
        pack = self.pack(v2_pack(), files=())
        row = self.gpu(pack, "art/lantern.png")
        write(pack / row["path"], dds(mipmaps=1))
        self.assertIssue(self.check(pack), "does not match its sha256")
        self.gpu(pack, "art/lantern.png", dds(mipmaps=1))
        self.assertIssue(self.check(pack), "has no mipmaps")
        self.gpu(pack, "art/lantern.png", dds(width=8))
        self.assertIssue(self.check(pack), "is 8x6, but the index says 4x6")
        self.gpu(pack, "art/lantern.png", b"not a dds")
        self.assertIssue(self.check(pack), "is not a DDS file")
        # With the PNG present, the runtime falls back to it: only a warning, plus the stale source hash.
        write(pack / "art" / "lantern.png", b"png")
        report = self.check(pack)
        self.assertEqual([], report.issues)
        self.assertWarning(report, "is not a DDS file")
        self.assertWarning(report, "was made from a different art/lantern.png")

    def test_gpu_index_voided_by_a_bad_row(self) -> None:
        pack = self.pack(v2_pack(), files=())
        self.gpu(pack, "effects/lantern_permission.png")
        report = self.check(pack)
        self.assertIssue(report, "is neither art nor a layer background; the runtime ignores every GPU variant")
        self.assertIssue(report, "art file missing")
        self.gpu(pack, "art/lantern.png", format="ASTC")
        self.assertWarning(self.check(pack), "format 'ASTC', which the runtime skips")

    def test_mod_folder(self) -> None:
        mod = self.root / "mod"
        write_json(mod / "packs" / "colorless.pack", {"format": "sts2_full_art_standalone_pack", "version": 1,
                                                      "pool": "colorless", "card_count": 2,
                                                      "cards": [v1_card(WHIP, "ki_whip"), v1_card(LANTERN, "lantern")]})
        write(mod / "art" / "lantern.png", b"png")
        write(mod / "art" / "unused.png", b"png")
        self.gpu(mod, "art/ki_whip.png")
        write_json(mod / "packs" / "fool" / "pack.json", v2_pack(id="megacrit.colorless"))
        write(mod / "packs" / "fool" / "art" / "lantern.png", b"png")
        write_json(mod / "packs" / "extra" / "pack.json",
                   v2_pack(id="someone.extra", priority=5, cards=[{"runtime_model": LANTERN, "art": "art/lantern.png"}]))
        write(mod / "packs" / "extra" / "art" / "lantern.png", b"png")
        write(mod / "packs" / "notes" / "readme.txt", "not a pack")

        reports, issues, warnings = validate_pack.validate_mod_folder(mod, None, False, False)
        self.assertEqual([], issues)
        self.assertEqual(["packs/colorless.pack", "packs/extra", "packs/fool"], [report.title for report in reports])
        self.assertEqual([[], [], []], [report.issues for report in reports])
        self.assertEqual(1, reports[0].gpu_only_art, "v1 art shipped only as a GPU variant is not missing")
        joined = "\n".join(warnings)
        self.assertIn("pack id megacrit.colorless is in packs/colorless.pack and packs/fool; only packs/fool loads",
                      joined)
        self.assertIn(f"{LANTERN} is in packs/extra, packs/fool; packs/extra wins", joined)
        self.assertNotIn(WHIP, joined, "the cards of a pack that loses its id are not in play")
        self.assertIn("packs/notes has no pack.json", joined)
        self.assertIn("1 files under art/, layers/, effects/ or gpu/ that no card uses, e.g. art/unused.png", joined)
        self.assertEqual(0, run_cli(["--mod-folder", str(mod)]))

    def test_v1_pack_with_gpu_only_art(self) -> None:
        mod = self.root / "mod"
        pack = write_json(mod / "packs" / "colorless.pack", {"format": "sts2_full_art_standalone_pack", "version": 1,
                                                             "pool": "colorless", "card_count": 1,
                                                             "cards": [v1_card(WHIP, "ki_whip")]})
        self.assertEqual(1, run_cli(["--pack", str(pack)]), "art is missing without a variant")
        self.gpu(mod, "art/ki_whip.png")
        self.assertEqual(0, run_cli(["--pack", str(pack)]))


if __name__ == "__main__":
    unittest.main()
