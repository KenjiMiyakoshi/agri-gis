# 0504: `Forms/MainForm` + `AttributeEditorControl` + WebView2 初期化

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 1d |
| Depends on | 0503, 0403 |
| Blocks | 0602 |

## 概要
WebView2 を埋め込んだ MainForm と属性編集用 UserControl を実装する。WebView2 の初期化は `EnsureCoreWebView2Async` + `NavigationCompleted` の両方を待ってから初回 PostMessage する。

## 背景・目的
案 B' で「`EnsureCoreWebView2Async()` 完了 + `NavigationCompleted` の両方を待ってから初回 PostMessage」が明示の要件。WebView2 初期化レースで初回メッセージが取りこぼされるバグを防ぐ。

## スコープ
### 含む
- `Forms/MainForm.cs`
  - SplitContainer: 左 WebView2 / 右 AttributeEditorControl
  - 起動シーケンス:
    1. `webView.EnsureCoreWebView2Async()` await
    2. `webView.Source = new Uri("http://localhost:5173")` (dev) or  Vite build 後の静的ホスト
    3. `webView.CoreWebView2.NavigationCompleted` を 1 回だけ待つ (TaskCompletionSource)
    4. 完了後 BridgeMessenger を生成し、IApiClient で layers を取って selector に流す
    5. ここで初めて Host → Web メッセージ送信を許可
  - WebGIS からの `feature_clicked` を受けたら AttributeEditorControl に entityId を渡す
- `Forms/AttributeEditorControl.cs` (UserControl)
  - `LoadAsync(LayerSchema schema, FeatureDto feature)`
  - `SchemaFormBuilder.Build` の FieldDescriptor を見て TextBox / NumericUpDown / CheckBox を動的生成
  - 保存ボタンで `AttributeValidator.Validate` → エラー赤表示 / `IApiClient.UpdateFeatureAsync`
  - 楽観ロック失敗 (ApiException.ParsedProblem.Status==409) なら「他のユーザが編集しました。再読込してください」ダイアログ
- `Forms/Designer.cs` 系は最小（手書きでも OK）

### 含まない
- 図形編集 UI（本サイクル外）
- 複数フォーム

## 受け入れ条件 (Acceptance Criteria)
- [ ] アプリ起動 → WebView2 で地図が表示 → フィーチャをクリック → 右ペインに属性編集が出る
- [ ] 保存して 200 で「保存しました」、422 で各属性エラー表示、409 で再読込ダイアログ
- [ ] WebView2 初期化失敗時にメッセージを表示して終了
- [ ] 初回 layer_select メッセージが NavigationCompleted より前に送られない（ログ確認）

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Forms\MainForm.cs` (本格実装)
- `D:\proj\agri-gis\windos-app\Forms\MainForm.Designer.cs` (本格実装)
- `D:\proj\agri-gis\windos-app\Forms\AttributeEditorControl.cs` (新規)
- `D:\proj\agri-gis\windos-app\Forms\AttributeEditorControl.Designer.cs` (新規)
- `D:\proj\agri-gis\windos-app\Program.cs` (BridgeMessenger を Form 起動後に DI 登録、または Form から new)

## 実装ノート
```csharp
public partial class MainForm : Form
{
    private readonly IApiClient _api;
    private IBridgeMessenger? _bridge;

    public MainForm(IApiClient api) { _api = api; InitializeComponent(); }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await webView.EnsureCoreWebView2Async();

            var navTcs = new TaskCompletionSource();
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = (s, ev) =>
            {
                webView.CoreWebView2.NavigationCompleted -= handler!;
                navTcs.TrySetResult();
            };
            webView.CoreWebView2.NavigationCompleted += handler;

            webView.Source = new Uri("http://localhost:5173");
            await navTcs.Task;

            _bridge = new BridgeMessenger(webView.CoreWebView2);
            _bridge.MessageReceived += OnBridgeMessage;

            var layers = await _api.GetLayersAsync(CancellationToken.None);
            // ... UI に流す + 初回 layer_select 送信
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 init failed: {ex.Message}");
            Close();
        }
    }
}
```

注意点:
- 単発 NavigationCompleted を 1 回だけ拾う書き方に注意 (`-=` してから result set)
- `AttributeEditorControl.LoadAsync` は schema が変わるたびに `Controls.Clear()` してから再生成

## テスト観点
- 手動確認のみ。自動テストは Core 単体 (0505)
