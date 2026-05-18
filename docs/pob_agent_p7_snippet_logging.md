# Phase 7 - Add Snippet Logging

Location: `G:/Code/MCPoe/Infrastructure/PoB/` plus a SQLite file/table location to be discussed.

Purpose: record real `pob_exec_lua` snippets so repeated patterns can later become recipes or orchestrators.

## Infrastructure Proposal Required

Before editing, propose:

- new SQLite file vs existing DB
- schema initialization location
- whether `DatabaseInitializer.cs` participates
- config key/path if needed

Recommendation: use a new PoB-specific DB unless the existing project conventions strongly argue otherwise.

## Schema

```sql
CREATE TABLE pob_exec_log (
  id          INTEGER PRIMARY KEY,
  ts          TEXT NOT NULL,
  code        TEXT NOT NULL,
  ok          INTEGER NOT NULL,
  result_json TEXT,
  truncated   INTEGER NOT NULL DEFAULT 0,
  error       TEXT,
  traceback   TEXT,
  duration_ms INTEGER,
  build_hash  TEXT
);
```

## Work

- Log inside `PoBService.ExecLuaAsync`.
- Store raw `code`.
- Store result JSON truncated to about 8KB.
- Set `truncated` when result is truncated.
- Store `error` and `traceback` for failed calls.
- Measure duration if cheap.
- Never let logging failure fail the tool call.

Recommendation: use a simple synchronous insert for v1 and swallow/log failures. Fire-and-forget can wait unless synchronous logging becomes visibly annoying.

## Exit Criteria

- Successful `pob_exec_lua` calls create log rows.
- Failed snippets are logged.
- Large results are truncated.
- This query produces useful clusters after a few sessions:

```sql
SELECT code, COUNT(*)
FROM pob_exec_log
WHERE ok = 1
GROUP BY code
ORDER BY 2 DESC;
```
