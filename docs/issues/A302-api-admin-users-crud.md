# A302: /api/admin/users CRUD + パスワード変更分離

| 項目 | 値 |
|---|---|
| Phase | AdminCrud |
| Estimate | 1d |
| Depends on | A101, A202, A206, A301 |
| Blocks | A506 |

## 概要
ユーザの CRUD エンドポイント `/api/admin/users` を実装する。パスワード変更は別エンドポイント `PUT /api/admin/users/{userId}/password` に分離。

## 背景・目的
採択案「案 P」の Admin CRUD セクション:
> `/api/admin/users` (CRUD)
> パスワード変更は別エンドポイント: `PUT /api/admin/users/{userId}/password`
> 論理削除 (`deleted_at`) + 部分 UNIQUE INDEX (`WHERE deleted_at IS NULL`)

## スコープ
### 含む
- `GET /api/admin/users` list（`?include_deleted=true`、`?org_id=`）
- `GET /api/admin/users/{userId}`
- `POST /api/admin/users` body: `{ login_id, display_name, password, org_id, roles[] }` → 201
- `PUT /api/admin/users/{userId}` body: `{ login_id, display_name, org_id, roles[] }`（password 含まない）
- `PUT /api/admin/users/{userId}/password` body: `{ new_password }` → 204
- `DELETE /api/admin/users/{userId}` 論理削除
- すべて `RequireAuthorization("AdminOnly")`
- roles 配列は `admin|general|guest` のみ許容、CRUD で user_roles を INSERT/DELETE で同期
- login_id 重複時は 409
- BCrypt work factor 11

### 含まない
- 本人パスワード変更 `/api/auth/change-password` (A205)
- self-delete 防止（採択案に明記なし、ただし実装ノートで言及）

## 受け入れ条件 (Acceptance Criteria)
- [ ] admin role でフル CRUD + パスワード変更
- [ ] general/guest で全エンドポイント 403
- [ ] POST: login_id 重複 → 409
- [ ] POST: roles に `viewer` 等の無効値 → 400
- [ ] POST: org_id 不在 → 400 (FK violation)
- [ ] PUT password: 8 文字未満 → 400
- [ ] PUT password: 新 hash が DB に反映、login で確認可能
- [ ] DELETE: 論理削除、同一 login_id 再利用可能
- [ ] roles 更新は `user_roles` を DELETE + INSERT で同期

## 影響ファイル
- `D:\proj\agri-gis\api\Admin\AdminUsersEndpoints.cs` (新規)
- `D:\proj\agri-gis\api\Admin\AdminDtos.cs` (UserDto, CreateUserRequest, UpdateUserRequest, ResetPasswordRequest を追加)
- `D:\proj\agri-gis\api\Program.cs`

## 実装ノート
```csharp
public static class AdminUsersEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/users").RequireAuthorization("AdminOnly");

        g.MapPost("/", async (CreateUserRequest req, NpgsqlConnection db) =>
        {
            ValidateRoles(req.roles);
            if (req.password is null || req.password.Length < 8)
                return Results.Problem("password >= 8 chars", statusCode: 400);
            await db.OpenAsync();
            using var tx = await db.BeginTransactionAsync();
            try
            {
                var hash = BCrypt.Net.BCrypt.HashPassword(req.password, 11);
                var userId = await db.ExecuteScalarAsync<Guid>(@"
                    INSERT INTO users(login_id, display_name, password_hash, org_id)
                    VALUES (@l, @d, @h, @o) RETURNING user_id",
                    new { l = req.login_id, d = req.display_name, h = hash, o = req.org_id });
                foreach (var r in req.roles.Distinct())
                    await db.ExecuteAsync(
                        "INSERT INTO user_roles(user_id, role) VALUES (@u, @r)",
                        new { u = userId, r });
                await tx.CommitAsync();
                return Results.Created($"/api/admin/users/{userId}",
                    new { user_id = userId, req.login_id, req.display_name, req.org_id, req.roles });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await tx.RollbackAsync();
                return Results.Problem("login_id already exists", statusCode: 409);
            }
        });

        g.MapPut("/{userId:guid}/password",
            async (Guid userId, ResetPasswordRequest req, NpgsqlConnection db) =>
        {
            if (req.new_password is null || req.new_password.Length < 8)
                return Results.Problem("new_password >= 8 chars", statusCode: 400);
            var hash = BCrypt.Net.BCrypt.HashPassword(req.new_password, 11);
            var rows = await db.ExecuteAsync(
                "UPDATE users SET password_hash=@h, updated_at=now() WHERE user_id=@u AND deleted_at IS NULL",
                new { h = hash, u = userId });
            return rows == 0 ? Results.NotFound() : Results.NoContent();
        });

        // GET list / GET one / PUT / DELETE 同様
    }

    private static void ValidateRoles(IEnumerable<string> roles)
    {
        var allowed = new HashSet<string> { "admin", "general", "guest" };
        if (roles.Any(r => !allowed.Contains(r)))
            throw new BadHttpRequestException("invalid role");
    }
}
```

注意点:
- self-delete (admin が自分を消す) を防ぐかどうかは採択案に明記なし。本イシューでは「ICurrentUser.UserId == 削除対象」なら 400 を返す軽い防御を入れる。
- roles 配列の DELETE + INSERT は transaction で行う

## テスト観点
- A506 (AdminUsersCrudTests):
  - admin で CRUD + password 変更
  - general/guest で 403
  - 重複 login_id 409
  - 論理削除後 login_id 再利用
  - 無効 role 400
  - password 8 文字未満 400
  - 新 password で login 成功 → A205 と組み合わせ
