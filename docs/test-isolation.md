# Test Isolation (Phase E' E'1 / E'4)

`api.tests` の並列耐性問題を整理し、再現性のあるテスト実行を保証する。

## 背景

Phase D' WD'4 で `LayersEndpointsStyleVersionTests` を追加すると、`api.tests` の full run で 58 件失敗、`DbReset.cs:29` で例外発生。個別 run では pass。

Phase D'' (= 本 Phase E') 送りになっていた。

## 仮説 → 確認手順 (WE'0 PoC)

### 仮説 1: TRUNCATE 対象が不足

`api.tests/Fixtures/DbReset.cs` は `feature_current`, `feature_history`, `audit_log`, `layers` 等を TRUNCATE するが、Phase E で追加した `layer_style_version` / `layer_history` が漏れている可能性。

**確認**: `\dt` 一覧と DbReset の TRUNCATE リストを突き合わせ。

**修正案 a**: DbReset に追加

```csharp
await ExecuteAsync(conn, "TRUNCATE layer_style_version CASCADE");
await ExecuteAsync(conn, "TRUNCATE layer_history CASCADE");
```

### 仮説 2: xunit collection の不適切な分割

Phase E では `PostgisCollection` で xunit が `[Collection("Postgis")]` 配下のテストを直列化する想定だが、全テストが同 collection 配下にあるとは限らない。

**確認**: `xunit.runner.json` 設定 + `Tests/` 配下の `[Collection]` attribute 利用率を grep で確認。

**修正案 b**: 全 testcontainer 利用テストに `[Collection(PostgisCollection.Name)]` 強制。

### 仮説 3: testcontainer の connection pool 枯渇

並列 collection (PostgisCollection と他 collection) が同 connection string で `NpgsqlDataSource` を共有すると、pool size を超えてエラー。

**確認**: `NpgsqlDataSourceBuilder` の `MaxPoolSize` 設定 + 並列度。

**修正案 c**: collection ごとに dedicated schema (`CREATE SCHEMA test_xxx`) を切る。

## 採用案 (WE'0 PoC 結果を反映、暫定: a + b)

### a. DbReset の TRUNCATE 拡張

```csharp
// E'101 (WE'1): Phase E で追加された 2 テーブルを TRUNCATE 対象に
await ExecuteAsync(conn, "TRUNCATE layer_style_version CASCADE");
await ExecuteAsync(conn, "TRUNCATE layer_history CASCADE");
```

### b. xunit Collection 整理

`PostgisCollectionAttribute` を全テストクラスに必ず付与:
```csharp
[Collection(PostgisCollection.Name)]
public sealed class XxxTests : IAsyncLifetime { ... }
```

`xunit.runner.json`:
```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": true,
  "maxParallelThreads": 4
}
```

Collection ごとに直列、Collection 間は並列 (デフォルト)。

### c. PoC で必要なら採用

並列 collection 数が多くなったら schema per collection。E' 段階では a + b で十分なはず。

## 受入条件

1. `dotnet test api.tests -c Release` 単独実行 と full run で同じ件数 pass
2. `LayersEndpointsStyleVersionTests` (WE'3) 追加後も regression なし
3. `xunit.runner.json` 設定が明示記録され、`testing-policy.md` に追記

## 関連

- `PHASE_E_PRIME_INDEX.md`
- Phase D' WD'4 (LayersEndpointsStyleVersionTests を送りにした PR)
- `docs/testing-policy.md`
