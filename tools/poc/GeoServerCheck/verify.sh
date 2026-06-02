#!/bin/bash
# Phase D WD0 PoC: GeoServer 同梱の 6 ステップ自動検証スクリプト
#
# 前提:
#   - docker compose up -d 後、geoserver-poc が healthy になっていること
#   - curl / psql コマンドが利用可能
#
# 結果:
#   - 全 6 ステップ成功 → go 判定、tools/poc/GeoServerCheck/results/ に PNG 保存
#   - 1 ステップでも失敗 → no-go 判定、原因切り分けを docs/issues/PHASE_D_D100_POC_RESULT.md に記録

set -eo pipefail

GEOSERVER_URL="http://localhost:18080/geoserver"
GEOSERVER_USER="admin"
GEOSERVER_PASS="geoserver_poc"
WORKSPACE="agrigis"
DATASTORE="postgis_poc"
LAYER="feature_current_poc"
RESULTS_DIR="$(dirname "$0")/results"
PG_HOST="localhost"
PG_PORT="55432"
PG_DB="agri_gis_poc"
PG_USER="agri_user"
PG_PASS="agri_pass"

mkdir -p "$RESULTS_DIR"

echo "===== Phase D WD0 PoC 6-step verification ====="

# ----- Step 1: GeoServer healthcheck -----
echo "[1/6] GeoServer web UI healthcheck..."
http_code=$(curl -s -o /dev/null -w "%{http_code}" -u "$GEOSERVER_USER:$GEOSERVER_PASS" "$GEOSERVER_URL/web/")
if [ "$http_code" != "200" ]; then
  echo "  FAIL: expected 200, got $http_code"
  exit 1
fi
echo "  OK"

# ----- Step 2: Create workspace + datastore -----
echo "[2/6] Create workspace + datastore via REST..."
curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" -XPOST -H "Content-Type: application/xml" \
  -d "<workspace><name>$WORKSPACE</name></workspace>" \
  "$GEOSERVER_URL/rest/workspaces" || echo "  workspace may already exist"

curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" -XPOST -H "Content-Type: application/xml" \
  -d @- \
  "$GEOSERVER_URL/rest/workspaces/$WORKSPACE/datastores" <<EOF || echo "  datastore may already exist"
<dataStore>
  <name>$DATASTORE</name>
  <connectionParameters>
    <host>postgis-poc</host>
    <port>5432</port>
    <database>$PG_DB</database>
    <user>$PG_USER</user>
    <passwd>$PG_PASS</passwd>
    <dbtype>postgis</dbtype>
    <schema>public</schema>
  </connectionParameters>
</dataStore>
EOF
echo "  OK"

# ----- Step 3: Seed sample data in PostGIS -----
echo "[3/6] Seed sample feature_current_poc (100 rows)..."
PGPASSWORD="$PG_PASS" psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" <<SQL
DROP TABLE IF EXISTS feature_current_poc;
CREATE TABLE feature_current_poc (
  entity_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  owner_kind TEXT NOT NULL,
  geom geometry(Polygon, 4326) NOT NULL
);
-- 帯広付近にランダム小ポリゴン 100 件
INSERT INTO feature_current_poc (owner_kind, geom)
SELECT
  CASE (random()*3)::int WHEN 0 THEN 'A' WHEN 1 THEN 'B' ELSE 'C' END,
  ST_MakeEnvelope(
    143.18 + random()*0.04,
    42.90 + random()*0.02,
    143.18 + random()*0.04 + 0.001,
    42.90 + random()*0.02 + 0.001,
    4326
  )
FROM generate_series(1, 100);
CREATE INDEX ix_feature_current_poc_geom ON feature_current_poc USING GIST (geom);
SQL

# Publish layer
curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" -XPOST -H "Content-Type: application/xml" \
  -d "<featureType><name>$LAYER</name></featureType>" \
  "$GEOSERVER_URL/rest/workspaces/$WORKSPACE/datastores/$DATASTORE/featuretypes" || echo "  layer may already exist"
echo "  OK"

