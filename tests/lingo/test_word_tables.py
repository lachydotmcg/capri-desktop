import hashlib
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("word_tables.py").resolve()
NAMES = tuple(p.name for p in (SCRIPT.parents[2] / "assets/words").glob("*.txt"))
EXISTING = [f"Existing key {i:03d}" for i in range(895)]
ADDED = ["Nothing matches.", "No chats kept yet.", "Email address", "App password",
         "Incoming mail server", "Nothing in this day."] + [f"New key {i:03d}" for i in range(49)]


class WordTablesTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.temp = Path(temp.name)
        self.current = self.temp / "current"
        self.previous = self.temp / "previous"
        for root, keys in [(self.current, EXISTING + ADDED), (self.previous, EXISTING)]:
            tables = root / "assets/words"
            tables.mkdir(parents=True)
            for name in NAMES:
                rows = keys if name == "english.txt" else [f"{key}\tTranslation of {key}" for key in keys]
                (tables / name).write_text("\n".join(rows) + "\n", encoding="utf-8")
        self.script = self.current / "tests/lingo/word_tables.py"
        self.script.parent.mkdir(parents=True)
        shutil.copyfile(SCRIPT, self.script)

    def checked(self, compare=False):
        args = [".", str(self.previous)] if compare else []
        result = subprocess.run([sys.executable, str(self.script), *args], cwd=self.current,
                                text=True, capture_output=True, check=False)
        return result.returncode, result.stdout + result.stderr

    def assert_rejected(self, path, fragment, compare=False):
        before = hashlib.sha256(path.read_bytes()).digest()
        code, output = self.checked(compare)
        self.assertEqual(hashlib.sha256(path.read_bytes()).digest(), before)
        self.assertEqual(code, 1, output)
        self.assertIn(fragment, output)
        return output

    def test_clean_default_and_comparison(self):
        code, output = self.checked()
        self.assertEqual(code, 0, output)
        self.assertIn("PASS current: 80 tables x 950 rows; zero per-table violations", output)
        code, output = self.checked(True)
        self.assertEqual(code, 0, output)
        self.assertIn("PASS 55 additions per table; six named keys included", output)

    def test_replacement_in_any_language(self):
        german = self.current / "assets/words/german.txt"
        german.write_bytes(german.read_bytes().replace(b"\tTranslation", "\t�Translation".encode(), 1))
        self.assert_rejected(german, "FAIL german.txt: line 1: mojibake or replacement character")

    def test_non_sampled_mojibake_in_last_row(self):
        zulu = self.current / "assets/words/zulu.txt"
        zulu.write_bytes(zulu.read_bytes().rstrip(b"\n") + " FranÃ§ais\n".encode())
        self.assert_rejected(zulu, "FAIL zulu.txt: line 950: mojibake or replacement character")

    def test_control_only_row_is_not_a_separator(self):
        german = self.current / "assets/words/german.txt"
        german.write_bytes(german.read_bytes() + "\u0085\n".encode())
        self.assert_rejected(german, "FAIL german.txt: line 951:")

    def test_nul_in_value(self):
        german = self.current / "assets/words/german.txt"
        german.write_bytes(german.read_bytes().replace(b"\tTranslation", b"\t\x00Translation", 1))
        self.assert_rejected(german, "FAIL german.txt: line 1:")

    def test_inventory_is_not_just_count(self):
        german = self.current / "assets/words/german.txt"
        renamed = german.rename(german.with_name("deutsch.txt"))
        self.assert_rejected(renamed, "language inventory missing=['german'], unexpected=['deutsch']")

    def test_echo_other_than_the_two_named_samples_fails(self):
        polish = self.current / "assets/words/polish.txt"
        key = b"Nothing in this day."
        polish.write_bytes(polish.read_bytes().replace(key + b"\tTranslation of " + key, key + b"\t" + key.upper()))
        self.assert_rejected(polish, "FAIL polish: corruption, echo or empty for ['Nothing in this day.']", True)

    def test_table_symlink_target_is_never_reported(self):
        marker = self.temp / "outside.txt"
        marker.write_text("PRIVATE-MARKER-Z8DQ\t�invalid\n", encoding="utf-8")
        for root, compare in [(self.current, False), (self.current, True), (self.previous, True)]:
            with self.subTest(root=root.name, compare=compare):
                german = root / "assets/words/german.txt"
                backup = german.rename(german.with_suffix(".bak"))
                try:
                    german.symlink_to(marker)
                    output = self.assert_rejected(marker, "symlinked table 'german.txt' is not read", compare)
                    self.assertNotIn("PRIVATE-MARKER-Z8DQ", output)
                    self.assertTrue(german.is_symlink())
                finally:
                    backup.replace(german)


if __name__ == "__main__":
    unittest.main()
