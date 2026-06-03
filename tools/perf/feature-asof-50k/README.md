# Phase E WE0 PoC — feature_asof view 性能計測

`PHASE_E_DESIGN_P.md` §11 P1 リスク (feature_current + feature_history UNION ALL の性能) を 50 万件 fixture で検証する PoC。

## 構成

| ファイル | 役割 |
|---------|------|
| `generate.sql` | feature_current 50 万件 + feature_history 100 万件 + feature_asof VIEW |
| `setup-geoserver.ps1` | GeoServer に `feature_asof` featureType を追加 |
| `run.ps1` | 3 経路 × 5 タイル の curl 計測 (cold cache) |
| `result.json` | 計測結果 (run.ps1 が出力) |

## 計測する 3 経路

| 経路 | featureType | CQL_FILTER |
|------|------------|-----------|
| 1 | `feature_current` | `layer_id=1000` (asOf 無し相当、Phase D 既存) |
| 2 | `feature_asof` | `layer_id=1000 AND valid_to='9999-12-31'` (asOf 無し相当、Phase E 新経路) |
| 3 | `feature_asof` | `layer_id=1000 AND valid_from <= '2024-06-15' AND '2024-06-15' < valid_to` (履歴経由) |

経路 1 vs 経路 2 で「feature_asof view のオーバーヘッド」、経路 2 vs 経路 3 で「履歴範囲スキャンの追加コスト」が見える。

## 使い方 (ユーザー手動)

```powershell
# 1. fixture 生成 (PostgreSQL に直接 SQL)
Get-Content tools/perf/feature-asof-50k/generate.sql -Raw | docker exec -i agri_postgis psql -U agri_user -d agri_gis -v ON_ERROR_STOP=1

# 2. GeoServer に feature_asof featureType 追加
$env:AGRI_GIS_GEOSERVER_ADMIN_PASSWORD = "geoserver_dev"
tools/perf/feature-asof-50k/setup-geoserver.ps1

# 3. 計測
tools/perf/feature-asof-50k/run.ps1
```

## 受け入れ基準

| 結果 | 判定 | 次アクション |
|------|------|------------|
| 経路 2/3 とも < 500ms (cold) | **go** | WE1 着手承認、`PHASE_E_E100_POC_RESULT.md` に記録 |
| 500ms-2s (amber) | Phase E 着手は可能だが、Phase E' でパーティショニング検討必須 | スコープ縮小なし、ただし WE5 性能 smoke で再検証 |
| > 2s (red) | **no-go** | パーティショニング戦略確定 → Phase E スコープに繰り込み or Phase E スコープ縮小 |

## クリーンアップ (PoC 完了後)

```powershell
# fixture 削除
docker exec agri_postgis psql -U agri_user -d agri_gis -c "DELETE FROM feature_history WHERE layer_id=1000; DELETE FROM feature_current WHERE layer_id=1000; DELETE FROM layers WHERE layer_id=1000;"

# GeoServer featureType 削除 (PoC で追加した feature_asof を残しても WE3 で同じ featureType を使うのでそのまま放置可)
```
