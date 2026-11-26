ALTER TABLE codes ADD COLUMN kind TEXT NOT NULL DEFAULT 'giveaway';
ALTER TABLE codes ADD COLUMN tag TEXT NOT NULL DEFAULT '';
CREATE TABLE code_flights (id TEXT PRIMARY KEY, code INTEGER NOT NULL, started INTEGER NOT NULL, guess INTEGER NOT NULL DEFAULT 0);
CREATE INDEX code_flights_code_started ON code_flights (code, started);
CREATE INDEX code_flights_started ON code_flights (started);
