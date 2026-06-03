# Feature Events SSE (Phase D' D'5)

PostgreSQL `LISTEN/NOTIFY` + Server-Sent Events (SSE) で「他クライアントの編集」をリアルタイム反映する。

## 背景

現状、WinForms の属性編集が WebGIS に反映されるには「F5 / layer 切替」が必要。複数 client (WinForms + 別ブラウザ) が同時に作業しているとき、お互いの変更が見えない。

Phase D' D'1 (cache busting) で「URL に style_version を載せる」仕組みを作ったが、これは API レスポンスの `styleVersion` を WebGIS が能動的に見ないと反映されない。

## 採用案: PostgreSQL LISTEN/NOTIFY + SSE

### 全体フロー

```
[WinForms]                  [API]                       [WebGIS]
   │                          │                            │
   │ PATCH /features/X        │                            │
   │ ──────────────────────►  │                            │
   │                          │ fn_feature_update          │
   │                          │   pg_notify(channel, msg)  │
   │                          │ ◄─── DB                    │
   │                          │                            │
   │                          │ LISTEN agri_gis_layer_invalidate
   │                          │ ◄─── Npgsql (persistent)   │
   │                          │                            │
   │                          │ SSE: layer_invalidate event│
   │                          │ ────────────────────────►  │
   │                          │                            │ setBaseLayerSource(
   │                          │                            │   ctx, layerId, theme,
   │                          │                            │   asOf, newStyleVersion)
   │                          │                            │   → 新 URL → 新タイル
```

### PostgreSQL 側 (D'302)

`0F02_notify_invalidation.sql`:

```sql
-- 既存 7 関数を CREATE OR REPLACE で更新:
-- fn_feature_insert, fn_feature_update, fn_feature_delete (Phase A)
-- fn_layer_schema_upsert (Phase A)
-- fn_layer_style_upsert, fn_layer_update, fn_layer_delete_v2 (Phase E)

-- 例: fn_feature_update の末尾 (RETURN 直前) に追加:
PERFORM pg_notify(
    'agri_gis_layer_invalidate',
    json_build_object(
        'layerId', p_layer_id,
        'reason', 'feature',
        'styleVersion', NULL,  -- feature 更新では style 不変
        'entityCount', 1,
        'occurredAt', now()
    )::text
);
```

`fn_layer_style_upsert` では `reason='style'`, `styleVersion=新版`:

```sql
PERFORM pg_notify(
    'agri_gis_layer_invalidate',
    json_build_object(
        'layerId', p_layer_id,
        'reason', 'style',
        'styleVersion', v_new_version,
        'occurredAt', now()
    )::text
);
```

メッセージは **8000 bytes 以下** (PostgreSQL の `pg_notify` 上限) に抑える (JSON は十分短い)。

### API 側 (D'301)

`api/Endpoints/EventsEndpoints.cs`:

```csharp
public static class EventsEndpoints
{
    public static RouteGroupBuilder MapEventsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/layers/{layerId:int}/stream", async (
            int layerId,
            HttpContext httpContext,
            ILayerInvalidationBroker broker,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            httpContext.Response.Headers.Append("Cache-Control", "no-cache");
            httpContext.Response.Headers.Append("Connection", "keep-alive");
            httpContext.Response.Headers.Append("X-Accel-Buffering", "no"); // nginx

            // 直近 5 秒分の event を replay (reconnect 時の取りこぼし対策)
            foreach (var ev in broker.ReplayRecent(layerId, TimeSpan.FromSeconds(5)))
            {
                await WriteEventAsync(httpContext.Response, ev, ct);
            }

            await foreach (var ev in broker.SubscribeAsync(layerId, ct))
            {
                await WriteEventAsync(httpContext.Response, ev, ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
            return Results.Empty;
        }).RequireAuthorization();
        return group;
    }

    private static async Task WriteEventAsync(HttpResponse resp, LayerInvalidationEvent ev, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(ev);
        await resp.WriteAsync($"event: layer_invalidate\ndata: {json}\n\n", ct);
    }
}
```

`ILayerInvalidationBroker` は singleton:
- 起動時に `NpgsqlConnection.Open()` + `LISTEN agri_gis_layer_invalidate`
- `Notification` イベントで内部 Channel に push
- 各 SSE クライアントは `ChannelReader` でフィルタ (layerId 一致) しつつ subscribe
- in-memory replay buffer (CircularBuffer) で直近 5 秒分を保持

```csharp
public sealed class PostgresLayerInvalidationBroker : ILayerInvalidationBroker, IHostedService
{
    private NpgsqlConnection _conn;
    private readonly Channel<LayerInvalidationEvent> _channel = Channel.CreateUnbounded<LayerInvalidationEvent>();
    private readonly Queue<LayerInvalidationEvent> _replay = new();

    public async Task StartAsync(CancellationToken ct)
    {
        _conn = new NpgsqlConnection(_connStr);
        await _conn.OpenAsync(ct);
        _conn.Notification += (s, e) => {
            var ev = JsonSerializer.Deserialize<LayerInvalidationEvent>(e.Payload);
            _channel.Writer.TryWrite(ev);
            lock (_replay)
            {
                _replay.Enqueue(ev);
                while (_replay.Count > 100) _replay.Dequeue();
            }
        };
        await using var cmd = new NpgsqlCommand("LISTEN agri_gis_layer_invalidate", _conn);
        await cmd.ExecuteNonQueryAsync(ct);
        // 永続 wait
        _ = Task.Run(async () => {
            while (!ct.IsCancellationRequested)
                await _conn.WaitAsync(ct);
        }, ct);
    }

    public IEnumerable<LayerInvalidationEvent> ReplayRecent(int layerId, TimeSpan window)
    {
        lock (_replay)
        {
            var cutoff = DateTime.UtcNow - window;
            return _replay.Where(e => e.LayerId == layerId && e.OccurredAt >= cutoff).ToList();
        }
    }

    public async IAsyncEnumerable<LayerInvalidationEvent> SubscribeAsync(
        int layerId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var ev in _channel.Reader.ReadAllAsync(ct))
        {
            if (ev.LayerId == layerId) yield return ev;
        }
    }
}
```

