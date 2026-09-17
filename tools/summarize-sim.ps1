<#
.SYNOPSIS
    Writes REPORT.md for a folder of sweep results (one subfolder per sweep,
    each holding runs.csv + days.csv), as run-overnight.ps1 lays them out.

.DESCRIPTION
    For every <Dir>/<sweep>/runs.csv it groups runs by CELL - the sweep's
    <sweep>.manifest.csv maps a config id to (variant, strategy, island); without
    a manifest a cell is the strategy alone - and reports per variant x strategy:

        n, wins (victory + escape), win rate with a Wilson 95% interval,
        mean day reached on a loss, raid nights, mean campfire_hp_min over raid
        nights, warriors lost, mean weapons_dawn, ring holes, hungry dawns,
        error/timeout rows.

    The baseline sweep also gets an island x strategy grid. Exceptions are
    counted from the player logs. Read the error/timeout column first: those
    are harness problems, not balance.

    Rerunnable on its own at any time:  .\tools\summarize-sim.ps1 -Dir SimLogs\overnight-2026-09-11
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [string]$Commit = "",
    [datetime]$Started = (Get-Date)
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dirPath = if ([System.IO.Path]::IsPathRooted($Dir)) { $Dir } else { Join-Path $root $Dir }
if (-not (Test-Path $dirPath)) { throw "No such folder: $dirPath" }

$inv = [System.Globalization.CultureInfo]::InvariantCulture
function Num { param($v) $d = 0.0; if ([double]::TryParse([string]$v, [System.Globalization.NumberStyles]::Float, $inv, [ref]$d)) { $d } else { 0.0 } }
function Mean { param($xs) $a = @($xs); if ($a.Count -eq 0) { $null } else { ($a | Measure-Object -Average).Average } }
function F1 { param($v) if ($null -eq $v) { "-" } else { $v.ToString("0.0", $inv) } }
function F0 { param($v) if ($null -eq $v) { "-" } else { $v.ToString("0", $inv) } }

# Wilson score interval, 95%. Honest at n = 6 where a normal approximation is not.
function Wilson {
    param([int]$Won, [int]$N)
    if ($N -eq 0) { return "-" }
    $z = 1.96; $p = $Won / $N
    $den = 1 + $z * $z / $N
    $centre = ($p + $z * $z / (2 * $N)) / $den
    $half = $z * [Math]::Sqrt($p * (1 - $p) / $N + $z * $z / (4 * $N * $N)) / $den
    # 0.0 / 1.0, not 0 / 1: PowerShell picks Math.Min(int, int) for (1, 0.56) and rounds the double first.
    "{0}% [{1}-{2}]" -f [Math]::Round(100 * $p), [Math]::Round(100 * [Math]::Max(0.0, $centre - $half)), [Math]::Round(100 * [Math]::Min(1.0, $centre + $half))
}

function Cell-Of { param([string]$ConfigId) $ConfigId -replace '_s\d+$', '' -replace '_try\d+$', '' }

$md = New-Object System.Text.StringBuilder
function W { param([string]$s = "") [void]$md.AppendLine($s) }

W ("# Sim batch report - {0}" -f (Split-Path $dirPath -Leaf))
W
W ("Commit {0} - started {1:yyyy-MM-dd HH:mm} - written {2:yyyy-MM-dd HH:mm}" -f $Commit, $Started, (Get-Date))
W
# Single-quoted on purpose: a backtick inside double quotes is an escape.
W 'Win = victory or escape. CI = Wilson 95%. `day` = mean calendar day reached on a LOSS. `fire min` = mean campfire_hp_min over raid nights (a breach reads as a low number here before it reads as a defeat). `w.lost` = full-time warriors lost, summed. `levy` = mean mustered_peak over raid nights (colonists who stood up with a spare weapon, 2026-09-16). `l.lost` = mustered colonists lost, summed. `weap` = mean weapons_dawn (spare weapons in the stockpile at dawn). `holes` = mean ring_holes on the last logged day. `hungry` = dawns with hunger > 0. **Read err/timeout first - those are harness problems, not balance.**'
W

$sweepDirs = Get-ChildItem $dirPath -Directory | Where-Object { Test-Path (Join-Path $_.FullName "runs.csv") } | Sort-Object Name
if ($sweepDirs.Count -eq 0) { W "_No sweep folder with a runs.csv under this directory._" }

