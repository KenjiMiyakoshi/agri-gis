# geoserver/data_dir/

dev `docker-compose.yml` の `geoserver` サービスが bind mount する GeoServer データディレクトリ。

## 構成

```
geoserver/data_dir/
├── workspaces/
│   └── agrigis/                # Workspace
│       └── postgis_main/        # DataStore (PostGIS 接続)
│           └── feature_current/  # Layer (PostGIS table)
└── styles/
    ├── default.sld              # 基本スタイル (Phase D D101 で配置)
    └── byOwner.sld              # owner_kind カテゴリ別カラー
```

## 初期セットアップ

初回 `docker compose up -d` 後に GeoServer Web UI (`http://localhost:8080/geoserver/web/`) でセットアップ。

または REST API スクリプト (`PHASE_D_WD0_POC_RESULT.md` 参照) で自動構築。

## .gitignore 戦略

- 設定ファイル (`workspaces/`, `styles/`) は git 管理
- ログ / GWC tile cache 等は `.gitignore` 対象
- セキュリティクレデンシャル (`security/`) も git 除外

## 関連

- `docs/issues/PHASE_D_DESIGN_P.md` §2.1 GeoServer 構成
- `tools/poc/GeoServerCheck/`: PoC で検証した最小構成
