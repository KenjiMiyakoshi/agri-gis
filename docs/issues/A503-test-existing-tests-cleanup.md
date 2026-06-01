# A503: 既存 AsOfTests の手動 UPDATE 削除 + MissingActorTests → AuthRequiredTests

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 0.5d |
| Depends on | A104, A204, A502 |
| Blocks | なし |

## 概要
C1 修復 (A104) により不要になった `AsOfTests.cs:53-61` の手動 UPDATE を削除。`MissingActorTests.cs` を `AuthRequiredTests.cs` にリネームし、X-Actor 欠落 (400) 期待を JWT 欠落 (401) 期待に更新する。

## 背景・目的
採択案「案 P」のテストセクション:
> 既存 `AsOfTests.cs:53-61` の手動 UPDATE 削除（C1 修復で不要）
> `MissingActorTests.cs` → `AuthRequiredTests.cs` にリネーム + assertion 更新（1 ファイル既存変更）
> 残り 17 既存テストファイルは**無変更**（既存 `WithActor("alice")` が JWT 発行に切り替わる）

## スコープ
### 含む
- `tests/Tests/AsOfTests.cs` の手動 UPDATE 削除 (53-61 行付近の `UPDATE feature_history SET valid_to=...` 等)
- `tests/Tests/MissingActorTests.cs` を `tests/Tests/Auth/AuthRequiredTests.cs` に rename
- AuthRequiredTests:
  - X-Actor 欠落 → JWT 欠落 (`Anonymous()`) に書き換え
  - 期待 status 400 → 401
  - 期待 ProblemDetails のキー名/値も合わせる (A203 と整合)
- 既存 17 テストファイルは無変更（`WithActor("alice")` が A502 で JWT 発行に自動的に切り替わる）

### 含まない
- 新規テスト (A504〜)
- 17 既存テストの内部変更

## 受け入れ条件 (Acceptance Criteria)
- [ ] `AsOfTests.cs` から手動 UPDATE が消えても green
- [ ] `MissingActorTests.cs` がリポジトリから消えている
- [ ] `Tests/Auth/AuthRequiredTests.cs` が存在
- [ ] AuthRequiredTests: anonymous で書き込みエンドポイントを叩く → 401 ProblemDetails
- [ ] AuthRequiredTests: anonymous で読み取りエンドポイントを叩く → 401（A206 の方針: guest = JWT 必須）
- [ ] 既存 17 テストが全 green（`WithActor("alice")` 経由）

## 影響ファイル
- `D:\proj\agri-gis\tests\Tests\AsOfTests.cs` (53-61 行削除)
- `D:\proj\agri-gis\tests\Tests\MissingActorTests.cs` (削除)
- `D:\proj\agri-gis\tests\Tests\Auth\AuthRequiredTests.cs` (新規、内容は MissingActorTests のリライト)

## 実装ノート
AsOfTests の対象箇所:
```csharp
// 削除対象（C1 修復前は手動でゼロ幅区間補正していた）
await db.ExecuteAsync(@"
    UPDATE feature_history
    SET valid_to = CURRENT_DATE
    WHERE entity_id = @id AND archived_reason = 'update'", new { id });
```

AuthRequiredTests の雛形:
```csharp
public class AuthRequiredTests : IClassFixture<ApiFactory>
{
    private readonly ApiClientFactory _factory;
    public AuthRequiredTests(ApiFactory api) { _factory = new(api); }

    [Fact]
    public async Task Anonymous_Post_Feature_Returns_401()
    {
        var client = _factory.Anonymous();
        var res = await client.PostFeatureRaw(new { /* ... */ });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("application/problem+json",
            res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Anonymous_Get_Features_Returns_401()
    {
        var client = _factory.Anonymous();
        var res = await client.GetFeaturesRaw();
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
```

注意点:
- 既存 17 テストは `WithActor("alice")` 呼び出しだけで動くはずだが、内部で X-Actor を直接送る系の helper があれば A403 と同期して削除
- `Tests/Auth/` ディレクトリは新規作成

## テスト観点
- 既存 17 テストの green 維持
- AuthRequiredTests 単独で anonymous → 401 の確認
