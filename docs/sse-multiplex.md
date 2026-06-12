# SSE Multiplex Design (Phase F' F'1)

複数 layer の invalidation event を単一 EventSource で配信する設計。

## 背景

Phase F WF4 で `eventStream.ts` を per-layer `Map<number, EventSource>` 化したが、layer 数だけ EventSource を張る。Chrome は origin あたり 6 connection 制限があり、6 layer 以上同時表示で他 fetch (tile / API) が pending する課題がある。

## 採用方針

### 1. 新 endpoint: `GET /api/events/stream-all`

```http
GET /api/events/stream-all?layerIds=1,2,3&access_token=...
Accept: text/event-stream
```

- `layerIds` クエリで購読対象を指定 (1〜N 個)
- `access_token` クエリで JWT 認証 (EventSource は Authorization ヘッダ不可)
- 1 connection で N 個の layer event を配信

### 2. Event 種別

```jsonc
event: layer_invalidate
data: {
  "layerId": 1,
  "reason": "feature" | "style" | "layer" | "permission",  // permission 追加
  "action": "insert" | "update" | "delete",                // 既存
  "styleVersion": 2,                                       // 既存
  "affectedOrgId": 3,                                      // permission のみ、影響を受ける org_id
  "occurredAt": "2026-06-12T10:30:00Z"
}
```

`reason='permission'` の event は、当該 org に所属する user 向けに「layer の閲覧/編集権限が変わった可能性がある」ことを通知する。WebGIS 側は `fetchLayers` 再取得 + 不要 layer の `removeLayer` で対応する。

### 3. 旧 endpoint の扱い

```http
GET /api/events/layers/{layerId}/stream
Sunset: Sun, 31 Dec 2026 23:59:59 GMT
Link: </api/events/stream-all>; rel="successor-version"
```

Phase G で物理削除予定。F' 期間中は両 endpoint を提供する。

### 4. Broker 実装

`ILayerInvalidationBroker` に以下を追加:

```csharp
public interface ILayerInvalidationBroker
{
    // 既存: per-layer subscription
    IAsyncEnumerable<LayerInvalidationEvent> SubscribeAsync(int layerId, CancellationToken ct);

    // F'102 (Phase F' WF'1): 複数 layer をまとめて購読
    IAsyncEnumerable<LayerInvalidationEvent> SubscribeMultiAsync(
        IReadOnlyList<int> layerIds, CancellationToken ct);

    // 既存: feature/style/layer 編集の publish
    void Publish(LayerInvalidationEvent ev);

    // F'104 (Phase F' WF'1): 権限変更の publish
    void PublishPermissionInvalidate(int orgId, IReadOnlyList<int> affectedLayerIds);
}
```

`PostgresLayerInvalidationBroker` 実装:
- 既存の `Channel<LayerInvalidationEvent>` を per-subscriber で持つ
- `SubscribeMultiAsync` は内部で `layerIds.Count` 個の per-layer subscription を 1 つの Channel に fan-in
- `PublishPermissionInvalidate` は `affectedLayerIds.Length` 個の `LayerInvalidationEvent { reason='permission', layerId, affectedOrgId }` を publish

## 5. WebGIS 側

```typescript
// 単一 EventSource で複数 layer を購読
let currentSource: EventSource | null = null;
let subscribedLayerIds: number[] = [];

export function subscribeLayers(ctx: MapContext, layerIds: number[]): void {
  // 同じ集合なら no-op
  if (sameSet(subscribedLayerIds, layerIds)) return;
  stopAllEventStreams();
  if (layerIds.length === 0) return;
  const token = getCurrentAccessToken();
  if (!token) return;
  const qs = `layerIds=${layerIds.join(',')}&access_token=${encodeURIComponent(token)}`;
  currentSource = new EventSource(`/api/events/stream-all?${qs}`);
  currentSource.addEventListener('layer_invalidate', handleInvalidate);
  subscribedLayerIds = [...layerIds];
}
```

`main.ts` の `layer_visibility_change` handler から `subscribeLayers(ctx, getVisibleLayerIds(ctx))` を呼ぶ。

## 6. 認可

`/api/events/stream-all` は `[Authorize]`、加えて `layerIds` の各 layer について `ILayerPermissionService.CanViewAsync` を検査。1 件でも view 不可なら 403。

## 7. テスト

- `StreamAllEndpointTests` (api.tests): 認証なし → 401、未許可 layer 含む → 403、複数 layer publish → 単一 connection で受信
- `PermissionInvalidateBrokerTests` (api.tests): `PublishPermissionInvalidate` 呼び出し → 配信 event の reason='permission' + affectedOrgId 反映
- `eventStreamMulti.test.ts` (webgis): `subscribeLayers` で複数購読 + `permission_invalidate` 受領 → handler 呼ばれる

## 関連

- `PHASE_F_COMPLETE.md` §「Phase F' 申し送り」
- `tile-invalidation-on-perm.md`