### WebGIS 側 (D'303)

`webgis/src/controllers/eventStream.ts`:

```typescript
export function startEventStream(ctx: MapContext, layerId: number): EventSource {
  const token = getCurrentAccessToken();
  // EventSource は Authorization ヘッダを設定できないので、access token は query param 経由で渡す
  // (代替: cookie + SameSite=Strict)
  const url = `/api/events/layers/${layerId}/stream?access_token=${encodeURIComponent(token!)}`;
  const es = new EventSource(url);
  es.addEventListener('layer_invalidate', (e) => {
    const ev = JSON.parse((e as MessageEvent).data);
    if (ev.layerId !== ctx.currentLayerId) return;
    debounce(() => {
      const newSv = ev.reason === 'style' ? ev.styleVersion : ctx.currentStyleVersion;
      ctx.currentStyleVersion = newSv;
      setBaseLayerSource(ctx, ctx.currentLayerId!, ctx.currentTheme, ctx.currentAsOf, newSv);
    }, 500);
  });
  es.onerror = (e) => {
    console.warn('[sse] reconnecting...', e);
    // EventSource は自動 reconnect (browser 内蔵)
  };
  return es;
}
```

500ms debounce で短間隔連打を抑制。

### WinForms 側 (D'305)

`windos-app/Services/LayerEventListener.cs` 新規:

```csharp
public sealed class LayerEventListener : IHostedService, IDisposable
{
    private readonly IApiClient _api;
    public event EventHandler<LayerInvalidationEvent>? Invalidated;

    public async Task StartAsync(CancellationToken ct) { /* SSE 購読開始 */ }
    public Task StopAsync(CancellationToken ct) { /* 接続終了 */ return Task.CompletedTask; }
}
```

`MainForm` は DI で `LayerEventListener` を受領、`Invalidated` イベントで `statusLabel.Text = $"属性更新: {ev.EntityCount} 件 / {ago} 秒前"` 等を更新。

**MainForm.cs に直接 SSE ロジックを書かない** (H5 リファクタを意識した分離)。

## 認可

- `access_token` query param で JWT を渡す (`EventSource` は header 設定不可)
- API 側で query param からも JWT 受領できるよう `JwtBearer` の `OnMessageReceived` で `Request.Query["access_token"]` を読む
- 認可は通常通り `[Authorize]` (general / guest / admin 全て可、read だけ)

## レート制限

- SSE 接続は client あたり 1 接続 (browser の `EventSource` 仕様)
- API 側で同 layerId 同 user の重複接続は許可 (admin が複数 tab 開く想定)
- `pg_notify` 自体に Postgres 側のレート制限は無いが、`channel` バッファ 8MB 超で `WARNING` が出る (現実的にはまず起きない)

## スケール (Phase H 申し送り)

複数 API インスタンス構成 (Phase H 本番) では:
- API インスタンス N → `pg_notify` 受領は 1 instance のみ (LISTEN は 1 connection)
- 全 instance に broadcast するには Redis pub-sub 中継が必要
- Phase D' では **1 API instance 前提** (dev compose の単一 service)、Phase H で Redis 切替

## 受入条件

1. WinForms で属性編集 → 保存 → 1 秒以内に WebGIS の地図上で**自動的に**色更新 (DevTools Network で `?sv=N+1` URL 確認)
2. SLD 更新 → 1 秒以内に WebGIS preview map も自動再描画
3. SSE 接続が 5 分以上維持 (keepalive で切断防止)
4. ネット断 → 自動 reconnect → 直近 5 秒の event を replay buffer で取りこぼしなく受領
5. `EventsEndpoints` への non-authorized アクセスは 401
6. WinForms `LayerEventListener` から `MainForm` に DI 経由で event 配信

## テスト

- `EventsStreamTests` (`api.tests`): testcontainer で `PERFORM pg_notify(...)` → SSE 応答 body に `event: layer_invalidate` が含まれる
- `LayerInvalidationBrokerTests`: replay buffer の 5 秒 window 動作
- `eventStream.spec.ts` (`webgis vitest`): `EventSource` モック → invalidate イベント → `setBaseLayerSource` が呼ばれる
- `LayerEventListenerTests` (`windos-app.tests`): SSE 接続 + イベント発火 + reconnect

## 関連

- `docs/PHASE_D_PRIME_INDEX.md`
- `docs/sld-cache-busting.md` (style_version 伝搬の最終消費者)
- `docs/feature-batch-update.md` (batch update も同じ notify を出す)
- `docs/bitemporal-asof.md` (Phase A fn_feature_* に追加配線する場所)
