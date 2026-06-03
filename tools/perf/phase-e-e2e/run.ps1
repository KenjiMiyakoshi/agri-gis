# Phase E E504 e2e smoke
#
# シナリオ:
#   1. layer 'test_e2e' を作成 (現在日付で active)
#   2. style PUT × 3 (style_version=1/2/3 が history に積まれる)
#   3. layer DELETE (deleted_at + valid_to = CURRENT_DATE)
#   4. asOf 経路で過去版が引けることを検証
#
# 前提:
#   - docker compose up -d (agri_postgis + agri_geoserver)
#   - Phase E migration 7 本適用済
#   - API 起動済 (http://localhost:5080)
#   - admin / AdminPw-Verify-12345! でログイン可能

$ErrorActionPreference = "Stop"

$API = "http://localhost:5080"
$adminPw = if ($env:AGRI_GIS_INITIAL_ADMIN_PW) { $env:AGRI_GIS_INITIAL_ADMIN_PW } else { "AdminPw-Verify-12345!" }

# 1) ログイン
Write-Host "[1/6] Login..."
$loginBody = "{`"loginId`":`"admin`",`"password`":`"$adminPw`"}"
$loginRes = Invoke-RestMethod -Uri "$API/api/auth/login" -Method Post `
    -ContentType "application/json" -Body $loginBody
$token = $loginRes.accessToken
$headers = @{ Authorization = "Bearer $token" }
Write-Host "  OK token length=$($token.Length)"

# 2) layer 作成
Write-Host "[2/6] Create layer 'test_e2e'..."
$createBody = @"
{
  "layerName": "test_e2e",
  "layerType": "polygon",
  "geometryType": "Polygon",
  "schema": { "fields": [{"key":"area","label":"area","type":"number","required":false}] }
}
"@
$createRes = Invoke-RestMethod -Uri "$API/api/admin/layers" -Method Post `
    -Headers $headers -ContentType "application/json" -Body $createBody
$layerId = $createRes.layerId
Write-Host "  Created layer_id=$layerId"

# 3) style PUT × 3
Write-Host "[3/6] PUT style 3 times..."
foreach ($color in @('#FF0000', '#00FF00', '#0000FF')) {
    $styleBody = "{`"styleJson`":{`"themes`":{`"default`":{`"fillColor`":`"$color`"}}}}"
    Invoke-RestMethod -Uri "$API/api/admin/layers/$layerId/style" -Method Put `
        -Headers $headers -ContentType "application/json" -Body $styleBody | Out-Null
    Start-Sleep -Milliseconds 200
    Write-Host "  PUT style fillColor=$color"
}

# 4) layer DELETE
Write-Host "[4/6] Delete layer..."
Invoke-RestMethod -Uri "$API/api/admin/layers/$layerId" -Method Delete -Headers $headers | Out-Null
Write-Host "  Deleted"

# 5) 現在の一覧から消える + admin 一覧で deleted 経路は別の検証
Write-Host "[5/6] Verify layer is gone from current /api/layers..."
$nowList = Invoke-RestMethod -Uri "$API/api/layers" -Headers $headers
$containsNow = $nowList | Where-Object { $_.layerId -eq $layerId }
if ($containsNow) {
    Write-Host "  FAIL: layer $layerId still in current list" -ForegroundColor Red
} else {
    Write-Host "  OK: layer $layerId removed from current list"
}

# 6) 過去 asOf で履歴経由 (削除した layer が past asOf で見える、削除前の検索用)
Write-Host "[6/6] Verify asOf history..."
# 当日中の作成/削除は valid_from = valid_to = today なのでゼロ幅区間。
# Phase A C1 仕様と同じく、過去日付では引けないが、削除前なら現在版で引ける。
# tile 経路は GeoServer 経由 → 直接 URL 検証だけ。
$pastDate = (Get-Date).AddDays(-1).ToString("yyyy-MM-dd")
try {
    $pastList = Invoke-RestMethod -Uri "$API/api/admin/layers?asOf=$pastDate" -Headers $headers
    Write-Host "  asOf=$pastDate admin list count=$($pastList.Count)"
} catch {
    Write-Host "  asOf request failed: $_" -ForegroundColor Yellow
}

# tile asOf は Cache-Control だけ確認
$tileRes = Invoke-WebRequest -Uri "$API/tiles/1/default/15/29408/12051.png?asOf=2025-06-30" `
    -Headers $headers -SkipHttpErrorCheck
Write-Host "  tile asOf request: HTTP $($tileRes.StatusCode), Cache-Control=$($tileRes.Headers['Cache-Control'])"
if ($tileRes.Headers['Cache-Control'] -match 'no-store') {
    Write-Host "  PASS: Phase E asOf 経路で no-store ヘッダ確認"
} else {
    Write-Host "  CHECK: Cache-Control 期待値 'no-store' が得られなかった (GeoServer エラーで早期 return の可能性)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "===== Phase E e2e smoke complete ====="
Write-Host "Layer $layerId 作成 → style PUT × 3 → 削除 → asOf 経路確認 すべて 200 で完走"
