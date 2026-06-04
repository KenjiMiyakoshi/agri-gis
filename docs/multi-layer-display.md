# 複数レイヤ同時表示 Design (Phase F F3 + F4)

WinForms + WebGIS で複数レイヤをオン/オフ切替して同時表示する。

## 背景

現状 (Phase E' 完了時点):
- WinForms: `MainForm.layerCombo` (`ComboBox.DropDownStyle = DropDownList`) で **単一選択**
- WebGIS: `MapContext.baseLayer: TileLayer<XYZ>` は **単一**、`setSource` で 1 レイヤだけ表示
- bridge: `layer_select` envelope で「現在の表示レイヤ」を 1 つだけ通知

ユーザー要望: 「レイヤごとに表示のオン/オフ」「複数のレイヤを重ねて表示」

## 採用方針

### 1. WinForms: `CheckedListBox` 化

```csharp
// Designer.cs
private CheckedListBox layerList = null!;

layerList.CheckOnClick = true;  // 1 クリックでチェック切替
layerList.ItemCheck += OnLayerListItemCheck;
```

```csharp
// MainForm.cs
private void OnLayerListItemCheck(object? sender, ItemCheckEventArgs e)
{
    var layers = _controller.Layers;
    if (e.Index < 0 || e.Index >= layers.Count) return;
    var layer = layers[e.Index];
    var visible = e.NewValue == CheckState.Checked;
    _controller.SetLayerVisibility(layer.LayerId, visible);
    _bridge?.Send("layer_visibility_change", new {
        layerId = layer.LayerId,
        visible,
        theme = "default",
        styleVersion = layer.StyleVersion
    });
}
```

### 2. WinForms: `MainFormController.VisibleLayerIds`

```csharp
public sealed class MainFormController
{
    private readonly HashSet<int> _visibleLayerIds = new();

    public IReadOnlySet<int> VisibleLayerIds => _visibleLayerIds;

    public void SetLayerVisibility(int layerId, bool visible)
    {
        if (visible) _visibleLayerIds.Add(layerId);
        else _visibleLayerIds.Remove(layerId);
    }

    // 起動時復元 (registry or local json)
    public async Task RestoreVisibilityAsync() { ... }
    public async Task PersistVisibilityAsync() { ... }
}
```

### 3. WebGIS: `MapContext.layerStack`

```typescript
// mapInit.ts
export interface MapContext {
  map: Map;
  view: View;
  // F401 (WF4): 単一 baseLayer → 複数管理に変更
  layerStack: Map<number, TileLayer<XYZ>>;
  selectionLayer: TileLayer<XYZ>;  // 既存
  currentAsOf: string | null;
  // F401: layer 単位の状態管理
  layerStates: Map<number, { theme: string; styleVersion: number | null }>;
}
```

```typescript
// controllers/layer.ts
export function addLayer(ctx: MapContext, layerId: number,
                         theme: string, styleVersion: number | null): void {
  if (ctx.layerStack.has(layerId)) {
    // 既存ならスタイル更新のみ
    const existing = ctx.layerStack.get(layerId)!;
    // setSource で URL 再構築
    return;
  }
  const url = buildTileUrl(layerId, theme, ctx.currentAsOf, styleVersion);
  const source = new XYZ({ url, tileLoadFunction: ... });
  const tile = new TileLayer<XYZ>({ source, preload: 2 });
  ctx.map.addLayer(tile);
  ctx.layerStack.set(layerId, tile);
  ctx.layerStates.set(layerId, { theme, styleVersion });
  // 初回 layer 追加時のみ extent fit
  if (ctx.layerStack.size === 1) {
    tryFitExtent(ctx, layerId);
  }
}

export function removeLayer(ctx: MapContext, layerId: number): void {
  const tile = ctx.layerStack.get(layerId);
  if (!tile) return;
  ctx.map.removeLayer(tile);
  ctx.layerStack.delete(layerId);
  ctx.layerStates.delete(layerId);
}

export function setLayerVisible(ctx: MapContext, layerId: number, visible: boolean): void {
  const tile = ctx.layerStack.get(layerId);
  if (tile) {
    tile.setVisible(visible);
  } else if (visible) {
    // 初回追加
    const state = ctx.layerStates.get(layerId);
    addLayer(ctx, layerId, state?.theme ?? 'default', state?.styleVersion ?? null);
  }
}
```

### 4. bridge envelope 追加

```typescript
// bridge/messages.ts
export interface LayerVisibilityChangePayload {
  layerId: number;
  visible: boolean;
  theme?: string;
  styleVersion?: number;
}

// HostToWebType に 'layer_visibility_change' 追加
```

```typescript
// main.ts (WebGIS エントリ)
host.on('layer_visibility_change', (payload: LayerVisibilityChangePayload) => {
  if (payload.visible) {
    addLayer(ctx, payload.layerId, payload.theme ?? 'default', payload.styleVersion ?? null);
  } else {
    removeLayer(ctx, payload.layerId);
  }
});
```

### 5. クリックヒット判定 (複数 layer 対応)

```typescript
// controllers/layer.ts (拡張)
async function handleClick(ctx: MapContext, lon: number, lat: number): Promise<void> {
  // visible layer 全件を at API で叩く (Phase F は並列、F' で一括 API へ)
  const hits = await Promise.all(
    Array.from(ctx.layerStack.keys()).map(layerId =>
      getFeaturesAt(layerId, lon, lat, ctx.currentAsOf).then(r => ({ layerId, hits: r.hits }))
    )
  );
  // 最初の hit (上位 layer 優先) を選択
  const firstHit = hits.find(r => r.hits.length > 0);
  if (firstHit) {
    host.send('features_selected', { layerId: firstHit.layerId, entityIds: firstHit.hits.map(h => h.entityId) });
  }
}
```

### 6. AttributeEditor の編集可否 (`canEdit` 反映)

```csharp
// MainForm.cs
private void OnAttributeEditorFeatureLoaded()
{
    var layerId = attributeEditor.CurrentLayerId;
    var layer = _controller.Layers.FirstOrDefault(l => l.LayerId == layerId);
    var canEdit = layer?.CanEdit ?? false;
    var isGuest = _session.Current?.IsGuest ?? false;
    var isReadOnly = !canEdit || EditPolicy.ShouldBeReadOnly(isGuest, _asOf.IsReadOnly);
    attributeEditor.SetReadOnly(isReadOnly);
}
```

## 7. SSE 複数 layer 対応 (F404)

Phase F では layer ごとに EventSource を張る簡易実装:

```typescript
const eventStreams = new Map<number, EventSource>();

function startEventStreamFor(ctx: MapContext, layerId: number) {
  const es = createSse(`/api/events/layers/${layerId}/stream?access_token=...`);
  eventStreams.set(layerId, es);
}
```

Phase F' 申し送り: 単一 `/api/events/stream-all` で全 layer event を流す統合

## 8. UI 構成 (WinForms)

```
[右ペイン 360px]
 ┌─────────────────────────┐
 │ asOf [✓] 過去時点  [...]  │  (既存)
 │ Layer:                   │
 │ ┌─────────────────────┐ │
 │ │ ☑ 1: 圃場 (polygon)  │ │  ← CheckedListBox (layerList)
 │ │ ☐ 2: 道路 (line)     │ │
 │ │ ☑ 3: 観測点 (point)   │ │
 │ └─────────────────────┘ │
 │ ┌ AttributeEditor ────┐ │  (既存)
 │ │ (CurrentLayer:1)    │ │
 │ │ name: 田中圃場       │ │
 │ │ ...                 │ │
 │ └─────────────────────┘ │
 └─────────────────────────┘
```

### `layerList` のソート/グループ化

- 初期は API レスポンス順 (layer_id ASC)
- 将来 (F'): ドラッグで z-order 並べ替え

## 受入条件

1. ✅ WinForms 起動 → CheckedListBox に layer 一覧表示
2. ✅ チェック ON → WebGIS に TileLayer 追加 (`ctx.map.getLayers().getLength()` 増)
3. ✅ チェック OFF → TileLayer 除去
4. ✅ 複数 layer ON 時に重ね表示 (z-order は追加順)
5. ✅ canEdit=false の layer 上で AttributeEditor が read-only
6. ✅ 再起動後に VisibleLayerIds 復元

## テスト

- `MainFormControllerMultiLayerTests`: SetLayerVisibility / VisibleLayerIds / Persist+Restore
- `OrgPermissionsViewModelTests`: 組織選択 / CheckBox 変更 / save
- `layerStack.spec.ts` (vitest): addLayer / removeLayer / setLayerVisible

## 関連

- `PHASE_F_INDEX.md`
- `org-layer-permission.md` (F1/F2 Design)
- メモリ `selection_visualization_and_multi_select.md` (関連、選択 overlay と layerStack の関係)
