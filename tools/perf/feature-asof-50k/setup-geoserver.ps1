# Phase E WE0 PoC: GeoServer に feature_asof featureType を追加
# 前提: tools/geoserver-setup/setup.ps1 で feature_current が既に公開済み

$ErrorActionPreference = "Stop"
$adminPass = if ($env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD) { $env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD } else { "geoserver_dev" }
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("admin:$adminPass"))
$headers = @{ Authorization = "Basic $basic" }

# feature_asof featureType (PostgreSQL VIEW 'feature_asof' を直接公開)
Write-Host "[1/2] Publish featureType 'feature_asof'..."
$ftXml = @"
<featureType>
  <name>feature_asof</name>
  <nativeName>feature_asof</nativeName>
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
try {
  Invoke-RestMethod -Uri "http://localhost:8080/geoserver/rest/workspaces/agrigis/datastores/postgis_main/featuretypes" `
    -Method Post -Headers $headers -ContentType "application/xml" -Body $ftXml | Out-Null
  Write-Host "  OK (created)"
} catch {
  if ($_.Exception.Response.StatusCode.value__ -eq 500 -or $_.Exception.Response.StatusCode -eq 'Conflict') {
    Write-Host "  (already exists)"
  } else { throw }
}

# featureType のデフォルトスタイルを t_default に
Write-Host "[2/2] Set default style 'agrigis:t_default'..."
$styleFqn = "agrigis:t_default"
$layerXml = "<layer><defaultStyle><name>$styleFqn</name></defaultStyle></layer>"
Invoke-RestMethod -Uri "http://localhost:8080/geoserver/rest/layers/agrigis:feature_asof" `
  -Method Put -Headers $headers -ContentType "application/xml" -Body $layerXml | Out-Null
Write-Host "  OK"

# Reload
Write-Host "Reloading GeoServer..."
Invoke-RestMethod -Uri "http://localhost:8080/geoserver/rest/reload" -Method Post -Headers $headers | Out-Null
Write-Host "  done"
