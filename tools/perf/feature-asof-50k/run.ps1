# Phase E WE0 PoC: 50万件 fixture × z=15 タイル × 5 リクエスト平均応答時間計測
#
# 前提:
#   - docker compose up -d で agri_postgis + agri_geoserver 起動済
#   - tools/geoserver-setup/setup.ps1 で feature_current featureType 公開済
#   - tools/perf/feature-asof-50k/generate.sql 実行済 (fixture + feature_asof view)
#   - tools/perf/feature-asof-50k/setup-geoserver.ps1 実行済 (feature_asof featureType 公開)

$ErrorActionPreference = "Stop"
$adminPass = if ($env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD) { $env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD } else { "geoserver_dev" }

# 帯広付近 z=15 タイル群 (5 タイル)
# 中心: x=29408, y=12041 想定だが fixture の bbox に合うように再計算
# fixture: x_3857 = 15938000 + (0..706)*5.7 ≒ 15938000 ~ 15942026
#          y_3857 = 5296500 + (0..707)*5.7 ≒ 5296500 ~ 5300528
# z=15 の tile bbox 1223m。x=29408 で minx=15937920。y で minY=5295828 程度

# データ位置: x_3857 ≒ 15938000-15942026, y_3857 ≒ 5296500-5300528
# z=15 tile 1223m → x_tile = (15940000 + 20037508) / 1223 ≒ 29417, y_tile ≒ 12051
$tiles = @(
  @{ z=15; x=29417; y=12051 },
  @{ z=15; x=29418; y=12051 },
  @{ z=15; x=29417; y=12052 },
  @{ z=15; x=29418; y=12052 },
  @{ z=15; x=29419; y=12051 }
)

function Get-TileBbox($z, $x, $y) {
  $halfSize = 20037508.342789244
  $count = [Math]::Pow(2, $z)
  $tileSize = (2.0 * $halfSize) / $count
  $minX = -$halfSize + $x * $tileSize
  $maxX = $minX + $tileSize
  $maxY = $halfSize - $y * $tileSize
  $minY = $maxY - $tileSize
  return "$minX,$minY,$maxX,$maxY"
}

function Measure-Tile($ft, $cqlFilter, $z, $x, $y) {
  $bbox = Get-TileBbox $z $x $y
  $url = "http://localhost:8080/geoserver/agrigis/wms?service=WMS&version=1.1.1&request=GetMap" +
         "&layers=agrigis:$ft" +
         "&styles=agrigis:t_default" +
         "&bbox=$bbox" +
         "&width=256&height=256&srs=EPSG:3857&format=image/png&transparent=true" +
         "&CQL_FILTER=$([Uri]::EscapeDataString($cqlFilter))"
  $tmpPng = "$env:TEMP\poc-tile-$([Guid]::NewGuid().Guid).png"
  $out = & curl.exe -s -u "admin:$adminPass" $url -o $tmpPng -w "%{time_total}|%{http_code}|%{size_download}" 2>$null
  Remove-Item $tmpPng -ErrorAction SilentlyContinue
  $parts = "$out" -split '\|'
  [PSCustomObject]@{
    time = [double]$parts[0]
    http = [int]$parts[1]
    size = [int]$parts[2]
  }
}

$results = @()

# 経路 1: feature_current 直接 (asOf 無し 想定 = Phase D 既存)
Write-Host "===== 1. feature_current direct (asOf 無し相当) ====="
$cqlCurrent = "layer_id=1000"
foreach ($t in $tiles) {
  $r = Measure-Tile "feature_current" $cqlCurrent $t.z $t.x $t.y
  Write-Host ("  tile z={0} x={1} y={2}: {3:N3}s (http={4}, size={5})" -f $t.z, $t.x, $t.y, $r.time, $r.http, $r.size)
  $results += [PSCustomObject]@{ path="feature_current"; tile="$($t.z)/$($t.x)/$($t.y)"; time=$r.time; http=$r.http; size=$r.size }
}

# 経路 2: feature_asof view (asOf 無し: valid_to='9999-12-31' で絞る)
Write-Host "===== 2. feature_asof + valid_to='9999-12-31' (asOf 無し相当) ====="
$cqlAsofCurrent = "layer_id=1000 AND valid_to='9999-12-31'"
foreach ($t in $tiles) {
  $r = Measure-Tile "feature_asof" $cqlAsofCurrent $t.z $t.x $t.y
  Write-Host ("  tile z={0} x={1} y={2}: {3:N3}s (http={4}, size={5})" -f $t.z, $t.x, $t.y, $r.time, $r.http, $r.size)
  $results += [PSCustomObject]@{ path="feature_asof_current"; tile="$($t.z)/$($t.x)/$($t.y)"; time=$r.time; http=$r.http; size=$r.size }
}

# 経路 3: feature_asof view + asOf=2024-06-15 (history を取りに行く)
Write-Host "===== 3. feature_asof + asOf=2024-06-15 (履歴経由) ====="
$cqlAsofPast = "layer_id=1000 AND valid_from <= '2024-06-15' AND '2024-06-15' < valid_to"
foreach ($t in $tiles) {
  $r = Measure-Tile "feature_asof" $cqlAsofPast $t.z $t.x $t.y
  Write-Host ("  tile z={0} x={1} y={2}: {3:N3}s (http={4}, size={5})" -f $t.z, $t.x, $t.y, $r.time, $r.http, $r.size)
  $results += [PSCustomObject]@{ path="feature_asof_past"; tile="$($t.z)/$($t.x)/$($t.y)"; time=$r.time; http=$r.http; size=$r.size }
}

Write-Host ""
Write-Host "===== Summary ====="
$results | Group-Object path | ForEach-Object {
  $avg = ($_.Group | Measure-Object -Property time -Average).Average
  $max = ($_.Group | Measure-Object -Property time -Maximum).Maximum
  Write-Host ("  {0}: avg={1:N3}s, max={2:N3}s" -f $_.Name, $avg, $max)
}

# 判定
$asofAvg = ($results | Where-Object path -eq 'feature_asof_current' | Measure-Object -Property time -Average).Average
$asofPastAvg = ($results | Where-Object path -eq 'feature_asof_past' | Measure-Object -Property time -Average).Average

Write-Host ""
if ($asofAvg -lt 0.5 -and $asofPastAvg -lt 0.5) {
  Write-Host "===== go: feature_asof + valid_to filter < 500ms cold ====="
} elseif ($asofAvg -lt 2.0 -and $asofPastAvg -lt 2.0) {
  Write-Host "===== amber: < 2s だが 500ms 超過、Phase E 着手は可能 ====="
} else {
  Write-Host "===== no-go: > 2s、パーティショニング検討 or スコープ縮小 ====="
}

# 結果を JSON で出力 (PHASE_E_E100_POC_RESULT.md 用)
$resultPath = "$PSScriptRoot/result.json"
@{
  generatedAt = (Get-Date).ToString("o")
  results = $results
  summary = @{
    feature_current_avg = ($results | Where-Object path -eq 'feature_current' | Measure-Object -Property time -Average).Average
    feature_asof_current_avg = $asofAvg
    feature_asof_past_avg = $asofPastAvg
  }
} | ConvertTo-Json -Depth 5 | Out-File -FilePath $resultPath -Encoding utf8
Write-Host ""
Write-Host "Result saved: $resultPath"
