param(
    [string]$Path,
    # Date ISO (yyyy-MM-dd) des trades LIVE à isoler ; vide = toutes les dates mode=LIVE
    [string]$LiveDate = ''
)

function Import-JournalCsvRobust {
    param([string]$CsvPath)

    $lines = Get-Content -Path $CsvPath
    if ($lines.Count -eq 0) { return @() }

    $headers = $lines[0] -split ';'
    $expected = $headers.Count
    $scoreIdx = [array]::IndexOf($headers, 'score_components')
    $fixedRows = 0
    $rows = New-Object System.Collections.Generic.List[object]

    for ($lineNo = 1; $lineNo -lt $lines.Count; $lineNo++) {
        $line = $lines[$lineNo]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $parts = $line -split ';'
        if ($parts.Count -lt $expected) { continue }

        # Known journal issue: score_components is serialized with ';' but not CSV-quoted.
        # When this happens, merge extra fields back into score_components to realign columns.
        if ($parts.Count -gt $expected -and $scoreIdx -ge 0) {
            $overflow = $parts.Count - $expected
            $scoreEnd = $scoreIdx + $overflow

            $rebuilt = @()
            if ($scoreIdx -gt 0) {
                $rebuilt += $parts[0..($scoreIdx - 1)]
            }
            $rebuilt += ($parts[$scoreIdx..$scoreEnd] -join ';')
            $rebuilt += $parts[($scoreEnd + 1)..($parts.Count - 1)]
            $parts = $rebuilt
            $fixedRows++
        }

        if ($parts.Count -ne $expected) { continue }

        $obj = [ordered]@{}
        for ($i = 0; $i -lt $expected; $i++) {
            $obj[$headers[$i]] = $parts[$i]
        }
        $rows.Add([pscustomobject]$obj)
    }

    if ($fixedRows -gt 0) {
        Write-Host "rows_fixed_for_score_components $fixedRows"
    }

    return $rows
}

function Get-EffectivePnlTicks {
    param($Row)

    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $style = [System.Globalization.NumberStyles]::Float

    $logged = 0.0
    if ([double]::TryParse([string]$Row.pnl_ticks, $style, $inv, [ref]$logged)) {
        if ([math]::Abs($logged) -gt 0.0001) { return $logged }
    }

    $entry = 0.0
    $sl = 0.0
    $tp = 0.0
    if (-not [double]::TryParse([string]$Row.entry_price, $style, $inv, [ref]$entry)) { return $logged }
    [void][double]::TryParse([string]$Row.sl_price, $style, $inv, [ref]$sl)
    [void][double]::TryParse([string]$Row.tp_price, $style, $inv, [ref]$tp)

    $exit = 0.0
    if ($Row.exit_kind -eq 'TakeProfit' -and $tp -gt 0) { $exit = $tp }
    elseif ($Row.exit_kind -eq 'StopLoss' -and $sl -gt 0) { $exit = $sl }
    else {
        [void][double]::TryParse([string]$Row.exit_price, $style, $inv, [ref]$exit)
    }
    if ($exit -le 0) { return $logged }

    $ts = 0.25
    if ($Row.side -eq 'LONG') { return ($exit - $entry) / $ts }
    return ($entry - $exit) / $ts
}

function Get-TradeMetrics {
    param($Sub)

    if ($Sub.Count -eq 0) { return $null }

    $pnls = @($Sub | ForEach-Object { Get-EffectivePnlTicks $_ })
    $wins = @($pnls | Where-Object { $_ -gt 0 })
    $losses = @($pnls | Where-Object { $_ -lt 0 })
    $tpN = @($Sub | Where-Object { $_.exit_kind -eq 'TakeProfit' }).Count
    $sum = ($pnls | Measure-Object -Sum).Sum
    $avgW = if ($wins.Count -gt 0) { ($wins | Measure-Object -Average).Average } else { 0 }
    $avgL = if ($losses.Count -gt 0) { [math]::Abs(($losses | Measure-Object -Average).Average) } else { 0 }
    $winSum = if ($wins.Count -gt 0) { ($wins | Measure-Object -Sum).Sum } else { 0 }
    $lossSum = if ($losses.Count -gt 0) { ($losses | Measure-Object -Sum).Sum } else { 0 }
    $exp = $sum / $Sub.Count
    $expR = if ($avgL -gt 0) { $exp / $avgL } else { 0 }
    $rr = if ($avgL -gt 0) { $avgW / $avgL } else { 0 }
    $pf = if ($lossSum -ne 0) { $winSum / [math]::Abs($lossSum) } else { 0 }

    [pscustomobject]@{
        N           = $Sub.Count
        WR          = [math]::Round(100.0 * $tpN / $Sub.Count, 1)
        Pnl         = [math]::Round($sum, 0)
        Expectancy  = [math]::Round($exp, 2)
        ExpectancyR = [math]::Round($expR, 2)
        AvgWin      = [math]::Round($avgW, 1)
        AvgLoss     = [math]::Round($avgL, 1)
        RRatio      = [math]::Round($rr, 2)
        PF          = [math]::Round($pf, 2)
    }
}

