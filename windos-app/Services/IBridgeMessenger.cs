namespace AgriGis.Desktop.Services;

public interface IBridgeMessenger
{
    // 受信時に発火。Form 側で購読して UI 反映する
    event EventHandler<Envelope>? MessageReceived;

    // type/payload を envelope 化して WebView2 に送る
    void Send(string type, object payload, string? requestId = null);
}
