# Server-Sent Events プロトコル仕様 (Phase D')

agri-gis の `/api/events/` 経路で配信される Server-Sent Events (SSE) の仕様。Phase D' D'301-D'303 で導入。

## エンドポイント

```
GET /api/events/layers/{layerId}/stream
Authorization: Bearer {jwt}        ← または ?access_token={jwt} (EventSource 用)
```

### レスポンス

```
HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache, no-store
Connection: keep-alive
X-Accel-Buffering: no
```

ボディは無限ストリーム。接続を保持しつつ event 形式でデータを送る。

### Event 形式

```
event: layer_invalidate
data: {"layerId":1,"reason":"feature","action":"update","occurredAt":"2026-06-03T13:00:00Z"}

event: layer_invalidate
data: {"layerId":1,"reason":"style","styleVersion":5,"occurredAt":"2026-06-03T13:01:00Z"}

: keepalive

```

### Event ペイロード

```typescript
{
  layerId: number;
  reason: 'feature' | 'style' | 'layer';
  action?: 'insert' | 'update' | 'delete';   // reason=feature/layer のみ
  styleVersion?: number;                      // reason=style のみ
  occurredAt: string;                         // ISO 8601 UTC
}
```

### Reason の意味

- `feature`: `feature_current` テーブルへの INSERT/UPDATE/DELETE
- `style`: `layer_style_version` テーブルへの INSERT (新 style_version 発行)
- `layer`: `layers` テーブルへの INSERT/UPDATE/DELETE

### Keepalive

30 秒に 1 回 `: keepalive\n\n` (SSE コメント行) を送信し、接続を維持。

### Replay buffer

接続直後に、サーバ側 in-memory replay buffer から直近 5 秒の event を再送する (reconnect 取りこぼし対策)。Broker は最新 100 event を保持。

## 認証

`Authorization: Bearer {jwt}` ヘッダが標準。`EventSource` ブラウザ API は Authorization ヘッダを送れないため、`?access_token={jwt}` クエリパラメータからも JWT を受領する (Program.cs `JwtBearer.OnMessageReceived` が `/api/events/` 経路のみで分岐)。

`access_token=` 経由でも検証は通常 (JWT 署名 + sid_session claim + IsActive)。失敗で 401。

## クライアント実装例 (WebGIS)

```typescript
import { startEventStream } from './controllers/eventStream';

const ctx = createMap('map');
await loadFeatures(ctx, 1, 'default');
startEventStream(ctx, 1);
// → 他クライアントが layer 1 を編集 → 自動で TileLayer 再生成
```

## 通知元

Phase D' D'302 (`db/migration/0F02_notify_invalidation.sql`) で 3 つの TRIGGER を設置:

| Trigger | Table | Event | 通知内容 |
|---------|-------|-------|---------|
| `trg_feature_current_notify` | `feature_current` | INSERT/UPDATE/DELETE | reason=feature + action |
| `trg_layer_style_version_notify` | `layer_style_version` | INSERT | reason=style + styleVersion |
| `trg_layers_notify` | `layers` | INSERT/UPDATE/DELETE | reason=layer + action |

各 TRIGGER は `pg_notify('agri_gis_layer_invalidate', payload)` で通知。API 側 `PostgresLayerInvalidationBroker` が `LISTEN agri_gis_layer_invalidate` で受領 → `Channel<LayerInvalidationEvent>` で subscriber 配布。

## レート / スケール

- 1 行 1 notification (batch update は N notification、WebGIS 側で 500ms debounce)
- `pg_notify` ペイロード上限 8000 bytes (現状の JSON は 200 bytes 程度、余裕)
- 1 API インスタンス前提 (本番複数インスタンスでは Redis pub-sub 中継、Phase H 候補)

## 関連

- `docs/sld-cache-busting.md` (style_version 伝搬の最終消費者)
- `docs/feature-events-sse.md` (Design)
- `db/migration/0F02_notify_invalidation.sql`
- `api/Services/LayerInvalidationBroker.cs`
- `api/Endpoints/EventsEndpoints.cs`
- `webgis/src/controllers/eventStream.ts`
