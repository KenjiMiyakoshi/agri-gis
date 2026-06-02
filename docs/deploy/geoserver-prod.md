# GeoServer 本番別ホスト構成 (Phase D)

Phase D 採用案 (`docs/issues/PHASE_D_DESIGN_P.md` §2.1) で確定:

- dev: `docker-compose.yml` に geoserver サービス同梱 (`agri_geoserver`)
- 本番: **別ホスト** (k8s / VM)、agri-gis API からは `appsettings.json: GeoServer.BaseUrl` で接続

本ドキュメントは本番セットアップ手順を 2 パターン (k8s + VM 直) で記載。

## 1. 共通前提

| 項目 | 値 |
|---|---|
| Java | OpenJDK 17+ |
| GeoServer | 2.25.x |
| PostgreSQL/PostGIS | 16 + 3.4 (agri-gis と同じ) |
| 認証 | basic auth (API ↔ GeoServer の内部、JWT は API 経由) |
| ストレージ | data_dir (workspace/datastore/styles/layers) |
| ネットワーク | API ↔ GeoServer は内部 (port 8080 / TLS 不要)、外部公開なし |

## 2. k8s パターン

### 2.1 Helm chart

公式: <https://github.com/kartoza/charts/tree/master/charts/geoserver>

```bash
helm repo add kartoza https://kartoza.github.io/charts
helm install agrigis-geoserver kartoza/geoserver \
  --set image.tag=2.25.0 \
  --set geoserverAdminPassword=$AGRI_GIS_GEOSERVER_ADMIN_PASSWORD \
  --set persistence.size=20Gi \
  --set postgis.host=agrigis-postgis.internal \
  --set postgis.database=agri_gis \
  --set postgis.user=agri_user
```

### 2.2 manifest (手書き例)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: agrigis-geoserver
spec:
  replicas: 1
  selector:
    matchLabels: { app: geoserver }
  template:
    metadata:
      labels: { app: geoserver }
    spec:
      containers:
      - name: geoserver
        image: kartoza/geoserver:2.25.0
        env:
        - { name: GEOSERVER_ADMIN_PASSWORD, valueFrom: { secretKeyRef: { name: geoserver-secret, key: admin-password } } }
        - { name: HOST, value: agrigis-postgis }
        - { name: POSTGRES_DB, value: agri_gis }
        - { name: POSTGRES_USER, value: agri_user }
        - { name: POSTGRES_PASS, valueFrom: { secretKeyRef: { name: postgis-secret, key: password } } }
        - { name: INITIAL_MEMORY, value: 2G }
        - { name: MAXIMUM_MEMORY, value: 4G }
        ports: [{ containerPort: 8080 }]
        volumeMounts:
        - { name: data, mountPath: /opt/geoserver/data_dir }
        readinessProbe:
          httpGet: { path: /geoserver/web/, port: 8080 }
          initialDelaySeconds: 90
      volumes:
      - name: data
        persistentVolumeClaim: { claimName: geoserver-data }
---
apiVersion: v1
kind: Service
metadata:
  name: agrigis-geoserver
spec:
  selector: { app: geoserver }
  ports:
  - { port: 8080, targetPort: 8080 }
```

API 側 (agri-gis) からは `appsettings.json` に:

```json
{
  "GeoServer": {
    "BaseUrl": "http://agrigis-geoserver:8080/geoserver",
    "AdminUser": "admin",
    "AdminPasswordEnv": "AGRI_GIS_GEOSERVER_ADMIN_PASSWORD",
    "Workspace": "agrigis"
  }
}
```

API Pod に `geoserver-secret` を environment に reference。

## 3. VM 直構成 (Ubuntu 22.04 例)

### 3.1 インストール

```bash
sudo apt update
sudo apt install -y openjdk-17-jre-headless wget unzip
sudo useradd -m -s /bin/bash geoserver
cd /opt
sudo wget https://sourceforge.net/projects/geoserver/files/GeoServer/2.25.0/geoserver-2.25.0-bin.zip
sudo unzip geoserver-2.25.0-bin.zip
sudo mv geoserver-2.25.0 geoserver
sudo chown -R geoserver:geoserver /opt/geoserver
```

### 3.2 PostGIS JNDI

`/opt/geoserver/data_dir/jndi.properties` (ない場合は新規):

```properties
java.naming.factory.initial=org.apache.naming.java.javaURLContextFactory
java.naming.factory.url.pkgs=org.apache.naming
```

`/opt/geoserver/webapps/geoserver/META-INF/context.xml` に PostgreSQL DataSource 定義。

### 3.3 systemd unit

`/etc/systemd/system/geoserver.service`:

```ini
[Unit]
Description=GeoServer 2.25.0
After=network.target

