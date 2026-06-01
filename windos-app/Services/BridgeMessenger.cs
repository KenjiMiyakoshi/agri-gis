using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace AgriGis.Desktop.Services;

// WebView2 (CoreWebView2) との双方向メッセージング。
// Form 側で WebView2 の初期化が完了した時点で CoreWebView2 を受け取る。
public sealed class BridgeMessenger : IBridgeMessenger, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly CoreWebView2 _webview;
    private readonly EventHandler<CoreWebView2WebMessageReceivedEventArgs> _handler;

    public event EventHandler<Envelope>? MessageReceived;

    public BridgeMessenger(CoreWebView2 webview)
    {
        _webview = webview;
        _handler = OnWebMessageReceived;
        _webview.WebMessageReceived += _handler;
    }

    public void Send(string type, object payload, string? requestId = null)
    {
        var env = new
        {
            type,
            payload,
            requestId
        };
        _webview.PostWebMessageAsString(JsonSerializer.Serialize(env, JsonOpts));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch
        {
            // 文字列でない場合は無視
            return;
        }

        try
        {
            var env = JsonSerializer.Deserialize<Envelope>(json, JsonOpts);
            if (env is not null)
            {
                MessageReceived?.Invoke(this, env);
            }
        }
        catch (JsonException)
        {
            // 不正 JSON は無視
        }
    }

    public void Dispose()
    {
        _webview.WebMessageReceived -= _handler;
    }
}
