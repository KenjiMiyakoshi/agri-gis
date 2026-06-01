# A404: 401 再ログインフロー + Guest UI 制限

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 0.5d |
| Depends on | A401, A402, A403 |
| Blocks | なし |

## 概要
401 を受領したら MainForm を Hide して LoginForm を再表示し、再ログイン後に MainForm を Show する。Guest の場合は保存系 UI を無効化する。

## 背景・目的
採択案「案 P」の WinForms セクション:
> 401 受領 → MainForm 保持 + Hide → LoginForm.ShowDialog → 再ログイン → MainForm.Show
> Guest UI 制限: `saveButton.Enabled = !session.IsGuest`

## スコープ
### 含む
- `BearerHandler` (A403) を拡張、または別の `UnauthorizedHandler` を `BearerHandler` の外側に挿入し、401 を捕捉
- 401 時の動作:
  1. ISessionStore.Clear()
  2. UI スレッドで MainForm.Hide
  3. LoginForm を新規生成 ShowDialog
  4. DialogResult.OK → MainForm.Show
  5. それ以外 → Application.Exit
- Guest UI 制限: MainForm の保存系ボタン (`saveButton`, `deleteButton` 等) を `session.IsGuest` で Enabled = false
- ISessionStore.Changed イベントを subscribe して UI 状態を更新

### 含まない
- 401 以外のリトライ
- expires_at を見たプロアクティブな再ログイン（Phase B、refresh と一緒に）

## 受け入れ条件 (Acceptance Criteria)
- [ ] 401 を受領したリクエスト後、MainForm が Hide → LoginForm 表示
- [ ] LoginForm キャンセル → アプリ終了
- [ ] 再ログイン成功 → MainForm 再表示、状態保持
- [ ] guest でログイン → 保存系ボタンが Disabled、地図表示・読み取りは可能
- [ ] guest が GET 系を叩く → 200（401 にならない、A206 で guest = JWT 必須読み取り OK）
- [ ] admin/general でログイン → 保存系ボタン Enabled

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Services\BearerHandler.cs` または新規 `Services/UnauthorizedHandler.cs`
- `D:\proj\agri-gis\windos-app\Forms\MainForm.cs` (Guest UI 制限ロジック)
- `D:\proj\agri-gis\windos-app\Program.cs` (UnauthorizedHandler 登録)

## 実装ノート
```csharp
// Services/UnauthorizedHandler.cs
public sealed class UnauthorizedHandler : DelegatingHandler
{
    private readonly IReloginCoordinator _relogin;
    public UnauthorizedHandler(IReloginCoordinator relogin) { _relogin = relogin; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var res = await base.SendAsync(request, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            _relogin.TriggerOnUiThread();
        return res;
    }
}

// IReloginCoordinator: Forms 層に居て、Form のリファレンスを保持
public interface IReloginCoordinator
{
    void TriggerOnUiThread();
}

public sealed class ReloginCoordinator : IReloginCoordinator
{
    // MainForm reference を Application.OpenForms から取得 or DI で受ける
    public void TriggerOnUiThread()
    {
        var main = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
        if (main is null) return;
        main.BeginInvoke(() =>
        {
            _session.Clear();
            main.Hide();
            using var login = _spFactory.CreateLoginForm();
            if (login.ShowDialog() == DialogResult.OK)
                main.Show();
            else
                Application.Exit();
        });
    }
}

// MainForm
private void OnSessionChanged(object? sender, EventArgs e)
{
    var s = _session.Current;
    saveButton.Enabled  = s is not null && !s.IsGuest;
    deleteButton.Enabled = s is not null && !s.IsGuest;
}
```

注意点:
- 401 のループ防止: LoginForm 自身の login 呼び出しは UnauthorizedHandler を通さない別 HttpClient で行うか、Session.Clear 後の 401 を無視する
- BeginInvoke で UI スレッド切り替えを確実に
- MainForm.Hide vs Close: 状態保持のため Hide

## テスト観点
- 手動 smoke:
  - admin login → 保存可能
  - guest login → 保存ボタン disabled
  - サーバを落として再起動 → 401 → LoginForm 出現 → 再ログイン → MainForm 復帰
