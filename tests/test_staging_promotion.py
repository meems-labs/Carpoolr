"""Safety checks for the CI step that writes staging image references."""

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

specification = importlib.util.spec_from_file_location(
    "staging_promotion", Path(__file__).resolve().parents[1] / "scripts/update-staging-images.py"
)
update_staging_images = importlib.util.module_from_spec(specification)
specification.loader.exec_module(update_staging_images)


class StagingPromotionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.staging = self.root / "deploy/kubernetes/overlays/staging/kustomization.yaml"
        self.staging.parent.mkdir(parents=True)
        self.original = (
            "images:\n"
            "  - name: ghcr.io/meems-labs/carpoolr-api\n    newTag: develop\n"
            "  - name: ghcr.io/meems-labs/carpoolr-web\n    newTag: develop\n"
        )
        self.staging.write_text(self.original, encoding="utf-8")
        self.production = self.root / "deploy/kubernetes/overlays/production/kustomization.yaml"
        self.production.parent.mkdir(parents=True)
        self.production.write_text("production-is-unchanged", encoding="utf-8")
        patch = mock.patch.object(
            update_staging_images, "__file__", str(self.root / "scripts/update-staging-images.py")
        )
        patch.start()
        self.addCleanup(patch.stop)

    def run_promotion(self, revision):
        with mock.patch.object(sys, "argv", ["promote", revision]):
            update_staging_images.main()

    def test_both_images_use_one_immutable_revision_without_touching_production(self):
        revision = "0123456789abcdef0123456789abcdef01234567"
        self.run_promotion(revision)
        promoted = self.staging.read_text(encoding="utf-8")
        self.assertEqual(promoted.count(f"newTag: {revision}"), 2)
        self.assertEqual(self.production.read_text(encoding="utf-8"), "production-is-unchanged")

    def test_invalid_revisions_never_write_the_manifest(self):
        for revision in ("develop", "abc123", "A" * 40, "0" * 39, "0" * 40 + " extra"):
            with self.subTest(revision=revision), self.assertRaises(SystemExit):
                self.run_promotion(revision)
            self.assertEqual(self.staging.read_text(encoding="utf-8"), self.original)

    def test_missing_declaration_does_not_partially_promote_one_image(self):
        invalid = self.original.replace("carpoolr-web", "another-image")
        self.staging.write_text(invalid, encoding="utf-8")
        with self.assertRaises(SystemExit):
            self.run_promotion("0" * 40)
        self.assertEqual(self.staging.read_text(encoding="utf-8"), invalid)


if __name__ == "__main__":
    unittest.main()
