using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BTChargeIndicator.WinUI;

internal sealed class WinUiNotificationService : IDisposable
{
    private readonly AppNotificationManager _manager;
    private bool _registered;

    public WinUiNotificationService()
    {
        _manager = AppNotificationManager.Default;
        try
        {
            _manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows notifications could not be registered: {ex}");
        }
    }

    public void Show(string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            _manager.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows notification could not be shown: {ex}");
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            _manager.Unregister();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows notifications could not be unregistered: {ex}");
        }
    }
}
