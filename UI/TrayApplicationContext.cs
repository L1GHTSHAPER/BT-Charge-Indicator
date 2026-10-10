using System.Diagnostics;
using BTChargeIndicator.Models;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BluetoothBatteryService _bluetoothService = new();
    private readonly BluetoothDeviceWatcher _deviceWatcher = new();
    private readonly DeviceConnectionTracker _connectionTracker = new(GetDeviceKey);
    private readonly ConnectionNotificationPresenter _connectionNotifications = new();
    private readonly UpdateService _updateService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly Bitmap _appLogo = AppBranding.CreateLogoBitmap(24);
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _deviceChangeTimer;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly HashSet<string> _lowBatteryNotifications = new(StringComparer.OrdinalIgnoreCase);
    // Images are shared across rebuilt menus and owned by this context.
    private readonly Dictionary<(TrayIconStyle Style, int Threshold), Bitmap> _trayIconPreviews = [];

    private AppSettings _settings;
    private IReadOnlyList<BluetoothBatteryDevice> _devices = [];
    private BluetoothAvailability _availability = BluetoothAvailability.Available;
    private string? _lastErrorMessage;
    private DateTime? _lastUpdated;
    private ReleaseInfo? _availableRelease;
    private Icon? _currentIcon;
    private bool _isExiting;
    private bool _automaticUpdateCheckStarted;
    private bool _isCheckingUpdates;
    private int _pendingDeviceChanges;
    private bool _processingDeviceChanges;
    private bool _deviceChangeRefreshPending;
    private bool _connectionRefreshPending;
    private DateTime? _connectionRetryAt;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        DesktopNotificationService.Initialize();
        _currentIcon = TrayIconRenderer.Create(
            null,
            _settings.IconStyle,
            _settings.LowBatteryThreshold);
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

        _deviceChangeTimer = new System.Windows.Forms.Timer
        {
            Interval = 500,
            Enabled = true
        };
        _deviceChangeTimer.Tick += async (_, _) => await ProcessDeviceChangesAsync();
        _deviceWatcher.Changed += DeviceWatcher_Changed;
        _deviceWatcher.Start();

        _ = RefreshAsync();
    }

    private void DeviceWatcher_Changed(BluetoothDeviceChangeKind change)
    {
        // DeviceWatcher callbacks run on worker threads; the timer consumes the
        // flags on the UI thread and coalesces Classic/BLE notifications.
        Interlocked.Or(ref _pendingDeviceChanges, (int)change);
    }

    private async Task ProcessDeviceChangesAsync()
    {
        if (_isExiting || _processingDeviceChanges)
        {
            return;
        }

        _processingDeviceChanges = true;
        try
        {
            var changes = (BluetoothDeviceChangeKind)Interlocked.Exchange(ref _pendingDeviceChanges, 0);
            _deviceChangeRefreshPending |= changes != BluetoothDeviceChangeKind.None;
            _connectionRefreshPending |= (changes & BluetoothDeviceChangeKind.Connected) ==
                                         BluetoothDeviceChangeKind.Connected;
            var retryDue = _connectionRetryAt.HasValue && DateTime.UtcNow >= _connectionRetryAt.Value;
            if (!_deviceChangeRefreshPending && !retryDue)
            {
                return;
            }

            if (!await RefreshAsync(refreshGattBattery: _connectionRefreshPending || retryDue) || _isExiting)
            {
                // Keep the request if a manual or scheduled scan is in progress.
                return;
            }

            _deviceChangeRefreshPending = false;
            if (_connectionRefreshPending)
            {
                // HFP/PnP battery data can arrive after the connection event.
                _connectionRefreshPending = false;
                _connectionRetryAt = DateTime.UtcNow.AddSeconds(5);
            }
            else if (retryDue)
            {
                _connectionRetryAt = null;
            }
        }
        finally
        {
            _processingDeviceChanges = false;
        }
    }

    private async Task<bool> RefreshAsync(bool refreshGattBattery = false)
    {
        if (_isExiting || !await _refreshLock.WaitAsync(0))
        {
            return false;
        }

        try
        {
            SetTrayText("BT Charge Indicator — обновление…");
            var result = await _bluetoothService.ScanAsync(refreshGattBattery);
            if (_isExiting)
            {
                return true;
            }
            _devices = result.Devices;
            _availability = result.Availability;
            _lastErrorMessage = result.ErrorMessage;
            _lastUpdated = DateTime.Now;

            ShowConnectionNotifications();

            UpdateTrayIcon();
            ReplaceContextMenu();
            ShowLowBatteryNotificationIfNeeded();

            if (!_automaticUpdateCheckStarted)
            {
                _automaticUpdateCheckStarted = true;
                _ = CheckForUpdatesIfNeededAsync();
            }
            return true;
        }
        finally
        {
            _refreshLock.Release();
            if (_isExiting)
            {
                _bluetoothService.Dispose();
                _refreshLock.Dispose();
            }
        }
    }

    private void ShowConnectionNotifications()
    {
        var changes = _connectionTracker.Observe(_devices, _availability);
        if (!_settings.ConnectionNotifications) return;
        foreach (var change in changes)
        {
            if (GetDevicePreferences(change.Device).IsHidden) continue;
            _connectionNotifications.Show(new ConnectionNotification(GetDeviceKey(change.Device),
                GetDisplayName(change.Device), change.IsConnected, change.Device.BatteryPercent));
        }
    }

    private void ShowConnectionPreview(bool connected)
    {
        var device = GetVisibleDevices().FirstOrDefault(item => item.IsConnected is true);
        _connectionNotifications.Show(new ConnectionNotification("preview",
            device is null ? "Bluetooth-наушники" : GetDisplayName(device), connected,
            device is null ? 76 : device.BatteryPercent));
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            MinimumSize = new Size(310, 0)
        };

        var title = new ToolStripLabel("BT Charge Indicator")
        {
            Image = _appLogo,
            ImageScaling = ToolStripItemImageScaling.None,
            Padding = new Padding(0, 4, 0, 4),
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
        };
        menu.Items.Add(title);
        menu.Items.Add(BuildStatusItem());
        menu.Items.Add(new ToolStripSeparator());

        var visibleDevices = GetVisibleDevices();
        if (visibleDevices.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem(
                _devices.Count == 0 ? GetEmptyStateText() : "Все найденные устройства скрыты")
            {
                Enabled = false
            });

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
            foreach (var device in visibleDevices)
            {
                menu.Items.Add(BuildDeviceMenuItem(device));
            }
        }

        var hiddenDevicesMenu = BuildHiddenDevicesMenu();
        if (hiddenDevicesMenu is not null)
        {
            menu.Items.Add(hiddenDevicesMenu);
        }

        menu.Items.Add(new ToolStripSeparator());

        var refreshItem = new ToolStripMenuItem("Обновить сейчас");
        refreshItem.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refreshItem);

        var bluetoothSettingsItem = new ToolStripMenuItem("Открыть параметры Bluetooth");
        bluetoothSettingsItem.Click += (_, _) => OpenBluetoothSettings();
        menu.Items.Add(bluetoothSettingsItem);

        var lowLatencyItem = new ToolStripMenuItem("Игровой режим / низкая задержка…")
        {
            ToolTipText = "Проверить поддержку Bluetooth LE Audio и открыть параметры Windows"
        };
        lowLatencyItem.Click += (_, _) => ShowLowLatencyModeHelp();
        menu.Items.Add(lowLatencyItem);

        menu.Items.Add(BuildRefreshIntervalMenu());
        menu.Items.Add(BuildTrayIconStyleMenu());
        menu.Items.Add(BuildLowBatteryThresholdMenu());

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

        var connectionMenu = new ToolStripMenuItem("Подключение устройств");
        var connectionNotificationsItem = new ToolStripMenuItem("Показывать анимацию")
        {
            CheckOnClick = true,
            Checked = _settings.ConnectionNotifications
        };
        connectionNotificationsItem.CheckedChanged += (_, _) =>
        {
            _settings.ConnectionNotifications = connectionNotificationsItem.Checked;
            if (!_settings.ConnectionNotifications) _connectionNotifications.Clear();
            _settingsStore.Save(_settings);
        };
        connectionMenu.DropDownItems.Add(connectionNotificationsItem);
        var previewConnectionItem = new ToolStripMenuItem("Пример подключения");
        previewConnectionItem.Click += (_, _) => ShowConnectionPreview(true);
        connectionMenu.DropDownItems.Add(previewConnectionItem);
        var previewDisconnectionItem = new ToolStripMenuItem("Пример отключения");
        previewDisconnectionItem.Click += (_, _) => ShowConnectionPreview(false);
        connectionMenu.DropDownItems.Add(previewDisconnectionItem);
        menu.Items.Add(connectionMenu);

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

        var automaticUpdatesItem = new ToolStripMenuItem("Автоматически проверять обновления")
        {
            CheckOnClick = true,
            Checked = _settings.CheckForUpdatesAutomatically
        };
        automaticUpdatesItem.CheckedChanged += (_, _) =>
        {
            _settings.CheckForUpdatesAutomatically = automaticUpdatesItem.Checked;
            _settingsStore.Save(_settings);
        };
        menu.Items.Add(automaticUpdatesItem);

        var updateItem = new ToolStripMenuItem(
            _availableRelease is null
                ? (_isCheckingUpdates ? "Проверка обновлений…" : "Проверить обновления")
                : $"Доступна версия {_availableRelease.Tag}")
        {
            Enabled = !_isCheckingUpdates
        };
        updateItem.Click += async (_, _) =>
        {
            if (_availableRelease is not null)
            {
                OpenUrl(_availableRelease.Url);
                return;
            }

            await CheckForUpdatesAsync(showResult: true);
        };
        menu.Items.Add(updateItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("О программе");
        aboutItem.Click += (_, _) => MessageBox.Show(
            $"BT Charge Indicator {Application.ProductVersion}\n\nПоказывает заряд сопряжённых Bluetooth-устройств в системном трее Windows.\n\nДвойной щелчок по иконке открывает параметры Bluetooth.",
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

    private ToolStripMenuItem BuildDeviceMenuItem(BluetoothBatteryDevice device)
    {
        var preferences = GetDevicePreferences(device);
        var item = new ToolStripMenuItem(FormatDevice(device))
        {
            ToolTipText = device.IsConnected switch
            {
                true => "Подключено",
                false => "Не подключено",
                _ => "Состояние подключения неизвестно"
            }
        };

        var openItem = new ToolStripMenuItem("Открыть параметры Bluetooth");
        openItem.Click += (_, _) => OpenBluetoothSettings();
        item.DropDownItems.Add(openItem);

        var diagnosticsItem = new ToolStripMenuItem("Диагностика устройства…");
        diagnosticsItem.Click += (_, _) => ShowDeviceDiagnostics(device);
        item.DropDownItems.Add(diagnosticsItem);

        var renameItem = new ToolStripMenuItem("Переименовать…");
        renameItem.Click += (_, _) =>
        {
            var value = TextPrompt.Show(
                "Имя устройства",
                "Введите имя. Пустое поле вернёт исходное название:",
                preferences.CustomName ?? device.Name);
            if (value is null)
            {
                return;
            }

            preferences.CustomName = string.IsNullOrWhiteSpace(value) ||
                                     string.Equals(value, device.Name, StringComparison.CurrentCulture)
                ? null
                : value;
            SaveDeviceSettings();
        };
        item.DropDownItems.Add(renameItem);
        item.DropDownItems.Add(new ToolStripSeparator());

        var trayItem = new ToolStripMenuItem("Учитывать в значке")
        {
            CheckOnClick = true,
            Checked = preferences.IncludeInTrayIcon
        };
        trayItem.CheckedChanged += (_, _) =>
        {
            preferences.IncludeInTrayIcon = trayItem.Checked;
            SaveDeviceSettings();
        };
        item.DropDownItems.Add(trayItem);

        var notificationItem = new ToolStripMenuItem("Уведомлять о низком заряде")
        {
            CheckOnClick = true,
            Checked = preferences.LowBatteryNotifications
        };
        notificationItem.CheckedChanged += (_, _) =>
        {
            preferences.LowBatteryNotifications = notificationItem.Checked;
            if (!notificationItem.Checked)
            {
                _lowBatteryNotifications.Remove(device.Id);
            }
            SaveDeviceSettings(updateIcon: false);
        };
        item.DropDownItems.Add(notificationItem);

        var visibleItem = new ToolStripMenuItem("Показывать в списке")
        {
            CheckOnClick = true,
            Checked = !preferences.IsHidden
        };
        visibleItem.CheckedChanged += (_, _) =>
        {
            preferences.IsHidden = !visibleItem.Checked;
            SaveDeviceSettings();
        };
        item.DropDownItems.Add(visibleItem);

        return item;
    }

    private ToolStripMenuItem? BuildHiddenDevicesMenu()
    {
        var hidden = _settings.Devices
            .Where(entry => entry.Value.IsHidden)
            .OrderBy(entry => entry.Value.CustomName ?? entry.Value.LastKnownName ?? entry.Key)
            .ToArray();
        if (hidden.Length == 0)
        {
            return null;
        }

        var menu = new ToolStripMenuItem($"Скрытые устройства ({hidden.Length})");
        foreach (var entry in hidden)
        {
            var preferences = entry.Value;
            var restoreItem = new ToolStripMenuItem(
                preferences.CustomName ?? preferences.LastKnownName ?? "Неизвестное устройство");
            restoreItem.Click += (_, _) =>
            {
                preferences.IsHidden = false;
                SaveDeviceSettings();
            };
            menu.DropDownItems.Add(restoreItem);
        }

        return menu;
    }

    private ToolStripMenuItem BuildLowBatteryThresholdMenu()
    {
        var menu = new ToolStripMenuItem($"Низкий заряд: {_settings.LowBatteryThreshold}%");
        foreach (var threshold in new[] { 10, 15, 20, 25, 30, 40, 50 })
        {
            var item = new ToolStripMenuItem($"{threshold}%")
            {
                Checked = _settings.LowBatteryThreshold == threshold
            };
            item.Click += (_, _) =>
            {
                _settings.LowBatteryThreshold = threshold;
                _settingsStore.Save(_settings);
                _lowBatteryNotifications.Clear();
                UpdateTrayIcon();
                ReplaceContextMenu();
            };
            menu.DropDownItems.Add(item);
        }

        return menu;
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

    private ToolStripMenuItem BuildTrayIconStyleMenu()
    {
        var styleMenu = new ToolStripMenuItem("Вид значка в трее");
        if (styleMenu.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowImageMargin = true;
            dropDown.ShowCheckMargin = true;
            dropDown.ImageScalingSize = new Size(20, 20);
        }
        AddTrayIconStyleItem(styleMenu, "Крупные цифры", TrayIconStyle.Percentage);
        AddTrayIconStyleItem(styleMenu, "Батарея", TrayIconStyle.Battery);
        AddTrayIconStyleItem(styleMenu, "Кольцо", TrayIconStyle.Ring);
        AddTrayIconStyleItem(styleMenu, "Сегментное кольцо", TrayIconStyle.SegmentedRing);
        AddTrayIconStyleItem(styleMenu, "Шкала", TrayIconStyle.Bars);
        AddTrayIconStyleItem(styleMenu, "Вертикальная батарея", TrayIconStyle.VerticalBattery);
        AddTrayIconStyleItem(styleMenu, "Цифры без фона", TrayIconStyle.Minimal);
        AddTrayIconStyleItem(styleMenu, "Капсула", TrayIconStyle.Capsule);
        AddTrayIconStyleItem(styleMenu, "Градиент", TrayIconStyle.Gradient);
        AddTrayIconStyleItem(styleMenu, "Дуга", TrayIconStyle.Gauge);
        return styleMenu;
    }

    private void AddTrayIconStyleItem(ToolStripMenuItem parent, string text, TrayIconStyle style)
    {
        var key = (style, _settings.LowBatteryThreshold);
        if (!_trayIconPreviews.TryGetValue(key, out var preview))
        {
            preview = TrayIconRenderer.CreateBitmap(73, style, _settings.LowBatteryThreshold);
            _trayIconPreviews.Add(key, preview);
        }
        var item = new ToolStripMenuItem(text)
        {
            Checked = _settings.IconStyle == style,
            Image = preview
        };
        item.Click += (_, _) =>
        {
            _settings.IconStyle = style;
            _settingsStore.Save(_settings);
            UpdateTrayIcon();
            ReplaceContextMenu();
        };
        parent.DropDownItems.Add(item);
    }

    private void ReplaceContextMenu()
    {
        var previous = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
        previous?.Dispose();
        // Release outdated previews only after the menu using them is disposed.
        foreach (var key in _trayIconPreviews.Keys
                     .Where(key => key.Threshold != _settings.LowBatteryThreshold).ToArray())
        {
            _trayIconPreviews[key].Dispose();
            _trayIconPreviews.Remove(key);
        }
    }

    private void UpdateTrayIcon()
    {
        var knownDevices = _devices
            .Where(device => device.BatteryPercent.HasValue &&
                             GetDevicePreferences(device).IncludeInTrayIcon)
            .ToArray();
        var connectedDevices = knownDevices.Where(device => device.IsConnected == true).ToArray();
        var relevantDevices = connectedDevices.Length > 0 ? connectedDevices : knownDevices;
        var lowestCharge = relevantDevices.Select(device => device.BatteryPercent).Min();

        var previousIcon = _currentIcon;
        _currentIcon = TrayIconRenderer.Create(
            lowestCharge,
            _settings.IconStyle,
            _settings.LowBatteryThreshold);
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

        var includedDevices = _devices
            .Where(device => GetDevicePreferences(device).IncludeInTrayIcon)
            .ToArray();
        var first = includedDevices.FirstOrDefault(device => device.BatteryPercent.HasValue);
        return first is null
            ? $"BT Charge Indicator — {includedDevices.Length} устр., заряд недоступен"
            : $"{GetDisplayName(first)}: {FormatBatterySummary(first)}";
    }

    private void SetTrayText(string text)
    {
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "…";
    }

    private IReadOnlyList<BluetoothBatteryDevice> GetVisibleDevices()
    {
        return _devices
            .Where(device => !GetDevicePreferences(device).IsHidden)
            .ToArray();
    }

    private DevicePreferences GetDevicePreferences(BluetoothBatteryDevice device)
    {
        var key = GetDeviceKey(device);
        if (!_settings.Devices.TryGetValue(key, out var preferences))
        {
            preferences = new DevicePreferences();
            _settings.Devices[key] = preferences;
        }

        preferences.LastKnownName = device.Name;
        return preferences;
    }

    private static string GetDeviceKey(BluetoothBatteryDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.Address))
        {
            return $"address:{device.Address.Trim()}";
        }

        if (device.ContainerId.HasValue)
        {
            return $"container:{device.ContainerId.Value:D}";
        }

        return $"id:{device.Id}";
    }

    private string GetDisplayName(BluetoothBatteryDevice device)
    {
        return GetDevicePreferences(device).CustomName ?? device.Name;
    }

    private static string FormatBatteryComponents(BatteryComponents? components)
    {
        if (components is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (components.LeftPercent.HasValue)
        {
            parts.Add($"Л {components.LeftPercent}%");
        }
        if (components.RightPercent.HasValue)
        {
            parts.Add($"П {components.RightPercent}%");
        }
        if (components.CasePercent.HasValue)
        {
            parts.Add($"кейс {components.CasePercent}%");
        }

        return parts.Count == 0 ? string.Empty : $"  [{string.Join(" · ", parts)}]";
    }

    private static string FormatBatterySummary(BluetoothBatteryDevice device)
    {
        if (device.Components is null)
        {
            return device.BatteryPercent is int percentage ? $"{percentage}%" : "нет данных";
        }

        var formatted = FormatBatteryComponents(device.Components).Trim();
        return formatted.Length >= 2 && formatted[0] == '[' && formatted[^1] == ']'
            ? formatted[1..^1]
            : formatted;
    }

    private void SaveDeviceSettings(bool updateIcon = true)
    {
        _settingsStore.Save(_settings);
        if (updateIcon)
        {
            UpdateTrayIcon();
        }
        ReplaceContextMenu();
    }

    private void ShowDeviceDiagnostics(BluetoothBatteryDevice device)
    {
        var components = device.Components is null
            ? "нет"
            : FormatBatteryComponents(device.Components).Trim();
        var categories = device.Categories.Count == 0
            ? "не указаны"
            : string.Join(", ", device.Categories);
        var connection = device.IsConnected switch
        {
            true => "подключено",
            false => "не подключено",
            _ => "неизвестно"
        };
        var source = device.BatterySource switch
        {
            BatteryReadingSource.WindowsDeviceProperty => "свойство батареи Windows",
            BatteryReadingSource.BluetoothHfp => "Bluetooth HFP",
            BatteryReadingSource.BluetoothGatt => "BLE GATT Battery Service",
            BatteryReadingSource.BluetoothGattComponents => "BLE GATT Battery Service (компоненты)",
            BatteryReadingSource.PlugAndPlay => "свойство Plug and Play",
            BatteryReadingSource.AirPodsAdvertisement => "Apple Continuity BLE",
            BatteryReadingSource.GoogleFastPairAdvertisement => "Google Fast Pair BLE",
            BatteryReadingSource.NothingRfcomm => "Nothing/CMF RFCOMM",
            BatteryReadingSource.DualSenseHid => "Bluetooth HID DualSense",
            _ => "источник заряда не найден"
        };

        var text =
            $"Устройство: {GetDisplayName(device)}\n" +
            $"Исходное имя: {device.Name}\n" +
            $"Состояние: {connection}\n" +
            $"Заряд: {(device.BatteryPercent is int percent ? $"{percent}%" : "нет данных")}\n" +
            $"Источник: {source}\n" +
            $"Компоненты: {components}\n" +
            $"Категории: {categories}\n" +
            $"Bluetooth CoD: {device.BluetoothClassMajor?.ToString() ?? "не указан"}\n" +
            $"Bluetooth-адрес: {device.Address ?? "не указан"}\n" +
            $"Container ID: {device.ContainerId?.ToString("D") ?? "не указан"}\n\n" +
            $"ID устройства:\n{device.Id}\n\n" +
            "Совет: Ctrl+C скопирует текст этого окна.";

        MessageBox.Show(
            text,
            "Диагностика Bluetooth-устройства",
            MessageBoxButtons.OK,
            device.BatteryPercent.HasValue ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task CheckForUpdatesIfNeededAsync()
    {
        if (!_settings.CheckForUpdatesAutomatically ||
            _settings.LastUpdateCheckUtc is DateTimeOffset lastCheck &&
            DateTimeOffset.UtcNow - lastCheck < TimeSpan.FromHours(24))
        {
            return;
        }

        await CheckForUpdatesAsync(showResult: false);
    }

    private async Task CheckForUpdatesAsync(bool showResult)
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        _isCheckingUpdates = true;
        ReplaceContextMenu();
        try
        {
            var release = await _updateService.GetLatestReleaseAsync();
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settingsStore.Save(_settings);

            if (release is not null && UpdateService.IsNewerThanCurrent(release))
            {
                _availableRelease = release;
                if (showResult)
                {
                    var open = MessageBox.Show(
                        $"Доступна версия {release.Tag}. Открыть страницу загрузки?",
                        "Обновление BT Charge Indicator",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    if (open == DialogResult.Yes)
                    {
                        OpenUrl(release.Url);
                    }
                }
                else if (!DesktopNotificationService.TryShow(
                             "Доступно обновление BT Charge Indicator",
                             $"Версия {release.Tag} готова к загрузке.",
                             "Открыть страницу",
                             release.Url))
                {
                    _notifyIcon.ShowBalloonTip(
                        5000,
                        "Доступно обновление BT Charge Indicator",
                        $"Версия {release.Tag} готова к загрузке.",
                        ToolTipIcon.Info);
                }
            }
            else if (showResult)
            {
                MessageBox.Show(
                    "Установлена актуальная версия.",
                    "Обновление BT Charge Indicator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update check failed.", ex);
            if (showResult)
            {
                MessageBox.Show(
                    $"Не удалось проверить обновления.\n\n{ex.Message}",
                    "Обновление BT Charge Indicator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _isCheckingUpdates = false;
            if (!_isExiting)
            {
                ReplaceContextMenu();
            }
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось открыть ссылку.\n\n{ex.Message}",
                "BT Charge Indicator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string FormatDevice(BluetoothBatteryDevice device)
    {
        var connection = device.IsConnected switch
        {
            true => "●",
            false => "○",
            null => "◌"
        };
        var battery = device.BatteryPercent is int percent ? $"{percent,3}%" : "  — ";
        var components = FormatBatteryComponents(device.Components);
        return $"{connection}  {battery}   {GetDisplayName(device)}{components}";
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
                             device.BatteryPercent <= _settings.LowBatteryThreshold &&
                             GetDevicePreferences(device).LowBatteryNotifications)
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

        var details = string.Join(", ", newLowDevices.Select(FormatLowBatteryDevice));
        if (!DesktopNotificationService.TryShow(
                "Низкий заряд Bluetooth-устройства",
                details,
                "Открыть Bluetooth",
                "ms-settings:bluetooth",
                allowSnooze: true))
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                "Низкий заряд Bluetooth-устройства",
                details,
                ToolTipIcon.Warning);
        }
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var visibleDevices = GetVisibleDevices();
        var text = visibleDevices.Count == 0
            ? GetEmptyStateText()
            : string.Join(
                Environment.NewLine,
                visibleDevices.Take(5).Select(device =>
                    $"{GetDisplayName(device)}: {FormatBatterySummary(device)}"));

        _notifyIcon.ShowBalloonTip(4000, "BT Charge Indicator", text, ToolTipIcon.Info);
    }

    private string FormatLowBatteryDevice(BluetoothBatteryDevice device)
    {
        if (device.Components is null)
        {
            return $"{GetDisplayName(device)}: {device.BatteryPercent}%";
        }

        var lowComponents = new[]
            {
                (Name: "Л", Percentage: device.Components.LeftPercent),
                (Name: "П", Percentage: device.Components.RightPercent),
                (Name: "кейс", Percentage: device.Components.CasePercent)
            }
            .Where(component => component.Percentage.HasValue &&
                                component.Percentage <= _settings.LowBatteryThreshold)
            .Select(component => $"{component.Name} {component.Percentage}%")
            .ToArray();

        return lowComponents.Length == 0
            ? $"{GetDisplayName(device)}: {device.BatteryPercent}%"
            : $"{GetDisplayName(device)}: {string.Join(" · ", lowComponents)}";
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

    private static void ShowLowLatencyModeHelp()
    {
        var openSettings = MessageBox.Show(
            "Windows не может принудительно включить игровой режим конкретных наушников: " +
            "его включает производитель в наушниках или в фирменном приложении.\n\n" +
            "Для более низкой задержки в Windows 11 проверьте Bluetooth LE Audio:\n" +
            "1. Откройте «Bluetooth и устройства» → «Устройства».\n" +
            "2. Включите «Использовать LE Audio, если доступно».\n\n" +
            "Пункт появится только если его поддерживают и Bluetooth-адаптер с драйвером, и наушники. " +
            "Открыть параметры Bluetooth?",
            "Игровой режим / низкая задержка",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (openSettings == DialogResult.Yes)
        {
            OpenBluetoothSettings();
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
        _deviceChangeTimer.Stop();
        _deviceWatcher.Changed -= DeviceWatcher_Changed;
        _deviceWatcher.Dispose();
        _connectionNotifications.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        foreach (var preview in _trayIconPreviews.Values)
        {
            preview.Dispose();
        }
        _trayIconPreviews.Clear();
        _appLogo.Dispose();
        _currentIcon?.Dispose();
        _refreshTimer.Dispose();
        _deviceChangeTimer.Dispose();
        if (_refreshLock.CurrentCount > 0)
        {
            _bluetoothService.Dispose();
            _refreshLock.Dispose();
        }
        _updateService.Dispose();
        base.ExitThreadCore();
    }
}
