# Phase D WD0 PoC — GeoServer 同梱検証 (skeleton)

`PHASE_D_DESIGN_P.md` の採用案 (案 A = GeoServer + MapProxy) が技術的に実現可能かを 0.5-1 人日で判定する PoC。

## 検証する 3 要件

1. **GeoServer + PostGIS 接続**: REST API で workspace/datastore/layer 公開可能
2. **SLD パラメタライズ**: 同じ z/x/y で `?STYLES=` を変えると配色が変わる PNG が返る
3. **CQL_FILTER で選択 raster overlay**: `entity_id IN (...)` 経由で 1 枚透過 PNG が返る + 性能計測

## 使い方 (ユーザーが手動で実行)

```bash
cd tools/poc/GeoServerCheck

# 起動
docker compose up -d
# postgis-poc が healthy になるまで 10-20 秒、geoserver-poc が healthy になるまで 60-90 秒

# 動作確認
docker compose ps
docker compose logs geoserver-poc | tail -20

# 自動検証 (curl + psql で 6 ステップ)
bash verify.sh

# 結果確認
ls -la results/
# step5_default.png   (default style の PNG)
# step6_byOwner.png   (byOwner style の PNG = 配色変化確認)
# step6_selection.png (CQL_FILTER 選択の PNG)

# クリーンアップ
docker compose down -v
```

## go/no-go 判定基準

| ステップ | 必須 | go 判定 | no-go 判定 |
|---|---|---|---|
| 1. GeoServer 起動 | 必須 | HTTP 200 | タイムアウト / 500 |
| 2. workspace + datastore | 必須 | 201 / 既存 200 | 401 / 500 |
| 3. PostGIS seed + layer 公開 | 必須 | INSERT + REST publish 成功 | psql 失敗 / REST 401 |
| 4. SLD 2 種アップロード | 必須 | 201 / 既存 200 | XML パース失敗 / 500 |
| 5. WMS GetMap (default) | 必須 | PNG ≥ 1KB | PNG = 0B / 500 |
| 6. WMS + CQL_FILTER (selection) | 必須 | PNG ≥ 1KB + 5 リクエスト平均 < 1.0s | > 2.0s → 性能チューニング Issue 起票 |

全 6 ステップ成功 → **go**、`docs/issues/PHASE_D_D100_POC_RESULT.md` に結果記録、WD1 着手。

1 ステップでも失敗 → **no-go**、原因切り分けタスクを起票し WD1 着手中止。

## 結果記録

`docs/issues/PHASE_D_D100_POC_RESULT.md` に以下を追記:

- 各ステップの成功/失敗
- step6 の 5 リクエスト応答時間
- step5/step6 PNG のスクリーンショット (or paths)
- go/no-go 判定
- 気づき (例: SLD parser error, JNDI 接続トラブル, kartoza image の癖)
- 朝の作業手順 (WD1 着手前提条件のチェックリスト)

## 注意点 (朝の作業者向け)

- **ポート競合**: dev `docker-compose.yml` の postgis (5432) を使っていると衝突するため、本 PoC は 55432 / 18080 にずらしている。dev compose が起動中でも並存可能
- **kartoza image の起動時間**: 60-90 秒。`docker compose logs -f geoserver-poc` でログを観察、`GeoServer is ready` 相当が出たら次へ
- **JNDI vs 直接接続**: PoC は kartoza image のデフォルト (env var 経由 datastore 自動構成) を使うが、`verify.sh` は明示的に REST POST で workspace/datastore を作る。これは Phase D 本番でも同じ流儀
- **SLD XML 妥当性**: `default.sld` / `byOwner.sld` は OGC SLD 1.0 準拠で書いてある。GeoServer が `application/vnd.ogc.sld+xml` で受け付けるはず

## ファイル構成

```
tools/poc/GeoServerCheck/
├── README.md             # 本ファイル
├── docker-compose.yml    # postgis + geoserver 最小構成
├── verify.sh             # 6 ステップ自動検証 (要 curl + psql)
├── sld/
│   ├── default.sld       # 緑塗りの単純スタイル
│   └── byOwner.sld       # owner_kind A/B/C の categorical color
├── pg_init/              # (空、PostGIS 初期化 SQL 不要)
├── data_dir/             # (空、GeoServer data_dir bind mount target)
└── results/              # verify.sh が PNG を出力 (.gitignore 推奨)
```

## 関連ドキュメント

- `docs/issues/PHASE_D_DESIGN_P.md`: 採用案
- `docs/issues/PHASE_D_ISSUES_INDEX.md` § D100: 受け入れ条件
- `docs/issues/PHASE_D_D100_POC_RESULT.md`: PoC 結果記録 (検証完了後に作成)