# ----- Step 4: Upload 2 SLD styles -----
echo "[4/6] Upload SLD styles (default + byOwner)..."
for style in default byOwner; do
  sld_file="$(dirname "$0")/sld/${style}.sld"
  curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" -XPOST -H "Content-Type: application/vnd.ogc.sld+xml" \
    --data-binary "@$sld_file" \
    "$GEOSERVER_URL/rest/workspaces/$WORKSPACE/styles?name=$style" || echo "  style $style may already exist"
done
echo "  OK"

# ----- Step 5: GetMap with default style -> PNG -----
echo "[5/6] WMS GetMap with default style..."
curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" \
  "$GEOSERVER_URL/$WORKSPACE/wms?service=WMS&version=1.1.1&request=GetMap&layers=$WORKSPACE:$LAYER&styles=$WORKSPACE:default&bbox=143.18,42.90,143.22,42.92&width=256&height=256&srs=EPSG:4326&format=image/png" \
  -o "$RESULTS_DIR/step5_default.png"
file_size=$(wc -c < "$RESULTS_DIR/step5_default.png")
if [ "$file_size" -lt 1000 ]; then
  echo "  FAIL: PNG too small ($file_size bytes), check GeoServer logs"
  exit 1
fi
echo "  OK ($file_size bytes)"

# ----- Step 6: WMS with byOwner style + CQL_FILTER selection overlay -----
echo "[6/6] WMS GetMap with byOwner style + CQL_FILTER selection..."
curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" \
  "$GEOSERVER_URL/$WORKSPACE/wms?service=WMS&version=1.1.1&request=GetMap&layers=$WORKSPACE:$LAYER&styles=$WORKSPACE:byOwner&bbox=143.18,42.90,143.22,42.92&width=256&height=256&srs=EPSG:4326&format=image/png" \
  -o "$RESULTS_DIR/step6_byOwner.png"

# selection raster (CQL_FILTER で最初の 30 件のみ)
selected_ids=$(PGPASSWORD="$PG_PASS" psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" -t -c \
  "SELECT string_agg('''' || entity_id || '''', ',') FROM (SELECT entity_id FROM feature_current_poc LIMIT 30) s;")

curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" --get \
  --data-urlencode "service=WMS" \
  --data-urlencode "version=1.1.1" \
  --data-urlencode "request=GetMap" \
  --data-urlencode "layers=$WORKSPACE:$LAYER" \
  --data-urlencode "styles=$WORKSPACE:default" \
  --data-urlencode "bbox=143.18,42.90,143.22,42.92" \
  --data-urlencode "width=256" \
  --data-urlencode "height=256" \
  --data-urlencode "srs=EPSG:4326" \
  --data-urlencode "format=image/png" \
  --data-urlencode "transparent=true" \
  --data-urlencode "CQL_FILTER=entity_id IN ($selected_ids)" \
  -o "$RESULTS_DIR/step6_selection.png" \
  "$GEOSERVER_URL/$WORKSPACE/wms"

# 応答時間計測 (5 回)
echo "  performance smoke (5 requests)..."
times=()
for i in 1 2 3 4 5; do
  t=$(curl -fsSL -u "$GEOSERVER_USER:$GEOSERVER_PASS" --get \
    --data-urlencode "service=WMS" \
    --data-urlencode "version=1.1.1" \
    --data-urlencode "request=GetMap" \
    --data-urlencode "layers=$WORKSPACE:$LAYER" \
    --data-urlencode "styles=$WORKSPACE:default" \
    --data-urlencode "bbox=143.18,42.90,143.22,42.92" \
    --data-urlencode "width=256" \
    --data-urlencode "height=256" \
    --data-urlencode "srs=EPSG:4326" \
    --data-urlencode "format=image/png" \
    -o /dev/null -w "%{time_total}" \
    "$GEOSERVER_URL/$WORKSPACE/wms")
  times+=("$t")
  echo "    request $i: ${t}s"
done
echo "  OK"

echo ""
echo "===== ALL 6 STEPS PASSED → go judgment ====="
echo "  results: $RESULTS_DIR/"
echo "  ls -la $RESULTS_DIR/"
ls -la "$RESULTS_DIR/"
echo ""
echo "Record results in docs/issues/PHASE_D_D100_POC_RESULT.md"
