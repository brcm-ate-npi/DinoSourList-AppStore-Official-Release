import hashlib
import importlib.util
import json
import tempfile
import subprocess
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("feed", Path(__file__).with_name("verify-native-feed.py"))
feed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(feed)
prune_spec = importlib.util.spec_from_file_location("prune", Path(__file__).with_name("prune-native-feed.py"))
prune = importlib.util.module_from_spec(prune_spec)
prune_spec.loader.exec_module(prune)


class FeedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for os_name in ("win", "osx"):
            name = "DinoSaurListNative-0.1.0-preview.18-" + os_name + "-preview-full.nupkg"
            content = (os_name + " package").encode()
            (self.root / name).write_bytes(content)
            asset = {"Type": "Full", "Version": "0.1.0-preview.18", "FileName": name, "Size": len(content), "SHA256": hashlib.sha256(content).hexdigest()}
            (self.root / ("releases." + os_name + "-preview.json")).write_text(json.dumps({"Assets": [asset]}))

    def test_valid_pair_and_wrong_expected_version(self):
        self.assertEqual("0.1.0-preview.18", feed.verify(self.root, "0.1.0-preview.18"))
        with self.assertRaises(ValueError):
            feed.verify(self.root, "wrong-version")

    def test_corrupted_package_and_missing_os_fail(self):
        package = next(self.root.glob("*-win-preview-full.nupkg"))
        package.write_bytes(b"x" * package.stat().st_size)
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            feed.verify(self.root)
        next(self.root.glob("releases.osx*.json")).unlink()
        with self.assertRaises(ValueError):
            feed.verify(self.root)

    def test_invalid_feed_never_changes_existing_live_files(self):
        target = self.root / "live"
        target.mkdir()
        old = target / "releases.win-preview.json"
        old.write_text("existing feed")
        next(self.root.glob("*-win-preview-full.nupkg")).write_bytes(b"corrupted")
        result = subprocess.run(["bash", str(Path(__file__).with_name("deploy-native-feed.sh")), str(self.root), str(target)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("existing feed", old.read_text())
        self.assertEqual([old], list(target.iterdir()))

    def test_path_escape_fails(self):
        path = self.root / "releases.win-preview.json"
        data = json.loads(path.read_text())
        data["Assets"][0]["FileName"] = "../outside.nupkg"
        path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, "filename"):
            feed.verify(self.root)

    def add_history(self):
        for platform in ("win", "osx"):
            for version in (7, 13, 18, 19, 20):
                (self.root / ("DinoSaurListNative-0.1.0-preview." + str(version) + "-" + platform + "-preview-full.nupkg")).write_bytes(b"old")

    def test_history_is_numeric_per_os_and_live_references_are_protected(self):
        self.add_history()
        # .18 remains referenced by the fixture feeds even though it is not latest-two.
        removed = [p.name for p in prune.candidates(self.root)]
        self.assertEqual(4, len(removed))
        self.assertTrue(all("preview.7-" in p or "preview.13-" in p for p in removed))
        self.assertEqual(removed, [p.name for p in prune.candidates(self.root)])
        self.assertTrue(all((self.root / p).exists() for p in removed))  # dry run

    def test_stable_channel_unknown_files_and_legacy_references_are_preserved(self):
        self.add_history()
        stable = self.root / "DinoSaurListNative-1.0.0-win-full.nupkg"
        stable.write_bytes(b"stable")
        unknown = self.root / "other-product-0.0.1-full.nupkg"
        unknown.write_bytes(b"other")
        old = "DinoSaurListNative-0.1.0-preview.7-win-preview-full.nupkg"
        (self.root / "RELEASES-win-preview").write_text("HASH " + old + " 3\n")
        removed = prune.candidates(self.root)
        self.assertNotIn(self.root / old, removed)
        self.assertNotIn(stable, removed)
        self.assertNotIn(unknown, removed)
        self.assertEqual(3, len(removed))

    def test_invalid_or_missing_feed_prevents_any_pruning(self):
        self.add_history()
        path = self.root / "releases.osx-preview.json"
        path.write_text("invalid json")
        with self.assertRaises(ValueError):
            prune.candidates(self.root)
        self.assertEqual(10, len(list(self.root.glob("*.nupkg"))))
        for path in self.root.glob("releases.*.json"):
            path.unlink()
        with self.assertRaisesRegex(ValueError, "No live feeds"):
            prune.candidates(self.root)

    def test_successful_deployment_prunes_after_new_feed_and_regenerates_listing(self):
        target = self.root / "live"
        target.mkdir()
        for platform in ("win", "osx"):
            for version in (7, 13):
                (target / ("DinoSaurListNative-0.1.0-preview." + str(version) + "-" + platform + "-preview-full.nupkg")).write_bytes(b"old")
        subprocess.run(["bash", str(Path(__file__).with_name("deploy-native-feed.sh")), str(self.root), str(target)], check=True, stdout=subprocess.PIPE)
        self.assertEqual(4, len(list(target.glob("*.nupkg"))))
        self.assertFalse(any("preview.7-" in p.name for p in target.iterdir()))
        names = [entry["name"] for entry in json.loads((target / "list.json").read_text())]
        self.assertFalse(any("preview.7-" in name for name in names))
        self.assertEqual("0.1.0-preview.18", feed.verify(target))


if __name__ == "__main__":
    unittest.main()
