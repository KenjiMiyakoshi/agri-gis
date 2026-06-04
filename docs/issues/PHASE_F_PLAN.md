# Phase F Plan — 課題分析と採用案

Phase F 着手前の課題分析と採用案。Phase D'/E'/C' 流儀踏襲。

## 0. 出発点

ユーザー要望:
> 現状は表示レイヤを切り替えるとそのレイヤのみ表示されるが、複数のレイヤを重ねて表示したい。なので、レイヤを切り替えるのではなく、レイヤごとに表示のオンオフができるようにしたい。また、複数の組織のユーザが同時利用することを見据えているため、ユーザ情報には組織という情報も持たせる必要があり、組織ごとにどのレイヤを表示可能か、編集可能かを管理者は設定できる必要があります。その組織に所属しているユーザが複数いる場合は、表示可能、編集可能なレイヤは同じです。組織に対してのレイヤ権限の設定ができるUIも必要です。

3 つの機能要件:
1. 複数レイヤの同時表示 (UI + WebGIS)
2. 組織ベースのレイヤアクセス制御 (DB + API)
3. 組織×レイヤ権限管理 UI (admin 向け)

複数組織で同時運用する本格運用前提。

## 1. 課題 1: 複数レイヤの同時表示

### 1.1 現状

- `MainForm.layerCombo` (`ComboBox.DropDownStyle = DropDownList`) で **単一選択**
- `OnLayerComboChanged` → `bridge.Send("layer_select", new { layerId })`
- WebGIS 側 `MapContext.baseLayer: TileLayer<XYZ>` は **単一**、`setSource` で 1 レイヤだけ表示

### 1.2 採用案

**案 A: `CheckedListBox` + WebGIS `MapContext.layerStack: Map<layerId, TileLayer>`**

- WinForms: `layerCombo` を `CheckedListBox` (`layerList`) に置換
- `ItemCheck` イベントで `bridge.Send("layer_visibility_change", new { layerId, visible })`
- WebGIS:
  - `MapContext.layerStack: Map<number, TileLayer<XYZ>>` 追加
  - `addLayer(ctx, layerId, theme)` / `removeLayer(ctx, layerId)` / `setLayerVisible(ctx, layerId, visible)`
  - 既存 `setBaseLayerSource` は段階的に deprecate
- 表示状態は `MainFormController.VisibleLayerIds: HashSet<int>` で管理、起動時復元

落選:
- 案 B: `DataGridView` with checkbox: 編集系 UI と混同しがち、CheckedListBox の方がシンプル
- 案 C: タブで 1 レイヤずつ独立表示: 「重ねて表示」要件を満たさない

### 1.3 影響範囲

- DB: なし (権限は別課題)
- API: なし (tile 経路は既存)
- WinForms: `MainForm.cs/Designer.cs` (CheckedListBox 化)、`MainFormController.cs` (VisibleLayerIds 管理)
- WebGIS: `mapInit.ts` (layerStack)、`controllers/layer.ts` (addLayer/removeLayer/setLayerVisible)、`bridge/messages.ts` (envelope 追加)

## 2. 課題 2: 組織ベースのレイヤアクセス制御

### 2.1 現状

- `users.org_id INT NOT NULL REFERENCES organizations(id)` (Phase A)
- `GET /api/layers` は **org フィルタ無し**、全 active layer を返す
- `POST /api/features` の認可は `WriteFeature` policy (admin/general role 必須) のみ、**layer 別の権限 check 無し**
- `/tiles/{layerId}/...` も role check のみ

### 2.2 採用案

**案 A: `org_layer_permission(org_id, layer_id, can_view, can_edit)` テーブル + admin filter bypass**

DDL:
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

- 単純 PK + CHECK 制約で edit > view の整合性保証
- バイテンポラル無し (Phase F では履歴管理不要、audit_log で代替)
- 既存 layer は backfill で全組織に `can_view=true / can_edit=false` 配布 (admin 所属 org のみ `can_edit=true`)

認可ヘルパ (`api/Auth/LayerPermissionService.cs`):
```csharp
public interface ILayerPermissionService {
    Task<bool> CanViewAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct);
    Task<bool> CanEditAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct);
}
```
- admin role は無条件 true (filter bypass)
- それ以外は `org_layer_permission` 参照

