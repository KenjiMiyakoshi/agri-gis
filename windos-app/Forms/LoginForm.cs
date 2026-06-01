using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Services;

namespace AgriGis.Desktop.Forms;

public partial class LoginForm : Form
{
    private readonly IApiClient _api;
    private readonly ISessionStore _store;

    public LoginForm(IApiClient api, ISessionStore store)
    {
        _api = api;
        _store = store;
        InitializeComponent();
        loginButton.Click += async (_, _) => await LoginAsync();
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = loginButton;
        CancelButton = cancelButton;
    }

    private async Task LoginAsync()
    {
        var loginId = usernameTextBox.Text.Trim();
        var password = passwordTextBox.Text;

        if (string.IsNullOrEmpty(loginId) || string.IsNullOrEmpty(password))
        {
            errorLabel.Text = "ログイン ID とパスワードを入力してください。";
            return;
        }

        try
        {
            loginButton.Enabled = false;
            errorLabel.Text = "ログイン中...";

            var res = await _api.LoginAsync(loginId, password, CancellationToken.None);

            _store.Set(new Session(
                AccessToken: res.AccessToken,
                ExpiresAt:   res.ExpiresAt,
                UserId:      res.User.UserId,
                LoginId:     res.User.LoginId,
                DisplayName: res.User.DisplayName,
                OrgId:       res.User.OrgId,
                Roles:       res.User.Roles));

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (UnauthorizedApiException)
        {
            errorLabel.Text = "ログイン ID またはパスワードが正しくありません。";
        }
        catch (Exception ex)
        {
            errorLabel.Text = $"ログイン失敗: {ex.Message}";
        }
        finally
        {
            loginButton.Enabled = true;
        }
    }
}
