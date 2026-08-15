namespace Avtomagazin.MauiShared;

public partial class LoginPage : ContentPage
{
    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;
    private readonly AppFlavor _flavor;
    private string _staffRole = "driver";

    public LoginPage(Session session, ApiHub api, SnapshotStore snapshot, AppFlavor flavor)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _snapshot = snapshot;
        _flavor = flavor;
        TitleLabel.Text = flavor.Title;
        SubtitleLabel.Text = flavor.Subtitle;
        RegisterButton.IsVisible = flavor.AllowRegister;
        NameBlock.IsVisible = flavor.AllowRegister;
        StaffRoleBlock.IsVisible = flavor.Client == "staff";
        EnvLabel.Text = MauiGateway.EnvironmentName == "Development"
            ? "Разработка · локальный API"
            : MauiGateway.EnvironmentName;
        HighlightStaffRole();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_session.IsAuthenticated && RoleAllowed(_session.Role))
        {
            await EnterAsync();
        }
        else if (_session.IsAuthenticated)
        {
            _session.SignOut();
        }
    }

    private void OnPickDriver(object? sender, EventArgs e)
    {
        _staffRole = "driver";
        HighlightStaffRole();
    }

    private void OnPickDispatcher(object? sender, EventArgs e)
    {
        _staffRole = "operator";
        HighlightStaffRole();
    }

    private void HighlightStaffRole()
    {
        DriverRoleButton.BackgroundColor = Color.FromArgb(_staffRole == "driver" ? "#3DBA7A" : "#1B3328");
        DriverRoleButton.TextColor = Color.FromArgb(_staffRole == "driver" ? "#082014" : "#F3F7F3");
        DispatcherRoleButton.BackgroundColor = Color.FromArgb(_staffRole == "operator" ? "#3DBA7A" : "#1B3328");
        DispatcherRoleButton.TextColor = Color.FromArgb(_staffRole == "operator" ? "#082014" : "#F3F7F3");
    }

    private async void OnLogin(object? sender, EventArgs e)
        => await SubmitAsync(register: false);

    private async void OnRegister(object? sender, EventArgs e)
        => await SubmitAsync(register: true);

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
                ErrorLabel.Text = "Не удалось войти. Проверьте email, пароль и что API запущен.";
                return;
            }

            if (string.Equals(login.Status, "pending", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(login.AccessToken))
            {
                ErrorLabel.Text = "Заявку отправили. Админ подтвердит вход в админке.";
                return;
            }

            if (!_flavor.AllowedRoles.Contains(login.Role))
            {
                ErrorLabel.Text = client == "resident"
                    ? "Этот аккаунт для персонала. Откройте приложение персонала."
                    : "Этот аккаунт для жителей. Откройте приложение жителя.";
                return;
            }

            _session.SignIn(login);
            await AfterSignInAsync();
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = ex.Message;
        }
    }

    private async Task AfterSignInAsync()
    {
        try
        {
            await _api.Client.RegisterDeviceAsync(new(
                _session.UserId,
                _session.DeviceToken,
                _session.Platform,
                "Озеричино"));
            if (_session.IsResident)
            {
                await FavoriteStore.PullAsync(_api.Client);
            }
        }
        catch
        {
            // вход всё равно открываем
        }

        await EnterAsync();
    }

    private async Task EnterAsync()
    {
        await _snapshot.LoadBootstrapAsync();
        _snapshot.LoadCache();
        _ = _snapshot.RefreshAsync(_api.Client);
        if (Shell.Current is IAppHost host)
        {
            host.ShowSignedIn();
        }
    }

    private bool RoleAllowed(string? role)
        => !string.IsNullOrWhiteSpace(role) && _flavor.AllowedRoles.Contains(role);
}
