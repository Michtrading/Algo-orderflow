param(
    [string]$Path,
    [double]$VolRatioMinFilter = 0
)

function Import-ShadowCsv {
    param([string]$CsvPath)

    if (-not (Test-Path $CsvPath)) {
        Write-Host "shadow_file_not_found $CsvPath"
        return @()
    }

    $lines = Get-Content -Path $CsvPath
    if ($lines.Count -le 1) { return @() }

    $headers = $lines[0] -split ';'
    $rows = New-Object System.Collections.Generic.List[object]

    for ($i = 1; $i -lt $lines.Count; $i++) {
        $parts = $lines[$i] -split ';'
        if ($parts.Count -ne $headers.Count) { continue }
        $obj = [ordered]@{}
        for ($j = 0; $j -lt $headers.Count; $j++) {
            $obj[$headers[$j]] = $parts[$j]
        }
        $rows.Add([pscustomobject]$obj)
    }
    return $rows
}

function Get-ShadowMetrics {
    param($Sub)

    if ($Sub.Count -eq 0) { return $null }

    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $style = [System.Globalization.NumberStyles]::Float
    $pnls = @()
    foreach ($x in $Sub) {
        $v = 0.0
        if ([double]::TryParse([string]$x.sim_pnl_ticks, $style, $inv, [ref]$v)) { $pnls += $v }
    }
    if ($pnls.Count -eq 0) { return $null }

    $wins = @($pnls | Where-Object { $_ -gt 0 })
    $losses = @($pnls | Where-Object { $_ -lt 0 })
    $tpN = @($Sub | Where-Object { $_.sim_exit_kind -eq 'TakeProfit' }).Count
    $sum = ($pnls | Measure-Object -Sum).Sum
    $avgL = if ($losses.Count -gt 0) { [math]::Abs(($losses | Measure-Object -Average).Average) } else { 0 }
    $exp = $sum / $Sub.Count
    $expR = if ($avgL -gt 0) { $exp / $avgL } else { 0 }

    [pscustomobject]@{
        N = $Sub.Count
        WR = [math]::Round(100.0 * $tpN / $Sub.Count, 1)
        Pnl = [math]::Round($sum, 0)
        Expectancy = [math]::Round($exp, 2)
        ExpectancyR = [math]::Round($expR, 2)
    }
}

function Show-ShadowMetrics {
    param($Sub, [string]$Label)
    $m = Get-ShadowMetrics $Sub
    if (-not $m) {
        Write-Host "$Label : (aucun)"
        return
    }
    Write-Host ("{0} : n={1} wr={2}% sim_pnl={3} exp={4}t exp_R={5}" -f `
        $Label, $m.N, $m.WR, $m.Pnl, $m.Expectancy, $m.ExpectancyR)
}

$rows = Import-ShadowCsv -CsvPath $Path
Write-Host "shadow_rows $($rows.Count)"

if ($rows.Count -eq 0) { exit 0 }

if ($VolRatioMinFilter -gt 0) {
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $rows = @($rows | Where-Object {
        $v = 0.0
        [double]::TryParse([string]$_.vol_ratio, [System.Globalization.NumberStyles]::Float, $inv, [ref]$v) | Out-Null
        $v -ge $VolRatioMinFilter
    })
    Write-Host "after_vol_filter_$VolRatioMinFilter rows $($rows.Count)"
}

Show-ShadowMetrics $rows "GLOBAL shadow (sim SL/TP)"

Write-Host "--- par engine ---"
$rows | Group-Object engine | Sort-Object Count -Descending | ForEach-Object {
    Show-ShadowMetrics $_.Group "engine=$($_.Name)"
}

Write-Host "--- par veto_reason (top 12) ---"
$rows | Group-Object veto_reason | Sort-Object Count -Descending | Select-Object -First 12 | ForEach-Object {
    Show-ShadowMetrics $_.Group "veto=$($_.Name)"
}

Write-Host "--- par vol_ratio bucket ---"
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$style = [System.Globalization.NumberStyles]::Float
foreach ($x in $rows) {
    $v = 0.0
    [void][double]::TryParse([string]$x.vol_ratio, $style, $inv, [ref]$v)
    if ($v -lt 1.05) { $x | Add-Member -NotePropertyName bucket -NotePropertyValue '<1.05' -Force }
    elseif ($v -lt 1.2) { $x | Add-Member -NotePropertyName bucket -NotePropertyValue '1.05-1.19' -Force }
    elseif ($v -lt 1.5) { $x | Add-Member -NotePropertyName bucket -NotePropertyValue '1.2-1.49' -Force }
    else { $x | Add-Member -NotePropertyName bucket -NotePropertyValue '>=1.5' -Force }
}
$rows | Group-Object bucket | Sort-Object Name | ForEach-Object {
    Show-ShadowMetrics $_.Group "vol=$($_.Name)"
}

Write-Host "--- par trade_mode x hypo_side ---"
$rows | Group-Object { "$($_.trade_mode)/$($_.hypo_side)" } | Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object {
    Show-ShadowMetrics $_.Group $_.Name
}

Write-Host "--- filtres candidats (shadow) ---"
Show-ShadowMetrics (@($rows | Where-Object { $_.veto_reason -eq 'no_vol_surge' })) "veto=no_vol_surge"
Show-ShadowMetrics (@($rows | Where-Object { $_.veto_reason -eq 'absorption_pattern_veto' })) "veto=absorption_pattern_veto"
Show-ShadowMetrics (@($rows | Where-Object { $_.veto_reason -like 'long=*' })) "veto=breakout_composite"
