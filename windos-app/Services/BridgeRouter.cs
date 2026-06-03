using System.Text.Json;

namespace AgriGis.Desktop.Services;

// H5-201 (WH5-2): MainForm の OnBridgeMessage switch を Register(type, handler) テーブル化。
//
// 設計:
// - Register("features_selected", async (payload, ct) => ...) でハンドラ登録
// - 受信した envelope の Type に対応するハンドラを呼び出し
// - 例外は OnError コールバックで通知 (status バーへ反映する用途)
// - UI スレッドへの marshalling は呼び出し側 (MainForm) の handler 内で行う
//   (Router は SynchronizationContext を持たず、test しやすさを優先)
public sealed class BridgeRouter
{
    public delegate Task EnvelopeHandler(JsonElement payload, CancellationToken ct);

    private readonly Dictionary<string, EnvelopeHandler> _handlers = new();

    /// <summary>例外発生時に通知する (status 表示や log 用途)。</summary>
    public Action<string, Exception>? OnError { get; set; }

    /// <summary>同じ type を二重登録すると後勝ち (テスト容易性)。</summary>
    public void Register(string type, EnvelopeHandler handler)
    {
        _handlers[type] = handler;
    }

    /// <summary>BridgeMessenger.MessageReceived から呼び出される。</summary>
    public async Task DispatchAsync(Envelope envelope, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(envelope.Type, out var handler))
        {
            // 未登録 type は無視 (将来 envelope 追加で MainForm 改修が不要になる)
            return;
        }
        try
        {
            await handler(envelope.Payload, ct);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(envelope.Type, ex);
        }
    }

    /// <summary>登録済 type 一覧 (テスト/診断用)。</summary>
    public IReadOnlyCollection<string> RegisteredTypes => _handlers.Keys;
}
