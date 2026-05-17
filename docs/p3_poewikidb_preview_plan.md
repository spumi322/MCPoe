  Current state:

  - dbscraper produced dataset/poe_wiki.db.
  - Full row-count verification passes against live poewiki Cargo:

    dotnet run --no-build -- verify-db
    Result:

    OK: all row counts match (154 HTTP requests).

  - items was fixed and now has 11,794 rows.
  - areas was fixed and now has 1,884 rows.
  - monster_types__tags is 0 locally and also 0 on Cargo, so accepted.

  Next MCPoe task:

  Add P3 PoE Wiki database tools in MCPoe.

  Minimal tool set:

  1. query_poe_wiki_database(sql)
      - Reads from data/poe_wiki.db.
      - Only allows read-only SELECT.
      - Enforces row limit, probably LIMIT 100 if missing.
      - Returns structured JSON:

        {
          "status": "OK",
          "grounded": true,
          "instruction": "Answer only from these SQL rows. If insufficient, call describe_poe_wiki_database or run another SELECT.",
          "rows": []
        }

      - SQL errors should be explicit:

        {
          "status": "SQL_ERROR",
          "grounded": false,
          "message": "no such column: ..."
  2. describe_poe_wiki_database(table?, search?)
      - No args: compact grouped table map.
      - table: exact columns for one table.
      - search: matching table/column names.
      - Purpose: help the LLM build multi-join SQL without guessing columns.

  Tool description idea:

  Use query_poe_wiki_database for exact structured Path of Exile Wiki data: items, mods, stats, skills, passives, monsters, maps, crafting, vendor rewards, spawn/generation weights.

  Use this instead of wiki search when the question asks for exact values, lists, relationships, filters, counts, joins, or what can roll on what.

  For multi-table joins or unknown columns, call describe_poe_wiki_database first.

  Rough DB map to include inline:

  - items: main item/base records
  - weapons, armours, shields, flasks, jewels, maps: typed item/base details
  - mods: modifier definitions
  - mod_stats: links mods to stat ids and values
  - stats/generic_stats: stat definitions
  - mod_spawn_weights, mod_generation_weights, spawn_weights: where/how mods appear
  - item_mods: item-to-mod links
  - skill, skill_gems, skill_levels, skill_quality, skill_stats_per_level: skill/gem data
  - passive_skills, passive_skill_stats, passive_skill_connections: passive tree data
  - monsters, monster_types, monster_base_stats, monster_resistances: monster data
  - maps, areas, atlas_nodes, atlas_regions: map/area/atlas data
  - crafting_bench_options/costs: bench crafting
  - vendor_rewards, quest_rewards: rewards
  - child tables use parent__field naming, e.g. items__tags, mods__tags, skill_gems__gem_tags

  Important implementation note:

  Do not trust the LLM with arbitrary SQL. Enforce:

  - must start with SELECT or WITH
  - reject semicolons with multiple statements
  - reject write keywords like INSERT, UPDATE, DELETE, DROP, ALTER, CREATE, REPLACE, VACUUM, ATTACH, DETACH, PRAGMA
  - open SQLite connection read-only
  - cap returned rows

  Useful dbscraper command references:

  dotnet run --no-build -- verify-db
  dotnet run --no-build -- build-db --no-refresh --append --tables items --no-child-tables --page-size 25
  dotnet run --no-build -- build-db --no-refresh --append --tables skill,weapons --page-size 25
