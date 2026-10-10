namespace BTChargeIndicator.UI;

internal sealed record ConnectionNotification(string Key, string DeviceName, bool IsConnected, int? BatteryPercent);

// Owned and used exclusively by the tray's UI thread.
internal sealed class ConnectionNotificationPresenter : IDisposable
{
    private readonly List<ConnectionNotification> _pending = [];
    private ConnectionToastForm? _current;
    private bool _disposed;

    public void Show(ConnectionNotification notification)
    {
        if (_disposed) return;
        if (_current is not null && string.Equals(_current.Notification.Key, notification.Key, StringComparison.OrdinalIgnoreCase))
        {
            if (_current.Notification.IsConnected != notification.IsConnected)
                _current.UpdateNotification(notification);
            return;
        }
        _pending.RemoveAll(item => string.Equals(item.Key, notification.Key, StringComparison.OrdinalIgnoreCase));
        if (_pending.Count >= 5) _pending.RemoveAt(0);
        _pending.Add(notification);
        ShowNext();
    }

    private void ShowNext()
    {
        if (_disposed || _current is not null || _pending.Count == 0) return;
        var notification = _pending[0];
        _pending.RemoveAt(0);
        var form = new ConnectionToastForm(notification);
        _current = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_current, form)) _current = null;
            ShowNext();
        };
        form.Show();
    }

    public void Clear()
    {
        _pending.Clear();
        _current?.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        Clear();
    }
}
