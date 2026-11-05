using System.Diagnostics;
using BTChargeIndicator.Localization;
using BTChargeIndicator.Models;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;
using BTChargeIndicator.Runtime;

namespace BTChargeIndicator.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BluetoothBatteryScannerAdapter _scanner = new();
    private readonly ApplicationRuntime _runtime;
    private readonly BluetoothDeviceWatcher _deviceWatcher = new();
    private readonly DeviceConnectionTracker _connectionTracker = new(GetDeviceKey);
    private readonly ConnectionNotificationPresenter _connectionNotifications = new();
    private readonly UpdateService _updateService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly BatteryProfileStore _profileStore = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly Bitmap _appLogo = AppBranding.CreateLogoBitmap(24);
    private readonly System.Windows.Forms.Timer _deviceChangeTimer;
    private readonly HashSet<string> _lowBatteryNotifications = new(StringComparer.OrdinalIgnoreCase);
    // Images are shared across rebuilt menus and owned by this context.
    private readonly Dictionary<(TrayIconStyle Style, int Threshold), Bitmap> _trayIconPreviews = [];
    private readonly Dictionary<string, DeviceTrayIconState> _deviceTrayIcons =
        new(StringComparer.OrdinalIgnoreCase);

    private AppSettings _settings;
    private IReadOnlyList<BatteryProfile> _profiles = [];
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
    private ApplicationRuntimeState? _pendingRuntimeState;
    private DateTime? _connectionRetryAt;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _profiles = _profileStore.Load();
        _runtime = new ApplicationRuntime(_scanner);
        _runtime.StateChanged += Runtime_StateChanged;
        AppText.Apply(_settings.Language);
        DesktopNotificationService.Initialize();
        _currentIcon = TrayIconRenderer.Create(
            null,
            _settings.IconStyle,
            _settings.LowBatteryThreshold);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = AppText.Get("Loading"),
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.DoubleClick += (_, _) => OpenBluetoothSettings();
        _connectionNotifications.Idle += ConnectionNotifications_Idle;

        _deviceChangeTimer = new System.Windows.Forms.Timer
        {
            Interval = 500,
            Enabled = true
        };
        _deviceChangeTimer.Tick += async (_, _) => await ProcessDeviceChangesAsync();
        _deviceWatcher.Changed += DeviceWatcher_Changed;
        _deviceWatcher.Start();

        _ = StartRuntimeAsync();
    }

    private void DeviceWatcher_Changed(BluetoothDeviceChangeKind change)
    {
        // DeviceWatcher callbacks run on worker threads; the timer consumes the
        // flags on the UI thread and coalesces Classic/BLE notifications.
        Interlocked.Or(ref _pendingDeviceChanges, (int)change);
    }

    private async Task ProcessDeviceChangesAsync()
    {
        ApplyPendingRuntimeState();
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

    private async Task StartRuntimeAsync()
    {
        try
        {
            await _runtime.StartAsync(TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Application runtime could not be started.", ex);
        }
    }

    private void Runtime_StateChanged(ApplicationRuntimeState state)
    {
        if (!_isExiting)
        {
            // The runtime can publish from a worker thread. The existing WinForms
            // timer consumes the latest snapshot on the UI thread.
            Volatile.Write(ref _pendingRuntimeState, state);
        }
    }

    private void ApplyPendingRuntimeState()
    {
        var state = Interlocked.Exchange(ref _pendingRuntimeState, null);
        if (state is null || state.IsScanning || _isExiting)
        {
            return;
        }

        _devices = state.Devices;
        _availability = state.Availability;
        _lastErrorMessage = state.ErrorMessage;
        _lastUpdated = state.LastUpdatedUtc?.LocalDateTime;

        ShowConnectionNotifications();
        UpdateTrayIcon();
        ReplaceContextMenu();
        ShowLowBatteryNotificationIfNeeded();

        if (!_automaticUpdateCheckStarted)
        {
            _automaticUpdateCheckStarted = true;
            _ = CheckForUpdatesIfNeededAsync();
        }
    }

    private async Task<bool> RefreshAsync(bool refreshGattBattery = false)
    {
        if (_isExiting)
        {
            return false;
        }

        try
        {
            await _runtime.RefreshAsync(refreshGattBattery);
            return !_isExiting;
        }
        catch (Exception ex)
        {
            if (!_isExiting)
            {
                AppLogger.Error("Bluetooth refresh request failed.", ex);
            }
            return false;
        }
    }

    private async Task UpdateRuntimeRefreshIntervalAsync(int seconds)
    {
        try
        {
            await _runtime.UpdateRefreshIntervalAsync(TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            if (!_isExiting)
            {
                AppLogger.Error("Bluetooth refresh interval could not be updated.", ex);
            }
        }
    }

    private async Task ShutdownRuntimeAsync()
    {
        try
        {
            await _runtime.DisposeAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Application runtime shutdown failed.", ex);
        }
        finally
        {
            _scanner.Dispose();
        }
    }

    private void ShowConnectionNotifications()
    {
        var changes = _connectionTracker.Observe(BatteryDeviceIdentity.Deduplicate(_devices), _availability);
        if (!_settings.ConnectionNotifications) return;
        foreach (var change in changes)
        {
            if (GetDevicePreferences(change.Device).IsHidden) continue;
            _connectionNotifications.Show(new ConnectionNotification(GetDeviceKey(change.Device),
                GetDisplayName(change.Device), change.IsConnected, GetEffectiveBatteryPercent(change.Device)));
        }
    }

    private void ShowConnectionPreview(bool connected)
    {
        var device = GetVisibleDevices().FirstOrDefault(item => item.IsConnected is true);
        _connectionNotifications.Show(new ConnectionNotification("preview",
            device is null ? AppText.Get("PreviewDevice") : GetDisplayName(device), connected,
            device is null ? 76 : GetEffectiveBatteryPercent(device) ?? 76));
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
                _devices.Count == 0 ? GetEmptyStateText() : AppText.Get("AllDevicesHidden"))
            {
                Enabled = false
            });

            if (_availability == BluetoothAvailability.Error && !string.IsNullOrWhiteSpace(_lastErrorMessage))
            {
                var detailsItem = new ToolStripMenuItem(AppText.Get("ErrorDetails"));
                detailsItem.Click += (_, _) => MessageBox.Show(
                    AppText.Format("ErrorWithLog", _lastErrorMessage, AppLogger.LogPath),
                    AppText.Get("BluetoothError"),
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

        var refreshItem = new ToolStripMenuItem(AppText.Get("RefreshNow"));
        refreshItem.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refreshItem);

        var settingsItem = new ToolStripMenuItem(AppText.Get("SettingsTitle"));
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var bluetoothSettingsItem = new ToolStripMenuItem(AppText.Get("BluetoothSettings"));
        bluetoothSettingsItem.Click += (_, _) => OpenBluetoothSettings();
        menu.Items.Add(bluetoothSettingsItem);

        var lowLatencyItem = new ToolStripMenuItem(AppText.Get("LowLatencyMenu"))
        {
            ToolTipText = AppText.Get("LowLatencyTooltip")
        };
        lowLatencyItem.Click += (_, _) => ShowLowLatencyModeHelp();
        menu.Items.Add(lowLatencyItem);

        menu.Items.Add(BuildLanguageMenu());
        menu.Items.Add(BuildRefreshIntervalMenu());
        menu.Items.Add(BuildTrayIconStyleMenu());
        menu.Items.Add(BuildGlobalAggregationMenu());
        var manageProfilesItem = new ToolStripMenuItem(AppText.Get("ManageProfiles"));
        manageProfilesItem.Click += (_, _) => OpenBatteryProfileEditor();
        menu.Items.Add(manageProfilesItem);
        menu.Items.Add(BuildLowBatteryThresholdMenu());

        var notificationsItem = new ToolStripMenuItem(AppText.Get("LowBatteryNotifications"))
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

        var connectionMenu = new ToolStripMenuItem(AppText.Get("DeviceConnections"));
        var connectionNotificationsItem = new ToolStripMenuItem(AppText.Get("ShowAnimation"))
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
        var previewConnectionItem = new ToolStripMenuItem(AppText.Get("PreviewConnection"));
        previewConnectionItem.Click += (_, _) => ShowConnectionPreview(true);
        connectionMenu.DropDownItems.Add(previewConnectionItem);
        var previewDisconnectionItem = new ToolStripMenuItem(AppText.Get("PreviewDisconnection"));
        previewDisconnectionItem.Click += (_, _) => ShowConnectionPreview(false);
        connectionMenu.DropDownItems.Add(previewDisconnectionItem);
        menu.Items.Add(connectionMenu);

        var autoStartItem = new ToolStripMenuItem(AppText.Get("StartWithWindows"))
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
                    AppText.Format("AutoStartError", ex.Message),
                    "BT Charge Indicator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        menu.Items.Add(autoStartItem);

        var automaticUpdatesItem = new ToolStripMenuItem(AppText.Get("AutomaticUpdates"))
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
                ? (_isCheckingUpdates ? AppText.Get("CheckingUpdates") : AppText.Get("CheckUpdates"))
                : AppText.Format("VersionAvailable", _availableRelease.Tag))
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

        var aboutItem = new ToolStripMenuItem(AppText.Get("About"));
        aboutItem.Click += (_, _) => MessageBox.Show(
            AppText.Format("AboutText", Application.ProductVersion),
            AppText.Get("About"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        menu.Items.Add(aboutItem);

        var exitItem = new ToolStripMenuItem(AppText.Get("Exit"));
        exitItem.Click += (_, _) => ExitThread();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OpenSettings()
    {
        using var settingsForm = new SettingsForm(
            _settings, _devices, _profileStore, _profiles, ApplySettings);
        settingsForm.ShowDialog();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings.DeepClone();
        _settingsStore.Save(_settings);
        AppText.Apply(_settings.Language);
        _ = UpdateRuntimeRefreshIntervalAsync(_settings.RefreshIntervalSeconds);
        _lowBatteryNotifications.Clear();
        UpdateTrayIcon();
        ReplaceContextMenu();
        _connectionNotifications.RefreshLanguage();
        ShowLowBatteryNotificationIfNeeded();
    }

    private void OpenBatteryProfileEditor()
    {
        using var editor = new BatteryProfileEditorForm(_profiles, _profileStore);
        if (editor.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _profiles = _profileStore.Load();
        UpdateTrayIcon();
        ReplaceContextMenu();
    }

    private ToolStripMenuItem BuildStatusItem()
    {
        var status = _availability switch
        {
            BluetoothAvailability.TurnedOff => AppText.Get("BluetoothOff"),
            BluetoothAvailability.NotFound => AppText.Get("AdapterNotFound"),
            BluetoothAvailability.AccessDenied => AppText.Get("BluetoothAccessDenied"),
            BluetoothAvailability.Error => AppText.Get("DataUnavailable"),
            _ when _lastUpdated.HasValue => AppText.Format("UpdatedAt", _lastUpdated),
            _ => AppText.Get("ReadingData")
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
                true => AppText.Get("Connected"),
                false => AppText.Get("NotConnected"),
                _ => AppText.Get("ConnectionUnknown")
            }
        };

        var openItem = new ToolStripMenuItem(AppText.Get("BluetoothSettings"));
        openItem.Click += (_, _) => OpenBluetoothSettings();
        item.DropDownItems.Add(openItem);

        var diagnosticsItem = new ToolStripMenuItem(AppText.Get("DeviceDiagnosticsMenu"));
        diagnosticsItem.Click += (_, _) => ShowDeviceDiagnostics(FindCurrentDevice(GetDeviceKey(device), device));
        item.DropDownItems.Add(diagnosticsItem);
        item.DropDownItems.Add(BuildDeviceProfileMenu(device, preferences));

        var renameItem = new ToolStripMenuItem(AppText.Get("Rename"));
        renameItem.Click += (_, _) =>
        {
            var value = TextPrompt.Show(
                AppText.Get("DeviceName"),
                AppText.Get("RenamePrompt"),
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

        var trayItem = new ToolStripMenuItem(AppText.Get("IncludeInTrayIcon"))
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

        var individualTrayItem = new ToolStripMenuItem(AppText.Get("IndividualTrayIcon"))
        {
            CheckOnClick = true,
            Checked = preferences.ShowIndividualTrayIcon
        };
        individualTrayItem.CheckedChanged += (_, _) =>
        {
            preferences.ShowIndividualTrayIcon = individualTrayItem.Checked;
            SaveDeviceSettings();
        };
        item.DropDownItems.Add(individualTrayItem);
        item.DropDownItems.Add(BuildDeviceTrayIconStyleMenu(preferences));

        var notificationItem = new ToolStripMenuItem(AppText.Get("DeviceLowBatteryNotifications"))
        {
            CheckOnClick = true,
            Checked = preferences.LowBatteryNotifications
        };
        notificationItem.CheckedChanged += (_, _) =>
        {
            preferences.LowBatteryNotifications = notificationItem.Checked;
            if (!notificationItem.Checked)
            {
                _lowBatteryNotifications.Remove(GetDeviceKey(device));
            }
            SaveDeviceSettings(updateIcon: false);
        };
        item.DropDownItems.Add(notificationItem);

        var visibleItem = new ToolStripMenuItem(AppText.Get("ShowInList"))
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

    private ToolStripMenuItem BuildDeviceProfileMenu(
        BluetoothBatteryDevice device,
        DevicePreferences preferences)
    {
        var menu = new ToolStripMenuItem(AppText.Get("BatteryProfile"));
        var defaultItem = new ToolStripMenuItem(AppText.Get("DefaultProfile"))
        {
            Checked = string.IsNullOrWhiteSpace(preferences.ProfileId) ||
                      string.Equals(preferences.ProfileId, BatteryProfileEngine.DefaultProfileId,
                          StringComparison.OrdinalIgnoreCase)
        };
        defaultItem.Click += (_, _) =>
        {
            preferences.ProfileId = null;
            SaveDeviceSettings();
        };
        menu.DropDownItems.Add(defaultItem);

        foreach (var profile in _profiles.Where(profile =>
                     !string.Equals(profile.Id, BatteryProfileEngine.DefaultProfileId,
                         StringComparison.OrdinalIgnoreCase)))
        {
            var item = new ToolStripMenuItem(profile.Name)
            {
                Checked = string.Equals(preferences.ProfileId, profile.Id,
                    StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                preferences.ProfileId = profile.Id;
                SaveDeviceSettings();
            };
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private ToolStripMenuItem BuildDeviceTrayIconStyleMenu(DevicePreferences preferences)
    {
        var menu = new ToolStripMenuItem(AppText.Get("IndividualTrayIconStyle"));
        ConfigureTrayIconPreviewMenu(menu);
        var inheritItem = new ToolStripMenuItem(AppText.Get("UseGlobalTrayIconStyle"))
        {
            Checked = !preferences.IndividualTrayIconStyle.HasValue
        };
        inheritItem.Click += (_, _) =>
        {
            preferences.IndividualTrayIconStyle = null;
            SaveDeviceSettings();
        };
        menu.DropDownItems.Add(inheritItem);

        foreach (var (style, key) in GetTrayIconStyleLabels())
        {
            AddTrayIconStyleItem(menu, AppText.Get(key), style,
                preferences.IndividualTrayIconStyle,
                selectedStyle =>
                {
                    preferences.IndividualTrayIconStyle = selectedStyle;
                    SaveDeviceSettings();
                });
        }
        return menu;
    }

    private static (TrayIconStyle Style, string Key)[] GetTrayIconStyleLabels() =>
    [
        (TrayIconStyle.Percentage, "StylePercentage"),
        (TrayIconStyle.Battery, "StyleBattery"),
        (TrayIconStyle.Ring, "StyleRing"),
        (TrayIconStyle.SegmentedRing, "StyleSegmentedRing"),
        (TrayIconStyle.Bars, "StyleBars"),
        (TrayIconStyle.VerticalBattery, "StyleVerticalBattery"),
        (TrayIconStyle.Minimal, "StyleMinimal"),
        (TrayIconStyle.Capsule, "StyleCapsule"),
        (TrayIconStyle.Gradient, "StyleGradient"),
        (TrayIconStyle.Gauge, "StyleGauge")
    ];

    private void UpdateIndividualTrayIcons()
    {
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in BatteryDeviceIdentity.Deduplicate(_devices))
        {
            var preferences = GetDevicePreferences(device);
            if (!preferences.ShowIndividualTrayIcon)
            {
                continue;
            }

            var key = GetDeviceKey(device);
            if (!activeKeys.Add(key))
            {
                continue;
            }

            var style = preferences.IndividualTrayIconStyle ?? _settings.IconStyle;
            var icon = TrayIconRenderer.Create(
                GetEffectiveBatteryPercent(device),
                style,
                _settings.LowBatteryThreshold);

            if (_deviceTrayIcons.TryGetValue(key, out var existing))
            {
                var previous = existing.Icon;
                existing.Icon = icon;
                existing.NotifyIcon.Icon = icon;
                existing.NotifyIcon.Text = BuildDeviceTrayTooltip(device);
                var previousMenu = existing.NotifyIcon.ContextMenuStrip;
                existing.NotifyIcon.ContextMenuStrip = BuildDeviceTrayContextMenu(device);
                previousMenu?.Dispose();
                previous.Dispose();
            }
            else
            {
                var notifyIcon = new NotifyIcon
                {
                    Icon = icon,
                    Visible = true,
                    Text = BuildDeviceTrayTooltip(device),
                    ContextMenuStrip = BuildDeviceTrayContextMenu(device)
                };
                notifyIcon.DoubleClick += (_, _) => OpenBluetoothSettings();
                _deviceTrayIcons.Add(key, new DeviceTrayIconState(notifyIcon, icon));
            }
        }

        foreach (var key in _deviceTrayIcons.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            _deviceTrayIcons[key].Dispose();
            _deviceTrayIcons.Remove(key);
        }
    }

    private ContextMenuStrip BuildDeviceTrayContextMenu(BluetoothBatteryDevice device)
    {
        var menu = new ContextMenuStrip { ShowCheckMargin = true };
        var deviceKey = GetDeviceKey(device);
        var detailsItem = new ToolStripMenuItem(AppText.Get("ShowDeviceBatteryDetails"));
        detailsItem.Click += (_, _) =>
        {
            var currentDevice = FindCurrentDevice(deviceKey, device);
            var text = $"{GetDisplayName(currentDevice)}: {FormatBatterySummary(currentDevice)}";
            _deviceTrayIcons.GetValueOrDefault(deviceKey)?.NotifyIcon.ShowBalloonTip(
                3500, "BT Charge Indicator", text, ToolTipIcon.Info);
        };
        menu.Items.Add(detailsItem);
        menu.Items.Add(new ToolStripSeparator());

        // Reuse the exact actions from the main tray icon's device submenu.
        using var deviceMenuItem = BuildDeviceMenuItem(device);
        foreach (ToolStripItem child in deviceMenuItem.DropDownItems.Cast<ToolStripItem>().ToArray())
        {
            deviceMenuItem.DropDownItems.Remove(child);
            menu.Items.Add(child);
        }
        return menu;
    }

    private BluetoothBatteryDevice FindCurrentDevice(string deviceKey, BluetoothBatteryDevice fallback) =>
        BatteryDeviceIdentity.Deduplicate(_devices).FirstOrDefault(candidate =>
            string.Equals(GetDeviceKey(candidate), deviceKey, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    private string BuildDeviceTrayTooltip(BluetoothBatteryDevice device)
    {
        var percentage = GetEffectiveBatteryPercent(device);
        var components = FormatBatteryComponents(device.Components).Trim();
        var text = components.Length == 0
            ? $"{GetDisplayName(device)}: {(percentage.HasValue ? $"{percentage}%" : AppText.Get("NoData"))}"
            : $"{GetDisplayName(device)}: {(percentage.HasValue ? $"{percentage}%" : AppText.Get("NoData"))} {components}";
        return text.Length <= 63 ? text : text[..60] + "…";
    }

    private ToolStripMenuItem BuildGlobalAggregationMenu()
    {
        var menu = new ToolStripMenuItem(AppText.Get("GlobalBatteryAggregation"));
        foreach (var (mode, key) in new[]
                 {
                     (BatteryAggregationMode.Lowest, "AggregationLowest"),
                     (BatteryAggregationMode.Highest, "AggregationHighest"),
                     (BatteryAggregationMode.Average, "AggregationAverage")
                 })
        {
            var item = new ToolStripMenuItem(AppText.Get(key))
            {
                Checked = _settings.GlobalAggregationMode == mode
            };
            item.Click += (_, _) =>
            {
                _settings.GlobalAggregationMode = mode;
                _settingsStore.Save(_settings);
                UpdateTrayIcon();
                ReplaceContextMenu();
            };
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private int? GetEffectiveBatteryPercent(BluetoothBatteryDevice device)
    {
        var preferences = GetDevicePreferences(device);
        var profile = BatteryProfileEngine.Resolve(device, _profiles, preferences.ProfileId);
        return BatteryProfileEngine.GetBatteryPercent(device, profile);
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

        var menu = new ToolStripMenuItem(AppText.Format("HiddenDevices", hidden.Length));
        foreach (var entry in hidden)
        {
            var preferences = entry.Value;
            var restoreItem = new ToolStripMenuItem(
                preferences.CustomName ?? preferences.LastKnownName ?? AppText.Get("UnknownDevice"));
            restoreItem.Click += (_, _) =>
            {
                preferences.IsHidden = false;
                SaveDeviceSettings();
            };
            menu.DropDownItems.Add(restoreItem);
        }

        return menu;
    }

    private ToolStripMenuItem BuildLanguageMenu()
    {
        var menu = new ToolStripMenuItem(AppText.Get("Language"));
        foreach (var (language, key) in new[]
                 {
                     (AppLanguage.Auto, "LanguageAuto"),
                     (AppLanguage.Russian, "LanguageRussian"),
                     (AppLanguage.English, "LanguageEnglish")
                 })
        {
            var item = new ToolStripMenuItem(AppText.Get(key))
            {
                Checked = _settings.Language == language
            };
            item.Click += (_, _) =>
            {
                _settings.Language = language;
                _settingsStore.Save(_settings);
                AppText.Apply(language);
                SetTrayText(BuildToolTip());
                ReplaceContextMenu();
                _connectionNotifications.RefreshLanguage();
            };
            menu.DropDownItems.Add(item);
        }
        return menu;
    }

    private ToolStripMenuItem BuildLowBatteryThresholdMenu()
    {
        var menu = new ToolStripMenuItem(AppText.Format("LowBatteryThreshold", _settings.LowBatteryThreshold));
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
        var intervalMenu = new ToolStripMenuItem(AppText.Get("RefreshInterval"));
        AddIntervalItem(intervalMenu, AppText.Get("Seconds30"), 30);
        AddIntervalItem(intervalMenu, AppText.Get("Minute1"), 60);
        AddIntervalItem(intervalMenu, AppText.Get("Minutes5"), 300);
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
            _ = UpdateRuntimeRefreshIntervalAsync(seconds);
            ReplaceContextMenu();
        };
        parent.DropDownItems.Add(item);
    }

    private ToolStripMenuItem BuildTrayIconStyleMenu()
    {
        var styleMenu = new ToolStripMenuItem(AppText.Get("TrayIconStyle"));
        ConfigureTrayIconPreviewMenu(styleMenu);
        foreach (var (style, key) in GetTrayIconStyleLabels())
        {
            AddTrayIconStyleItem(styleMenu, AppText.Get(key), style, _settings.IconStyle, selectedStyle =>
            {
                _settings.IconStyle = selectedStyle;
                _settingsStore.Save(_settings);
                UpdateTrayIcon();
                ReplaceContextMenu();
            });
        }
        return styleMenu;
    }

    private static void ConfigureTrayIconPreviewMenu(ToolStripMenuItem menu)
    {
        if (menu.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowImageMargin = true;
            dropDown.ShowCheckMargin = true;
            dropDown.ImageScalingSize = new Size(20, 20);
        }
    }

    private void AddTrayIconStyleItem(
        ToolStripMenuItem parent, string text, TrayIconStyle style,
        TrayIconStyle? selectedStyle, Action<TrayIconStyle> onSelected)
    {
        var key = (style, _settings.LowBatteryThreshold);
        if (!_trayIconPreviews.TryGetValue(key, out var preview))
        {
            preview = TrayIconRenderer.CreateBitmap(73, style, _settings.LowBatteryThreshold);
            _trayIconPreviews.Add(key, preview);
        }
        var item = new ToolStripMenuItem(text)
        {
            Checked = selectedStyle == style,
            Image = preview
        };
        item.Click += (_, _) => onSelected(style);
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
        var knownDevices = BatteryDeviceIdentity.Deduplicate(_devices)
            .Where(device => GetEffectiveBatteryPercent(device).HasValue &&
                             GetDevicePreferences(device).IncludeInTrayIcon)
            .ToArray();
        var connectedDevices = knownDevices.Where(device => device.IsConnected == true).ToArray();
        var relevantDevices = connectedDevices.Length > 0 ? connectedDevices : knownDevices;
        var displayedCharge = BatteryAggregationEngine.AggregateValues(
            relevantDevices.Select(GetEffectiveBatteryPercent), _settings.GlobalAggregationMode);

        var previousIcon = _currentIcon;
        _currentIcon = TrayIconRenderer.Create(
            displayedCharge,
            _settings.IconStyle,
            _settings.LowBatteryThreshold);
        _notifyIcon.Icon = _currentIcon;
        previousIcon?.Dispose();

        SetTrayText(BuildToolTip());
        UpdateIndividualTrayIcons();
    }

    private string BuildToolTip()
    {
        if (_availability == BluetoothAvailability.TurnedOff)
        {
            return AppText.Get("TooltipBluetoothOff");
        }

        if (_devices.Count == 0)
        {
            return AppText.Get("TooltipNoDevices");
        }

        var includedDevices = BatteryDeviceIdentity.Deduplicate(_devices)
            .Where(device => GetDevicePreferences(device).IncludeInTrayIcon)
            .ToArray();
        var knownDevices = includedDevices
            .Where(device => GetEffectiveBatteryPercent(device).HasValue)
            .ToArray();
        var connectedDevices = knownDevices.Where(device => device.IsConnected == true).ToArray();
        var relevantDevices = connectedDevices.Length > 0 ? connectedDevices : knownDevices;
        var first = relevantDevices.FirstOrDefault();
        if (first is null)
        {
            return AppText.Format("TooltipBatteryUnavailable", includedDevices.Length);
        }

        var percentage = BatteryAggregationEngine.AggregateValues(
            relevantDevices.Select(GetEffectiveBatteryPercent), _settings.GlobalAggregationMode);
        var details = first.Components is null
            ? string.Empty
            : $" ({FormatBatterySummary(first)})";
        var otherDevices = relevantDevices.Length > 1 ? $" +{relevantDevices.Length - 1}" : string.Empty;
        return $"{percentage}% · {GetDisplayName(first)}{otherDevices}{details}";
    }

    private void SetTrayText(string text)
    {
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "…";
    }

    private IReadOnlyList<BluetoothBatteryDevice> GetVisibleDevices()
    {
        return BatteryDeviceIdentity.Deduplicate(_devices)
            .Where(device => !GetDevicePreferences(device).IsHidden)
            .ToArray();
    }

    private DevicePreferences GetDevicePreferences(BluetoothBatteryDevice device)
    {
        var key = GetDeviceKey(device);
        if (!_settings.Devices.TryGetValue(key, out var preferences) || preferences is null)
        {
            // Migrate the previous address-based key to the stable Windows container ID.
            var legacyAddressKey = string.IsNullOrWhiteSpace(device.Address)
                ? null
                : $"address:{device.Address.Trim()}";
            if (key.StartsWith("container:", StringComparison.OrdinalIgnoreCase) &&
                legacyAddressKey is not null &&
                _settings.Devices.Remove(legacyAddressKey, out var legacyPreferences) &&
                legacyPreferences is not null)
            {
                preferences = legacyPreferences;
                _settings.Devices[key] = preferences;
                _settingsStore.Save(_settings);
            }
            else
            {
                preferences = new DevicePreferences();
                _settings.Devices[key] = preferences;
            }
        }

        preferences.LastKnownName = device.Name;
        return preferences;
    }

    private static string GetDeviceKey(BluetoothBatteryDevice device) =>
        BatteryDeviceIdentity.GetKey(device);

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
            parts.Add(AppText.Format("ComponentBattery", AppText.Get("LeftShort"), components.LeftPercent));
        }
        if (components.RightPercent.HasValue)
        {
            parts.Add(AppText.Format("ComponentBattery", AppText.Get("RightShort"), components.RightPercent));
        }
        if (components.CasePercent.HasValue)
        {
            parts.Add(AppText.Format("ComponentBattery", AppText.Get("CaseShort"), components.CasePercent));
        }

        return parts.Count == 0 ? string.Empty : $"  [{string.Join(" · ", parts)}]";
    }

    private string FormatBatterySummary(BluetoothBatteryDevice device)
    {
        if (device.Components is null)
        {
            return GetEffectiveBatteryPercent(device) is int percentage ? $"{percentage}%" : AppText.Get("NoData");
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
            ? AppText.Get("None")
            : FormatBatteryComponents(device.Components).Trim();
        var categories = device.Categories.Count == 0
            ? AppText.Get("NotSpecified")
            : string.Join(", ", device.Categories);
        var connection = device.IsConnected switch
        {
            true => AppText.Get("ConnectedState"),
            false => AppText.Get("NotConnectedState"),
            _ => AppText.Get("UnknownState")
        };
        var source = device.BatterySource switch
        {
            BatteryReadingSource.WindowsDeviceProperty => AppText.Get("SourceWindows"),
            BatteryReadingSource.BluetoothHfp => "Bluetooth HFP",
            BatteryReadingSource.BluetoothGatt => "BLE GATT Battery Service",
            BatteryReadingSource.BluetoothGattComponents => AppText.Get("SourceGattComponents"),
            BatteryReadingSource.PlugAndPlay => AppText.Get("SourcePlugAndPlay"),
            BatteryReadingSource.AirPodsAdvertisement => "Apple Continuity BLE",
            BatteryReadingSource.GoogleFastPairAdvertisement => "Google Fast Pair BLE",
            BatteryReadingSource.NothingRfcomm => "Nothing/CMF RFCOMM",
            BatteryReadingSource.DualSenseHid => "Bluetooth HID DualSense",
            _ => AppText.Get("SourceNotFound")
        };

        var effectiveBatteryPercent = GetEffectiveBatteryPercent(device);
        var text = AppText.Format("DiagnosticsText",
            GetDisplayName(device), device.Name, connection,
            effectiveBatteryPercent is int percent ? $"{percent}%" : AppText.Get("NoData"),
            source, components, categories,
            device.BluetoothClassMajor?.ToString() ?? AppText.Get("Unspecified"),
            device.Address ?? AppText.Get("Unspecified"),
            device.ContainerId?.ToString("D") ?? AppText.Get("Unspecified"), device.Id);

        MessageBox.Show(
            text,
            AppText.Get("DeviceDiagnostics"),
            MessageBoxButtons.OK,
            effectiveBatteryPercent.HasValue ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
                        AppText.Format("UpdatePrompt", release.Tag),
                        AppText.Get("UpdateTitle"),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    if (open == DialogResult.Yes)
                    {
                        OpenUrl(release.Url);
                    }
                }
                else if (!DesktopNotificationService.TryShow(
                             AppText.Get("UpdateAvailableTitle"),
                             AppText.Format("UpdateAvailableBody", release.Tag),
                             AppText.Get("OpenPage"),
                             release.Url))
                {
                    _notifyIcon.ShowBalloonTip(
                        5000,
                        AppText.Get("UpdateAvailableTitle"),
                        AppText.Format("UpdateAvailableBody", release.Tag),
                        ToolTipIcon.Info);
                }
            }
            else if (showResult)
            {
                MessageBox.Show(
                    AppText.Get("UpToDate"),
                    AppText.Get("UpdateTitle"),
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
                    AppText.Format("UpdateCheckError", ex.Message),
                    AppText.Get("UpdateTitle"),
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
                AppText.Format("OpenLinkError", ex.Message),
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
        var battery = GetEffectiveBatteryPercent(device) is int percent ? $"{percent,3}%" : "  — ";
        var components = FormatBatteryComponents(device.Components);
        return $"{connection}  {battery}   {GetDisplayName(device)}{components}";
    }

    private string GetEmptyStateText()
    {
        return _availability switch
        {
            BluetoothAvailability.TurnedOff => AppText.Get("EnableBluetooth"),
            BluetoothAvailability.NotFound => AppText.Get("AdapterNotFound"),
            BluetoothAvailability.AccessDenied => AppText.Get("WindowsDeniedBluetooth"),
            BluetoothAvailability.Error => AppText.Get("DeviceReadError"),
            _ => AppText.Get("NoPairedDevices")
        };
    }

    private void ConnectionNotifications_Idle(object? sender, EventArgs e)
    {
        if (!_isExiting) ShowLowBatteryNotificationIfNeeded();
    }

    private void ShowLowBatteryNotificationIfNeeded()
    {
        var currentlyLow = BatteryDeviceIdentity.Deduplicate(_devices)
            .Where(device => device.IsConnected == true &&
                             GetEffectiveBatteryPercent(device) is int percentage &&
                             percentage <= _settings.LowBatteryThreshold &&
                             GetDevicePreferences(device).LowBatteryNotifications)
            .ToArray();

        var recoveredIds = _lowBatteryNotifications
            .Where(id => currentlyLow.All(device => !string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        foreach (var id in recoveredIds)
        {
            _lowBatteryNotifications.Remove(id);
        }

        // Re-evaluate after the final connection card closes. Do not mark devices
        // as notified while waiting: they may disconnect, recover, or be muted.
        if (!_settings.LowBatteryNotifications || _connectionNotifications.IsBusy)
        {
            return;
        }

        var newLowDevices = currentlyLow
            .Where(device => _lowBatteryNotifications.Add(GetDeviceKey(device)))
            .ToArray();
        if (newLowDevices.Length == 0)
        {
            return;
        }

        var details = string.Join(", ", newLowDevices.Select(FormatLowBatteryDevice));
        if (!DesktopNotificationService.TryShow(
                AppText.Get("LowBatteryTitle"),
                details,
                AppText.Get("OpenBluetooth"),
                "ms-settings:bluetooth",
                allowSnooze: true))
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                AppText.Get("LowBatteryTitle"),
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
            return $"{GetDisplayName(device)}: {GetEffectiveBatteryPercent(device)}%";
        }

        var lowComponents = new[]
            {
                (Name: AppText.Get("LeftShort"), Percentage: device.Components.LeftPercent),
                (Name: AppText.Get("RightShort"), Percentage: device.Components.RightPercent),
                (Name: AppText.Get("CaseShort"), Percentage: device.Components.CasePercent)
            }
            .Where(component => component.Percentage.HasValue &&
                                component.Percentage <= _settings.LowBatteryThreshold)
            .Select(component => $"{component.Name} {component.Percentage}%")
            .ToArray();

        return lowComponents.Length == 0
            ? $"{GetDisplayName(device)}: {GetEffectiveBatteryPercent(device)}%"
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
                AppText.Format("BluetoothSettingsError", ex.Message),
                "BT Charge Indicator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void ShowLowLatencyModeHelp()
    {
        var openSettings = MessageBox.Show(
            AppText.Get("LowLatencyHelp"),
            AppText.Get("LowLatencyTitle"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (openSettings == DialogResult.Yes)
        {
            OpenBluetoothSettings();
        }
    }

    private sealed class DeviceTrayIconState : IDisposable
    {
        public NotifyIcon NotifyIcon { get; }
        public Icon Icon { get; set; }

        public DeviceTrayIconState(NotifyIcon notifyIcon, Icon icon)
        {
            NotifyIcon = notifyIcon;
            Icon = icon;
        }

        public void Dispose()
        {
            NotifyIcon.Visible = false;
            NotifyIcon.ContextMenuStrip?.Dispose();
            NotifyIcon.Dispose();
            Icon.Dispose();
        }
    }

    protected override void ExitThreadCore()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _deviceChangeTimer.Stop();
        _deviceWatcher.Changed -= DeviceWatcher_Changed;
        _runtime.StateChanged -= Runtime_StateChanged;
        _ = ShutdownRuntimeAsync();
        _deviceWatcher.Dispose();
        _connectionNotifications.Idle -= ConnectionNotifications_Idle;
        _connectionNotifications.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        foreach (var preview in _trayIconPreviews.Values)
        {
            preview.Dispose();
        }
        _trayIconPreviews.Clear();
        foreach (var deviceTrayIcon in _deviceTrayIcons.Values)
        {
            deviceTrayIcon.Dispose();
        }
        _deviceTrayIcons.Clear();
        _appLogo.Dispose();
        _currentIcon?.Dispose();
        _deviceChangeTimer.Dispose();
        _updateService.Dispose();
        base.ExitThreadCore();
    }
}
