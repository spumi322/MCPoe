# Phase 3 - Add Exec and Safe Serialization

Location: `G:/Code/utils/PathOfBuilding/src/API/`

Purpose: add the minimal upstream runtime substrate for agent-written Lua.

## Guardrails

- Upstream PoB repo only.
- Do not touch MCPoe infrastructure yet.
- Keep the existing bridge protocol unchanged.
- Use the live PoB engine environment for v1.
- For v1, snippets return one value. Agents should return a table for multiple fields.

## Work

- Add `SafeSerialize.lua`.
- Add upstream `exec` handler.
- Confirm LuaJIT loader compatibility before choosing `load(...)` or `loadstring(...)`.
- Return a stable envelope:
  - success: `{ ok = true, value = ... }`
  - failure: `{ ok = false, error = "...", traceback = "..." }`

Recommended handler shape:

```lua
handlers.exec = function(params)
  if not params or type(params.code) ~= "string" then
    return { ok = false, error = "missing code" }
  end

  local chunk, err = load_or_loadstring(params.code, "exec")
  if not chunk then
    return { ok = false, error = tostring(err) }
  end

  local ok, result = xpcall(chunk, debug.traceback)
  if not ok then
    return { ok = false, error = tostring(result), traceback = tostring(result) }
  end

  return { ok = true, value = SafeSerialize(result) }
end
```

## SafeSerialize Requirements

- Walk recursively with a visited set.
- Break cycles.
- Default max depth: 4.
- Default max serialized size: 256KB.
- Depth overflow: `{ "__truncated": true, "reason": "depth" }`.
- Size overflow: `{ "__truncated": true, "reason": "size" }`.
- Functions/userdata/threads become `"<function>"`, `"<userdata>"`, `"<thread>"`.
- Non-string/non-number keys are coerced with `tostring(k)`.
- Output must be JSON-encoder-safe.

## Exit Criteria

- `exec` returns a scalar.
- `exec` returns a table.
- Syntax errors return structured errors.
- Runtime errors return structured errors with traceback.
- `SafeSerialize` handles cycles, deep tables, large tables, functions, userdata, threads, and non-string keys.
- All Phase 1 surviving actions still work.

Stop and confirm before Phase 4.
