# 0304: 楽観ロック / スキーマ違反 / asOf / X-Actor テスト

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | 0303 |
| Blocks | なし |

## 概要
ハッピーパス以外の主要な経路（楽観ロック、属性スキーマ違反、asOf 過去断面、X-Actor 必須）を網羅する。

## 背景・目的
案 B' のヤバい部分（バージョン不整合・スキーマ不整合・タイムトラベル）を CI で守る。

## スコープ
### 含む
- `Tests/Concurrency/OptimisticLockTests.cs`
  - POST → PATCH(If-Match=99) で 409
  - PATCH(If-Match なし) で 428
- `Tests/Validation/SchemaViolationTests.cs`
  - POST で required の `name` を欠落 → 422 + errors[0].attributeKey="name", code="required"
  - POST で `name` に数値 → 422 + errors[0].code="type_mismatch"
- `Tests/Validation/MissingActorTests.cs`
  - POST / PATCH / DELETE / PUT(schema) のすべてで X-Actor なしを 400 で検証
- `Tests/Bitemporal/AsOfTests.cs`
  - POST → PATCH(version=1, 図形変更) 後:
    - GET `?layerId=1`（asOf 無し）で 1 件、geometry が新図形
    - GET `?layerId=1&asOf=<昨日>` で旧図形を含む（history から）
  - GET `?layerId=1&asOf=2026-01-01T00:00:00Z` (ISO datetime) で 400
- `Tests/Schema/SchemaUpsertTests.cs`
  - PUT /api/admin/layers/1/schema を 2 回呼ぶ
  - layers.schema_version が 1→2→3
  - layer_schema_version の最古行に valid_to が入っている

### 含まない
- WebView2 / WinForms 統合 (本サイクル外)

## 受け入れ条件 (Acceptance Criteria)
- [ ] 上記 5 ファイル合計で 10 ケース以上が pass
- [ ] 409 の ProblemDetails JSON が `{ status:409, extensions: { requestId } }` を満たす
- [ ] 422 の `errors[]` 配列構造が固定

## 影響ファイル
- `D:\proj\agri-gis\api.tests\Tests\Concurrency\OptimisticLockTests.cs` (新規)
- `D:\proj\agri-gis\api.tests\Tests\Validation\SchemaViolationTests.cs` (新規)
- `D:\proj\agri-gis\api.tests\Tests\Validation\MissingActorTests.cs` (新規)
- `D:\proj\agri-gis\api.tests\Tests\Bitemporal\AsOfTests.cs` (新規)
- `D:\proj\agri-gis\api.tests\Tests\Schema\SchemaUpsertTests.cs` (新規)

## 実装ノート
- asOf 過去日付テストでは、PATCH 前後の `valid_from` の動き方に注意。`fn_feature_update` は valid_from/to を変更しない実装（0108）なので、PATCH 前に投入した行は valid_from=今日, valid_to=9999-12-31 のまま history へ写る。テストでは「`asOf = 過去日付` でも history がヒットすべき」を確認するため、history 行の valid_from/to を **テスト内で UPDATE して過去化** するか、もしくは asOf にしか着目しない検証で済ますか整理する。
  - 推奨: テストでは history 行を直接 UPDATE して `valid_to = 'yesterday'`, history.archived_at は問わず asOf 指定が history を返すことを確認

```csharp
// asOf テストの一例
[Fact]
public async Task AsOf_PastDate_ReturnsHistoryGeometry()
{
    using var client = ApiClientFactory.New(_fx).WithActor("alice");
    var created = await client.PostAsJsonAsync("/api/features", new { /* ... */ });
    var entityId = ExtractEntityId(created);

    // 1 度 PATCH（version=1 → 2）
    client.DefaultRequestHeaders.Add("If-Match", "1");
    await client.PatchAsync($"/api/features/{entityId}", JsonContent.Create(new { /* 新図形 */ }));

    // history 行を「昨日まで有効」に書き換え
    await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
    {
        await conn.OpenAsync();
        await new NpgsqlCommand(
            "UPDATE feature_history SET valid_from = CURRENT_DATE - 7, valid_to = CURRENT_DATE - 1",
            conn).ExecuteNonQueryAsync();
    }

    var past = (CURRENT_DATE - 3).ToString("yyyy-MM-dd");
    var res = await client.GetAsync($"/api/features?layerId=1&asOf={past}");
    var fc = await res.Content.ReadFromJsonAsync<FeatureCollectionDto>();
    Assert.Contains(fc!.Features, f => f.Properties.Version == 1);
}
```

注意点:
- 422 の検証は `JsonElement` で `extensions.errors[0].attributeKey` 等を直接読む
- 428 はライブラリによっては `Method not allowed` 扱いになるので、`(int)res.StatusCode == 428` で確認

## テスト観点
- 楽観ロック (409, 428)
- バリデーション (422)
- X-Actor 必須 (400)
- asOf (UNION ALL の動作)
- schema_upsert
