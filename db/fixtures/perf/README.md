# Performance fixtures

WB0 (B506) で使う性能計測用 fixture。

- `sample_5000.geojson`: 北海道帯広市周辺の擬似観測点 5000 件 (FeatureCollection, EPSG:4326)
  - 生成スクリプト: `api.tests/Tests/Performance/PerfFixtureGenerator.cs`
  - 用途: B506 `BulkInsertSpike` で `fn_feature_insert × N` の所要時間を計測

CI では `[Trait("Category","Performance")]` でデフォルト除外。
ローカル計測手順は `docs/layer-import.md` (B601) で記載予定。
