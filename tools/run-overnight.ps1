<#
.SYNOPSIS
    Unattended overnight batch: rebuild the sim player, run several headless
    sweeps back to back, write one report.

.DESCRIPTION
    One command before bed (2026-09-11):

        .\tools\run-overnight.ps1

    It keeps the machine awake, rebuilds the headless sim player from the
    working tree (the editor must be CLOSED - batchmode cannot open a project
    another editor holds), verifies the script DLL is at least as new as every
    .cs under Assets (Bee copies it with the COMPILE time, not the build time,
    so "newer than the build started" was a false failure), then runs each
    sweep through run-sim.ps1 with -Parallel processes and files the results
    under SimLogs/overnight-<date>/<sweep>/. When the last sweep ends it runs
    summarize-sim.ps1, which writes REPORT.md beside them - that is the file
    to open in the morning - and then puts the machine to SLEEP after a 60 s
    countdown (Ctrl+C cancels it; -NoSleep leaves the box on). A build or
    precondition failure never sleeps the machine: you are still at the desk.

    A failed build stops everything (a stale player would answer last night's
    question). A failed SWEEP is logged and the next one runs; the report names
    it. Every line the batch prints is also in overnight.log.

    The sweeps (all Turtle / Rush / Eco, terrainSeed = seed so a seed is ONE
    island):

        baseline    12 islands x 3 repeats            fresh win-rate baseline
        raids       raid size/day, quiet nights,      where each strategy's
                    prosperity weight, 6 islands       win rate crosses 50%
        difficulty  Peaceful..Brutal, 6 islands        ladder spacing
        islands     3 sizes x 3 styles, 4 islands      SizeScale / style stalls

    Every sweep file it plays is saved next to its results, so any one can be
    replayed alone: .\tools\run-sim.ps1 -Sweep SimLogs\overnight-<date>\raids.sweep.json

.PARAMETER OutDir
    Where the night's results go. Default SimLogs/overnight-<yyyy-MM-dd>.

.PARAMETER Parallel
    Player processes per sweep. Default 8 (half the logical cores of the dev
    box); more than that mostly buys NavMesh job contention.

.PARAMETER Sweeps
    Which of the four to run, in order. Default all.

.PARAMETER SkipBuild
    Use the sim player as built. Only when you have just built it yourself and
    know it matches the tree.

.PARAMETER DryRun
    Write the sweep files, check the build preconditions and print the run
    count and a time estimate, but launch nothing.

.PARAMETER UnityExe
    Path to Unity.exe. Default: the version in ProjectSettings/ProjectVersion.txt
    under D:\Programs\unity editor\.

.PARAMETER NoSleep
    Leave the machine on when the batch finishes instead of sleeping it.

.EXAMPLE
    .\tools\run-overnight.ps1 -DryRun
    .\tools\run-overnight.ps1
    .\tools\run-overnight.ps1 -Sweeps baseline,raids -Parallel 6
    .\tools\run-overnight.ps1 -SkipBuild -Sweeps islands
#>
[CmdletBinding()]
param(
    [string]$OutDir = "",
    [int]$Parallel = 8,
    [string[]]$Sweeps = @("baseline", "raids", "difficulty", "islands"),
    [switch]$SkipBuild,
    [switch]$DryRun,
    [string]$UnityExe = "",
    [switch]$NoSleep
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "islandrts"
$exe = Join-Path $project "Build\SimPlayer\islandrts-sim.exe"
$dll = Join-Path $project "Build\SimPlayer\islandrts-sim_Data\Managed\Assembly-CSharp.dll"
$runSim = Join-Path $PSScriptRoot "run-sim.ps1"
$summarize = Join-Path $PSScriptRoot "summarize-sim.ps1"
$simLogs = Join-Path $root "SimLogs"

if (-not $OutDir) { $OutDir = "SimLogs/overnight-{0:yyyy-MM-dd}" -f (Get-Date) }
$outPath = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
New-Item -ItemType Directory -Force -Path $outPath | Out-Null

$logFile = Join-Path $outPath "overnight.log"
function Log {
    param([string]$Text)
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}" -f (Get-Date), $Text
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding utf8
}

# ---------------------------------------------------------------------------
# The sweeps. Ids are <variant>__<strategy>__<island>; repeats append _s<seed>
# (SimSweep.Expand), and manifest.csv maps the id prefix back to its cell so the
# report never has to parse an id.
# ---------------------------------------------------------------------------
$strategies = @("Turtle", "Rush", "Eco")
$islands12 = @(1042, 8851, 4711, 7, 23, 101, 314, 777, 2024, 3141, 5150, 9001)
$islands6 = $islands12[0..5]
$islands4 = $islands12[0..3]

