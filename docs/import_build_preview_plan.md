# MCPoe Disk XML Build Import Preview Plan

## Goal

Add one user-facing import tool that loads a Path of Building XML file from disk.

The tool should let Claude import a build by passing only a local file path instead of sending thousands of XML lines through the chat context.

Target tool:

```text
pob_import_build(source, name?)
```

Supported source examples:

```text
F:\POB\Builds\manaman.xml
G:\Builds\rf-jugg.xml
```

Out of scope for this phase:

```text
raw PoB import code
pobb.in links
poe.ninja links
maxroll links
pastebin links
raw XML pasted through chat
non-XML text files
```

## Why

Raw PoB XML can be thousands of lines and is too large for Claude Desktop chat context.

Claude should pass only a small local file path:

```text
F:\POB\Builds\manaman.xml
```

MCPoe should read the XML locally and forward it to the existing `load_build_xml` engine action.

The XML should not be returned to Claude.

## High-Level Flow

```text
Claude
  -> pob_import_build(source, name?)
    -> MCPoe classifies source
    -> MCPoe rejects unsupported source types for this preview
    -> MCPoe resolves supported local XML file source
    -> MCPoe validates basic XML shape
    -> MCPoe calls existing LoadBuildXmlAsync(xml, name)
    -> Lua PoB engine loads XML
    -> MCPoe returns compact success/error metadata
```

## Import Resolution

Resolution order:

1. Classify source
2. Resolve only supported source types
3. Reject everything else

## Source Classification

Add the classification skeleton now so future import-code support has a stable place to plug in.

The classifier should identify broad source categories:

```text
empty
  Empty or whitespace-only input.

local_xml_file
  Existing local file with .xml extension.

unsupported_file
  Existing local file with any extension other than .xml.

missing_path
  Input looks like a local file path but the file does not exist.

url
  Syntactically valid URI with http or https scheme.

raw_xml
  Input starts like XML or contains a PathOfBuilding root marker.

compact_text
  Non-empty text that is not a path, URL, or raw XML.
  This is the future home for raw PoB import-code handling.
```

Only `local_xml_file` is supported in this phase.

All other classifications should return a clear unsupported-source error.

### Local XML File Path

If `source` points to an existing file:

```text
*.xml
  Read as XML and load directly.

other extension
  Reject with a clear error.
```

This is the only supported import path for the first implementation.

### Unsupported Inputs

Reject unsupported inputs explicitly.

Examples:

```text
https://pobb.in/abc123
raw PoB import code
raw XML pasted directly into the tool
*.txt
```

The error should say that this preview only supports local `.xml` files.

Suggested unsupported-source response metadata:

```json
{
  "ok": false,
  "import": {
    "sourceType": "url",
    "supported": false
  },
  "error": "This preview only supports local .xml files."
}
```

## XML Validation

Before calling `load_build_xml`, validate basic shape:

```text
file exists
file extension is .xml
file size is within the configured limit
trimmed XML starts with "<"
root marker is <PathOfBuilding>
length is above a small sanity threshold
```

Do not perform deep XML validation in the import tool.

The PoB engine remains the authority on whether the XML is loadable.

## Real XML Reference

Checked a full local PoB export:

```text
F:\POB\Builds\manaman.xml
```

Observed:

```text
size: 250,190 bytes
lines: 5,668
encoding declaration: UTF-8
root: PathOfBuilding
top-level elements:
  Build
  Tree
  Skills
  Config
  TreeView
  Items
  Import
  Calcs
  Party
  Notes
```

The file starts with an XML declaration, then `<PathOfBuilding>` on line 2.

The file contains large normal PoB sections:

```text
many PlayerStat entries
multiple passive tree specs
multiple skill sets
many items and item sets
calculation UI state
long build guide notes with escaped XML entities
```

Plan impact:

```text
25 MB file limit is generous enough for normal exports.
Validation should allow an XML declaration before the root element.
The root marker check should look for <PathOfBuilding>, not assume it is the first literal text.
```

## Existing Load XML Contract

MCPoe already exposes the low-level tool:

```text
pob_load_build_xml(xml, name?)
```

C# service method:

```text
IPoBService.LoadBuildXmlAsync(string xml, string? name, CancellationToken ct)
```

The service sends this Lua action:

```json
{
  "action": "load_build_xml",
  "params": {
    "xml": "...",
    "name": "optional display name"
  }
}
```

If `name` is omitted or blank, MCPoe sends only:

```json
{
  "xml": "..."
}
```

Lua handler behavior:

```text
missing/non-string xml -> { ok = false, error = "missing xml" }
missing name           -> uses "API Build"
success                -> { ok = true, build_id = 1 }
```

