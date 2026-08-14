namespace Avtomagazin.Mobile;

public partial class RolePage : ContentPage
{
    private readonly Session _session;
    private readonly ApiHub _api;
    private readonly SnapshotStore _snapshot;

    public RolePage(Session session, ApiHub api, SnapshotStore snapshot)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _snapshot = snapshot;
        GatewayEntry.Text = _session.Gateway;
    }

    private async void OnResident(object? sender, EventArgs e)
    {
        SaveGateway();
        await _snapshot.LoadBootstrapAsync();
        _snapshot.LoadCache();
        _ = _snapshot.RefreshAsync(_api.Client);
        ((AppShell)Shell.Current).ShowResident();
    }

    private async void OnOzerichino(object? sender, EventArgs e) => await EnterAsync("seller@demo.by");

    private async void OnGrodno(object? sender, EventArgs e) => await EnterAsync("driver@demo.by");

    private async void OnOperator(object? sender, EventArgs e) => await EnterAsync("operator@demo.by");

    private async Task EnterAsync(string email)
    {
        SaveGateway();
        ErrorLabel.Text = "";
        try
        {
            var login = await _api.Client.LoginAsync(email, "demo");
            if (login is null)
            {
                ErrorLabel.Text = "Не удалось войти. Проверь адрес API и что стек запущен.";
                return;
            }

            _session.SignIn(login);
            await _snapshot.RefreshAsync(_api.Client);
            if (_session.IsDriver)
            {
                ((AppShell)Shell.Current).ShowDriver();
            }
            else
            {
                ((AppShell)Shell.Current).ShowOperator();
            }
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = ex.Message;
        }
    }

    private void SaveGateway()
    {
        _session.Gateway = (GatewayEntry.Text ?? "").Trim();
        _session.RememberGateway();
    }
}