function New-Run {
    param([string]$Variant, [string]$Strategy, [int]$Island, [hashtable]$Knobs = @{})
    $run = [ordered]@{
        id             = "{0}__{1}__{2}" -f $Variant, $Strategy.ToLower(), $Island
        strategy       = $Strategy
        seed           = $Island
        terrainSeed    = $Island
        daysToSurvive  = 30
        maxGameSeconds = 6000
    }
    foreach ($k in $Knobs.Keys) { $run[$k] = $Knobs[$k] }
    [pscustomobject]$run
}

function New-Sweep {
    param([string]$Name, [int]$Repeats, [object[]]$Runs)
    [pscustomobject]@{
        name                 = $Name
        outputDir            = "SimLogs"
        captureDeltaTime     = 0.016666668
        repeats              = $Repeats
        # Headless is 15-30x realtime on this box but eight processes share it;
        # a full 30-day run with held dawns is 6000 game seconds. The frozen-clock
        # guard (60 s of no game time) is what catches a real hang.
        maxWallSecondsPerRun = 2400
        runs                 = $Runs
    }
}

function Build-Sweep {
    param([string]$Name)
    switch ($Name) {
        "baseline" {
            $runs = @(foreach ($i in $islands12) { foreach ($s in $strategies) { New-Run "base" $s $i } })
            return New-Sweep $Name 3 $runs
        }
        "raids" {
            # One knob at a time around the shipped values (size/day 0.4, quiet
            # nights 2, prosperity 0.08, base 2). The shipped cell is the baseline.
            $variants = [ordered]@{
                "pd025" = @{ raidSizePerDay = 0.25 }
                "pd055" = @{ raidSizePerDay = 0.55 }
                "pd070" = @{ raidSizePerDay = 0.70 }
                "qn1"   = @{ raidMinQuietNights = 1 }
                "qn3"   = @{ raidMinQuietNights = 3 }
                "pr004" = @{ raidSizePerProsperity = 0.04 }
                "pr012" = @{ raidSizePerProsperity = 0.12 }
                "bs3"   = @{ raidBaseSize = 3.0 }
            }
            $runs = @(foreach ($v in $variants.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) { New-Run $v $s $i $variants[$v] } } })
            return New-Sweep $Name 1 $runs
        }
        "difficulty" {
            # The preset applies every multiplier but the calendar, which the
            # sweep writes itself (Peaceful / Relaxed ship as 20-day runs).
            $levels = [ordered]@{ "peaceful" = 20; "relaxed" = 20; "normal" = 30; "hard" = 30; "brutal" = 30 }
            $runs = @(foreach ($l in $levels.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) {
                New-Run $l $s $i @{ difficulty = $l; daysToSurvive = $levels[$l] } } } })
            return New-Sweep $Name 1 $runs
        }
        "islands" {
            $sizes = @("Small", "Medium", "Large")
            $styles = @("Rolling", "Terraced", "Rugged")
            $runs = @(foreach ($sz in $sizes) { foreach ($st in $styles) { foreach ($i in $islands4) { foreach ($s in $strategies) {
                New-Run ("{0}-{1}" -f $sz.ToLower(), $st.ToLower()) $s $i @{ islandSize = $sz; islandStyle = $st } } } } })
            return New-Sweep $Name 1 $runs
        }
        default { throw "Unknown sweep '$Name' (baseline | raids | difficulty | islands)" }
    }
}

function Write-Manifest {
    param($Sweep, [string]$Path)
    $rows = foreach ($r in $Sweep.runs) {
        $parts = $r.id -split "__"
        [pscustomobject]@{
            cell     = $r.id
            variant  = $parts[0]
            strategy = $r.strategy
            island   = $r.terrainSeed
        }
    }
    $rows | Export-Csv $Path -NoTypeInformation
}

# ---------------------------------------------------------------------------
# Preconditions
# ---------------------------------------------------------------------------
if (-not (Test-Path $runSim)) { throw "run-sim.ps1 not found beside this script." }
if (-not (Test-Path $summarize)) { throw "summarize-sim.ps1 not found beside this script." }

if (-not $UnityExe) {
    $verLine = Get-Content (Join-Path $project "ProjectSettings\ProjectVersion.txt") | Where-Object { $_ -match '^m_EditorVersion:\s*(\S+)' } | Select-Object -First 1
    if ($verLine -match '^m_EditorVersion:\s*(\S+)') { $UnityExe = "D:\Programs\unity editor\$($Matches[1])\Editor\Unity.exe" }
}

