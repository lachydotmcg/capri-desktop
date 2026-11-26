import random
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SLOTS = re.compile(r"\{[0-9]+\}")
GARBLED = re.compile(r"[\ufffd\u0080-\u009f]|Ã[\u0080-\u00bf]|Â[\u0080-\u00bf]|â(?:€|‚)|ï»¿|ðŸ")
CONTROL = re.compile(r"[\x00-\x08\x0b-\x1f\x7f-\x9f]")
NAMED = {"Nothing matches.", "No chats kept yet.", "Email address", "App password",
         "Incoming mail server", "Nothing in this day."}
LANGUAGES = {
    "afrikaans", "albanian", "amharic", "arabic", "armenian", "azerbaijani", "basque",
    "belarusian", "bengali", "bosnian", "bulgarian", "burmese", "catalan", "croatian",
    "czech", "danish", "dutch", "english", "estonian", "filipino", "finnish", "french",
    "galician", "georgian", "german", "greek", "gujarati", "hausa", "hebrew", "hindi",
    "hungarian", "icelandic", "igbo", "indonesian", "irish", "italian", "japanese",
    "javanese", "kannada", "kazakh", "khmer", "korean", "lao", "latvian", "lithuanian",
    "macedonian", "malay", "malayalam", "maltese", "marathi", "mongolian", "nepali",
    "norwegian", "persian", "polish", "portuguese", "punjabi", "romanian", "russian",
    "serbian", "simplified-chinese", "slovak", "slovenian", "somali", "spanish", "swahili",
    "swedish", "tamil", "telugu", "thai", "traditional-chinese", "turkish", "ukrainian",
    "urdu", "uzbek", "vietnamese", "welsh", "xhosa", "yoruba", "zulu",
}


def read_table(file, ncolumns):
    rows, issues, count = {}, [], 0
    if file.is_symlink():
        return rows, count, [f"symlinked table {file.name!r} is not read"]
    try:
        for i, line in enumerate(file.read_text(encoding="utf-8-sig").split("\n"), 1):
            if CONTROL.search(line):
                issues.append(f"line {i}: control character in row: {line!r}")
            if not line.strip():
                continue
            count += 1
            fields = line.split("\t")
            key = fields[0]
            if len(fields) != ncolumns or any(not col.strip() for col in fields) or key != key.strip():
                issues.append(f"line {i}: invalid {ncolumns}-column row: {line!r}")
            if GARBLED.search(line):
                issues.append(f"line {i}: mojibake or replacement character: {line!r}")
            if key in rows:
                issues.append(f"line {i}: duplicate key {key!r}")
            val = fields[-1]
            if Counter(SLOTS.findall(key)) != Counter(SLOTS.findall(val)):
                issues.append(f"line {i}: slot mismatch {key!r} -> {val!r}")
            rows[key] = val
    except UnicodeDecodeError as e:
        issues.append(f"invalid UTF-8: {e}")
    return rows, count, issues


def matrix(root, expected):
    directory = root / "assets/words"
    files = sorted(directory.glob("*.txt"))
    english, _, english_issues = read_table(directory / "english.txt", 1)
    for issue in english_issues:
        print(f"FAIL english.txt: {issue}")
    result, bad = {}, bool(english_issues)
    if len(files) != 80:
        print(f"FAIL {directory}: {len(files)} tables, expected 80")
        bad += 1
    stems = {file.stem for file in files}
    if stems != LANGUAGES:
        print(f"FAIL {directory}: language inventory missing={sorted(LANGUAGES - stems)!r}, "
              f"unexpected={sorted(stems - LANGUAGES)!r}")
        bad += 1
    for file in files:
        rows, n, issues = read_table(file, 1 if file.stem == "english" else 2)
        if n != expected:
            issues.append(f"{n} nonempty rows, expected {expected}")
        if set(rows) != set(english):
            issues.append(f"missing={sorted(set(english)-set(rows))!r}, extra={sorted(set(rows)-set(english))!r}")
        for issue in issues:
            print(f"FAIL {file.name}: {issue}")
        bad += bool(issues)
        result[file.stem] = rows
    if not bad:
        print(f"PASS {root.resolve().name}: {len(files)} tables x {expected} rows; zero per-table violations")
    return result, bad


def compare(new, old):
    added = set(new["english"]) - set(old["english"])
    changed_old = {l: [k for k in old[l] if old[l][k] != new.get(l, {}).get(k)] for l in old}
    bad = 0
    for lang, keys in changed_old.items():
        if keys:
            print(f"FAIL {lang}: existing rows altered: {keys!r}")
            bad += 1
    if len(added) != 55 or not NAMED <= added:
        print(f"FAIL English delta: {len(added)} additions, missing named keys {NAMED - added!r}")
        bad += 1
    else:
        print("PASS 55 additions per table; six named keys included")
    fixed = ["spanish", "japanese", "polish", "turkish"]
    other = sorted(set(new) - set(fixed) - {"english"})
    for lang in fixed + random.Random(0x178008D7).sample(other, 2):
        table = new[lang]
        echoes = [k for k in sorted(added) if table.get(k, "").casefold() == k.casefold()]
        errors = sorted(set([k for k in added if GARBLED.search(table.get(k, ""))] + echoes))
        for key in ["Email address", "Nothing matches."]:
            if not table.get(key) or key in echoes:
                errors.append(key)
        errors = sorted(set(errors))
        if errors:
            print(f"FAIL {lang}: corruption, echo or empty for {errors!r}")
            bad += 1
        else:
            print(f"PASS {lang}: {len(added)} new rows; no echoes; "
                  f"{table['Email address']!r} / {table['Nothing matches.']!r}")
    return bad


def main():
    current, failed = matrix(Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT, 950)
    if len(sys.argv) > 2:
        baseline, old_failed = matrix(Path(sys.argv[2]), 895)
        failed += old_failed + compare(current, baseline)
    return int(bool(failed))


if __name__ == "__main__":
    raise SystemExit(main())
