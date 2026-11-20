"""Keeps docs/pack.schema.json in step with tools/validate_pack.py, which mirrors the runtime.

Run: python -m unittest discover -s tools/tests
"""
from __future__ import annotations

import json
import re
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_pack  # noqa: E402

SCHEMA = json.loads((Path(__file__).resolve().parents[2] / "docs" / "pack.schema.json").read_text(encoding="utf-8"))
DEFS = SCHEMA["$defs"]

try:
    import jsonschema
except ImportError:  # Optional: CI installs nothing beyond the standard library.
    jsonschema = None


def matches(pattern: str, value: str) -> bool:
    # JSON Schema patterns are unanchored searches; ours anchor themselves.
    return re.search(pattern, value) is not None


class SchemaTests(unittest.TestCase):
    def test_top_level_fields_and_id(self) -> None:
        self.assertEqual(validate_pack.V2_FIELDS, set(SCHEMA["properties"]))
        self.assertEqual(validate_pack.V2_FORMAT, SCHEMA["properties"]["format"]["const"])
        self.assertEqual(validate_pack.V2_FORMAT_VERSION, SCHEMA["properties"]["format_version"]["const"])
        self.assertEqual(validate_pack.PACK_ID_RE.pattern, SCHEMA["properties"]["id"]["pattern"])

    def test_known_fields_are_ones_the_validator_knows(self) -> None:
        self.assertLessEqual(set(DEFS["card"]["properties"]), validate_pack.CARD_FIELDS)
        self.assertEqual(validate_pack.LAYER_FIELDS, set(DEFS["layers"]["properties"]))
        self.assertLessEqual(set(DEFS["effects"]["properties"]), validate_pack.EFFECT_FIELDS)
        registration = DEFS["layers"]["properties"]["registration"]
        self.assertEqual(validate_pack.REGISTRATION_FIELDS, set(registration["required"]))

    def test_layer_ranges(self) -> None:
        for name, (low, high) in validate_pack.LAYER_RANGES.items():
            with self.subTest(name=name):
                bounds = DEFS["layers"]["properties"][name]
                self.assertEqual((low, high), (bounds["minimum"], bounds["maximum"]))

    def test_surfaces(self) -> None:
        self.assertEqual(validate_pack.RUNTIME_SURFACES, set(DEFS["effects"]["properties"]["surface"]["enum"]))

    def test_canvas_bounds(self) -> None:
        width, height = DEFS["canvas"]["prefixItems"]
        self.assertTrue(validate_pack.valid_canvas(width["minimum"], width["minimum"] + 1))
        self.assertFalse(validate_pack.valid_canvas(width["minimum"] - 1, height["maximum"]))
        self.assertFalse(validate_pack.valid_canvas(width["maximum"] + 1, height["maximum"]))
        self.assertFalse(validate_pack.valid_canvas(width["minimum"], height["maximum"] + 1))

    def test_versions(self) -> None:
        pattern = DEFS["version"]["pattern"]
        for value in ("0.113.1", "v1.2.3", "1.0.0-rc.1", "1.0.0+build.5", "1.0.0-beta+exp.sha.5114f85",
                      "1.0", "1.0.0.0", "x1.0.0", "1.0.0-", "1. 0.0"):
            with self.subTest(value=value):
                self.assertEqual(validate_pack.VERSION_RE.fullmatch(value) is not None, matches(pattern, value))
        self.assertTrue(matches(pattern, ""), "an empty version means no minimum")

    def test_paths_agree_with_the_runtime_rules(self) -> None:
        pattern = DEFS["path"]["pattern"]
        for value in ("art/x.png", "art/X.PNG", "layers/a b/background.png", "a.b/c.png", "art/x.png.png",
                      "", " art/x.png", "art/x.png ", "/art/x.png", "\\art\\x.png", "art\\x.png", "C:/x.png",
                      "../x.png", "art/../x.png", "./x.png", "art/./x.png", "art//x.png", "art/x.jpg",
                      "art /x.png", "art./x.png", "art/x .png", "art/x?.png", "art/x\x01.png", "art/x|y.png",
                      "art/.png", "art/..png", ".hidden/x.png", "art/x"):
            with self.subTest(value=value):
                self.assertEqual(validate_pack.path_problem(value) is None, matches(pattern, value),
                                 validate_pack.path_problem(value))

    @unittest.skipIf(jsonschema is None, "jsonschema is not installed")
    def test_validates_packs(self) -> None:
        jsonschema.Draft202012Validator.check_schema(SCHEMA)
        validator = jsonschema.Draft202012Validator(SCHEMA)
        good = {"$schema": "./pack.schema.json", "format": "full_art_pack", "format_version": 2,
                "id": "someone.lantern", "name": "Lantern", "version": "1.0.0", "authors": ["someone"],
                "credits": "Art (c) its authors", "requires": [{"mod": "SomeMod", "min_version": "1.2.0"}],
                "min_game_version": "0.113.1", "priority": 5,
                "cards": [{"runtime_model": "SomeMod.Cards.Lantern", "art": "art/lantern.png",
                           "display_name": "Lantern", "status": "final",
                           "layers": {"background": "layers/lantern/background.png", "foreground": "art/lantern.png",
                                      "foreground_mask": "layers/lantern/mask.png", "foreground_mask_channel": "alpha",
                                      "registration": {"mode": "normalized_source_uv",
                                                       "source_dimensions": [1024, 1536],
                                                       "mask_dimensions": [512, 768],
                                                       "background_dimensions": [1024, 1536]},
                                      "depth": 0.5, "frame_inset": 0.05},
                           "effects": {"surface": "foil", "permission_mask": "effects/lantern.png",
                                       "intensity": 1, "phase_seed": None}}]}
        self.assertEqual([], [error.message for error in validator.iter_errors(good)])
        for name, broken in (("bad id", {"id": "Lantern"}), ("old format", {"format_version": 1}),
                             ("typo", {"prioirty": 1}), ("escaping art", {"cards": [{"runtime_model": "M.C", "art": "../x.png"}]}),
                             ("depth range", {"cards": [{"runtime_model": "M.C", "art": "a.png", "layers": {"depth": 2}}]}),
                             ("surface", {"cards": [{"runtime_model": "M.C", "art": "a.png", "effects": {"surface": "glow"}}]}),
                             ("tiny canvas", {"cards": [{"runtime_model": "M.C", "art": "a.png", "layers": {
                                 "registration": {"mode": "normalized_source_uv", "source_dimensions": [50, 100],
                                                  "mask_dimensions": [100, 150], "background_dimensions": [100, 150]}}}]})):
            with self.subTest(name=name):
                self.assertTrue(list(validator.iter_errors({**good, **broken})), name)


if __name__ == "__main__":
    unittest.main()
