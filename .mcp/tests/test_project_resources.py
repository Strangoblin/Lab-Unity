"""Resource remapping is read-only and never expands the gate's scope."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from validation.project_resources import ResourceError, load_resources, resource_path


class ResourceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        (self.root / ".agents").mkdir()
        (self.root / "AGENTS.md").write_text("test root")
        (self.root / ".mcp").mkdir()
        (self.root / "content").mkdir()
        (self.root / "content/target.cs").write_text("source")

    def tearDown(self):
        self.temp.cleanup()

    def bind(self, resources):
        (self.root / ".mcp/project-resources.json").write_text(json.dumps({"version": 1, "resources": resources}))

    def test_remap_to_project_without_assets(self):
        self.bind({"unity.debug-output-feature": "content/target.cs"})
        self.assertEqual(resource_path("unity.debug-output-feature", project_root=self.root), self.root / "content/target.cs")
        (self.root / "content/renamed.cs").write_text("source")
        self.bind({"unity.debug-output-feature": "content/renamed.cs"})
        self.assertEqual(resource_path("unity.debug-output-feature", project_root=self.root).name, "renamed.cs")

    def test_missing_never_falls_back(self):
        with self.assertRaises(ResourceError):
            load_resources(self.root)
        self.bind({"missing": "content/no.cs"})
        self.assertIn("missing", load_resources(self.root)[1])
        with self.assertRaises(ResourceError):
            resource_path("missing", project_root=self.root)
        with self.assertRaises(ResourceError):
            resource_path("unknown", project_root=self.root)

    def test_rejects_escaping_and_malformed_configuration(self):
        for path in ["../other.cs", "/tmp/other.cs", "content/../../other.cs", "C:/other.cs", ""]:
            with self.subTest(path=path):
                self.bind({"bad": path})
                with self.assertRaises(ResourceError):
                    load_resources(self.root)
        (self.root / "content/escape").symlink_to(self.root.parent, target_is_directory=True)
        self.bind({"bad": "content/escape/no.cs"})
        with self.assertRaises(ResourceError):
            load_resources(self.root)

    def test_migrated_camera_retains_existing_use_decision(self):
        from validation.script_library import list_scripts, validate_decision
        self.assertTrue(any(item["name"] == "camera-offscreen-capture.cs" for item in list_scripts()))
        self.assertEqual(validate_decision("USE camera-offscreen-capture.cs")["status"], "OK")
        self.assertEqual(validate_decision("USE deliberately-missing.cs")["status"], "DENIED")

    def test_locations_do_not_authorize_writes(self):
        from validation.project_paths import validate_path
        self.bind({"asset.root": "content"})
        self.assertEqual(resource_path("asset.root", project_root=self.root), self.root / "content")
        self.assertEqual(validate_path(str(self.root / "content/new.cs"))["status"], "DENIED")


if __name__ == "__main__":
    unittest.main()
