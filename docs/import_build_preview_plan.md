# MCPoe Build Import Preview Plan

## Goal

Add one user-facing PoB import tool that accepts compact build sources instead of raw XML.

The tool should let Claude import builds without sending thousands of XML lines through the chat context.

Target tool:

```text
pob_import_build(source, name?)
```

Supported source examples:

```text
https://pobb.in/abc123
G:\Builds\rf-jugg.txt
G:\Builds\rf-jugg.xml
raw PoB import code
raw XML, as fallback only
```

## Preferred Strategy

Use an existing Python library/script in a dedicated virtual environment to translate PoB import codes into raw XML.

Do not write the PoB import-code decoder in C# first.

Only rewrite the decoder from scratch if the Python route fails in practice.

## Why

Raw PoB XML can be thousands of lines and is too large for Claude Desktop chat context.

Claude should pass only a small locator or code:

```text
pobb.in link
local file path
short/medium import code
```

MCPoe should resolve that source locally, produce XML internally, and forward the XML to the existing `load_build_xml` engine action.

The XML should not be returned to Claude.

## Existing References

Path of Building already maps common share sites to raw PoB-code endpoints in `BuildSiteTools.lua`.

Important mappings:

```text
pobb.in/{id}               -> https://pobb.in/pob/{id}
poe.ninja/.../pob/{id}     -> https://poe.ninja/poe1/pob/raw/{id}
maxroll.gg/poe/pob/{id}    -> https://maxroll.gg/poe/api/pob/{id}
pastebin.com/{id}          -> https://pastebin.com/raw/{id}
pastebinp.com/{id}         -> https://pastebinp.com/raw/{id}
rentry.co/{id}             -> https://rentry.co/paste/{id}/raw
poedb.tw/pob/{id}          -> https://poedb.tw/pob/{id}/raw
```

The Python `pobapi` project already implements PoB import-code decoding using base64url decode plus zlib decompression.

Use it as the first decoder candidate.

## High-Level Flow

```text
Claude
  -> pob_import_build(source, name?)
    -> MCPoe classifies source
      -> local file, URL, raw import code, or raw XML
    -> MCPoe resolves source into either XML or PoB import code
    -> Python decoder converts PoB import code to XML
    -> MCPoe calls existing LoadBuildXmlAsync(xml, name)
    -> Lua PoB engine loads XML
    -> MCPoe returns compact success/error metadata
```

## Python Environment

Create a project-local virtual environment.

Example:

```bash
python -m venv ./.venv
./.venv/Scripts/python.exe -m pip install pobapi
```

Recommended config shape:

```json
{
  "PoBImport": {
    "PythonPath": ".venv\\Scripts\\python.exe",
    "DecoderScriptPath": "tools\\pob_decode.py"
  }
}
```

Paths should be resolved relative to `AppContext.BaseDirectory` unless absolute.

## Decoder Script Contract

Keep the Python script small and boring.

Suggested command shape:

```bash
python tools/pob_decode.py --input-code-file temp_code.txt --output-xml-file temp_build.xml
```

Inputs:

```text
--input-code-file
  Text file containing the PoB import code.

--output-xml-file
  File where decoded XML will be written.
```

Exit behavior:

```text
0
  Decode succeeded and output XML was written.

non-zero
  Decode failed. stderr contains a concise error.
```

The script should not print XML to stdout.

Reason: stdout/stderr can be logged or included in tool errors. Large XML should stay in files.

## MCPoe Import Resolution

Resolution order:

1. Local file path
2. Raw XML
3. Supported URL
4. Raw PoB import code

### Local File Path

If `source` points to an existing file:

```text
*.xml
  Read as XML and load directly.

other text file
  Read content.
  If content is XML, load directly.
  Otherwise treat content as PoB import code and decode through Python.
```

This is the most important path for avoiding chat context limits.

### Raw XML

Keep raw XML support as a fallback, but do not encourage Claude to use it.

Tool descriptions should clearly prefer file paths, URLs, or import codes.

### Supported URLs

Only fetch from an allowlist of known PoB sharing hosts.

Initial allowlist:

```text
pobb.in
poe.ninja
maxroll.gg
pastebin.com
pastebinp.com
rentry.co
poedb.tw
```

Do not make `pob_import_build` a general-purpose URL fetcher.

For `pobb.in`, fetch:

```text
https://pobb.in/pob/{id}
```

The fetched response should be treated as a PoB import code, then decoded through Python.

### Raw PoB Import Code

If source is not a path, not XML, and not a supported URL, treat it as a PoB import code.

Run through the Python decoder.

Validate decoded output before loading.

## XML Validation

Before calling `load_build_xml`, validate basic shape:

```text
trimmed XML starts with "<"
contains "PathOfBuilding" or another known root marker
length is above a small sanity threshold
```

Do not perform deep XML validation in the import tool.

The PoB engine remains the authority on whether the XML is loadable.

## Tool Response Shape

Return compact metadata only.

Do not return XML.

Example result:

```json
{
  "ok": true,
  "import": {
    "sourceType": "pobb_in",
    "resolvedFrom": "https://pobb.in/abc123",
    "decodedXmlBytes": 184522,
    "loaded": true
  },
  "session": {
    "hasLoadedBuild": true,
    "buildName": "RF Jugg"
  }
}
```

On error:

```json
{
  "ok": false,
  "error": "Failed to decode PoB import code: ..."
}
```

## Security And Safety

Use an allowlist for URL hosts.

Limit fetched response size.

Limit local file size.

Use temp files under a controlled temp directory.

Delete temp files after successful or failed decode.

Set a timeout for:

```text
HTTP fetch
Python decoder process
PoB load action
```

Do not include raw import code or XML in normal logs.

Log only source type, byte sizes, and failure summaries.

## Implementation Order

1. Add preview Python environment manually and confirm `pobapi` can decode a known import code.
2. Create `tools/pob_decode.py` with the file-in/file-out contract.
3. Add a C# import resolver service.
4. Add `IPoBService.ImportBuildAsync(source, name, ct)`.
5. Add MCP tool `pob_import_build`.
6. Route decoded XML into the existing `LoadBuildXmlAsync`.
7. Keep `pob_load_build_xml` available as a low-level fallback.
8. Add tests for source classification and URL rewriting.
9. Add an integration test guarded by local Python/PoB availability.

## Fallback Plan

If `pobapi` does not work reliably:

1. Keep the same C# import resolver and MCP tool.
2. Replace only the decoder backend.
3. Implement the minimal codec in C#:

```text
base64url normalize
base64 decode
zlib decompress
fallback raw deflate decompress if needed
UTF-8 XML string
```

This fallback should preserve the same public MCP tool contract.

## Exit Criteria

The feature is good enough when Claude can say:

```text
Import https://pobb.in/abc123 with MCPoe and show me build info.
```

And MCPoe:

```text
fetches raw code
decodes it outside Claude context
loads XML into PoB
returns compact success metadata
allows pob_get_build_info and pob_get_stats to work
```

