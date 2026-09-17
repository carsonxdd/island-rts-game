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
        rivals      0 / 1 / 2 neighbours, a fixed        what a neighbour costs the
                    Turtle / Rush / Eco one, 6 islands   player; how the rival fares

    Added 2026-09-16 night, after the levy / sleep / 150-75 clock landed:

        levy        levyShare 0 / .33 / .5 / .75 and     the new AI's own dial: how
                    levyRackExtra 0 / 2 / 4, 6 islands   much army may be spare spears
        conquest    the player as Conqueror against a    does war ever pay; how many
                    Turtle / Rush / Eco / random / two   fires fall, what loot comes back
                    rivals, 6 islands
        long        45- and 60-day calendars, 6 islands  raids of 28-35 and 50+ people
        clock       day/night 100/50, 150/75, 200/100    how much of the post-sleep drop
                    6 islands                            is the clock
        economy     foodPerDay .5 / 1.5 / 2, starting    where the sleep-shortened day
                    food 25 / 100, warrior 25F, 6 islands starves a colony

    Every sweep file it plays is saved next to its results, so any one can be
    replayed alone: .\tools\run-sim.ps1 -Sweep SimLogs\overnight-<date>\raids.sweep.json

.PARAMETER OutDir
    Where the night's results go. Default SimLogs/overnight-<yyyy-MM-dd>.

.PARAMETER Parallel
    Player processes per sweep. Default 8 (half the logical cores of the dev
    box); more than that mostly buys NavMesh job contention.

.PARAMETER Sweeps
    Which of the ten to run, in order. Default all.

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
    [string[]]$Sweeps = @("baseline", "raids", "difficulty", "islands", "rivals", "levy", "conquest", "long", "clock", "economy"),
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

