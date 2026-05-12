-- =============================================================================
-- P1b: mods.db -- SQLite schema derived from poewiki.net Cargo API
-- =============================================================================
-- Source: live Cargo API queries against Headhunter, May 2026
-- Field names and types confirmed from actual API responses.
-- =============================================================================

PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

-- ---------------------------------------------------------------------------
-- 1. items (core item data)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS items (
    page_id             INTEGER PRIMARY KEY,
    page_name           TEXT NOT NULL,
    name                TEXT NOT NULL,
    class_id            TEXT,
    rarity_id           TEXT,
    base_item           TEXT,
    base_item_page      TEXT,
    metadata_id         TEXT,
    frame_type          TEXT,
    size_x              INTEGER,
    size_y              INTEGER,
    drop_enabled        INTEGER,
    drop_level          INTEGER,
    drop_level_maximum  INTEGER,
    tags                TEXT,
    is_in_game          INTEGER,
    is_drop_restricted  INTEGER,
    release_version     TEXT,
    removal_version     TEXT,
    inventory_icon      TEXT,
    html                TEXT,
    flavour_text        TEXT,
    implicit_stat_text  TEXT,
    explicit_stat_text  TEXT,
    stat_text           TEXT,
    drop_areas          TEXT,
    drop_monsters       TEXT,
    drop_text           TEXT,
    acquisition_tags    TEXT
);

CREATE INDEX idx_items_name ON items(name);
CREATE INDEX idx_items_class_id ON items(class_id);
CREATE INDEX idx_items_rarity_id ON items(rarity_id);
CREATE INDEX idx_items_base_item ON items(base_item);

-- ---------------------------------------------------------------------------
-- 2. item_mods (which mods are assigned to an item)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS item_mods (
    page_id         INTEGER NOT NULL,
    mod_id          TEXT NOT NULL,
    text            TEXT,
    is_implicit     INTEGER NOT NULL DEFAULT 0,
    is_random       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (page_id, mod_id),
    FOREIGN KEY (page_id) REFERENCES items(page_id)
);

-- ---------------------------------------------------------------------------
-- 3. item_stats (aggregated stat values per item)
-- ---------------------------------------------------------------------------
-- NOTE: composite key includes min/max because the same stat_id can appear
-- twice on one item (e.g. base_maximum_life from implicit + explicit).
CREATE TABLE IF NOT EXISTS item_stats (
    page_id         INTEGER NOT NULL,
    stat_id         TEXT NOT NULL,
    min             REAL,
    max             REAL,
    FOREIGN KEY (page_id) REFERENCES items(page_id)
);

CREATE INDEX idx_item_stats_lookup ON item_stats(page_id, stat_id);

-- ---------------------------------------------------------------------------
-- 4. mods (modifier definitions)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS mods (
    id              TEXT PRIMARY KEY,
    stat_text       TEXT,
    generation_type INTEGER,
    domain          INTEGER,
    required_level  INTEGER
);

-- ---------------------------------------------------------------------------
-- 5. mod_stats (per-mod stat ranges)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS mod_stats (
    mod_id          TEXT NOT NULL,
    stat_id         TEXT NOT NULL,
    min             REAL,
    max             REAL,
    PRIMARY KEY (mod_id, stat_id),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

-- ---------------------------------------------------------------------------
-- 6. item_sell_prices
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS item_sell_prices (
    page_id         INTEGER NOT NULL,
    name            TEXT NOT NULL,
    amount          INTEGER,
    PRIMARY KEY (page_id, name),
    FOREIGN KEY (page_id) REFERENCES items(page_id)
);

-- ---------------------------------------------------------------------------
-- 7. spawn_weights (empty for uniques, included for completeness)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS spawn_weights (
    mod_id          TEXT NOT NULL,
    tag             TEXT NOT NULL,
    weight          INTEGER,
    PRIMARY KEY (mod_id, tag),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

-- ---------------------------------------------------------------------------
-- 8. legacy_variants
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS legacy_variants (
    page_id             INTEGER NOT NULL,
    removal_version     TEXT,
    implicit_stat_text  TEXT,
    explicit_stat_text  TEXT,
    stat_text           TEXT,
    base_item           TEXT,
    required_level      INTEGER,
    FOREIGN KEY (page_id) REFERENCES items(page_id)
);

-- ---------------------------------------------------------------------------
-- 9. item_purchase_costs
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS item_purchase_costs (
    page_id         INTEGER NOT NULL,
    name            TEXT NOT NULL,
    amount          INTEGER,
    PRIMARY KEY (page_id, name),
    FOREIGN KEY (page_id) REFERENCES items(page_id)
);
