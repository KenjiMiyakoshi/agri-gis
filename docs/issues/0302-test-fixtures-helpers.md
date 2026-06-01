# 0302: テストフィクスチャ (TRUNCATE/seed) と共通ヘルパ

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 0.5d |
| Depends on | 0301 |
| Blocks | 0303, 0304 |

## 概要
テストごとにテーブルを TRUNCATE して seed を再投入する仕組みと、共通ヘルパ（HttpClient 取り回し、actor / request-id 設定など）を整える。

## 背景・目的
不変条件テストは「INSERT → current 1 / history 0 / audit 1」のように状態を厳密にカウントする。コンテナを共有しつつ各テストの開始状態を一定にするために TRUNCATE 戦略を取る。

## スコープ
### 含む
- `Fixtures/DbReset.cs`
  - `TRUNCATE feature_current, feature_history, audit_log, layer_schema_version RESTART IDENTITY CASCADE`
  - その後、layers を再シード（schema 含む）
  - `layer_schema_version` を初期化（layer ぶんの初期行を投入）
- `Fixtures/ApiClientFactory.cs`
  - `WithActor(string)` / `WithRequestId(string?)` のメソッドチェーン
  - 戻り値は `HttpClient`
- `Fixtures/RowCounters.cs`
  - `CountCurrent()`, `CountHistory()`, `CountAudit()`, `CountSchemaVersion()` などの SELECT COUNT(*)
- 各テストクラスのコンストラクタで `DbReset.Run()` を呼ぶ規約

### 含まない
- 個別ケース (0303, 0304)

## 受け入れ条件 (Acceptance Criteria)
- [ ] テストクラス間でフィクスチャ（コンテナ）は共有される
- [ ] テストごとに DbReset が走り、各テーブルの行数が一定
- [ ] `WithActor("alice")` で `X-Actor: alice` が付く HttpClient が返る
- [ ] `WithRequestId(null)` ならヘッダを付けない（サーバ採番させる）

## 影響ファイル
- `D:\proj\agri-gis\api.tests\Fixtures\DbReset.cs` (新規)
- `D:\proj\agri-gis\api.tests\Fixtures\ApiClientFactory.cs` (新規)
- `D:\proj\agri-gis\api.tests\Fixtures\RowCounters.cs` (新規)

## 実装ノート
```csharp
// Fixtures/DbReset.cs
public static class DbReset
{
    public static async Task RunAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using (var c = new NpgsqlCommand(
            "TRUNCATE feature_current, feature_history, audit_log, layer_schema_version RESTART IDENTITY CASCADE; " +
            "DELETE FROM layers;",
            conn))
            await c.ExecuteNonQueryAsync();

        // layers / layer_schema_version を再シード
        await using var c2 = new NpgsqlCommand(@"
            INSERT INTO layers (layer_name, layer_type, schema_json, schema_version) VALUES
              ('サンプル圃場', 'polygon',
               '{""fields"":[{""key"":""name"",""type"":""string"",""required"":true},{""key"":""crop"",""type"":""string"",""required"":false}]}'::jsonb, 1),
              ('サンプル観測点', 'point',
               '{""fields"":[{""key"":""name"",""type"":""string"",""required"":true}]}'::jsonb, 1);
            INSERT INTO layer_schema_version (layer_id, schema_version, schema_json, valid_from, valid_to, created_by)
            SELECT layer_id, schema_version, schema_json, now(), NULL, 'system' FROM layers;
        ", conn);
        await c2.ExecuteNonQueryAsync();
    }
}
```

注意点:
- `RESTART IDENTITY CASCADE` で feature_id / history_id / audit_id の連番が毎テストで 1 から
- `feature_current.layer_id` の FK 制約から `layers` を消すと CASCADE で吹っ飛ぶので順序注意

## テスト観点
- 自身: DbReset を 2 回呼んでもエラーにならない
