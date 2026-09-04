<#
.SYNOPSIS
    Runs a balance-simulation sweep against the headless sim player.

.DESCRIPTION
    Launches Build/SimPlayer/islandrts-sim.exe with -batchmode -nographics and
    the given sweep file, waits for it to finish, and prints a summary of the
    runs.csv it produced.

    Build the player first: Unity > Tools > Island RTS > Simulation >
    Build Headless Sim Player.

.PARAMETER Sweep
    Path to the sweep JSON. Defaults to SimSweeps/example.json.

.PARAMETER Parallel
    Number of concurrent player processes. Each gets its own output subfolder;
    they do not share a project directory, so this is safe (unlike editor
    batchmode, which locks the project).

.EXAMPLE
    .\tools\run-sim.ps1
    .\tools\run-sim.ps1 -Sweep SimSweeps/enemy-ramp.json -Parallel 4
#>
[CmdletBinding()]
param(
    [string]$Sweep = "SimSweeps/example.json",
    [int]$Parallel = 1
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "islandrts\Build\SimPlayer\islandrts-sim.exe"
$sweepPath = if ([System.IO.Path]::IsPathRooted($Sweep)) { $Sweep } else { Join-Path $root $Sweep }

if (-not (Test-Path $exe)) {
    Write-Error "Sim player not found at $exe`nBuild it first: Unity > Tools > Island RTS > Simulation > Build Headless Sim Player"
}
if (-not (Test-Path $sweepPath)) {
    Write-Error "Sweep file not found: $sweepPath`nWrite a starter one: Unity > Tools > Island RTS > Simulation > Write Example Sweep"
}

Write-Host "Sim player : $exe"
Write-Host "Sweep      : $sweepPath"
Write-Host "Processes  : $Parallel"
Write-Host ""

$logDir = Join-Path $root "SimLogs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Sharding is done here rather than in the player: the sweep is split into N
# temporary sweep files of every-Nth run, each writing to its own output dir.
# Sharing one runs.csv across processes would interleave appends and corrupt
# rows, so the shards are merged after they all exit.
$sweepJson = Get-Content $sweepPath -Raw | ConvertFrom-Json
$allRuns = @($sweepJson.runs)
if ($Parallel -gt $allRuns.Count) {
    Write-Host "Only $($allRuns.Count) runs in the sweep - dropping to $($allRuns.Count) processes."
    $Parallel = $allRuns.Count
}

$shardDir = Join-Path $logDir "shards"
if (Test-Path $shardDir) { Remove-Item $shardDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $shardDir | Out-Null

# Clear the merged CSVs up front. They are only rewritten when every shard has
# exited, so leaving the previous sweep's files in place means anything reading
# them mid-run silently gets the OLD sweep's results and looks complete.
foreach ($stale in @("runs.csv", "days.csv")) {
    $p = Join-Path $logDir $stale
    if (Test-Path $p) { Remove-Item $p -Force }
}

$started = Get-Date
$jobs = @()

for ($i = 0; $i -lt $Parallel; $i++) {
    $mine = @(for ($j = $i; $j -lt $allRuns.Count; $j += $Parallel) { $allRuns[$j] })

    $shard = $sweepJson | ConvertTo-Json -Depth 10 | ConvertFrom-Json   # deep copy
    $shard.runs = $mine
    $shard.outputDir = "SimLogs/shards/$i"

    $shardFile = Join-Path $shardDir "sweep-$i.json"
    $shard | ConvertTo-Json -Depth 10 | Set-Content $shardFile -Encoding utf8

    $log = Join-Path $logDir "player-$i.log"
    $shardArgs = @("-batchmode", "-nographics", "-simconfig", $shardFile, "-logFile", $log)
    $proc = Start-Process -FilePath $exe -ArgumentList $shardArgs -PassThru -NoNewWindow -WorkingDirectory $root
    $jobs += $proc
    Write-Host "Started PID $($proc.Id)  -  $($mine.Count) runs  (log: $log)"
}

$jobs | ForEach-Object { $_.WaitForExit() }
$elapsed = (Get-Date) - $started
Write-Host ""
Write-Host ("Finished in {0:mm\:ss}" -f $elapsed)

# Merge the shards into the canonical pair of CSVs.
foreach ($name in @("runs.csv", "days.csv")) {
    $parts = Get-ChildItem (Join-Path $shardDir "*\$name") -ErrorAction SilentlyContinue
    if (-not $parts) { continue }
    $merged = @(foreach ($p in $parts) { Import-Csv $p.FullName })
    $merged | Export-Csv (Join-Path $logDir $name) -NoTypeInformation
}

$runs = Join-Path $logDir "runs.csv"
if (Test-Path $runs) {
    $rows = @(Import-Csv $runs)
    Write-Host ""
    Write-Host "=== $runs ==="
    $rows | Group-Object strategy, outcome |
        Sort-Object Name |
        Format-Table @{L = "strategy/outcome"; E = { $_.Name } }, Count -AutoSize

    $errs = @($rows | Where-Object { $_.outcome -eq "error" -or $_.outcome -eq "timeout" })
    if ($errs.Count -gt 0) {
        Write-Warning "$($errs.Count) run(s) ended in error/timeout - those are harness problems, not balance:"
        $errs | Select-Object config_id, outcome, note | Format-Table -AutoSize
    }
    Write-Host "Total runs: $($rows.Count)"
} else {
    Write-Warning "No runs.csv found at $runs - check the player logs in $logDir."
}
