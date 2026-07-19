using System.Diagnostics;
using BTChargeIndicator.Models;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BluetoothBatteryService _bluetoothService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly HashSet<string> _lowBatteryNotifications = new(StringComparer.OrdinalIgnoreCase);

    private AppSettings _settings;
    private IReadOnlyList<BluetoothBatteryDevice> _devices = [];
    private BluetoothAvailability _availability = BluetoothAvailability.Available;
    private string? _lastErrorMessage;
    private DateTime? _lastUpdated;
    private Icon? _currentIcon;
    private bool _isExiting;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _currentIcon = TrayIconRenderer.Create(null);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "BT Charge Indicator — загрузка…",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.DoubleClick += (_, _) => OpenBluetoothSettings();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = _settings.RefreshIntervalSeconds * 1000,
            Enabled = true
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            SetTrayText("BT Charge Indicator — обновление…");
            var result = await _bluetoothService.ScanAsync();
            _devices = result.Devices;
            _availability = result.Availability;
            _lastErrorMessage = result.ErrorMessage;
            _lastUpdated = DateTime.Now;

            UpdateTrayIcon();
            ReplaceContextMenu();
            ShowLowBatteryNotificationIfNeeded();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            MinimumSize = new Size(310, 0)
        };

        var title = new ToolStripMenuItem("BT Charge Indicator")
        {
            Enabled = false,
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
        };
        menu.Items.Add(title);
        menu.Items.Add(BuildStatusItem());
        menu.Items.Add(new ToolStripSeparator());

        if (_devices.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem(GetEmptyStateText()) { Enabled = false });

            if (_availability == BluetoothAvailability.Error && !string.IsNullOrWhiteSpace(_lastErrorMessage))
            {
                var detailsItem = new ToolStripMenuItem("Показать сведения об ошибке");
                detailsItem.Click += (_, _) => MessageBox.Show(
                    $"{_lastErrorMessage}\n\nЖурнал: {AppLogger.LogPath}",
                    "Ошибка Bluetooth",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                menu.Items.Add(detailsItem);
            }
        }
        else
        {
            foreach (var device in _devices)
            {
                var deviceItem = new ToolStripMenuItem(FormatDevice(device))
                {
                    ToolTipText = device.IsConnected switch
                    {
                        true => "Подключено",
                        false => "Не подключено",
                        _ => "Состояние подключения неизвестно"
                    }
                };
                deviceItem.Click += (_, _) => OpenBluetoothSettings();
                menu.Items.Add(deviceItem);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var refreshItem = new ToolStripMenuItem("Обновить сейчас");
        refreshItem.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refreshItem);

        var bluetoothSettingsItem = new ToolStripMenuItem("Открыть параметры Bluetooth");
        bluetoothSettingsItem.Click += (_, _) => OpenBluetoothSettings();
        menu.Items.Add(bluetoothSettingsItem);

        menu.Items.Add(BuildRefreshIntervalMenu());

        var notificationsItem = new ToolStripMenuItem("Уведомлять при низком заряде")
        {
            CheckOnClick = true,
            Checked = _settings.LowBatteryNotifications
        };
        notificationsItem.CheckedChanged += (_, _) =>
        {
            _settings.LowBatteryNotifications = notificationsItem.Checked;
            if (!notificationsItem.Checked)
            {
                _lowBatteryNotifications.Clear();
            }
            _settingsStore.Save(_settings);
        };
        menu.Items.Add(notificationsItem);

        var autoStartItem = new ToolStripMenuItem("Запускать вместе с Windows")
        {
            CheckOnClick = true,
            Checked = AutoStartManager.IsEnabled
        };
        var isRevertingAutoStart = false;
        autoStartItem.CheckedChanged += (_, _) =>
        {
            if (isRevertingAutoStart)
            {
                return;
            }

            try
            {
                AutoStartManager.SetEnabled(autoStartItem.Checked);
            }
            catch (Exception ex)
            {
                isRevertingAutoStart = true;
                autoStartItem.Checked = !autoStartItem.Checked;
                isRevertingAutoStart = false;
                MessageBox.Show(
                    $"Не удалось изменить автозапуск.\n\n{ex.Message}",
                    "BT Charge Indicator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("О программе");
        aboutItem.Click += (_, _) => MessageBox.Show(
            $"BT Charge Indicator {Application.ProductVersion}\n\nspecial for Vitalik\n\nПоказывает заряд сопряжённых Bluetooth-устройств в системном трее Windows.\n\nДвойной щелчок по иконке открывает параметры Bluetooth.",
            "О программе",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        menu.Items.Add(aboutItem);

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitThread();
        menu.Items.Add(exitItem);

        return menu;
    }

    private ToolStripMenuItem BuildStatusItem()
    {
        var status = _availability switch
        {
            BluetoothAvailability.TurnedOff => "Bluetooth выключен",
            BluetoothAvailability.NotFound => "Bluetooth-адаптер не найден",
            BluetoothAvailability.AccessDenied => "Нет доступа к Bluetooth",
            BluetoothAvailability.Error => "Не удалось получить данные",
            _ when _lastUpdated.HasValue => $"Обновлено: {_lastUpdated:HH:mm}",
            _ => "Получение данных…"
        };

        return new ToolStripMenuItem(status)
        {
            Enabled = false,
            ToolTipText = _lastErrorMessage ?? string.Empty
        };
    }

    private ToolStripMenuItem BuildRefreshIntervalMenu()
    {
        var intervalMenu = new ToolStripMenuItem("Интервал обновления");
        AddIntervalItem(intervalMenu, "30 секунд", 30);
        AddIntervalItem(intervalMenu, "1 минута", 60);
        AddIntervalItem(intervalMenu, "5 минут", 300);
        return intervalMenu;
    }

    private void AddIntervalItem(ToolStripMenuItem parent, string text, int seconds)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = _settings.RefreshIntervalSeconds == seconds
        };
        item.Click += (_, _) =>
        {
            _settings.RefreshIntervalSeconds = seconds;
            _settingsStore.Save(_settings);
            _refreshTimer.Interval = seconds * 1000;
            ReplaceContextMenu();
        };
        parent.DropDownItems.Add(item);
    }

    private void ReplaceContextMenu()
    {
        var previous = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
        previous?.Dispose();
    }

    private void UpdateTrayIcon()
    {
        var knownDevices = _devices.Where(device => device.BatteryPercent.HasValue).ToArray();
        var connectedDevices = knownDevices.Where(device => device.IsConnected == true).ToArray();
        var relevantDevices = connectedDevices.Length > 0 ? connectedDevices : knownDevices;
        var lowestCharge = relevantDevices.Select(device => device.BatteryPercent).Min();

        var previousIcon = _currentIcon;
        _currentIcon = TrayIconRenderer.Create(lowestCharge);
        _notifyIcon.Icon = _currentIcon;
        previousIcon?.Dispose();

        SetTrayText(BuildToolTip());
    }

    private string BuildToolTip()
    {
        if (_availability == BluetoothAvailability.TurnedOff)
        {
            return "BT Charge Indicator — Bluetooth выключен";
        }

        if (_devices.Count == 0)
        {
            return "BT Charge Indicator — устройства не найдены";
        }

        var first = _devices.FirstOrDefault(device => device.BatteryPercent.HasValue);
        return first is null
            ? $"BT Charge Indicator — {_devices.Count} устр., заряд недоступен"
            : $"{first.Name}: {first.BatteryPercent}%";
    }

    private void SetTrayText(string text)
    {
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "…";
    }

    private static string FormatDevice(BluetoothBatteryDevice device)
    {
        var connection = device.IsConnected switch
        {
            true => "●",
            false => "○",
            null => "◌"
        };
        var battery = device.BatteryPercent is int percent ? $"{percent,3}%" : "  — ";
        return $"{connection}  {battery}   {device.Name}";
    }

    private string GetEmptyStateText()
    {
        return _availability switch
        {
            BluetoothAvailability.TurnedOff => "Включите Bluetooth для получения данных",
            BluetoothAvailability.NotFound => "Bluetooth-адаптер не найден",
            BluetoothAvailability.AccessDenied => "Windows запретила доступ к Bluetooth",
            BluetoothAvailability.Error => "Ошибка чтения Bluetooth-устройств",
            _ => "Сопряжённые устройства не найдены"
        };
    }

    private void ShowLowBatteryNotificationIfNeeded()
    {
        var currentlyLow = _devices
            .Where(device => device is { IsConnected: true, BatteryPercent: not null } &&
                             device.BatteryPercent <= _settings.LowBatteryThreshold)
            .ToArray();

        var recoveredIds = _lowBatteryNotifications
            .Where(id => currentlyLow.All(device => !string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        foreach (var id in recoveredIds)
        {
            _lowBatteryNotifications.Remove(id);
        }

        if (!_settings.LowBatteryNotifications)
        {
            return;
        }

        var newLowDevices = currentlyLow
            .Where(device => _lowBatteryNotifications.Add(device.Id))
            .ToArray();
        if (newLowDevices.Length == 0)
        {
            return;
        }

        var details = string.Join(", ", newLowDevices.Select(device => $"{device.Name}: {device.BatteryPercent}%"));
        _notifyIcon.ShowBalloonTip(
            5000,
            "Низкий заряд Bluetooth-устройства",
            details,
            ToolTipIcon.Warning);
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var text = _devices.Count == 0
            ? GetEmptyStateText()
            : string.Join(
                Environment.NewLine,
                _devices.Take(5).Select(device =>
                    $"{device.Name}: {(device.BatteryPercent is int percent ? $"{percent}%" : "нет данных")}"));

        _notifyIcon.ShowBalloonTip(4000, "BT Charge Indicator", text, ToolTipIcon.Info);
    }

    private static void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось открыть параметры Bluetooth.\n\n{ex.Message}",
                "BT Charge Indicator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    protected override void ExitThreadCore()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
        base.ExitThreadCore();
    }
}