if (-not $OutDir) {
    $OutDir = "SimLogs/overnight-{0:yyyy-MM-dd}" -f (Get-Date)
    # A second batch on the same day must not land in a finished batch's folder:
    # run-sim APPENDS runs.csv, so the two nights would merge and REPORT.md be overwritten
    # (2026-09-16). Step aside to a -HHmm suffix when the default folder already holds a report.
    if (Test-Path (Join-Path (Join-Path $root $OutDir) "REPORT.md")) {
        $OutDir = "SimLogs/overnight-{0:yyyy-MM-dd-HHmm}" -f (Get-Date)
    }
}
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
    param([string]$Name, [int]$Repeats, [object[]]$Runs, [int]$WallSeconds = 2400)
    [pscustomobject]@{
        name                 = $Name
        outputDir            = "SimLogs"
        captureDeltaTime     = 0.016666668
        repeats              = $Repeats
        # Headless is 15-30x realtime on this box but eight processes share it;
        # a full 30-day run with held dawns is 6000 game seconds. The frozen-clock
        # guard (60 s of no game time) is what catches a real hang.
        maxWallSecondsPerRun = $WallSeconds
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
        "rivals" {
            # Neighbours (2026-09-16, lap step 3). r0 is the control cell - the same
            # islands with nobody else on them - so the cost of a rival is read
            # against a same-night baseline, not against an older batch. r1 / r2
            # land one / two rivals with the shipped rule (a random personality
            # off the seeded stream); the three fixed cells pin the rival's play
            # so "how does a Turtle neighbour fare" is one column, not a coin.
            $variants = [ordered]@{
                "r0"       = @{ rivalCount = 0 }
                "r1"       = @{ rivalCount = 1 }
                "r1turtle" = @{ rivalCount = 1; rivalStrategy = "Turtle" }
                "r1rush"   = @{ rivalCount = 1; rivalStrategy = "Rush" }
                "r1eco"    = @{ rivalCount = 1; rivalStrategy = "Eco" }
                "r2"       = @{ rivalCount = 2 }
            }
            $runs = @(foreach ($v in $variants.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) { New-Run $v $s $i $variants[$v] } } })
            return New-Sweep $Name 1 $runs
        }
        "levy" {
            # The levy's own dial (2026-09-16 night). levyShare overrides every
            # policy's share of the wanted strength left to spare spears (Turtle /
            # Eco ship at 0.5, Rush at 1/3); the "ls" cells vary it alone, the
            # "re" cells keep the shipped share and pad the rack past the army's
            # gap. ls050 IS the shipped Turtle/Eco cell (Rush's shipped is 1/3),
            # re0 is the shipped rule for all three, so both ladders have a
            # same-night control.
            $variants = [ordered]@{
                "ls000" = @{ levyShare = 0.0 }
                "ls033" = @{ levyShare = 0.33 }
                "ls050" = @{ levyShare = 0.5 }
                "ls075" = @{ levyShare = 0.75 }
                "re0"   = @{ levyRackExtra = 0 }
                "re2"   = @{ levyRackExtra = 2 }
                "re4"   = @{ levyRackExtra = 4 }
            }
            $runs = @(foreach ($v in $variants.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) { New-Run $v $s $i $variants[$v] } } })
            return New-Sweep $Name 1 $runs
        }
        "conquest" {
            # The player plays the Conqueror (2026-09-16: declares war on the first
            # landed rival at day 12, sails every quiet day with the surplus) against
            # a pinned neighbour, a random one, and two. Read player_landings and the
            # loot columns in runs.csv and "conquered" under rival fate; the r1 cells
            # of the rivals sweep are the same islands played without the war.
            $variants = [ordered]@{
                "vturtle" = @{ rivalCount = 1; rivalStrategy = "Turtle" }
                "vrush"   = @{ rivalCount = 1; rivalStrategy = "Rush" }
                "veco"    = @{ rivalCount = 1; rivalStrategy = "Eco" }
                "vrandom" = @{ rivalCount = 1 }
                "vtwo"    = @{ rivalCount = 2 }
            }
            $runs = @(foreach ($v in $variants.Keys) { foreach ($i in $islands6) { New-Run $v "Conqueror" $i $variants[$v] } })
            return New-Sweep $Name 1 $runs
        }
        "long" {
            # Past the 30-day calendar (2026-09-16 night): the levy and the full-time
            # army against day-45 raids of ~28 and day-60 raids of ~35, villages past
            # 50 people (the hitch report's size). The game-time cap follows the
            # calendar (SimRunner.CalendarCapSeconds); the wall cap here does not,
            # so a 60-day run at eight processes gets twice the usual wall time.
            $lengths = [ordered]@{ "d45" = 45; "d60" = 60 }
            $runs = @(foreach ($l in $lengths.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) {
                New-Run $l $s $i @{ daysToSurvive = $lengths[$l] } } } })
            return New-Sweep $Name 1 $runs 4800
        }
        "clock" {
            # The day clock (2026-09-16 night): 100/50 was the shipped clock until
            # sleep landed, 150/75 is shipped now, 200/100 is the next step. With
            # sleep from midnight to dawn a longer day is more labor per calendar
            # day AND a longer night to hold; c150 is the same-night control.
            $clocks = [ordered]@{
                "c100" = @{ dayLengthSeconds = 100; nightLengthSeconds = 50 }
                "c150" = @{ dayLengthSeconds = 150; nightLengthSeconds = 75 }
                "c200" = @{ dayLengthSeconds = 200; nightLengthSeconds = 100 }
            }
            $runs = @(foreach ($c in $clocks.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) { New-Run $c $s $i $clocks[$c] } } })
            return New-Sweep $Name 1 $runs
        }
        "economy" {
            # Food after sleep cut the working day (2026-09-16 night): one knob at a
            # time around the shipped values (1 food per colonist-day, 50 starting
            # food, 15 food per warrior). Read hungry dawns and departures before
            # the win rate - a colony that starved is a food problem, not a raid one.
            $variants = [ordered]@{
                "fd050" = @{ foodPerDay = 0.5 }
                "fd150" = @{ foodPerDay = 1.5 }
                "fd200" = @{ foodPerDay = 2.0 }
                "sf25"  = @{ startingFood = 25 }
                "sf100" = @{ startingFood = 100 }
                "wc25"  = @{ warriorCostFood = 25 }
            }
            $runs = @(foreach ($v in $variants.Keys) { foreach ($i in $islands6) { foreach ($s in $strategies) { New-Run $v $s $i $variants[$v] } } })
            return New-Sweep $Name 1 $runs
        }
        default { throw "Unknown sweep '$Name' (baseline | raids | difficulty | islands | rivals | levy | conquest | long | clock | economy)" }
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
