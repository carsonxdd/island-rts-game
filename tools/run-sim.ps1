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

.PARAMETER Visual
    Watch the sweep instead of running it headless. Each process gets a window
    with the spectator camera and a metrics caption, and the windows are tiled
    across the primary screen. Slower than headless, because frames get drawn.

    Rendering does NOT change what a run decides: the simulation still steps at
    captureDeltaTime and only the DRAW is skipped between frames (the sweep's
    renderFrameInterval). Use it to see why a strategy loses, not to gather
    numbers faster.

.PARAMETER WindowSize
    "WIDTHxHEIGHT" for each visual window. Left unset in lab mode, the cell size
    is computed so the grid fills the primary screen exactly (nine cells on a
    1920x1080 desktop come out 640x353 or so, taskbar allowed for). Outside the
    lab it defaults to 640x360.

.PARAMETER Lab
    The screen-filling comparison, nine windows by default. Builds the sweep
    itself instead of reading one: every seed in -Seeds becomes a ROW and every
    strategy in -Strategies a COLUMN, so each row is the same island and the
    same raid rolls played three different ways. Implies -Visual and one process
    per cell.

        TURTLE seed 1042 | RUSH seed 1042 | ECO seed 1042
        TURTLE seed 8851 | RUSH seed 8851 | ECO seed 8851
        TURTLE seed 4711 | RUSH seed 4711 | ECO seed 4711

.PARAMETER Seeds
    Comma-separated island seeds, one per lab row. Default "1042,8851,4711".

.PARAMETER Strategies
    Comma-separated strategies, one per lab column. Default "Turtle,Rush,Eco".

.PARAMETER RenderInterval
    Frames simulated per frame DRAWN in visual mode. 0 (default) means 8, about
    3x realtime: fast enough to get through a 30-day run, slow enough to read
    what the colony is doing. Raise it to skim, lower it to study a fight. It
    never affects what a run decides, only how much wall time a run takes.

.PARAMETER Parallel
    Number of concurrent player processes. Each gets its own output subfolder;
    they do not share a project directory, so this is safe (unlike editor
    batchmode, which locks the project).

.EXAMPLE
    .\tools\run-sim.ps1
    .\tools\run-sim.ps1 -Sweep SimSweeps/enemy-ramp.json -Parallel 4
    .\tools\run-sim.ps1 -Sweep SimSweeps/watch.json -Visual
    .\tools\run-sim.ps1 -Lab
    .\tools\run-sim.ps1 -Lab -Seeds 7,8,9 -WindowSize 480x270
    .\tools\run-sim.ps1 -Lab -Strategies Eco -Seeds 1,2,3,4,5,6,7,8,9
