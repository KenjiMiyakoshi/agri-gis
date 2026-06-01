# A402: LoginForm + Designer + 起動フロー

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 1d |
| Depends on | A205, A401, A403 |
| Blocks | A404 |

## 概要
新規 `Forms/LoginForm.cs` + `LoginForm.Designer.cs` を実装し、アプリ起動時に LoginForm を先に表示してログイン成功後に MainForm を開くフローを構築する。

## 背景・目的
採択案「案 P」の WinForms セクション:
> 新規 `windos-app/Forms/LoginForm.cs` + `.Designer.cs`（既存 MainForm 同型スタイル）

## スコープ
### 含む
- `Forms/LoginForm.cs` + `.Designer.cs`
- UI: LoginId textbox, Password textbox (PasswordChar='*'), Login button, Server URL display, error label
- `LoginForm.LoginAsync()`: `ApiClient.LoginAsync(loginId, password)` を呼び、成功なら ISessionStore.Set + DialogResult.OK、失敗ならエラー表示
- Enter キーで Login button 起動
- `Program.cs` の起動フロー:
  1. `LoginForm` を ShowDialog
  2. DialogResult.OK なら `MainForm` を起動
  3. それ以外なら終了
- 既存 MainForm 同型のスタイル（フォント、配色、サイズ）

### 含まない
- 401 受領後の再ログイン (A404)
- パスワード変更 UI（Phase B）
- Remember me（永続化なし、採択案明示）

## 受け入れ条件 (Acceptance Criteria)
- [ ] アプリ起動で LoginForm が最初に表示される
- [ ] 正しい login_id/password で MainForm が表示される
- [ ] 不正な credentials で error label に「ログインに失敗しました」表示、フォーム残留
- [ ] Esc キーでフォーム閉じる → アプリ終了
- [ ] LoginForm.Designer.cs はデザイナで開いて編集可能
- [ ] ISessionStore.Current が成功後に非 null

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Forms\LoginForm.cs` (新規)
- `D:\proj\agri-gis\windos-app\Forms\LoginForm.Designer.cs` (新規)
- `D:\proj\agri-gis\windos-app\Forms\LoginForm.resx` (新規)
- `D:\proj\agri-gis\windos-app\Program.cs`

## 実装ノート
```csharp
public partial class LoginForm : Form
{
    private readonly ApiClient _api;
    private readonly ISessionStore _session;

    public LoginForm(ApiClient api, ISessionStore session)
    {
        InitializeComponent();
        _api = api; _session = session;
        AcceptButton = btnLogin;
        CancelButton = btnCancel;
    }

    private async void btnLogin_Click(object? sender, EventArgs e)
    {
        btnLogin.Enabled = false;
        lblError.Text = "";
        try
        {
            var res = await _api.LoginAsync(txtLoginId.Text, txtPassword.Text);
            _session.Set(new Session(
                res.AccessToken, res.ExpiresAt,
                res.UserId, res.LoginId, res.DisplayName, res.OrgId, res.Roles));
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            lblError.Text = "ログインに失敗しました";
        }
        catch (Exception ex)
        {
            lblError.Text = $"通信エラー: {ex.Message}";
        }
        finally
        {
            btnLogin.Enabled = true;
        }
    }
}

// Program.cs
[STAThread]
static void Main()
{
    ApplicationConfiguration.Initialize();
    using var sp = BuildServiceProvider();
    var login = sp.GetRequiredService<LoginForm>();
    if (login.ShowDialog() != DialogResult.OK) return;
    Application.Run(sp.GetRequiredService<MainForm>());
}
```

注意点:
- MainForm との一貫性のため、既存 MainForm の Font/BackColor を参考にする
- Password textbox は `UseSystemPasswordChar = true`

## テスト観点
- 手動 smoke: 正常 login / 異常 login / Esc 終了
- A402 単体 UI テストは Phase A 範囲外（必要なら別途）
