namespace Avtomagazin.Admin;

public sealed class UiToasts
{
    private readonly List<ToastItem> _items = [];
    private readonly object _gate = new();

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToList();
            }
        }
    }

    public void Success(string message) => Push(ToastKind.Success, message);
    public void Error(string message) => Push(ToastKind.Error, message);
    public void Info(string message) => Push(ToastKind.Info, message);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            _items.RemoveAll(x => x.Id == id);
        }

        Changed?.Invoke();
    }

    private void Push(ToastKind kind, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var item = new ToastItem(Guid.NewGuid(), kind, message.Trim(), DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _items.Add(item);
            while (_items.Count > 4)
            {
                _items.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        _ = AutoDismissAsync(item.Id);
    }

    private async Task AutoDismissAsync(Guid id)
    {
        await Task.Delay(5200);
        Dismiss(id);
    }
}

public enum ToastKind
{
    Info,
    Success,
    Error
}

public sealed record ToastItem(Guid Id, ToastKind Kind, string Message, DateTimeOffset CreatedAtUtc);
