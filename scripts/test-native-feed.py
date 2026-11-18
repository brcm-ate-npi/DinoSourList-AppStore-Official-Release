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


if __name__ == "__main__":
    unittest.main()
