namespace Avtomagazin.MauiShared;

public partial class LoginPage : ContentPage
{
    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;
    private readonly AppFlavor _flavor;
    private string _staffRole = "driver";
    private bool _registerMode;
    private bool _showPassword;

    public LoginPage(Session session, ApiHub api, SnapshotStore snapshot, AppFlavor flavor)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _snapshot = snapshot;
        _flavor = flavor;
        TitleLabel.Text = string.IsNullOrWhiteSpace(flavor.Subtitle) ? "Вход" : flavor.Subtitle;
        ModeButton.IsVisible = flavor.AllowRegister;
        StaffRoleBlock.IsVisible = flavor.Client == "staff";
        BgImage.Source = ImageSource.FromFile("login_bg.png");
        ApplyMode();
    }

    private void OnTogglePassword(object? sender, EventArgs e)
    {
        _showPassword = !_showPassword;
        PasswordEntry.IsPassword = !_showPassword;
        PasswordEye.Text = _showPassword ? "скрыть" : "показать";
    }

    private void OnPickDriver(object? sender, EventArgs e)
    {
        _staffRole = "driver";
        HighlightStaffRole();
    }

    private void OnPickSeller(object? sender, EventArgs e)
    {
        _staffRole = "seller";
        HighlightStaffRole();
    }

    private void OnPickDispatcher(object? sender, EventArgs e)
    {
        _staffRole = "operator";
        HighlightStaffRole();
    }

    private void HighlightStaffRole()
    {
        PaintRole(DriverRoleButton, _staffRole == "driver");
        PaintRole(SellerRoleButton, _staffRole == "seller");
        PaintRole(DispatcherRoleButton, _staffRole == "operator");
    }

    private static void PaintRole(Button button, bool on)
    {
        button.BackgroundColor = Color.FromArgb(on ? "#3DBA7A" : "#1B3328");
        button.TextColor = Color.FromArgb(on ? "#082014" : "#F3F7F3");
    }

    private void OnToggleMode(object? sender, EventArgs e)
    {
        _registerMode = !_registerMode;
        ApplyMode();
    }

    private void ApplyMode()
    {
        RegisterExtras.IsVisible = _registerMode && _flavor.AllowRegister;
        PrimaryButton.Text = _registerMode ? "Отправить заявку" : "Войти";
        ModeButton.Text = _registerMode ? "Уже есть аккаунт" : "Нужна заявка";
        if (_registerMode)
        {
            HighlightStaffRole();
        }
    }

    private async void OnPrimary(object? sender, EventArgs e)
        => await SubmitAsync(register: _registerMode);

    private async Task SubmitAsync(bool register)
    {
        ErrorLabel.Text = "";
        var email = EmailEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";
        var client = _flavor.Client;
        try
        {
            var login = register
                ? await _api.Client.RegisterAsync(email, password, NameEntry.Text, client, client == "staff" ? _staffRole : null)
                : await _api.Client.LoginAsync(email, password);
            if (login is null)
            {
                ErrorLabel.Text = "Не удалось войти. Проверьте email и пароль.";
                return;
            }

            if (string.Equals(login.Status, "pending", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(login.AccessToken))
            {
                ErrorLabel.Text = "Заявка отправлена. Ждите подтверждения админа.";
                _registerMode = false;
                ApplyMode();
                return;
            }

            if (!_flavor.AllowedRoles.Contains(login.Role))
            {
                ErrorLabel.Text = client == "resident"
                    ? "Это аккаунт персонала — откройте приложение персонала."
                    : "Это аккаунт жителя — откройте приложение жителя.";
                return;
            }

            _session.SignIn(login);
            await AfterSignInAsync();
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = FriendlyLoginError(ex);
        }
    }

    private static string FriendlyLoginError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("502", StringComparison.Ordinal)
            || msg.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Нет связи с сервером. Проверьте, что API запущен.";
        }

        return msg.Length > 140 ? msg[..140] + "…" : msg;
    }

    private async Task AfterSignInAsync()
    {
        await SessionGate.BootstrapAsync(_session, _api, _snapshot);
        if (Shell.Current is IAppHost host)
        {
            host.ShowSignedIn();
        }
    }
}
