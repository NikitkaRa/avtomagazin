using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public partial class LoginPage : ContentPage
{
    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;
    private readonly AppFlavor _flavor;
    private string _staffRole = Roles.Driver;
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
        StaffRoleBlock.IsVisible = flavor.Client == AuthClients.Staff;
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
        _staffRole = Roles.Driver;
        HighlightStaffRole();
    }

    private void OnPickSeller(object? sender, EventArgs e)
    {
        _staffRole = Roles.Seller;
        HighlightStaffRole();
    }

    private void OnPickDispatcher(object? sender, EventArgs e)
    {
        _staffRole = Roles.Operator;
        HighlightStaffRole();
    }

    private void HighlightStaffRole()
    {
        PaintRole(DriverRoleButton, _staffRole == Roles.Driver);
        PaintRole(SellerRoleButton, _staffRole == Roles.Seller);
        PaintRole(DispatcherRoleButton, _staffRole == Roles.Operator);
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
                ? await _api.Client.RegisterAsync(email, password, NameEntry.Text, client, client == AuthClients.Staff ? _staffRole : null)
                : await _api.Client.LoginAsync(email, password);
            if (login is null)
            {
                ErrorLabel.Text = "Не удалось войти. Проверьте email и пароль.";
                return;
            }

            if (string.Equals(login.Status, UserStatuses.Pending, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(login.AccessToken))
            {
                ErrorLabel.Text = "Заявка отправлена. Ждите подтверждения админа.";
                _registerMode = false;
                ApplyMode();
                return;
            }

            if (!_flavor.AllowedRoles.Contains(login.Role))
            {
                ErrorLabel.Text = client == AuthClients.Resident
                    ? "Это аккаунт персонала — откройте приложение персонала."
                    : "Это аккаунт жителя — откройте приложение жителя.";
                return;
            }

            await _session.SignInAsync(login);
            await AfterSignInAsync();
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = ApiErrors.FriendlyLogin(ex);
        }
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