function Show-Metrics {
    param($Sub, [string]$Label)

    $m = Get-TradeMetrics $Sub
    if (-not $m) {
        Write-Host "$Label : (aucun trade)"
        return
    }
    Write-Host ("{0} : n={1} wr={2}% pnl_ticks={3} expectancy={4}t exp_R={5} avg_win={6}t avg_loss={7}t R_ratio={8} PF={9}" -f `
        $Label, $m.N, $m.WR, $m.Pnl, $m.Expectancy, $m.ExpectancyR, $m.AvgWin, $m.AvgLoss, $m.RRatio, $m.PF)
}

function Show-BtLiveSplit {
    param($AllRows, [string]$FilterLiveDate)

    $hasMode = $AllRows[0].PSObject.Properties.Name -contains 'mode'
    if (-not $hasMode) {
        Write-Host "--- BT vs LIVE : colonne mode absente ---"
        return
    }

    $liveRows = @($AllRows | Where-Object { $_.mode -eq 'LIVE' })
    $btRows = @($AllRows | Where-Object { $_.mode -eq 'BT' })

    if ($FilterLiveDate) {
        $liveRows = @($liveRows | Where-Object { $_.timestamp_utc.Substring(0, 10) -eq $FilterLiveDate })
    }

    $liveDates = @($liveRows | ForEach-Object { $_.timestamp_utc.Substring(0, 10) } | Sort-Object -Unique)
    if ($liveDates.Count -gt 0 -and -not $FilterLiveDate) {
        $btRows = @($btRows | Where-Object { $liveDates -notcontains $_.timestamp_utc.Substring(0, 10) })
    }

    $btDates = @($btRows | ForEach-Object { $_.timestamp_utc.Substring(0, 10) } | Sort-Object -Unique)
    $btRange = if ($btDates.Count -gt 0) { "$($btDates[0]) -> $($btDates[-1])" } else { "n/a" }
    $liveRange = if ($liveDates.Count -gt 0) { ($liveDates -join ', ') } else { "n/a" }

    Write-Host "--- BT vs LIVE (exp_R = expectancy / avg_loss) ---"
    Write-Host "BT periode: $btRange | LIVE dates: $liveRange"
    Show-Metrics $btRows "BACKTEST"
    Show-Metrics $liveRows "LIVE"

    if ($btRows.Count -gt 0 -and ($btRows[0].PSObject.Properties.Name -contains 'vwap_regime')) {
        Write-Host "--- BT par vwap_regime ---"
        $btRows | Group-Object vwap_regime | Sort-Object Count -Descending | ForEach-Object {
            Show-Metrics $_.Group "BT regime=$($_.Name)"
        }
    }

    if ($btRows.Count -gt 0 -and ($btRows[0].PSObject.Properties.Name -contains 'trade_mode')) {
        Write-Host "--- BT par trade_mode ---"
        $btRows | Group-Object trade_mode | ForEach-Object {
            Show-Metrics $_.Group "BT mode=$($_.Name)"
        }
    }

    if ($btRows.Count -gt 0 -and ($btRows[0].PSObject.Properties.Name -contains 'break_trigger')) {
        Write-Host "--- BT breakout triggers (top 8) ---"
        $btBreak = @($btRows | Where-Object { $_.trade_mode -eq 'Breakout' -and $_.break_trigger })
        if ($btBreak.Count -gt 0) {
            $btBreak | Group-Object break_trigger | Sort-Object Count -Descending | Select-Object -First 8 | ForEach-Object {
                Show-Metrics $_.Group "trigger=$($_.Name)"
            }
        }
    }

    Write-Host "--- Segments faibles BT (filtres candidats) ---"
    Show-Metrics (@($btRows | Where-Object { $_.vwap_regime -eq 'Range' })) "BT Range"
    Show-Metrics (@($btRows | Where-Object { $_.nearest_level -in @('ONH', 'ONL') })) "BT ONH/ONL"
    Show-Metrics (@($btRows | Where-Object {
            $_.side -eq 'SHORT' -and $_.above_vwap -eq '0' -and $_.vwap_regime -eq 'Range'
        })) "BT SHORT sous VWAP en Range"

    if ($liveRows.Count -gt 0 -and $liveRows.Count -le 30) {
        Write-Host "--- LIVE detail (<=30 trades) ---"
        foreach ($t in $liveRows) {
            $p = Get-EffectivePnlTicks $t
            $day = $t.timestamp_utc.Substring(0, 10)
            Write-Host ("  {0} id={1} {2} {3} {4} {5} {6} pnl={7}t" -f `
                $day, $t.trade_id, $t.side, $t.trade_mode, $t.vwap_regime, $t.nearest_level, $t.exit_kind, [math]::Round($p, 0))
        }
    }
}

