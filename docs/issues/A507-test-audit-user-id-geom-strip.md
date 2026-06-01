# A507: AuditUserIdTests + AuditLogGeomStripTests (C2 回帰)

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 0.5d |
| Depends on | A102, A105, A106, A502 |
| Blocks | なし |

## 概要
audit_log.actor_user_id が呼び出しユーザに一致するか、audit_log.before_doc/after_doc から geom が strip されて geom_geojson が入っているかを回帰テストで担保する。

## 背景・目的
採択案「案 P」のテストセクション:
> `Tests/Audit/AuditUserIdTests.cs`
> `Tests/Audit/AuditLogGeomStripTests.cs`（C2 回帰）

## スコープ
### 含む
#### AuditUserIdTests
- alice (admin) で feature INSERT → audit_log.actor_user_id = SeedUsers.Alice.UserId
- bob (general) で feature UPDATE → audit_log.actor_user_id = SeedUsers.Bob.UserId
- alice で feature DELETE → 同様
- audit_log.actor (display_name snapshot) が users.display_name と一致
- 連続操作の順序保存（id 昇順で正しい順）

#### AuditLogGeomStripTests
- INSERT 後の audit_log.after_doc に `geom` key が無い
- INSERT 後の `after_doc->>'geom_geojson'` が valid GeoJSON 文字列
- UPDATE 後 before_doc/after_doc 両方で同様
- DELETE 後 before_doc も同様
- geom = NULL の feature を作って `geom_geojson IS NULL` を確認

### 含まない
- C1 回帰 (A508)
- BCrypt 確認 (A508)

## 受け入れ条件 (Acceptance Criteria)
- [ ] audit_log.actor_user_id が呼び出しユーザに一致
- [ ] audit_log JSON に `geom` key が存在しない
- [ ] geom_geojson が GeoJSON として valid（System.Text.Json でパース可能、type=Polygon 等）
- [ ] geom NULL の場合 geom_geojson が JSON null

## 影響ファイル
- `D:\proj\agri-gis\tests\Tests\Audit\AuditUserIdTests.cs` (新規)
- `D:\proj\agri-gis\tests\Tests\Audit\AuditLogGeomStripTests.cs` (新規)

## 実装ノート
```csharp
// AuditUserIdTests.cs
[Fact]
public async Task Feature_Insert_Records_Caller_UserId()
{
    var bob = _factory.WithActor("bob");
    var created = await bob.PostFeatureAsync(new { /* ... */ });

    using var db = await OpenDb();
    var row = await db.QuerySingleAsync<(Guid actor_user_id, string actor)>(
        "SELECT actor_user_id, actor FROM audit_log WHERE action='feature_insert' ORDER BY id DESC LIMIT 1");
    Assert.Equal(SeedUsers.Bob.UserId, row.actor_user_id);
    Assert.Equal(SeedUsers.Bob.DisplayName, row.actor);
}

// AuditLogGeomStripTests.cs
[Fact]
public async Task Audit_AfterDoc_Has_GeomGeojson_Not_Geom()
{
    var alice = _factory.WithActor("alice");
    await alice.PostFeatureAsync(new { /* polygon */ });

    using var db = await OpenDb();
    var json = await db.ExecuteScalarAsync<string>(
        "SELECT after_doc::text FROM audit_log WHERE action='feature_insert' ORDER BY id DESC LIMIT 1");
    using var doc = JsonDocument.Parse(json);
    Assert.False(doc.RootElement.TryGetProperty("geom", out _));
    Assert.True(doc.RootElement.TryGetProperty("geom_geojson", out var gj));
    Assert.Equal(JsonValueKind.String, gj.ValueKind);
    using var gjDoc = JsonDocument.Parse(gj.GetString()!);
    Assert.Equal("Polygon", gjDoc.RootElement.GetProperty("type").GetString());
}

[Fact]
public async Task Audit_NullGeom_Has_Null_GeomGeojson()
{
    var alice = _factory.WithActor("alice");
    await alice.PostFeatureAsync(new { geom = (object?)null, /* attrs */ });

    using var db = await OpenDb();
    var json = await db.ExecuteScalarAsync<string>(
        "SELECT after_doc::text FROM audit_log WHERE action='feature_insert' ORDER BY id DESC LIMIT 1");
    using var doc = JsonDocument.Parse(json);
    Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("geom_geojson").ValueKind);
}
```

注意点:
- audit_log の id は SERIAL なので DESC LIMIT 1 で直近を取れる前提
- geom_geojson は JSONB の文字列値（string）か null

## テスト観点
- A102/A105/A106 の回帰防止
