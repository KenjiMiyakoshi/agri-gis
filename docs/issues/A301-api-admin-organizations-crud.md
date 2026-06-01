# A301: /api/admin/organizations CRUD

| 項目 | 値 |
|---|---|
| Phase | AdminCrud |
| Estimate | 1d |
| Depends on | A101, A202, A206 |
| Blocks | A506 |

## 概要
組織の CRUD エンドポイント `/api/admin/organizations` を実装する。論理削除、部分 UNIQUE INDEX 利用、admin ロール限定。

## 背景・目的
採択案「案 P」の Admin CRUD セクション:
> `/api/admin/organizations` (CRUD)
> 論理削除 (`deleted_at`) + 部分 UNIQUE INDEX (`WHERE deleted_at IS NULL`)

## スコープ
### 含む
- `GET /api/admin/organizations` (list): `?include_deleted=true` で論理削除済も含める
- `GET /api/admin/organizations/{id}`
- `POST /api/admin/organizations` body: `{ name, code }` → 201 + Location
- `PUT /api/admin/organizations/{id}` body: `{ name, code }`
- `DELETE /api/admin/organizations/{id}` 論理削除（`UPDATE SET deleted_at = now()`）
- すべて `RequireAuthorization("AdminOnly")`
- code 重複時は 409 ProblemDetails (DB の 23505 をマップ)
- 削除済 org への参照 (users.org_id) がある場合 DELETE は 409 (FK ON DELETE RESTRICT で防御)

### 含まない
- 物理削除（Phase B 以降、必要なら）
- 組織階層 (parent_id 等)

## 受け入れ条件 (Acceptance Criteria)
- [ ] admin role でフル CRUD
- [ ] general/guest role で全エンドポイント 403
- [ ] anonymous で 401
- [ ] 同一 code を POST → 409
- [ ] 論理削除後、同一 code を POST → 201（部分 UNIQUE が効く）
- [ ] 削除済 org にひもづく user がある状態で DELETE → 409 (FK restrict) または policy で 400
- [ ] `?include_deleted=true` を付けない list は deleted_at IS NULL のみ

## 影響ファイル
- `D:\proj\agri-gis\api\Admin\AdminOrganizationsEndpoints.cs` (新規)
- `D:\proj\agri-gis\api\Admin\AdminDtos.cs` (新規: OrganizationDto, CreateOrganizationRequest, etc.)
- `D:\proj\agri-gis\api\Program.cs` (MapGroup 登録)

## 実装ノート
```csharp
public static class AdminOrganizationsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/organizations").RequireAuthorization("AdminOnly");

        g.MapGet("/", async (NpgsqlConnection db, bool include_deleted = false) =>
        {
            var sql = include_deleted
                ? "SELECT id, name, code, deleted_at, created_at, updated_at FROM organizations ORDER BY id"
                : "SELECT id, name, code, deleted_at, created_at, updated_at FROM organizations WHERE deleted_at IS NULL ORDER BY id";
            return Results.Ok(await db.QueryAsync(sql));
        });

        g.MapPost("/", async (CreateOrganizationRequest req, NpgsqlConnection db) =>
        {
            try
            {
                var id = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO organizations(name, code) VALUES (@n, @c) RETURNING id",
                    new { n = req.name, c = req.code });
                return Results.Created($"/api/admin/organizations/{id}", new { id, req.name, req.code });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Problem("code already exists", statusCode: 409);
            }
        });

        g.MapPut("/{id:int}", async (int id, UpdateOrganizationRequest req, NpgsqlConnection db) =>
        {
            var rows = await db.ExecuteAsync(@"
                UPDATE organizations SET name=@n, code=@c, updated_at=now()
                WHERE id=@i AND deleted_at IS NULL", new { i = id, n = req.name, c = req.code });
            return rows == 0 ? Results.NotFound() : Results.NoContent();
        });

        g.MapDelete("/{id:int}", async (int id, NpgsqlConnection db) =>
        {
            try
            {
                var rows = await db.ExecuteAsync(
                    "UPDATE organizations SET deleted_at=now() WHERE id=@i AND deleted_at IS NULL",
                    new { i = id });
                return rows == 0 ? Results.NotFound() : Results.NoContent();
            }
            catch (PostgresException ex) when (ex.SqlState == "23503")
            {
                return Results.Problem("organization has users", statusCode: 409);
            }
        });
    }
}
```

注意点:
- 論理削除は UPDATE deleted_at だが FK ON DELETE RESTRICT は物理削除の話。論理削除でも users.org_id 残存ユーザがいる場合は **業務ルール上拒否**するかは要決定。本イシューでは「論理削除 OK、ただしひもづく生 user がいると 409」のガードを SQL で実装:

```sql
-- 削除前ガード
IF EXISTS (SELECT 1 FROM users WHERE org_id = @i AND deleted_at IS NULL) THEN
    -- 409 へ
END IF;
```

## テスト観点
- A506 (AdminOrgsCrudTests):
  - admin で CRUD 一通り
  - general/guest で 403
  - 論理削除済 code 再利用
  - users あり状態で DELETE → 409
