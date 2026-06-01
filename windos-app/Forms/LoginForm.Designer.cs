#nullable enable

namespace AgriGis.Desktop.Forms;

partial class LoginForm
{
    private System.ComponentModel.IContainer? components = null;
    private Label titleLabel = null!;
    private Label usernameLabel = null!;
    private TextBox usernameTextBox = null!;
    private Label passwordLabel = null!;
    private TextBox passwordTextBox = null!;
    private Label errorLabel = null!;
    private Button loginButton = null!;
    private Button cancelButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        titleLabel = new Label();
        usernameLabel = new Label();
        usernameTextBox = new TextBox();
        passwordLabel = new Label();
        passwordTextBox = new TextBox();
        errorLabel = new Label();
        loginButton = new Button();
        cancelButton = new Button();

        SuspendLayout();

        // titleLabel
        titleLabel.Text = "AgriGis ログイン";
        titleLabel.Font = new System.Drawing.Font("Yu Gothic UI", 12F, System.Drawing.FontStyle.Bold);
        titleLabel.Location = new System.Drawing.Point(16, 16);
        titleLabel.Size = new System.Drawing.Size(280, 24);
        titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        // usernameLabel
        usernameLabel.Text = "ログイン ID:";
        usernameLabel.Location = new System.Drawing.Point(16, 56);
        usernameLabel.Size = new System.Drawing.Size(90, 24);
        usernameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        // usernameTextBox
        usernameTextBox.Location = new System.Drawing.Point(112, 56);
        usernameTextBox.Size = new System.Drawing.Size(184, 23);
        usernameTextBox.TabIndex = 0;

        // passwordLabel
        passwordLabel.Text = "パスワード:";
        passwordLabel.Location = new System.Drawing.Point(16, 88);
        passwordLabel.Size = new System.Drawing.Size(90, 24);
        passwordLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        // passwordTextBox
        passwordTextBox.Location = new System.Drawing.Point(112, 88);
        passwordTextBox.Size = new System.Drawing.Size(184, 23);
        passwordTextBox.UseSystemPasswordChar = true;
        passwordTextBox.TabIndex = 1;

        // errorLabel
        errorLabel.Location = new System.Drawing.Point(16, 120);
        errorLabel.Size = new System.Drawing.Size(280, 36);
        errorLabel.ForeColor = System.Drawing.Color.Firebrick;
        errorLabel.TextAlign = System.Drawing.ContentAlignment.TopLeft;

        // loginButton
        loginButton.Text = "ログイン";
        loginButton.Location = new System.Drawing.Point(112, 168);
        loginButton.Size = new System.Drawing.Size(92, 28);
        loginButton.TabIndex = 2;

        // cancelButton
        cancelButton.Text = "キャンセル";
        cancelButton.Location = new System.Drawing.Point(208, 168);
        cancelButton.Size = new System.Drawing.Size(92, 28);
        cancelButton.TabIndex = 3;

        // LoginForm
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(320, 216);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Name = "LoginForm";
        Text = "AgriGis ログイン";

        Controls.Add(titleLabel);
        Controls.Add(usernameLabel);
        Controls.Add(usernameTextBox);
        Controls.Add(passwordLabel);
        Controls.Add(passwordTextBox);
        Controls.Add(errorLabel);
        Controls.Add(loginButton);
        Controls.Add(cancelButton);

        ResumeLayout(false);
        PerformLayout();
    }
}