MCPoe wraps that Lua response in the normal MCP tool envelope.

On success, `PoBEngineManager` updates PoB state metadata:

```text
HasLoadedBuild = true
BuildName = supplied name, or "API Build"
```

Therefore `pob_import_build` can call `LoadBuildXmlAsync(xml, name, ct)` and treat the existing success response as the authoritative load result.

## Tool Response Shape

Return compact metadata only.

Do not return XML.

Use the existing MCPoe response envelope style where practical.

The import result should distinguish:

```text
loaded
  File was classified, read, validated, and loaded by PoB.

unsupported_source
  Source was classified, but this preview does not support that source type.

invalid_local_xml
  Source is a local .xml file, but local validation failed before PoB load.

load_failed
  XML was read and sent to PoB, but PoB returned an error.

read_failed
  File existed/classified as local XML, but MCPoe could not read it.

error
  Unexpected MCPoe-side exception.
```

Happy path:

```json
{
  "ok": true,
  "import": {
    "sourceType": "local_xml_file",
    "resolvedFrom": "G:\\Builds\\rf-jugg.xml",
    "xmlBytes": 184522,
    "loaded": true
  },
  "pobState": {
    "hasLoadedBuild": true,
    "buildName": "RF Jugg"
  },
  "loadResult": {
    "ok": true,
    "build_id": 1
  }
}
```

Unsupported source:

```json
{
  "ok": false,
  "import": {
    "sourceType": "url",
    "supported": false
  },
  "errorCode": "unsupported_source",
  "error": "This preview only supports local .xml files."
}
```

Invalid local XML:

```json
{
  "ok": false,
  "import": {
    "sourceType": "local_xml_file",
    "resolvedFrom": "G:\\Builds\\bad.xml",
    "xmlBytes": 42
  },
  "errorCode": "invalid_local_xml",
  "error": "File does not look like a Path of Building XML file."
}
```

Read failure:

```json
{
  "ok": false,
  "import": {
    "sourceType": "local_xml_file",
    "resolvedFrom": "G:\\Builds\\rf-jugg.xml"
  },
  "errorCode": "read_failed",
  "error": "Failed to read XML file: ..."
}
```

PoB load failure:

```json
{
  "ok": false,
  "import": {
    "sourceType": "local_xml_file",
    "resolvedFrom": "G:\\Builds\\rf-jugg.xml",
    "xmlBytes": 184522
  },
  "errorCode": "load_failed",
  "error": "PoB failed to load XML: ..."
}
```

Unexpected error:

```json
{
  "ok": false,
  "errorCode": "error",
  "error": "Import failed: ..."
}
```

The final wire response may wrap these objects in MCPoe's standard MCP tool envelope.

Do not include raw XML in any success or error response.

## Security And Safety

This is a trusted local tool for a power user.

Keep only lightweight guardrails:

```text
read only .xml files for this preview
do not fetch URLs
do not log XML content
keep a generous file-size limit to avoid accidental giant reads
```

Set a timeout for:

```text
PoB load action
```

## Pragmatic Implementation Notes

This is a solo, local, non-commercial MCP server for Claude Desktop.

Assume a power user and keep the implementation direct:

```text
path handling
  Accept normal local paths.
  Absolute Windows paths are the main target.
  Relative paths can resolve against the MCPoe working directory if useful.

file size limit
  Use a generous hardcoded limit such as 25 MB.

file encoding
  Start with File.ReadAllTextAsync.
  Revisit only if a real XML file fails because of encoding.

classification order
  Check existing local files before classifying compact text.
  Detect http/https URLs before treating text as an import code placeholder.
  Detect raw XML before compact text.

missing path detection
  Use simple Windows-path heuristics for nicer errors.
  Drive letter, slash/backslash, or .xml suffix is enough.

name behavior
  If name is omitted, preserve existing LoadBuildXmlAsync behavior and let PoB use "API Build".
  Do not infer a build name from the filename in the first version.

response wrapping
  Prefer reusing the existing MCPoe response envelope style.
  Parse the LoadBuildXmlAsync response only enough to detect success and include the existing result.
  Keep import metadata compact: source type, resolved path, byte count.
  In the updated global envelope, expose runtime/build state as `metadata.pobState`.

logging
  Log resolved path, source type, byte size, and failure summary.
  Do not log XML content.

tool description
  The MCP tool description must say "local .xml file only" clearly.
  It should not mention URL/import-code support except as unsupported in this preview.
```

## Implementation Order

