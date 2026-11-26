# Lingo word tables

Checks that every language table under `assets/words/` is complete and clean: the expected 80 language tables, one
table per language, the same 950 English keys in each, no duplicate or empty rows, matching `{0}` slots, valid UTF-8,
and no mojibake, replacement characters or control bytes in any row.

Run it from the repository root:

```bash
python3 tests/lingo/word_tables.py
```

It prints `PASS` with zero per-table violations and exits 0 when every table is sound. A renamed or unexpected table
fails the language inventory, and a symlinked table fails without being read, so no file outside the checkout is
opened.

To also compare against an older checkout's tables (55 new keys, no existing row changed, the named keys present, and
the fixed languages plus two sampled ones free of corruption, echo or empty values), pass its root as a second
argument:

```bash
python3 tests/lingo/word_tables.py . /path/to/older/checkout
```

The second checkout must be the base this branch grew from, whose tables have 895 keys. An added key whose value
echoes the English key fails the comparison.