$overall = @()

foreach ($sd in $sweepDirs) {
    $name = $sd.Name
    $runs = @(Import-Csv (Join-Path $sd.FullName "runs.csv"))
    $daysFile = Join-Path $sd.FullName "days.csv"
    $days = if (Test-Path $daysFile) { @(Import-Csv $daysFile) } else { @() }

    $expected = $null
    $sweepFile = Join-Path $dirPath "$name.sweep.json"
    if (Test-Path $sweepFile) {
        try { $sj = Get-Content $sweepFile -Raw | ConvertFrom-Json; $expected = @($sj.runs).Count * [Math]::Max(1, [int]$sj.repeats) } catch { }
    }

    $manifest = @{}
    $manifestFile = Join-Path $dirPath "$name.manifest.csv"
    if (Test-Path $manifestFile) {
        foreach ($m in Import-Csv $manifestFile) { $manifest[$m.cell] = $m }
    }

    # Days rows by config id, once.
    $daysById = @{}
    foreach ($d in $days) {
        if (-not $daysById.ContainsKey($d.config_id)) { $daysById[$d.config_id] = New-Object System.Collections.Generic.List[object] }
        $daysById[$d.config_id].Add($d)
    }

    # Exceptions in the player logs, whole-sweep.
    $exceptions = 0
    foreach ($lf in Get-ChildItem $sd.FullName -Filter "player-*.log" -ErrorAction SilentlyContinue) {
        $exceptions += @(Select-String -Path $lf.FullName -Pattern "Exception" -SimpleMatch).Count
    }

    $bad = @($runs | Where-Object { $_.outcome -eq "error" -or $_.outcome -eq "timeout" })
    $won = @($runs | Where-Object { $_.outcome -eq "victory" -or $_.outcome -eq "escape" })

    W ("## {0}" -f $name)
    W
    $expText = if ($null -ne $expected) { " of $expected expected" } else { "" }
    W ("{0} runs{1} - {2} won - {3} error/timeout - {4} exception lines in player logs" -f $runs.Count, $expText, $won.Count, $bad.Count, $exceptions)
    if ($null -ne $expected -and $runs.Count -lt $expected) { W; W ("**Short by {0} runs** - the sweep was cut or a process died; see overnight.log and the player logs." -f ($expected - $runs.Count)) }
    W

    # Annotate every run with its cell.
    $annotated = foreach ($r in $runs) {
        $cell = Cell-Of $r.config_id
        $m = $manifest[$cell]
        [pscustomobject]@{
            run      = $r
            cell     = $cell
            variant  = if ($m) { $m.variant } else { "-" }
            strategy = if ($m) { $m.strategy } else { $r.strategy }
            island   = if ($m) { [int]$m.island } else { -1 }
            days     = if ($daysById.ContainsKey($r.config_id)) { $daysById[$r.config_id] } else { @() }
        }
    }

    # ---- variant x strategy ------------------------------------------------
    $variantOrder = @()
    if ($manifest.Count -gt 0) { $variantOrder = @($manifest.Values | ForEach-Object { $_.variant } | Select-Object -Unique) } else { $variantOrder = @("-") }
    $stratOrder = @("Turtle", "Rush", "Eco")
    $extraStrats = @($annotated | ForEach-Object { $_.strategy } | Where-Object { $stratOrder -notcontains $_ } | Select-Object -Unique)
    $stratOrder = $stratOrder + $extraStrats

    W "| variant | strategy | n | won | win% [CI] | day | raids | fire min | w.lost | levy | l.lost | weap | holes | hungry | err/timeout |"
    W "|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
    foreach ($v in $variantOrder) {
        foreach ($s in $stratOrder) {
            $g = @($annotated | Where-Object { $_.variant -eq $v -and $_.strategy -eq $s })
            if ($g.Count -eq 0) { continue }
            $gWon = @($g | Where-Object { $_.run.outcome -eq "victory" -or $_.run.outcome -eq "escape" }).Count
            $gBad = @($g | Where-Object { $_.run.outcome -eq "error" -or $_.run.outcome -eq "timeout" }).Count
            $lossDays = @($g | Where-Object { $_.run.outcome -eq "defeat" } | ForEach-Object { Num $_.run.day_reached })
            $allDays = @($g | ForEach-Object { $_.days } | ForEach-Object { $_ })
            $raidNights = @($allDays | Where-Object { (Num $_.raid) -gt 0 })
            $fireMin = Mean ($raidNights | ForEach-Object { Num $_.campfire_hp_min })
            $wLost = ($allDays | ForEach-Object { Num $_.warriors_lost } | Measure-Object -Sum).Sum
            $levy = Mean ($raidNights | ForEach-Object { Num $_.mustered_peak })
            $lLost = ($allDays | ForEach-Object { Num $_.levy_lost } | Measure-Object -Sum).Sum
            $weap = Mean ($allDays | ForEach-Object { Num $_.weapons_dawn })
            $lastDays = @($g | ForEach-Object { $ds = @($_.days); if ($ds.Count -gt 0) { $ds[$ds.Count - 1] } })
            $holes = Mean ($lastDays | ForEach-Object { Num $_.ring_holes })
            $hungry = @($allDays | Where-Object { (Num $_.hunger_dawn) -gt 0 }).Count
            $vLabel = if ($v -eq "-") { "" } else { $v }
            W ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} | {14} |" -f
                $vLabel, $s, $g.Count, $gWon, (Wilson $gWon $g.Count), (F1 (Mean $lossDays)), $raidNights.Count,
                (F0 $fireMin), (F0 $wLost), (F1 $levy), (F0 $lLost), (F1 $weap), (F1 $holes), $hungry, $gBad)
            $overall += [pscustomobject]@{ sweep = $name; variant = $vLabel; strategy = $s; n = $g.Count; won = $gWon; bad = $gBad }
        }
    }
    W

    # ---- neighbours (2026-09-16): only when a rival landed somewhere in this sweep ----
    # The main table stays as it was so old and new reports line up; the rival
    # gets its own table with the columns days.csv / runs.csv carry for it.
    $withRival = @($annotated | Where-Object { $ds = @($_.days); $ds.Count -gt 0 -and (Num $ds[$ds.Count - 1].rival_arrival_day) -gt 0 })
    if ($withRival.Count -gt 0) {
        W "### Neighbours"
        W
        W '`landed` = runs where a rival came ashore (mean arrival day). `met` = runs whose fog touched it. `opinion` = mean hidden opinion at the last dawn (-100..100; the player sees Wary / Cool / Neutral / Warm / Friendly at -50 / -15 / 15 / 50). `raids@rival` = nights the raid was rolled onto the rival''s shore. `landings` / `relief` = hostile landings on and relief parties to the player''s shore, summed. `rival fate` = alive / fell (its fire destroyed - only raiders can) / deserted (fire standing, nobody left), with the mean day it fell. `rival war` = mean rival warriors at the last dawn. `rival food` = mean rival food at the last dawn.'
        W
        W "| variant | strategy | n | landed (day) | met | opinion | raids@rival | landings | relief | rival fate | rival war | rival food |"
        W "|---|---|---:|---|---:|---:|---:|---:|---:|---|---:|---:|"
        foreach ($v in $variantOrder) {
            foreach ($st in $stratOrder) {
                $g = @($annotated | Where-Object { $_.variant -eq $v -and $_.strategy -eq $st })
                if ($g.Count -eq 0) { continue }
                $lastDays = @($g | ForEach-Object { $ds = @($_.days); if ($ds.Count -gt 0) { $ds[$ds.Count - 1] } })
                $landed = @($lastDays | Where-Object { (Num $_.rival_arrival_day) -gt 0 })
                if ($landed.Count -eq 0) { continue }
                $arrival = Mean ($landed | ForEach-Object { Num $_.rival_arrival_day })
                $met = @($landed | Where-Object { (Num $_.rival_contact) -gt 0 }).Count
                $opinion = Mean ($landed | ForEach-Object { Num $_.rival_opinion_pts })
                $allDays = @($g | ForEach-Object { $_.days } | ForEach-Object { $_ })
                $raidsAtRival = @($allDays | Where-Object { (Num $_.raid_at_rival) -gt 0 }).Count
                $landings = ($landed | ForEach-Object { Num $_.rival_landings } | Measure-Object -Sum).Sum
                $relief = ($landed | ForEach-Object { Num $_.rival_relief } | Measure-Object -Sum).Sum
                $fates = @($g | ForEach-Object { $_.run.rival_fate } | Where-Object { $_ -and $_ -ne "none" } | Group-Object | Sort-Object Name | ForEach-Object { "{0} {1}" -f $_.Name, $_.Count })
                $fellDays = @($g | Where-Object { (Num $_.run.rival_fell_day) -gt 0 } | ForEach-Object { Num $_.run.rival_fell_day })
                $fateText = if ($fates.Count -gt 0) { $fates -join ", " } else { "-" }
                if ($fellDays.Count -gt 0) { $fateText += " (fell d" + (F1 (Mean $fellDays)) + ")" }
                $rWar = Mean ($landed | ForEach-Object { Num $_.rival_warriors })
                $rFood = Mean ($landed | ForEach-Object { Num $_.rival_food_dawn })
                $vLabel = if ($v -eq "-") { "" } else { $v }
                W ("| {0} | {1} | {2} | {3} ({4}) | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} |" -f
                    $vLabel, $st, $g.Count, $landed.Count, (F1 $arrival), $met, (F0 $opinion), $raidsAtRival,
                    (F0 $landings), (F0 $relief), $fateText, (F1 $rWar), (F0 $rFood))
            }
        }
        W
    }

    # ---- island grid, when the cells carry islands and more than one variant is not in play ----
    $islands = @($annotated | Where-Object { $_.island -ge 0 } | ForEach-Object { $_.island } | Select-Object -Unique | Sort-Object)
    if ($islands.Count -gt 1 -and $variantOrder.Count -eq 1) {
        W "### By island (won/played)"
        W
        W ("| island | " + (($stratOrder | ForEach-Object { $_ }) -join " | ") + " |")
        W ("|---:|" + (($stratOrder | ForEach-Object { "---:" }) -join "|") + "|")
        foreach ($i in $islands) {
            $cells = foreach ($s in $stratOrder) {
                $g = @($annotated | Where-Object { $_.island -eq $i -and $_.strategy -eq $s })
                if ($g.Count -eq 0) { "-" } else {
                    $w = @($g | Where-Object { $_.run.outcome -eq "victory" -or $_.run.outcome -eq "escape" }).Count
                    $lost = @($g | Where-Object { $_.run.outcome -eq "defeat" } | ForEach-Object { "d" + $_.run.day_reached })
                    if ($lost.Count -gt 0) { "{0}/{1} ({2})" -f $w, $g.Count, ($lost -join " ") } else { "{0}/{1}" -f $w, $g.Count }
                }
            }
            W ("| {0} | {1} |" -f $i, ($cells -join " | "))
        }
        W
        W "_A loss cell lists the calendar day of each defeat. An island every strategy loses on the same early day is a map problem (landing, ring, reachability), not a policy problem._"
        W
    }

    # ---- harness problems ------------------------------------------------
    if ($bad.Count -gt 0) {
        W "### Error / timeout rows"
        W
        W "| config | outcome | day | note |"
        W "|---|---|---:|---|"
        foreach ($b in $bad) { W ("| {0} | {1} | {2} | {3} |" -f $b.config_id, $b.outcome, $b.day_reached, ($b.note -replace '\|', '/')) }
        W
    }
}

# ---- one-screen summary at the top would be nicer, but a StringBuilder appends; prepend it now ----
$top = New-Object System.Text.StringBuilder
[void]$top.AppendLine("## At a glance")
[void]$top.AppendLine()
[void]$top.AppendLine("| sweep | variant | strategy | n | won | win% [CI] | err/timeout |")
[void]$top.AppendLine("|---|---|---|---:|---:|---|---:|")
foreach ($o in $overall) {
    [void]$top.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $o.sweep, $o.variant, $o.strategy, $o.n, $o.won, (Wilson $o.won $o.n), $o.bad))
}
[void]$top.AppendLine()

$body = $md.ToString()
$marker = [Environment]::NewLine + "## "
$firstSection = $body.IndexOf($marker)
$final = if ($firstSection -ge 0) { $body.Substring(0, $firstSection + [Environment]::NewLine.Length) + $top.ToString() + $body.Substring($firstSection + [Environment]::NewLine.Length) } else { $body + $top.ToString() }

$reportPath = Join-Path $dirPath "REPORT.md"
[System.IO.File]::WriteAllText($reportPath, $final, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $reportPath"
