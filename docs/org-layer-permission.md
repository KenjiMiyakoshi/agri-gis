# org_layer_permission Design (Phase F F1 + F2)

組織 (organizations) × レイヤ (layers) の閲覧/編集権限を管理する仕組み。

## 背景

現状 (Phase E' 完了時点):
- 認証: JWT + 3 ロール (admin/general/guest) — Phase A
- `users.org_id` 必須 — Phase A
- `GET /api/layers` は org フィルタ無し、全 active layer を返す
- `POST /api/features` の認可は WriteFeature policy (admin/general) のみ、layer 別チェック無し

ユーザー要望: 「組織ごとにどのレイヤを表示可能か、編集可能かを管理者が設定」「同じ組織のユーザは同権限」

## 採用方針

### 1. DB: `org_layer_permission` 単純テーブル

```sql
CREATE TABLE org_layer_permission (
    org_id     INTEGER NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    layer_id   INTEGER NOT NULL REFERENCES layers(layer_id)  ON DELETE CASCADE,
    can_view   BOOLEAN NOT NULL DEFAULT false,
    can_edit   BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (org_id, layer_id),
    CHECK (NOT (can_edit AND NOT can_view))  -- edit 可なら view 可も必須
);
CREATE INDEX ix_org_layer_perm_layer ON org_layer_permission(layer_id);
CREATE INDEX ix_org_layer_perm_view  ON org_layer_permission(org_id) WHERE can_view;
```

### 2. バイテンポラル無し

Phase A/E イディオム (`valid_from/valid_to` 半開区間 + history) は **採用しない**。

理由:
- 権限は「現時点で誰が何できるか」が重要、過去の権限状態を asOf で取りたい要件は無い
- 履歴は `audit_log.action='org_layer_perm_upsert'` で十分
- asOf クエリは feature/layer 単位、権限まで遡る意味は低い

### 3. 既存 layer の backfill

```sql
INSERT INTO org_layer_permission (org_id, layer_id, can_view, can_edit)
SELECT o.id, l.layer_id,
       true AS can_view,
       CASE WHEN admin_user_exists_in_org(o.id) THEN true ELSE false END AS can_edit
FROM organizations o, layers l
WHERE l.valid_to = '9999-12-31'::date
ON CONFLICT (org_id, layer_id) DO NOTHING;
```

- デフォルト `can_view=true` で「既存運用との互換性」確保
- admin 所属 org のみ `can_edit=true` (一般組織は閲覧のみで開始、後から admin が設定)

### 4. fn_org_layer_perm_upsert 関数

```sql
CREATE OR REPLACE FUNCTION fn_org_layer_perm_upsert(
    p_org_id     INT,
    p_layer_id   INT,
    p_can_view   BOOLEAN,
    p_can_edit   BOOLEAN,
    p_actor      TEXT,
    p_request_id TEXT,
    p_user_id    UUID,
    p_org_id_act INT
) RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_before JSONB;
    v_after  JSONB;
BEGIN
    -- CHECK 制約の事前検査 (UI 側で補正済の前提だが二重防御)
    IF p_can_edit AND NOT p_can_view THEN
        RAISE EXCEPTION 'can_edit requires can_view' USING ERRCODE = '23514';
    END IF;

    SELECT to_jsonb(p.*) INTO v_before
      FROM org_layer_permission p
     WHERE p.org_id = p_org_id AND p.layer_id = p_layer_id;

    INSERT INTO org_layer_permission (org_id, layer_id, can_view, can_edit, updated_at)
    VALUES (p_org_id, p_layer_id, p_can_view, p_can_edit, now())
    ON CONFLICT (org_id, layer_id)
    DO UPDATE SET
        can_view = EXCLUDED.can_view,
        can_edit = EXCLUDED.can_edit,
        updated_at = now();

    SELECT to_jsonb(p.*) INTO v_after
      FROM org_layer_permission p
     WHERE p.org_id = p_org_id AND p.layer_id = p_layer_id;

    INSERT INTO audit_log (
        actor, actor_user_id, actor_org_id, action, target_table,
        layer_id, entity_id, feature_id,
        before_doc, after_doc, request_id
    ) VALUES (
        p_actor, p_user_id, p_org_id_act, 'org_layer_perm_upsert', 'org_layer_permission',
        p_layer_id, NULL, NULL,
        v_before, v_after, p_request_id
    );
END;
$$;
```

## 5. API: 認可サービス + endpoint

### `ILayerPermissionService`

```csharp
public interface ILayerPermissionService
{
    Task<bool> CanViewAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct);
    Task<bool> CanEditAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct);
    Task<IReadOnlyDictionary<int, (bool CanView, bool CanEdit)>> GetForOrgAsync(int orgId, CancellationToken ct);
}

public sealed class LayerPermissionService : ILayerPermissionService
{
    public async Task<bool> CanViewAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (roles.Contains("admin")) return true;  // filter bypass
        await using var cmd = _db.CreateCommand(@"
            SELECT can_view FROM org_layer_permission
             WHERE org_id = @oid AND layer_id = @lid");
        cmd.Parameters.AddWithValue("oid", orgId);
        cmd.Parameters.AddWithValue("lid", layerId);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is true;
    }
    // ...
}
```

### `GET /api/layers` (org フィルタ)

```csharp
group.MapGet("/", async (string? asOf, ICurrentUser user, NpgsqlDataSource db) =>
{
    var isAdmin = user.HasRole("admin");
    string sql = isAdmin
        ? @"SELECT l.*, true AS can_edit FROM layers l WHERE l.valid_to = '9999-12-31'::date"
        : @"SELECT l.*, p.can_edit
              FROM layers l
              JOIN org_layer_permission p
                ON p.layer_id = l.layer_id AND p.org_id = @uOrgId AND p.can_view
             WHERE l.valid_to = '9999-12-31'::date";
    // ...
});
```

### `POST /api/features` (can_edit 検査)

```csharp
group.MapPost("/", async (CreateFeatureRequestDto req, ICurrentUser user,
                          ILayerPermissionService perm, ...) =>
{
    if (!await perm.CanEditAsync(user.OrgId, req.LayerId, user.Roles, ct))
        return Results.Forbid();
    // ...
});
```

### `/tiles/{layerId}` (深層防御)

```csharp
group.MapGet("/{layerId:int}/{theme}/...", async (int layerId, ...,
                                                   ICurrentUser user,
                                                   ILayerPermissionService perm, ...) =>
{
    if (!await perm.CanViewAsync(user.OrgId, layerId, user.Roles, ct))
        return Results.Forbid();
    // GeoServer proxy
});
```

### `AdminOrgLayerPermissionsEndpoints` (新規)

```csharp
[RequireRole("admin")]
group.MapGet("/{orgId:int}/layer-permissions", async (int orgId, NpgsqlDataSource db) =>
{
    const string sql = @"
        SELECT p.org_id, p.layer_id, l.layer_name, l.layer_type,
               p.can_view, p.can_edit
          FROM org_layer_permission p
          JOIN layers l ON l.layer_id = p.layer_id
         WHERE p.org_id = @oid
           AND l.valid_to = '9999-12-31'::date
         ORDER BY p.layer_id";
    // ...
});

group.MapPut("/{orgId:int}/layer-permissions",
    async (int orgId, OrgLayerPermsUpsertDto req, ICurrentUser user,
           NpgsqlDataSource db, ...) =>
{
    // バルク upsert (1 トランザクション)
    foreach (var p in req.Permissions) {
        await fn_org_layer_perm_upsert(orgId, p.LayerId, p.CanView, p.CanEdit, ...);
    }
});
```

## 6. DTO

```csharp
public sealed record LayerDto(
    int LayerId, string LayerName, string LayerType,
    int? OwnerOrgId, bool IsShared,
    DateTimeOffset CreatedAt, int SchemaVersion, LayerSchemaDto Schema,
    int StyleVersion,
    bool CanEdit  // F201 で追加
);

public sealed record OrgLayerPermissionDto(
    int OrgId, int LayerId, string LayerName, string LayerType,
    bool CanView, bool CanEdit
);

public sealed record OrgLayerPermsUpsertDto(
    IReadOnlyList<OrgLayerPermItemDto> Permissions
);

public sealed record OrgLayerPermItemDto(
    int LayerId, bool CanView, bool CanEdit
);
```

## 7. バイテンポラル × 権限の挙動 (重要)

- 権限は **現時点のみ** で評価
- `GET /api/layers?asOf=2025-01-01` の場合も、現在の org_layer_permission を参照
- 過去の権限状態は audit_log で監査可能 (asOf クエリには含めない)
- 理由: 過去時点 layer の閲覧/編集は「歴史閲覧」用途、権限まで遡る意味が低い

## 受入条件

1. ✅ `org_layer_permission` テーブル作成、CHECK 制約動作
2. ✅ backfill 後 `organizations × layers` 件数の行
3. ✅ admin 所属 org の全 layer が `can_edit=true`
4. ✅ general user の `GET /api/layers` が `can_view=true` のみ返却
5. ✅ admin の `GET /api/layers` は全件、`can_edit=true`
6. ✅ `POST /api/features` で `can_edit=false` のレイヤに 403
7. ✅ `GET /tiles/{layerId}/...` で `can_view=false` のレイヤに 403
8. ✅ `PUT /api/admin/organizations/{orgId}/layer-permissions` でバルク upsert + audit_log

## テスト

- `LayerPermissionServiceTests`: admin bypass + non-admin orgId filter + can_view/can_edit 取得
- `LayersEndpointsOrgFilterTests`: 3 ケース (general 制限 / admin 全件 / canEdit 反映)
- `FeatureEndpointsCanEditTests`: 3 ケース (can_edit=false で 403 / can_edit=true で 201/200 / admin bypass)
- `TilesEndpointsCanViewTests`: 3 ケース (can_view=false で 403 / can_view=true で 200 / admin bypass)
- `AdminOrgLayerPermissionsEndpointsTests`: 3 ケース (GET + PUT upsert + non-admin で 403)

## 関連

- `PHASE_F_INDEX.md`
- `multi-layer-display.md` (F3/F4 Design)
- メモリ `bitemporal_audit.md` (Phase A/E イディオム、ここでは採用せず)