[Service]
Type=simple
User=geoserver
Environment=GEOSERVER_DATA_DIR=/opt/geoserver/data_dir
Environment=JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64
Environment=JAVA_OPTS=-Xmx4G -Xms2G -DGEOSERVER_ADMIN_PASSWORD=${GEOSERVER_ADMIN_PASSWORD}
EnvironmentFile=/etc/agrigis/geoserver.env
ExecStart=/opt/geoserver/bin/startup.sh
ExecStop=/opt/geoserver/bin/shutdown.sh
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

`/etc/agrigis/geoserver.env`:

```
GEOSERVER_ADMIN_PASSWORD=...
POSTGRES_HOST=agrigis-postgis.internal
POSTGRES_DB=agri_gis
POSTGRES_USER=agri_user
POSTGRES_PASS=...
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now geoserver
```

## 4. SSL/TLS (reverse proxy)

GeoServer 自体は HTTP 8080。本番では Nginx / Caddy / k8s Ingress で TLS 終端し、内部経由のみ API ↔ GeoServer。**API は外部に出ない経路で叩く** (security boundary)。

```nginx
# 内部ネットワーク内
upstream geoserver_internal { server 10.0.1.42:8080; }
# 外部公開なし (API のみ /tiles/* を JWT 認可付きで proxy)
```

## 5. 初期セットアップ手順

1. GeoServer 起動 → Web UI (`https://geoserver.internal/geoserver/web/`) で admin password 変更
2. Workspace 作成: `agrigis`
3. DataStore 作成: `postgis_main` (PostgreSQL 接続情報)
4. Layer 公開: `feature_current` (PostGIS Table)
5. SLD アップロード: `default.sld` / 他 theme.sld
6. 接続テスト: `curl https://geoserver.internal/geoserver/agrigis/wms?service=WMS&request=GetCapabilities` で 200

または `tools/poc/GeoServerCheck/verify.sh` を本番接続情報で動かす。

## 6. JWT pass-through 注意点

agri-gis API は Bearer JWT を受領し、GeoServer に basic auth で proxy する。GeoServer は JWT を理解しないため、**JWT pass-through は不可**。

代替: API でユーザ認可を確定してから GeoServer に basic auth で叩く (現実装)。
セキュリティ境界は API ↔ GeoServer の HTTP basic auth + ネットワーク分離。

## 7. デプロイ手順 (Phase D 完了時の本番切替)

1. **JWT 互換性破壊**: WD1 D103 で sid_session claim を必須化、Phase A/B/C 期の token は全て invalid (401)
2. デプロイ後、全ユーザ再ログインが必要
3. アナウンス例:
   > Phase D デプロイに伴い、再ログインが必要です。現在のセッションは自動的にログアウトされます。

## 8. パフォーマンス指標

`docs/issues/PHASE_D_ISSUES_INDEX.md` D504 受け入れ条件:

- z=15 タイル平均応答時間 < 500ms (cold cache)
- z=15 タイル平均応答時間 < 50ms (warm cache、Phase D' MapProxy 後)
- 50 万件 fixture で性能 smoke 実施

性能ボトルネック調査:
- PostGIS GIST index 確認 (`feature_current_geom_gix`)
- GeoServer JVM heap (`MAXIMUM_MEMORY`)
- WMS GetMap の `tiled=true` パラメタ確認

## 9. 関連

- `docs/issues/PHASE_D_DESIGN_P.md` §2.1
- `docs/rendering.md` — 描画アーキ解説
- `tools/poc/GeoServerCheck/` — dev PoC スケルトン
