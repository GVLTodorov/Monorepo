#!/usr/bin/env pwsh
# Regenerates the '### Test coverage' block in README.md from a ReportGenerator
# JsonSummary report and the trx files produced by `dotnet test`. Run from the
# repository root after `dotnet test` and `reportgenerator` have both completed.

$ErrorActionPreference = "Stop"

$readmePath = "README.md"
$summaryPath = "CoverageReport/Summary.json"
$assemblyDisplayNames = [ordered]@{
    Mongo    = "MongoDB"
    Postgres = "PostgreSQL"
    Sqlite   = "SQLite"
}

function Get-PassedTestCount {
    $passed = 0
    $trxFiles = Get-ChildItem -Path "TestResults" -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue
    foreach ($file in $trxFiles) {
        [xml]$trx = [System.IO.File]::ReadAllText($file.FullName)
        $counters = $trx.TestRun.ResultSummary.Counters
        if ($counters) {
            $passed += [int]$counters.passed
        }
    }
    return $passed
}

function Format-Percent($covered, $total) {
    if (-not $total) { return "n/a" }
    return "{0:N2}%" -f (($covered / $total) * 100)
}

function Format-Fraction($covered, $total) {
    return "{0:N0} / {1:N0}" -f $covered, $total
}

if (-not (Test-Path $summaryPath)) {
    Write-Error "Coverage summary not found at $summaryPath"
    exit 1
}

$summaryFullPath = Join-Path (Get-Location).Path $summaryPath
$summaryJson = [System.IO.File]::ReadAllText($summaryFullPath) | ConvertFrom-Json
$assemblies = @{}
foreach ($a in $summaryJson.coverage.assemblies) {
    $assemblies[$a.name] = $a
}

foreach ($key in $assemblyDisplayNames.Keys) {
    if (-not $assemblies.ContainsKey($key)) {
        Write-Error "Coverage summary is missing assembly '$key'"
        exit 1
    }
}

$passedTests = Get-PassedTestCount
if ($passedTests -eq 0) {
    Write-Error "Could not find any passing tests in TestResults/**/*.trx"
    exit 1
}

$today = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")

$rows = foreach ($key in $assemblyDisplayNames.Keys) {
    $a = $assemblies[$key]
    $displayName = $assemblyDisplayNames[$key]
    $linePct = Format-Percent $a.coveredlines $a.coverablelines
    $branchPct = Format-Percent $a.coveredbranches $a.totalbranches
    $lines = Format-Fraction $a.coveredlines $a.coverablelines
    $branches = Format-Fraction $a.coveredbranches $a.totalbranches
    "| $displayName | $linePct | $branchPct | $lines | $branches |"
}

$summary = $summaryJson.summary
$overallLinePct = Format-Percent $summary.coveredlines $summary.coverablelines
$overallBranchPct = Format-Percent $summary.coveredbranches $summary.totalbranches
$overallLines = Format-Fraction $summary.coveredlines $summary.coverablelines
$overallBranches = Format-Fraction $summary.coveredbranches $summary.totalbranches

$block = @"
Coverage was collected on $today from all $passedTests passing tests with Coverlet's Cobertura collector. Test and benchmark assemblies are excluded, and automatically generated property accessors are skipped.

| Production library | Line coverage | Branch coverage | Covered lines | Covered branches |
|---|---:|---:|---:|---:|
$($rows -join "`n")
| **Overall** | **$overallLinePct** | **$overallBranchPct** | **$overallLines** | **$overallBranches** |
"@

$readmeFullPath = Join-Path (Get-Location).Path $readmePath
$readme = [System.IO.File]::ReadAllText($readmeFullPath)
$pattern = '(?s)(<!-- coverage:start -->\r?\n).*?(\r?\n<!-- coverage:end -->)'
if ($readme -notmatch $pattern) {
    Write-Error "Could not find coverage markers in README.md"
    exit 1
}

$evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
    param($match)
    $match.Groups[1].Value + $block + $match.Groups[2].Value
}
$newReadme = [regex]::Replace($readme, $pattern, $evaluator)

if ($newReadme -ne $readme) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($readmeFullPath, $newReadme, $utf8NoBom)
    Write-Output "README.md coverage section updated."
} else {
    Write-Output "README.md coverage section already up to date."
}
