# Phase D dev GeoServer 自動セットアップ
#
# 前提:
#   - docker compose up -d で agri_geoserver + agri_postgis 起動済
#   - postgis に Phase D migration 4 本適用済
#   - GeoServer admin password = $env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD (なければ geoserver_dev)
#
# 実行内容:
#   1. workspace 'agrigis' 作成
#   2. datastore 'postgis_main' 作成 (Docker network 内 postgis hostname で接続)
#   3. featureType 'feature_current' 直接公開 (テーブル全体)
#   4. style 't_default' を SLD でアップロード
#   5. feature_current のデフォルトスタイルに t_default を設定
#
# 各 layer (layer_id=1, 2, ...) は TilesEndpoints の CQL_FILTER=layer_id=N で絞る (hotfix)

$ErrorActionPreference = "Stop"

$GS = "http://localhost:8080/geoserver"
$WS = "agrigis"
$DS = "postgis_main"
$FT = "feature_current"
$STYLE = "t_default"

$adminUser = "admin"
$adminPass = if ($env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD) { $env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD } else { "geoserver_dev" }
$pair = "${adminUser}:${adminPass}"
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pair))
$headers = @{ Authorization = "Basic $basic" }

function Invoke-GeoServer {
    param(
        [string]$Method,
        [string]$Path,
        [string]$ContentType = "application/xml",
        [string]$Body = $null
    )
    $url = "$GS$Path"
    try {
        if ($Body) {
            Invoke-RestMethod -Uri $url -Method $Method -Headers $headers `
                -ContentType $ContentType -Body $Body -ErrorAction Stop
        } else {
            Invoke-RestMethod -Uri $url -Method $Method -Headers $headers -ErrorAction Stop
        }
        return $true
    } catch {
        $resp = $_.Exception.Response
        if ($resp -and ($resp.StatusCode -eq 409 -or $resp.StatusCode.value__ -eq 409 -or $resp.StatusCode -eq "Conflict")) {
            Write-Host "  (already exists, skipping)"
            return $true
        }
        if ($resp -and ($resp.StatusCode.value__ -eq 500 -or $resp.StatusCode -eq "InternalServerError")) {
            # 500 でも既存の場合は無視
            Write-Host "  (500, possibly already exists, skipping)"
            return $true
        }
        Write-Host "  FAILED: $_"
        throw
    }
}

# 1) Workspace
Write-Host "[1/5] Create workspace '$WS'..."
$wsXml = "<workspace><name>$WS</name></workspace>"
Invoke-GeoServer -Method Post -Path "/rest/workspaces" -Body $wsXml | Out-Null
Write-Host "  OK"

# 2) Datastore
Write-Host "[2/5] Create datastore '$DS'..."
$dsXml = @"
<dataStore>
  <name>$DS</name>
  <connectionParameters>
    <entry key="host">postgis</entry>
    <entry key="port">5432</entry>
    <entry key="database">agri_gis</entry>
    <entry key="user">agri_user</entry>
    <entry key="passwd">agri_pass</entry>
    <entry key="dbtype">postgis</entry>
    <entry key="schema">public</entry>
    <entry key="Loose bbox">true</entry>
    <entry key="preparedStatements">true</entry>
  </connectionParameters>
</dataStore>
"@
Invoke-GeoServer -Method Post -Path "/rest/workspaces/$WS/datastores" -Body $dsXml | Out-Null
Write-Host "  OK"

# 3) FeatureType (feature_current テーブル直接公開、CQL_FILTER で layer_id 絞り込み)
Write-Host "[3/5] Publish featureType '$FT' (whole table, layer_id filtered via CQL_FILTER)..."
$ftXml = @"
<featureType>
  <name>$FT</name>
  <nativeName>$FT</nativeName>
  <namespace>
    <name>$WS</name>
  </namespace>
  <title>Feature Current (filtered by CQL_FILTER=layer_id=N)</title>
  <srs>EPSG:4326</srs>
  <nativeBoundingBox>
    <minx>122.0</minx>
    <miny>20.0</miny>
    <maxx>154.0</maxx>
    <maxy>46.0</maxy>
    <crs>EPSG:4326</crs>
  </nativeBoundingBox>
  <latLonBoundingBox>
    <minx>122.0</minx>
    <miny>20.0</miny>
    <maxx>154.0</maxx>
    <maxy>46.0</maxy>
    <crs>EPSG:4326</crs>
  </latLonBoundingBox>
  <enabled>true</enabled>
</featureType>
"@
Invoke-GeoServer -Method Post -Path "/rest/workspaces/$WS/datastores/$DS/featuretypes" -Body $ftXml | Out-Null
Write-Host "  OK"

# 4) Style 't_default' を SLD でアップロード
Write-Host "[4/5] Upload style '$STYLE'..."
# まず style を作成 (空)
$styleXml = "<style><name>$STYLE</name><filename>$STYLE.sld</filename></style>"
try {
    Invoke-GeoServer -Method Post -Path "/rest/workspaces/$WS/styles" -Body $styleXml | Out-Null
} catch {
    Write-Host "  style metadata may already exist, continuing"
}
# SLD 内容をアップロード
$sldPath = "$PSScriptRoot/../poc/GeoServerCheck/sld/default.sld"
if (-not (Test-Path $sldPath)) {
    throw "SLD file not found: $sldPath"
}
$sld = Get-Content $sldPath -Raw
# PUT で SLD 内容をセット
try {
    Invoke-RestMethod -Uri "$GS/rest/workspaces/$WS/styles/$STYLE" -Method Put `
        -Headers $headers -ContentType "application/vnd.ogc.sld+xml" -Body $sld -ErrorAction Stop | Out-Null
    Write-Host "  OK (SLD uploaded)"
} catch {
    Write-Host "  PUT failed, trying POST: $_"
    Invoke-RestMethod -Uri "$GS/rest/workspaces/$WS/styles?name=$STYLE" -Method Post `
        -Headers $headers -ContentType "application/vnd.ogc.sld+xml" -Body $sld -ErrorAction Stop | Out-Null
    Write-Host "  OK (SLD POST'd)"
}

# 5) featureType のデフォルトスタイルを t_default に
Write-Host "[5/7] Set default style of '$FT' to '$STYLE'..."
$styleFqn = "${WS}:${STYLE}"
$layerXml = @"
<layer>
  <defaultStyle>
    <name>$styleFqn</name>
  </defaultStyle>
</layer>
"@
$ftFqn = "${WS}:${FT}"
Invoke-RestMethod -Uri "$GS/rest/layers/$ftFqn" -Method Put `
    -Headers $headers -ContentType "application/xml" -Body $layerXml -ErrorAction Stop | Out-Null
Write-Host "  OK"

# 6) E301 (WE3): feature_asof featureType を公開 (Phase E asOf 経路で使う)
# WE1 で CREATE OR REPLACE VIEW feature_asof = feature_current UNION ALL feature_history が DB に存在する前提。
$FT_ASOF = "feature_asof"
Write-Host "[6/7] Publish featureType '$FT_ASOF' (Phase E asOf 経路)..."
$ftAsofXml = @"
<featureType>
  <name>$FT_ASOF</name>
  <nativeName>$FT_ASOF</nativeName>
  <namespace>
    <name>$WS</name>
  </namespace>
  <title>Feature ASOF (feature_current UNION ALL feature_history)</title>
  <srs>EPSG:3857</srs>
  <enabled>true</enabled>
  <projectionPolicy>FORCE_DECLARED</projectionPolicy>
  <nativeBoundingBox>
    <minx>-20037508.34</minx>
    <miny>-20037508.34</miny>
    <maxx>20037508.34</maxx>
    <maxy>20037508.34</maxy>
    <crs>EPSG:3857</crs>
  </nativeBoundingBox>
  <latLonBoundingBox>
    <minx>-180</minx><miny>-85</miny><maxx>180</maxx><maxy>85</maxy>
    <crs>EPSG:4326</crs>
  </latLonBoundingBox>
</featureType>
"@
Invoke-GeoServer -Method Post -Path "/rest/workspaces/$WS/datastores/$DS/featuretypes" -Body $ftAsofXml | Out-Null
Write-Host "  OK"

# feature_asof のデフォルトスタイルも t_default
$ftAsofFqn = "${WS}:${FT_ASOF}"
try {
    Invoke-RestMethod -Uri "$GS/rest/layers/$ftAsofFqn" -Method Put `
        -Headers $headers -ContentType "application/xml" -Body $layerXml -ErrorAction Stop | Out-Null
    Write-Host "  default style assigned to '$FT_ASOF'"
} catch {
    Write-Host "  default style assignment skipped (layer may not be ready yet): $_"
}

# 7) E301 (WE3): GeoServer reload (新 featureType を即時反映)
Write-Host "[7/7] Reloading GeoServer to apply new featureType..."
try {
    Invoke-RestMethod -Uri "$GS/rest/reload" -Method Post -Headers $headers -ErrorAction Stop | Out-Null
    Write-Host "  OK"
} catch {
    Write-Host "  reload skipped: $_"
}

Write-Host ""
Write-Host "===== GeoServer setup complete ====="
Write-Host "Workspace: $WS"
Write-Host "DataStore: $DS"
Write-Host "FeatureType: $FT (use CQL_FILTER=layer_id=N for per-layer filtering)"
Write-Host "FeatureType: $FT_ASOF (Phase E asOf 経路、CQL_FILTER + valid_from/_to で過去時点)"
Write-Host "Style: $STYLE"
Write-Host ""
Write-Host "Test GetMap (layer_id=1 / z=10):"
$testUrl = "$GS/$WS/wms?service=WMS&version=1.1.1&request=GetMap&layers=$ftFqn&styles=$styleFqn&bbox=-20037508,-20037508,20037508,20037508&width=256&height=256&srs=EPSG:3857&format=image/png&transparent=true&CQL_FILTER=layer_id%3D1"
Write-Host "  curl -u admin:<pwd> '$testUrl' -o /tmp/test.png"