function Test-EditorHoldsProject {
    # Unity keeps Temp/UnityLockfile OPEN while the project is loaded; a stale
    # file from a crash is not held and opens fine. So try to open it exclusively.
    $lock = Join-Path $project "Temp\UnityLockfile"
    if (-not (Test-Path $lock)) { return $false }
    try {
        $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        return $false
    } catch { return $true }
}

$startedAt = Get-Date
$commit = (& git -C $root rev-parse --short HEAD 2>$null)
$dirty = @(& git -C $root status --porcelain -- '*.cs' 2>$null).Count

Log "=== OVERNIGHT BATCH ==="
Log "Output    : $outPath"
Log "Commit    : $commit ($dirty modified .cs files in the tree)"
Log "Sweeps    : $($Sweeps -join ', ')"
Log "Parallel  : $Parallel"

# Write every sweep and its manifest up front, so a DryRun leaves them to inspect.
$plans = @()
$totalRuns = 0
foreach ($name in $Sweeps) {
    $sweep = Build-Sweep $name
    $file = Join-Path $outPath "$name.sweep.json"
    $sweep | Select-Object -Property * -ExcludeProperty name | ConvertTo-Json -Depth 10 | Set-Content $file -Encoding utf8
    Write-Manifest -Sweep $sweep -Path (Join-Path $outPath "$name.manifest.csv")
    $count = $sweep.runs.Count * $sweep.repeats
    $totalRuns += $count
    $plans += [pscustomobject]@{ name = $name; file = $file; runs = $count }
    Log ("Sweep {0,-10} {1,4} runs -> {2}" -f $name, $count, $file)
}
# ~5 min per 30-day run headless with eight processes sharing the box.
$estHours = $totalRuns * 5.0 / $Parallel / 60.0
Log ("Total {0} runs, roughly {1:F1} h at 5 min/run" -f $totalRuns, $estHours)

if (-not $SkipBuild) {
    if (-not (Test-Path $UnityExe)) { throw "Unity.exe not found at $UnityExe - pass -UnityExe." }
    if (Test-EditorHoldsProject) {
        $msg = "The Unity editor has the project open. Close it (or pass -SkipBuild after building the sim player yourself)."
        if ($DryRun) { Log "!!! $msg" } else { throw $msg }
    }
    Log "Build     : $UnityExe -> SimTools.BuildSimPlayerBatch"
}
else {
    if (-not (Test-Path $exe)) { throw "Sim player not found at $exe and -SkipBuild given." }
    Log ("Build     : skipped, using DLL from {0:yyyy-MM-dd HH:mm}" -f (Get-Item $dll).LastWriteTime)
}

if ($DryRun) {
    Log "Dry run - nothing launched."
    return
}

# ---------------------------------------------------------------------------
# Keep the machine awake for the duration (the dev box sleeps after an hour).
# ES_CONTINUOUS | ES_SYSTEM_REQUIRED; cleared in the finally below.
# ---------------------------------------------------------------------------
if (-not ("Win32KeepAwake" -as [type])) {
    Add-Type -Namespace "" -Name Win32KeepAwake -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@
}
# ES_CONTINUOUS (0x80000000) | ES_SYSTEM_REQUIRED (1), in decimal: PowerShell reads 0x80000000 as a negative Int32.
[void][Win32KeepAwake]::SetThreadExecutionState([uint32]2147483648 -bor [uint32]1)

# Everything printed from here on (the build line, each sweep's summary table,
# the report's "Wrote" line) also lands in console.log. The dashboard's
# in-place redraws are console cursor writes and are not transcribed.
try { Start-Transcript -Path (Join-Path $outPath "console.log") -Append | Out-Null } catch { }

