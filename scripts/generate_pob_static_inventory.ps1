param(
    [string]$PobSrc = "G:\Code\utils\PathOfBuilding\src",
    [string]$RawOutput = ".\docs\pob_static_inventory.raw.generated.json",
    [string]$FilteredOutput = ".\docs\pob_static_inventory.filtered.generated.json",
    [int]$MaxOutputFields = 180
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PobSrc -PathType Container)) {
    throw "PoB source directory not found: $PobSrc"
}

$resolvedPobSrc = (Resolve-Path -LiteralPath $PobSrc).Path
$resolvedRawOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RawOutput)
$resolvedFilteredOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($FilteredOutput)

foreach ($path in @($resolvedRawOutput, $resolvedFilteredOutput)) {
    $outputDir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
        New-Item -ItemType Directory -Path $outputDir | Out-Null
    }
}

$entries = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new()

function Convert-ToRepoPath {
    param([string]$Path)
    $relative = $Path.Substring($resolvedPobSrc.Length).TrimStart("\", "/")
    return ("src/" + ($relative -replace "\\", "/"))
}

function Add-Entry {
    param(
        [string]$Path,
        [string]$Kind,
        [string]$Sig,
        [string]$SourceFile,
        [int]$Line,
        [string]$Notes
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $key = "$Kind|$Path|$SourceFile|$Line"
    if (-not $seen.Add($key)) {
        return
    }

    $entries.Add([pscustomobject][ordered]@{
        path = $Path
        kind = $Kind
        sig = $Sig
        source_file = $SourceFile
        line = $Line
        status = "static"
        notes = $Notes
    })
}

function Get-KindForFunction {
    param([string]$Path)
    if ($Path.Contains(":")) {
        return "method"
    }
    return "function"
}

$luaFiles = Get-ChildItem -LiteralPath $resolvedPobSrc -Recurse -File -Filter "*.lua" |
    Sort-Object FullName

foreach ($file in $luaFiles) {
    $sourceFile = Convert-ToRepoPath $file.FullName
    $lines = Get-Content -LiteralPath $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $lineNo = $i + 1

        if ($line -match '^\s*function\s+([A-Za-z_][A-Za-z0-9_]*(?:[.:][A-Za-z_][A-Za-z0-9_]*)*)\s*\(([^)]*)\)') {
            $path = $Matches[1]
            $args = $Matches[2].Trim()
            Add-Entry $path (Get-KindForFunction $path) "$path($args)" $sourceFile $lineNo "Declared function; candidate only, validate runtime accessibility."
            continue
        }

        if ($line -match '^\s*(?:local\s+)?([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*=\s*function\s*\(([^)]*)\)') {
            $path = $Matches[1]
            $args = $Matches[2].Trim()
            Add-Entry $path "function" "$path($args)" $sourceFile $lineNo "Function assigned to a table/local symbol; candidate only, validate runtime accessibility."
            continue
        }

        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*=\s*\{\s*\}') {
            Add-Entry $Matches[1] "table" "unknown" $sourceFile $lineNo "Empty table assignment; static table candidate."
            continue
        }

        if ($line -match '^\s*local\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{\s*\}') {
            Add-Entry $Matches[1] "local_table" "unknown" $sourceFile $lineNo "Local table assignment; may be module-private unless returned or exported."
            continue
        }

        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*=\s*LoadModule\("([^"]+)"') {
            Add-Entry $Matches[1] "module_ref" "unknown" $sourceFile $lineNo "Assigned from LoadModule(`"$($Matches[2])`"); runtime object shape depends on module return."
            continue
        }

        if ($line -match '^\s*return\s+([A-Za-z_][A-Za-z0-9_]*)\s*$') {
            Add-Entry $Matches[1] "module_return" "unknown" $sourceFile $lineNo "Module return symbol; useful for connecting local tables to LoadModule callers."
            continue
        }

        foreach ($match in [regex]::Matches($line, '\.output\.([A-Za-z_][A-Za-z0-9_]*)')) {
            Add-Entry "env.player.output.$($match.Groups[1].Value)" "output_field" "unknown" $sourceFile $lineNo "Output field reference discovered statically; confirm field existence for a loaded build."
        }

        foreach ($match in [regex]::Matches($line, 'output\["([^"]+)"\]')) {
            Add-Entry "env.player.output.$($match.Groups[1].Value)" "output_field" "unknown" $sourceFile $lineNo "Output field reference discovered statically; confirm field existence for a loaded build."
        }
    }
}

$byKind = [ordered]@{}
$entries | Group-Object -Property kind | Sort-Object Name | ForEach-Object {
    $byKind[$_.Name] = $_.Count
}

$corePathPattern = @(
    '^calcs\.(initEnv|perform|buildOutput|calcFullDPS|getNodeCalculator|getMiscCalculator|buildActiveSkill|createActiveSkill|buildActiveSkillModList|defence|offence|buildDefenceEstimations|takenHitFromDamage|applyDmgTakenConversion)$',
    '^(ModStoreClass|ModListClass|ModDBClass):(NewMod|MergeNewMod|AddMod|AddList|AddDB|Sum|More|Flag|Override|List|Tabulate|Max|HasMod|GetCondition|GetMultiplier|GetStat|EvalMod|Print)$',
    '^ItemClass:(new|ParseRaw|BuildRaw|BuildAndParseRaw|BuildModList|BuildModListForSlotNum|GetPrimarySlot|Craft|NormaliseQuality)$',
    '^PassiveSpecClass:(Init|Load|Save|ImportFromNodeList|AllocNode|DeallocSingleNode|DeallocNode|ResetNodes|CountAllocNodes|BuildAllDependsAndPaths|SelectClass|SelectAscendClass|SelectSecondaryAscendClass|EncodeURL|DecodeURL)$',
    '^data\.(skills|uniques|itemBases|minions|gems|passiveTree|nodes|groups|additions|clusterJewels|pantheons|bosses|monsterExperienceLevelMap)$'
)

$coreEntries = $entries | Where-Object {
    $path = $_.path
    foreach ($pattern in $corePathPattern) {
        if ($path -match $pattern) {
            return $true
        }
    }
    return $false
}

$moduleEntries = $entries |
    Where-Object {
        $_.kind -in @("module_ref", "module_return") -and
        ($_.source_file -match '^src/(Modules|Classes|Data|API)/')
    } |
    Sort-Object source_file, line

$outputFieldEntries = $entries |
    Where-Object { $_.kind -eq "output_field" } |
    Sort-Object path, source_file, line |
    Group-Object path |
    ForEach-Object { $_.Group | Select-Object -First 1 } |
    Sort-Object path |
    Select-Object -First $MaxOutputFields

$selectedEntries = @($coreEntries + $moduleEntries + $outputFieldEntries) |
    Sort-Object path, source_file, line -Unique

$selectedByKind = [ordered]@{}
$selectedEntries | Group-Object -Property kind | Sort-Object Name | ForEach-Object {
    $selectedByKind[$_.Name] = $_.Count
}

$metadata = [ordered]@{
    generated_at = (Get-Date).ToUniversalTime().ToString("o")
    source = [ordered]@{
        repo = (Split-Path -Parent $resolvedPobSrc) -replace "\\", "/"
        src = $resolvedPobSrc -replace "\\", "/"
        method = "static regex scan of Lua declarations, table assignments, module returns, LoadModule references, and output field references"
    }
    warnings = @(
        "Candidate inventory only; entries are not runtime verified.",
        "Static scan cannot prove globals are visible in HeadlessWrapper exec scope.",
        "Static scan cannot prove self/upvalue requirements or side effects.",
        "Large data tables should be filtered at runtime and not returned wholesale."
    )
}

$rawInventory = [ordered]@{
    generated_at = $metadata.generated_at
    source = $metadata.source
    warnings = $metadata.warnings
    summary = [ordered]@{
        lua_file_count = $luaFiles.Count
        entry_count = $entries.Count
        by_kind = $byKind
        filter = "none; full static scan"
    }
    entries = @($entries)
}

$filteredInventory = [ordered]@{
    generated_at = $metadata.generated_at
    source = $metadata.source
    warnings = $metadata.warnings
    summary = [ordered]@{
        lua_file_count = $luaFiles.Count
        full_scan_entry_count = $entries.Count
        written_entry_count = @($selectedEntries).Count
        full_scan_by_kind = $byKind
        written_by_kind = $selectedByKind
        filter = "core calcs/class methods, module refs/returns, selected data tables, and up to $MaxOutputFields unique output fields"
    }
    entries = @($selectedEntries)
}

$rawJson = $rawInventory | ConvertTo-Json -Depth 10
Set-Content -LiteralPath $resolvedRawOutput -Value $rawJson -Encoding utf8

$filteredJson = $filteredInventory | ConvertTo-Json -Depth 10
Set-Content -LiteralPath $resolvedFilteredOutput -Value $filteredJson -Encoding utf8

Write-Host "Scanned $($entries.Count) static entries from $($luaFiles.Count) Lua files."
Write-Host "Wrote raw inventory to $resolvedRawOutput"
Write-Host "Wrote $(@($selectedEntries).Count) filtered entries to $resolvedFilteredOutput"
