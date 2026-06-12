# Tile Invalidation on Permission Change Design (Phase F' F'2)

権限変更時に WebGIS の TileLayer を即時 invalidate するセキュリティ穴埋め設計。

## 背景

Phase F WF2 で実装した tile 認可 (`/tiles/{layerId}` の can_view 検査) は **新規 tile fetch 時のみ** 効く。既に WebGIS の OL タイルキャッシュ (`Cache-Control: max-age=86400, immutable`) に乗っている tile は、権限剥奪後も 24h or 再ログインまで表示され続ける。

セキュリティ的な穴:
- admin が salesA から layer 1 の view 権限を剥奪
- salesA の WebGIS は layer 1 の tile を画面に表示し続ける (cache hit)
- salesA は layer 1 の図形を視覚的に閲覧できてしまう (Phase F 受け入れ条件 §5 違反相当)

## 採用方針

### 1. 即時通知経路

```
admin → PUT /api/admin/organizations/{orgId}/layer-permissions
     → fn_org_layer_perm_upsert (DB)
     → broker.PublishPermissionInvalidate(orgId, affectedLayerIds[])
     → SSE: event layer_invalidate { reason: 'permission', layerId, affectedOrgId: orgId }
     → 該当 org に所属する user の WebGIS が受信
     → fetchLayers + layerStack 再構成
```

### 2. WebGIS 側の処理

```typescript
async function handlePermissionInvalidate(ev: LayerInvalidationEvent): Promise<void> {
  if (ev.reason !== 'permission') return;
  // 自分が affectedOrgId の org に所属しているかは JWT claim 経由でない。
  // 送信側 (API) が org 単位で filter しているので、受信した時点で「自分宛」と判定する。
  // すべての visible layer について再判定する。
  const myLayers = await fetchLayers();  // 現在の許可 layer list
  const myLayerIds = new Set(myLayers.map(l => l.layerId));
  const visibleIds = getVisibleLayerIds(ctx);
  for (const lid of visibleIds) {
    if (!myLayerIds.has(lid)) {
      // 許可されなくなった layer
      stopEventStreamFor(lid);  // (旧 endpoint 互換)
      removeLayer(ctx, lid);
    } else {
      // 残った layer は source 再生成 (cache flush)
      const theme = ctx.themeByLayer.get(lid) ?? 'default';
      const sv = myLayers.find(l => l.layerId === lid)?.styleVersion ?? null;
      addLayer(ctx, lid, theme, ctx.currentAsOf, sv);
    }
  }
  // WinForms 側にも通知 (CheckedListBox 再構成)
  sendToHost({ type: 'permission_changed', payload: {} });
}
```

### 3. API 側の broker publish

```csharp
// AdminOrgLayerPermissionsEndpoints PUT (F'401)
foreach (var p in req.Permissions!)
{
    // upsert (既存)
    await fn_org_layer_perm_upsert(...);
}
await tx.CommitAsync();

// F'401: tx commit 後に broker publish
var changedLayerIds = req.Permissions.Select(p => p.LayerId).Distinct().ToList();
broker.PublishPermissionInvalidate(orgId, changedLayerIds);
```

### 4. WinForms 側の対応

`bridge messages.ts` に Web → Host envelope を追加:

```typescript
export interface PermissionChangedPayload {}
```

WinForms `MainForm.OnBridgeMessage` で受領 → `ReloadLayersAsync()` を呼んで CheckedListBox 再構成。

### 5. 影響範囲のスコープ判定

- `PublishPermissionInvalidate(orgId, layerIds)` は org 単位で配信される
- 該当 org に所属する全 user の SSE channel に届く
- user 単位の細粒度判定 (どの layer が増減したか) は WebGIS 側で `fetchLayers` 再取得で決定する

## 6. パフォーマンス

- 権限変更頻度: admin 操作のみ。1 日数回〜数十回想定
- 影響範囲: 該当 org の active user 数 (通常 10〜100 程度)
- 同期処理: broker.Publish は非同期 (`Channel.Writer.TryWrite`)、PUT レスポンスは即時返却

## 7. テスト

- `PermissionChangeBroadcastTests` (api.tests): PUT → broker.Publish 呼ばれる + 配信 event の payload 確認
- `eventStreamMulti.test.ts` (webgis): `permission_invalidate` 受領 → fetchLayers + removeLayer 呼ばれる

## 関連

- `sse-multiplex.md`
- `PHASE_F_COMPLETE.md` §「Phase F' 申し送り」