$results = @()
try {
    # ---- build ------------------------------------------------------------
    if (-not $SkipBuild) {
        $buildLog = Join-Path $outPath "build.log"
        $buildStart = Get-Date
        $buildArgs = @("-batchmode", "-nographics", "-quit",
                       "-projectPath", "`"$project`"",
                       "-executeMethod", "SimTools.BuildSimPlayerBatch",
                       "-logFile", "`"$buildLog`"")
        $p = Start-Process -FilePath $UnityExe -ArgumentList $buildArgs -PassThru -Wait -NoNewWindow
        # Bee copies Assembly-CSharp.dll into the player with the timestamp of
        # its COMPILE, which an unchanged incremental build leaves alone - so
        # "newer than the build started" failed a good build (2026-09-11).
        # Fresh = at least as new as every .cs under Assets, and Unity's own
        # result line says Success.
        $newestSrc = Get-ChildItem (Join-Path $project "Assets") -Recurse -Filter "*.cs" |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $dllTime = if (Test-Path $dll) { (Get-Item $dll).LastWriteTime } else { [datetime]::MinValue }
        $fresh = (Test-Path $dll) -and ($null -eq $newestSrc -or $dllTime -ge $newestSrc.LastWriteTime.AddSeconds(-2))
        $success = (Test-Path $buildLog) -and [bool](Select-String -Path $buildLog -Pattern "Build Finished, Result: Success" -Quiet)
        if ($p.ExitCode -ne 0 -or -not $success -or -not $fresh) {
            $why = "exit $($p.ExitCode), log says success: $success, DLL fresh: $fresh"
            if (-not $fresh -and $newestSrc) { $why += " (DLL $($dllTime.ToString('HH:mm:ss')) older than $($newestSrc.Name) $($newestSrc.LastWriteTime.ToString('HH:mm:ss')))" }
            throw "Sim player build failed ($why). See $buildLog"
        }
        Log ("Build OK in {0:mm\:ss}; DLL {1:HH:mm:ss}" -f ((Get-Date) - $buildStart), $dllTime)
    }

    # ---- sweeps -----------------------------------------------------------
    foreach ($plan in $plans) {
        $sweepDir = Join-Path $outPath $plan.name
        New-Item -ItemType Directory -Force -Path $sweepDir | Out-Null
        $sweepStart = Get-Date
        Log ("--- {0}: {1} runs starting" -f $plan.name, $plan.runs)

        $ok = $true
        try {
            # Not captured: run-sim.ps1 draws its live dashboard on the console
            # (one row per process, day / pop / food / warriors / fire / state,
            # a finished-runs counter and the survive tally), the same one the
            # lab shows. The transcript started above keeps its printed summary.
            & $runSim -Sweep $plan.file -Parallel $Parallel
        } catch {
            $ok = $false
            Log ("!!! {0} FAILED: {1}" -f $plan.name, $_.Exception.Message)
        }

        # run-sim.ps1 merges into SimLogs/runs.csv + days.csv and writes
        # player-N.log there; file them under the sweep's own folder.
        foreach ($f in @("runs.csv", "days.csv")) {
            $src = Join-Path $simLogs $f
            if (Test-Path $src) { Move-Item $src (Join-Path $sweepDir $f) -Force }
        }
        Get-ChildItem $simLogs -Filter "player-*.log" -ErrorAction SilentlyContinue | ForEach-Object { Move-Item $_.FullName (Join-Path $sweepDir $_.Name) -Force }

        $rows = @()
        $rowsFile = Join-Path $sweepDir "runs.csv"
        if (Test-Path $rowsFile) { $rows = @(Import-Csv $rowsFile) }
        $bad = @($rows | Where-Object { $_.outcome -eq "error" -or $_.outcome -eq "timeout" }).Count
        $elapsed = (Get-Date) - $sweepStart
        Log ("--- {0}: {1} rows ({2} error/timeout) in {3:hh\:mm\:ss}" -f $plan.name, $rows.Count, $bad, $elapsed)
        $results += [pscustomobject]@{ name = $plan.name; ok = $ok; rows = $rows.Count; expected = $plan.runs; bad = $bad; elapsed = $elapsed }
    }
}
finally {
    [void][Win32KeepAwake]::SetThreadExecutionState([uint32]2147483648)
}

# ---- report -------------------------------------------------------------
Log "Writing report"
try {
    & $summarize -Dir $outPath -Commit $commit -Started $startedAt
    Log "Report    : $(Join-Path $outPath 'REPORT.md')"
} catch {
    Log ("!!! report FAILED: {0}" -f $_.Exception.Message)
}

Log ("=== DONE in {0:hh\:mm\:ss} ===" -f ((Get-Date) - $startedAt))
$results | Format-Table name, ok, rows, expected, bad, elapsed -AutoSize

# ---- sleep --------------------------------------------------------------
# Reached only after the sweeps ran (a build failure throws above and leaves
# the box on - you are still at the desk when that happens). Keep-awake is
# already cleared by the finally; the countdown is the Ctrl+C window.
if ($NoSleep) {
    Log "Sleep     : skipped (-NoSleep)"
    try { Stop-Transcript | Out-Null } catch { }
}
else {
    Log "Sleep     : machine sleeps in 60 s (Ctrl+C to stay on, -NoSleep next time)"
    try { Stop-Transcript | Out-Null } catch { }
    for ($i = 60; $i -gt 0; $i -= 10) {
        Write-Host ("  sleeping in {0} s..." -f $i)
        Start-Sleep -Seconds 10
    }
    Add-Type -AssemblyName System.Windows.Forms
    # Suspend (not Hibernate), no forced close of other apps, wake events allowed.
    [void][System.Windows.Forms.Application]::SetSuspendState([System.Windows.Forms.PowerState]::Suspend, $false, $false)
}
