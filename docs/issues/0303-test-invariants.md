# 0303: 不変条件テスト (INSERT/UPDATE/DELETE)

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 1d |
| Depends on | 0302, 0210, 0211, 0212 |
| Blocks | 0304 |

## 概要
書き込み 3 系統 (INSERT / UPDATE / DELETE) について、`feature_current`, `feature_history`, `audit_log` のカウントが期待どおり変化することを検証する。

## 背景・目的
案 B' の「実装初日からバイテンポラル＋監査」の中核を担保する。コードを書き換えてもこのテストが落ちれば破壊が検知できるという最後の防波堤。

## スコープ
### 含む
- `Tests/Invariants/InsertInvariantTests.cs`
  - POST 後: current=1, history=0, audit=+1 (action='feature_insert')
  - audit.after_doc が NULL でない、before_doc が NULL
- `Tests/Invariants/UpdateInvariantTests.cs`
  - POST → PATCH 後: current=1 (version=2), history=+1 (version=1, archived_reason='update'), audit=+1 (action='feature_update')
  - audit.before_doc と audit.after_doc が両方 NOT NULL
- `Tests/Invariants/DeleteInvariantTests.cs`
  - POST → DELETE 後: current=0, history=+1 (archived_reason='delete'), audit=+1 (action='feature_delete')
  - audit.after_doc が NULL
- 各テストは X-Actor を付ける（合法系）

### 含まない
- 楽観ロック / 422 / 404 系 (0304)
- スキーマ更新の不変条件（0304 で扱う）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 上記 3 シナリオが pass
- [ ] テスト名で意図が読み取れる (`Insert_AddsCurrent_AndAuditOnly`, ...)
- [ ] 各テストで RowCounters の値を assert する箇所が明確

## 影響ファイル
- `D:\proj\agri-gis\api.tests\Tests\Invariants\InsertInvariantTests.cs` (新規)
- `D:\proj\agri-gis\api.tests\Tests\Invariants\UpdateInvariantTests.cs` (新規)
- `D:\proj\agri-gis\api.tests\Tests\Invariants\DeleteInvariantTests.cs` (新規)

## 実装ノート
```csharp
[Collection("postgis")]
public class InsertInvariantTests
{
    private readonly PostgisContainerFixture _fx;
    public InsertInvariantTests(PostgisContainerFixture fx)
    {
        _fx = fx;
        DbReset.RunAsync(_fx.ConnectionString).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Insert_AddsCurrent_AndAuditOnly()
    {
        using var client = ApiClientFactory.New(_fx).WithActor("alice");
        var body = new
        {
            layerId = 1,
            geometry = JsonDocument.Parse("""{"type":"Point","coordinates":[143.2,42.91]}""").RootElement,
            attributes = new Dictionary<string, object> { ["name"] = "X" }
        };
        var res = await client.PostAsJsonAsync("/api/features", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var counts = new RowCounters(_fx.ConnectionString);
        Assert.Equal(1, await counts.Current());
        Assert.Equal(0, await counts.History());
        Assert.Equal(1, await counts.Audit());
        Assert.Equal("feature_insert", await counts.LatestAuditAction());
    }
}
```

注意点:
- 図形は `Point` で十分（バリデーション通りやすい）
- audit.action の文字列は 0107/0108/0109 のスペックに合わせる

## テスト観点
- 不変条件 3 件