#>
[CmdletBinding()]
param(
    [string]$Sweep = "SimSweeps/example.json",
    [int]$Parallel = 1,
    [switch]$Visual,
    [string]$WindowSize = "",
    [switch]$Lab,
    [string]$Seeds = "1042,8851,4711",
    [string]$Strategies = "Turtle,Rush,Eco",
    [int]$RenderInterval = 0
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "islandrts\Build\SimPlayer\islandrts-sim.exe"
$sweepPath = if ([System.IO.Path]::IsPathRooted($Sweep)) { $Sweep } else { Join-Path $root $Sweep }

if (-not (Test-Path $exe)) {
    Write-Error "Sim player not found at $exe`nBuild it first: Unity > Tools > Island RTS > Simulation > Build Headless Sim Player"
}

# Grid shape, 0 outside the lab: the window sizing and the tiler both read it.
$gridCols = 0
$gridRows = 0

if ($Lab) {
    # The lab is a grid, not a list. Rows are seeds and columns are strategies,
    # and the runs are emitted ROW-MAJOR because the tiler fills left-to-right:
    # that is what puts the same island's three players side by side on one row,
    # which is the whole reason to watch six at once.
    $labSeeds = @($Seeds -split ',' | ForEach-Object { [int]$_.Trim() })
    $labStrats = @($Strategies -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($labSeeds.Count -eq 0 -or $labStrats.Count -eq 0) {
        Write-Error "-Lab needs at least one seed and one strategy."
    }

    $labRuns = @(
        foreach ($seed in $labSeeds) {
            foreach ($strat in $labStrats) {
                # terrainSeed tracks the seed so a row really is ONE island. Leaving
                # it at -1 would hand each window the scene's own island and the
                # comparison would be three different maps.
                [pscustomobject]@{
                    id             = "{0}_{1}" -f $strat.ToLower(), $seed
                    strategy       = $strat
                    seed           = $seed
                    terrainSeed    = $seed
                    daysToSurvive  = 30
                    maxGameSeconds = 6000
                }
            }
        }
    )

    $sweepJson = [pscustomobject]@{
        outputDir        = "SimLogs"
        captureDeltaTime = 0.016666668
        repeats          = 1
        runs             = $labRuns
    }
    $sweepPath = "<lab: $($labSeeds.Count) seeds x $($labStrats.Count) strategies>"
    $Visual = $true
    $Parallel = $labRuns.Count
    # The grid shape is known here, so the tiler does not have to guess it from
    # the cell width: columns are strategies, rows are seeds.
    $gridCols = $labStrats.Count
    $gridRows = $labSeeds.Count
}
elseif (-not (Test-Path $sweepPath)) {
    Write-Error "Sweep file not found: $sweepPath`nWrite a starter one: Unity > Tools > Island RTS > Simulation > Write Example Sweep"
}

$winW = 640
$winH = 360
if ($Visual) {
    if ($WindowSize) {
        if ($WindowSize -notmatch '^\s*(\d+)\s*[xX]\s*(\d+)\s*$') {
            Write-Error "WindowSize must look like 640x360, got '$WindowSize'"
        }
        $winW = [int]$Matches[1]
        $winH = [int]$Matches[2]
    }
    elseif ($gridCols -gt 0 -and $gridRows -gt 0) {
        # No size asked for and the grid shape is known: divide the desktop by it
        # so the windows cover the screen with no gap and no overlap. The taskbar
        # is already out of WorkingArea, and -popupwindow means no title bar to
        # allow for.
        Add-Type -AssemblyName System.Windows.Forms
        $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $winW = [Math]::Max(320, [Math]::Floor($area.Width / $gridCols))
        $winH = [Math]::Max(180, [Math]::Floor($area.Height / $gridRows))
    }
}

# How much wall time a visual run takes. The draw rate used to scale with the
# window count, which made a full lab run about 7x realtime - too fast to read
# what a colony was doing, which is the only reason to watch at all (2026-09-10).
# It is now a flat 8, roughly 3x realtime, whatever the window count. This never
# touches the simulation step: captureDeltaTime is unchanged and every window
# still gets 60 simulated frames per game-second, so the decisions are the same
# ones the headless sweep makes.
$renderEvery = 1
if ($Visual) {
    $renderEvery = if ($RenderInterval -gt 0) { $RenderInterval } else { 8 }
}

Write-Host "Sim player : $exe"
Write-Host "Sweep      : $sweepPath"
Write-Host "Processes  : $Parallel"
if ($Visual) { Write-Host "Mode       : VISUAL ${winW}x${winH}, draw 1 frame in $renderEvery (decisions identical to headless)" }
else         { Write-Host "Mode       : headless" }
Write-Host ""

$logDir = Join-Path $root "SimLogs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Sharding is done here rather than in the player: the sweep is split into N
# temporary sweep files of every-Nth run, each writing to its own output dir.
# Sharing one runs.csv across processes would interleave appends and corrupt
# rows, so the shards are merged after they all exit.
if (-not $Lab) { $sweepJson = Get-Content $sweepPath -Raw | ConvertFrom-Json }
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

# Unity exposes window size on the command line but not window position, so a
# tiled grid has to be arranged afterwards through user32. Purely cosmetic: if
# the P/Invoke or the window handle is not there, the runs still play, just
# stacked on top of each other.
function Place-SimWindows {
    param($Procs, [int]$CellW, [int]$CellH, [int]$Cols = 0)

    if (-not ("Win32WindowPlacer" -as [type])) {
        Add-Type -Namespace "" -Name Win32WindowPlacer -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool MoveWindow(System.IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
'@
    }

    # The lab knows its own shape; anything else fits as many columns as the
    # desktop takes. Deriving it from the cell width would put 2 columns on a
    # 1920 screen the moment the cells were sized to fill it exactly (3 x 640 is
    # not less than 1920).
    $cols = if ($Cols -gt 0) { $Cols }
            else { [Math]::Max(1, [Math]::Floor([System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Width / $CellW)) }
    $col = 0
    $row = 0

    foreach ($p in $Procs) {
        # The window does not exist the instant the process starts; the engine has
        # to boot first. Give each one a bounded chance to appear, then move on.
        $waited = 0
        while ($p.MainWindowHandle -eq [System.IntPtr]::Zero -and -not $p.HasExited -and $waited -lt 60) {
            Start-Sleep -Milliseconds 500
            $p.Refresh()
            $waited++
        }
        if ($p.MainWindowHandle -eq [System.IntPtr]::Zero) { continue }

        [void][Win32WindowPlacer]::MoveWindow($p.MainWindowHandle, $col * $CellW, $row * $CellH, $CellW, $CellH, $true)

        $col++
        if ($col -ge $cols) { $col = 0; $row++ }
    }
}

# The launching console becomes the control centre while the windows play.
#
# It cannot read this from the CSVs: runs.csv and days.csv are both appended when
# a run ENDS, so for the twenty minutes a run takes they say nothing. Each visual
# process therefore overwrites a one-line status.csv in its own shard directory
# once a second (SimStatus), and this reads those. Completed runs are counted
# from each shard's runs.csv, which is the real record.
function Read-SimStatus {
    param([string]$Dir)
    $p = Join-Path $Dir "status.csv"
    if (-not (Test-Path $p)) { return $null }
    # The writer swaps the file in rather than rewriting it, so a torn read should
    # not happen - but a reader that throws would take the whole sweep down.
    try { return @(Import-Csv $p -ErrorAction Stop)[0] } catch { return $null }
}

function Format-SimRow {
    param($Status, [int]$Index)

    if ($null -eq $Status) {
        return ("{0,-16} {1,6} {2,5} {3,5} {4,5} {5,5}  {6}" -f "window $Index", "-", "-", "-", "-", "-", "starting")
    }

    $label = "{0}/{1}" -f $Status.strategy.ToUpper(), $Status.seed
    $fire  = if ([int]$Status.fire_pct -lt 0) { "-" } else { "$($Status.fire_pct)%" }

    $state = if ($Status.outcome) { $Status.outcome.ToUpper() }
             elseif ([int]$Status.hunger -ge 2) { "STARVING" }
             elseif ([int]$Status.raid_tonight -eq 1) { "raid tonight" }
             elseif ([int]$Status.enemies -gt 0) { "under attack ($($Status.enemies))" }
             elseif ([int]$Status.hunger -eq 1) { "hungry" }
             else { "" }

    "{0,-16} {1,6} {2,5} {3,5} {4,5} {5,5}  {6}" -f `
        $label, "$($Status.day)/$($Status.days_to_survive)", $Status.colonists,
        $Status.food, $Status.warriors, $fire, $state
}

function Watch-SimRuns {
    param($Procs, [string[]]$ShardDirs, $Started)

    # Redrawing in place needs a console with a real cursor. Anything redirected
    # (a CI log, a pipe) silently does not, so fall back to waiting quietly.
    $inPlace = $true
    try { [void][Console]::CursorTop } catch { $inPlace = $false }
    if (-not $inPlace) {
        $Procs | ForEach-Object { $_.WaitForExit() }
        return
    }

    $header = "{0,-16} {1,6} {2,5} {3,5} {4,5} {5,5}  {6}" -f "window", "day", "pop", "food", "war", "fire", "state"
    $blockLines = $Procs.Count + 5

    for ($i = 0; $i -lt $blockLines; $i++) { Write-Host "" }
    $origin = [Math]::Max(0, [Console]::CursorTop - $blockLines)
    [Console]::CursorVisible = $false

    try {
        while ($true) {
            $alive = @($Procs | Where-Object { -not $_.HasExited }).Count

            $lines = New-Object System.Collections.Generic.List[string]
            $elapsed = (Get-Date) - $Started
            $lines.Add(("SIMULATION LAB      elapsed {0:mm\:ss}      {1} of {2} windows running" -f $elapsed, $alive, $Procs.Count))
            $lines.Add("")
            $lines.Add($header)

            for ($i = 0; $i -lt $Procs.Count; $i++) {
                $lines.Add((Format-SimRow -Status (Read-SimStatus $ShardDirs[$i]) -Index $i))
            }

            # The tally is cumulative across every run each window has finished,
            # which is why it comes from runs.csv and not from the status lines.
            $done = @(
                foreach ($d in $ShardDirs) {
                    $f = Join-Path $d "runs.csv"
                    if (Test-Path $f) { try { Import-Csv $f -ErrorAction Stop } catch { } }
                }
            )
            $lines.Add("")
            if ($done.Count -gt 0) {
                $tally = @(
                    foreach ($g in ($done | Group-Object strategy | Sort-Object Name)) {
                        $won = @($g.Group | Where-Object { $_.outcome -eq "victory" -or $_.outcome -eq "escape" }).Count
                        "{0} {1}/{2}" -f $g.Name, $won, $g.Count
                    }
                )
                $lines.Add("survived: " + ($tally -join "   "))
            }
            else {
                $lines.Add("survived: no runs finished yet")
            }

            # Positioned writes with NO trailing newline. Write-Host on the last
            # line would push the cursor one past the block, and at the bottom of
            # the buffer that scrolls the console - which slides the block out
            # from under $origin and the next redraw lands in the wrong place.
            $width = [Math]::Max(20, [Console]::WindowWidth - 1)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $l = $lines[$i]
                $text = if ($l.Length -gt $width) { $l.Substring(0, $width) } else { $l.PadRight($width) }
                [Console]::SetCursorPosition(0, $origin + $i)
                [Console]::Write($text)
            }

            if ($alive -eq 0) { break }
            Start-Sleep -Seconds 1
        }
    }
    finally {
        [Console]::CursorVisible = $true
        # Leave the cursor under the block so the sweep summary prints below it
        # rather than over the final readout.
        try { [Console]::SetCursorPosition(0, [Math]::Min($origin + $blockLines, [Console]::BufferHeight - 1)) } catch { }
    }
}

if ($Visual) { Add-Type -AssemblyName System.Windows.Forms }

$started = Get-Date
$jobs = @()
$shardOutDirs = @()

for ($i = 0; $i -lt $Parallel; $i++) {
    $mine = @(for ($j = $i; $j -lt $allRuns.Count; $j += $Parallel) { $allRuns[$j] })

    $shard = $sweepJson | ConvertTo-Json -Depth 10 | ConvertFrom-Json   # deep copy
    $shard.runs = $mine
    $shard.outputDir = "SimLogs/shards/$i"
    # Add-Member -Force rather than assignment: a hand-written sweep need not have
    # the property at all, and assigning to one a PSCustomObject lacks throws.
    $shard | Add-Member -NotePropertyName renderFrameInterval -NotePropertyValue $renderEvery -Force

    $shardFile = Join-Path $shardDir "sweep-$i.json"
    $shard | ConvertTo-Json -Depth 10 | Set-Content $shardFile -Encoding utf8

    $log = Join-Path $logDir "player-$i.log"
    if ($Visual) {
        # -popupwindow drops the title bar so the windows tile flush against each
        # other. Unity can set a window's SIZE from the command line but not its
        # POSITION, so placing them is done below through user32.
        $shardArgs = @("-screen-width", $winW, "-screen-height", $winH,
                       "-screen-fullscreen", "0", "-popupwindow",
                       "-simvisual", "-simconfig", $shardFile, "-logFile", $log)
        $proc = Start-Process -FilePath $exe -ArgumentList $shardArgs -PassThru -WorkingDirectory $root
    }
    else {
        $shardArgs = @("-batchmode", "-nographics", "-simconfig", $shardFile, "-logFile", $log)
        $proc = Start-Process -FilePath $exe -ArgumentList $shardArgs -PassThru -NoNewWindow -WorkingDirectory $root
    }
    $jobs += $proc
    $shardOutDirs += (Join-Path $logDir "shards\$i")
    Write-Host "Started PID $($proc.Id)  -  $($mine.Count) runs  (log: $log)"
}

if ($Visual) { Place-SimWindows -Procs $jobs -CellW $winW -CellH $winH -Cols $gridCols }

if ($Visual) { Watch-SimRuns -Procs $jobs -ShardDirs $shardOutDirs -Started $started }
else         { $jobs | ForEach-Object { $_.WaitForExit() } }
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