1. Add a C# source classifier with the source types listed above.
2. Add a local XML resolver branch for `local_xml_file`.
3. Return unsupported-source errors for all other classifications.
4. Add `IPoBService.ImportBuildAsync(source, name, ct)` for the classified import flow.
5. Add MCP tool `pob_import_build`.
6. Route loaded XML into the existing `LoadBuildXmlAsync`.
7. Keep `pob_load_build_xml` available as a low-level fallback.
8. Add tests for classification, local XML resolution, and unsupported-source rejection.
9. Add a focused integration test using a small local XML fixture.

## Future Scope

PoB import-code and URL import are deliberately deferred.

Do not implement this in the disk XML preview.

### Code Import Research Notes

Primary conclusion:

```text
Do not use pobapi as the runtime dependency for MCPoe code import.
Implement the small import-code decoder directly when code import becomes in scope.
```

Path of Building is the compatibility authority.

Current PoB export/import behavior:

```text
export: XML -> zlib deflate -> base64 -> URL-safe text
import: URL-safe text -> base64 -> zlib inflate -> XML
```

Community implementations surveyed:

```text
G:\Code\Utils\pasteofexile
G:\Code\Utils\poediscordbot
G:\Code\Utils\discord-pob
G:\Code\Utils\pob-web
G:\Code\Utils\Poblink
G:\Code\Utils\poeapi\PathOFBuildingAPI
```

Best reference implementation found:

```text
G:\Code\Utils\pasteofexile\pob\src\utils.rs
```

`pasteofexile` / `pobb.in` behavior:

```text
pob::decompress(data: &str) -> Result<String>
  -> trim input
  -> base64 URL-safe decode
  -> zlib decode with flate2::bufread::ZlibDecoder
  -> decode XML as UTF-8
  -> fallback to Windows-1252
  -> return XML text
```

`pobb.in` endpoint distinction:

```text
https://pobb.in/pob/{id}
  Returns PoB import code. Decode to XML.

https://pobb.in/{id}/raw
  Returns XML. Load directly.
```

`poediscordbot` confirms the same codec:

```text
G:\Code\Utils\poediscordbot\poediscordbot\cogs\pob\importers\pob_xml_decoder.py

enc.replace("-", "+").replace("_", "/")
base64.b64decode(...)
zlib.decompress(...)
decode XML as Windows-1252
parse with defusedxml.ElementTree
```

`poediscordbot` URL importers:

```text
G:\Code\Utils\poediscordbot\poediscordbot\cogs\pob\importers\pobbin.py
  https://pobb.in/{id}/raw

G:\Code\Utils\poediscordbot\poediscordbot\cogs\pob\importers\poeninja.py
  https://poe.ninja/pob/raw/{id}

G:\Code\Utils\poediscordbot\poediscordbot\cogs\pob\importers\pastebin.py
  https://pastebin.com/raw/{id}
```

`pob-web` is useful confirmation, but not a dependency candidate:

```text
G:\Code\Utils\pob-web\packages\driver\src\c\driver.c

Runs original PoB Lua in WebAssembly.
Exposes zlib Deflate and Inflate through a C bridge.
Too heavy for MCPoe's import-code-to-XML need.
```

`Poblink` is not a decoder:

```text
G:\Code\Utils\Poblink\Poblink.user.js

Generates protocol links:
pob:pastebin/{id}
pob:pobbin/{id}
pob:poeninja/{id}
```

`pobapi` confirms the codec, but is a poor fit:

```text
G:\Code\Utils\poeapi\PathOFBuildingAPI

pobapi.from_import_code(import_code)
  -> calls private pobapi.util._fetch_xml_from_import_code(import_code)
  -> parses XML into a PathOfBuildingAPI object

private helper:
  base64.urlsafe_b64decode(import_code)
  zlib.decompress(decoded_bytes)
  return XML bytes
```

Why not `pobapi`:

```text
raw XML helper is private
public API returns a parsed object, not XML
URL support is pastebin-only
dependency stack is larger than the codec MCPoe needs
```

Future compatibility checks before implementing code import:

```text
current Path of Building export copied directly from PoB
pobb.in/pob/{id}
pobb.in/{id}/raw
poe.ninja/pob/raw/{id}
maxroll.gg/poe/api/pob/{id}
pastebin.com/raw/{id}
base64 padding behavior
UTF-8 vs Windows-1252 XML payloads
```

## Exit Criteria

The feature is good enough when Claude can say:

```text
Import G:\Builds\rf-jugg.xml with MCPoe and show me build info.
```

And MCPoe:

```text
reads the XML from disk
loads XML into PoB
returns compact success metadata
allows pob_get_build_info and pob_get_stats to work
rejects URLs, import codes, raw XML, and non-XML files clearly
```
