CREATE TABLE mod_downloads (folder TEXT PRIMARY KEY, count INTEGER NOT NULL DEFAULT 0);
CREATE TABLE mod_ratings (folder TEXT NOT NULL, visitor TEXT NOT NULL, stars INTEGER NOT NULL CHECK (stars BETWEEN 1 AND 5), PRIMARY KEY (folder, visitor));