落選:
- 案 B: ロール拡張 (`per_layer_admin` 等): 権限を role 名で表現すると組み合わせ爆発
- 案 C: PostgreSQL Row Level Security (RLS): 強力だが Phase F の layer 粒度には過剰、Phase G の feature 粒度で本格採用

### 2.3 影響範囲

- DB: `0F03_org_layer_permission.sql` (新規) + 既存 layers × organizations の backfill
- API:
  - `LayerEndpoints.cs` GET / の SQL に JOIN 追加
  - `AdminOrgLayerPermissionsEndpoints.cs` 新規 (GET + PUT)
  - `LayerPermissionService.cs` 新規 + DI 登録
  - `FeatureEndpoints.cs` POST/PATCH/DELETE で can_edit 検査
  - `TilesEndpoints.cs` で can_view 検査
- DTO: `LayerDto.CanEdit` (bool) 追加、`OrgLayerPermissionDto` 新規

## 3. 課題 3: 組織×レイヤ権限管理 UI

### 3.1 採用案

**案 A: `OrgPermissionsForm` 新規 (組織 ComboBox + DataGridView)**

UI 構成:
```
┌── 組織×レイヤ権限管理 ─────────────────────┐
│  組織: [▼ 既定組織 ▼]            [再読込]   │
│  ┌───────────────────────────────────────┐ │
│  │ Id │ レイヤ名     │ Type  │ 閲覧 │ 編集 │ │
│  │ 1  │ 圃場         │ poly  │ ☑    │ ☑    │ │
│  │ 2  │ 道路         │ line  │ ☑    │ ☐    │ │
│  │ 3  │ 観測点       │ point │ ☐    │ ☐    │ │
│  └───────────────────────────────────────┘ │
│            [保存]  [閉じる]                  │
└─────────────────────────────────────────────┘
```

- `LayerAdminForm` に「権限管理...」ボタン追加 (admin role のみ Visible)
- `DataGridViewCheckBoxColumn` × 2 (canView/canEdit)
- `CellValueChanged` で `edit=true` 時に `view=true` 強制 (UI 側で CHECK 制約を先取り、ユーザーフレンドリー)
- 保存時 BindingList → `PUT /api/admin/organizations/{orgId}/layer-permissions` でバルク upsert

### 3.2 影響範囲

- WinForms: `OrgPermissionsForm.cs/Designer.cs` 新規、`LayerAdminForm.cs` ボタン追加
- API: `AdminOrgLayerPermissionsEndpoints.cs` (PUT/GET)
- DTO: `OrgLayerPermissionDto`, `OrgLayerPermissionsUpsertDto`

## 4. 5 件まとめての一貫性 (深層防御)

「複数表示 + 権限」の組み合わせで重要:
- (1) WebGIS が DOM 操作で勝手にレイヤを追加するシナリオでも tile が漏洩しない (F205)
- (2) UI で view=false のレイヤを表示しようとしても WebGIS 側 fetch エラー → 一覧から自動除外 (F401)
- (3) 編集権限剥奪後の編集操作は API で 403 (F204)
- (4) `LayerDto.canEdit` を WinForms/WebGIS が見て編集 UI の有効/無効を制御 (F302/F405)

## 5. Tile cache invalidation 課題 (申し送り)

権限変更時:
- 既に WebGIS の WebView2 キャッシュに格納された tile が残る (`Cache-Control: max-age=86400, immutable`)
- 一時的に「権限剥奪したのに古い tile が見える」状態が発生
- Phase F では受容、Phase F' で SSE invalidation か timestamp cache buster で対応

## 6. 残課題 (Phase F' / Phase G 候補)

### Phase F' 候補
- レイヤ z-order ドラッグ並べ替え UI
- SSE 単一 connection 統合 (`/api/events/stream-all`)
- tile cache invalidation on permission change
- 「共有レイヤ」 (`is_shared=true`) の細粒度設定
- バルク権限編集 (複数組織まとめて)

### Phase G 候補
- **feature-level RLS** (PostgreSQL Row Level Security): 異組織の feature が地理的に重なるケース対応
- マルチテナント完全分離 (DB スキーマ分離 or テナント毎 DB)
- 共有レイヤの細粒度権限 (組織グループ単位)

## 関連

- `PHASE_F_INDEX.md`
- `PHASE_F_WAVE_PLAN.md`
- `PHASE_F_ISSUES_INDEX.md`