$rows = Import-JournalCsvRobust -CsvPath $Path
Write-Host "total_trades $($rows.Count)"

if ($rows.Count -eq 0) { exit 0 }

$isV2 = $rows[0].PSObject.Properties.Name -contains 'trade_mode'
Write-Host "schema: $(if ($isV2) { 'v2' } else { 'legacy' })"

$tp = @($rows | Where-Object { $_.exit_kind -eq 'TakeProfit' })
$sl = @($rows | Where-Object { $_.exit_kind -eq 'StopLoss' })
Write-Host "TP $($tp.Count) SL $($sl.Count)"
Write-Host ("win_rate {0:N2}%" -f (100.0 * $tp.Count / $rows.Count))

Show-BtLiveSplit -AllRows $rows -FilterLiveDate $LiveDate

function Show-Stats($sub, $label) {
    if ($sub.Count -eq 0) { return }
    $pnl = ($sub | ForEach-Object { Get-EffectivePnlTicks $_ } | Measure-Object -Sum).Sum
    $wr = 100.0 * (@($sub | Where-Object { $_.exit_kind -eq 'TakeProfit' }).Count) / $sub.Count
    Write-Host "$label : n=$($sub.Count) wr=$([math]::Round($wr,1))% pnl_ticks=$([math]::Round($pnl,0))"
}

Show-Stats (@($rows | Where-Object { $_.side -eq 'LONG' })) "LONG"
Show-Stats (@($rows | Where-Object { $_.side -eq 'SHORT' })) "SHORT"

if ($isV2) {
    Write-Host "--- v2 by trade_mode ---"
    $rows | Group-Object trade_mode | ForEach-Object { Show-Stats $_.Group "mode=$($_.Name)" }

    Write-Host "--- v2 by vwap_regime ---"
    $rows | Group-Object vwap_regime | ForEach-Object { Show-Stats $_.Group "regime=$($_.Name)" }

    Write-Host "--- SHORT below VWAP (above_vwap=0) ---"
    $below = @($rows | Where-Object { $_.side -eq 'SHORT' -and $_.above_vwap -eq '0' })
    Show-Stats $below "SHORT_below_vwap"
    $belowRange = @($below | Where-Object { $_.vwap_regime -eq 'Range' })
    Show-Stats $belowRange "SHORT_below_vwap_RANGE"

    Write-Host "--- LONG above VWAP ---"
    $above = @($rows | Where-Object { $_.side -eq 'LONG' -and $_.above_vwap -eq '1' })
    Show-Stats $above "LONG_above_vwap"
    $aboveRange = @($above | Where-Object { $_.vwap_regime -eq 'Range' })
    Show-Stats $aboveRange "LONG_above_vwap_RANGE"

    Write-Host "--- by nearest_level ---"
    $rows | Group-Object nearest_level | Sort-Object Count -Descending | Select-Object -First 8 | ForEach-Object {
        Show-Stats $_.Group "level=$($_.Name)"
    }
}
else {
    Write-Host "--- legacy score components ---"
    function Comp-Avg($sub, $idx, $name) {
        $vals = @()
        foreach ($x in $sub) {
            $p = $x.score_components -split ';'
            if ($p.Count -gt $idx) { $vals += [decimal]$p[$idx] }
        }
        if ($vals.Count -gt 0) {
            $avg = ($vals | Measure-Object -Average).Average
            Write-Host "  $name avg=$([math]::Round($avg,3))"
        }
    }
    Comp-Avg $tp 0 'delta_ineff'
    Comp-Avg $sl 0 'delta_ineff'
}

$byDay = $rows | Group-Object { $_.timestamp_utc.Substring(0, 10) }
Write-Host "days $($byDay.Count)"
$dayStats = foreach ($g in $byDay) {
    $pnl = ($g.Group | ForEach-Object { Get-EffectivePnlTicks $_ } | Measure-Object -Sum).Sum
    $wr = 100.0 * (@($g.Group | Where-Object { $_.exit_kind -eq 'TakeProfit' }).Count) / $g.Count
    [pscustomobject]@{ Day = $g.Name; N = $g.Count; WR = $wr; Pnl = $pnl }
}
Write-Host "worst 3 days:"
$dayStats | Sort-Object Pnl | Select-Object -First 3 | ForEach-Object {
    Write-Host "  $($_.Day) n=$($_.N) wr=$([math]::Round($_.WR,1))% pnl=$([math]::Round($_.Pnl,0))"
}
Write-Host "best 3 days:"
$dayStats | Sort-Object Pnl -Descending | Select-Object -First 3 | ForEach-Object {
    Write-Host "  $($_.Day) n=$($_.N) wr=$([math]::Round($_.WR,1))% pnl=$([math]::Round($_.Pnl,0))"
}
