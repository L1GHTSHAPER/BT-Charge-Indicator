using BTChargeIndicator.Localization;
using BTChargeIndicator.Models;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.UI;

/// <summary>
/// Central settings window for device, profile, tray, notification, and advanced options.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _applySettings;
    private readonly IReadOnlyList<BluetoothBatteryDevice> _devices;
    private readonly BatteryProfileStore _profileStore;
    private IReadOnlyList<BatteryProfile> _profiles;
    private readonly List<DeviceEditorRow> _deviceRows = [];

    private readonly ComboBox _globalAggregation = CreateCombo();
    private readonly ComboBox _globalIconStyle = CreateCombo();
    private readonly ComboBox _threshold = CreateCombo();
    private readonly CheckBox _lowBatteryNotifications = new();
    private readonly CheckBox _connectionNotifications = new();
    private readonly ComboBox _language = CreateCombo();
    private readonly ComboBox _refreshInterval = CreateCombo();
    private readonly CheckBox _automaticUpdates = new();
    private readonly CheckBox _startWithWindows = new();

    public AppSettings UpdatedSettings { get; private set; }

    public SettingsForm(
        AppSettings settings,
        IReadOnlyList<BluetoothBatteryDevice> devices,
        BatteryProfileStore profileStore,
        IReadOnlyList<BatteryProfile> profiles,
        Action<AppSettings> applySettings)
    {
        _settings = settings.DeepClone();
        _applySettings = applySettings;
        UpdatedSettings = _settings.DeepClone();
        _devices = devices;
        _profileStore = profileStore;
        _profiles = profiles;

        Text = AppText.Get("SettingsTitle");
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 560);
        Size = new Size(900, 680);
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = SystemFonts.MessageBoxFont;

        BuildControls();
        LoadSettings();
    }

    private void BuildControls()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        root.Controls.Add(tabs, 0, 0);

        var overview = new TabPage(AppText.Get("SettingsTabOverview"));
        var overviewText = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(780, 0),
            Padding = new Padding(14),
            Text = AppText.Format("SettingsOverviewText", BatteryDeviceIdentity.Deduplicate(_devices).Count,
                _profiles.Count(profile => profile.Id != BatteryProfileEngine.DefaultProfileId),
                BatteryDeviceIdentity.Deduplicate(_devices).Count(device => device.IsConnected == true))
        };
        overview.Controls.Add(overviewText);
        tabs.TabPages.Add(overview);

        var devicesPage = new TabPage(AppText.Get("SettingsTabDevices"));
        var devicePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };
        devicesPage.Controls.Add(devicePanel);
        BuildDeviceRows(devicePanel);
        tabs.TabPages.Add(devicesPage);

        var profilesPage = new TabPage(AppText.Get("SettingsTabProfiles"));
        var profileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3
        };
        profileLayout.Controls.Add(new Label
        {
            Text = AppText.Get("SettingsProfileHelp"),
            AutoSize = true,
            MaximumSize = new Size(760, 0)
        }, 0, 0);
        var manageProfiles = new Button
        {
            Text = AppText.Get("ManageProfiles"),
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 0)
        };
        manageProfiles.Click += (_, _) => OpenProfileEditor();
        profileLayout.Controls.Add(manageProfiles, 0, 1);
        profilesPage.Controls.Add(profileLayout);
        tabs.TabPages.Add(profilesPage);

        var trayPage = new TabPage(AppText.Get("SettingsTabTray"));
        var trayLayout = CreateSettingsLayout();
        AddRow(trayLayout, AppText.Get("GlobalBatteryAggregation"), _globalAggregation);
        AddRow(trayLayout, AppText.Get("TrayIconStyle"), _globalIconStyle);
        AddRow(trayLayout, AppText.Get("LowBatteryThreshold"), _threshold);
        trayPage.Controls.Add(trayLayout);
        tabs.TabPages.Add(trayPage);

        var notificationsPage = new TabPage(AppText.Get("SettingsTabNotifications"));
        var notificationsLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(14)
        };
        _lowBatteryNotifications.Text = AppText.Get("LowBatteryNotifications");
        _lowBatteryNotifications.AutoSize = true;
        _connectionNotifications.Text = AppText.Get("DeviceConnections");
        _connectionNotifications.AutoSize = true;
        notificationsLayout.Controls.Add(_lowBatteryNotifications);
        notificationsLayout.Controls.Add(_connectionNotifications);
        notificationsLayout.Controls.Add(new Label
        {
            Text = AppText.Get("SettingsNotificationsHelp"),
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Padding = new Padding(0, 12, 0, 0)
        });
        notificationsPage.Controls.Add(notificationsLayout);
        tabs.TabPages.Add(notificationsPage);

        var advancedPage = new TabPage(AppText.Get("SettingsTabAdvanced"));
        var advancedLayout = CreateSettingsLayout();
        AddRow(advancedLayout, AppText.Get("Language"), _language);
        AddRow(advancedLayout, AppText.Get("RefreshInterval"), _refreshInterval);
        _automaticUpdates.Text = AppText.Get("AutomaticUpdates");
        _automaticUpdates.AutoSize = true;
        _startWithWindows.Text = AppText.Get("StartWithWindows");
        _startWithWindows.AutoSize = true;
        AddRow(advancedLayout, string.Empty, _automaticUpdates);
        AddRow(advancedLayout, string.Empty, _startWithWindows);
        advancedPage.Controls.Add(advancedLayout);
        tabs.TabPages.Add(advancedPage);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        var save = new Button { Text = AppText.Get("SettingsSave"), AutoSize = true, MinimumSize = new Size(100, 32) };
        save.Click += (_, _) => SaveAndClose();
        var apply = new Button { Text = AppText.Get("SettingsApply"), AutoSize = true, MinimumSize = new Size(100, 32) };
        apply.Click += (_, _) => ApplyChanges();
        var cancel = new Button { Text = AppText.Get("SettingsCancel"), AutoSize = true, MinimumSize = new Size(100, 32) };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        AcceptButton = save;
        CancelButton = cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 1);
    }

    private void BuildDeviceRows(FlowLayoutPanel panel)
    {
        foreach (var device in BatteryDeviceIdentity.Deduplicate(_devices)
                     .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var preferences = GetDevicePreferences(device);
            var row = new TableLayoutPanel
            {
                Width = 800,
                Height = 72,
                ColumnCount = 5,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                BorderStyle = BorderStyle.FixedSingle
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));

            var battery = BatteryProfileEngine.GetBatteryPercent(
                device,
                BatteryProfileEngine.Resolve(device, _profiles, preferences.ProfileId));
            var name = new Label
            {
                Text = $"{preferences.CustomName ?? device.Name} · {(battery.HasValue ? $"{battery}%" : AppText.Get("NoData"))}",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font(Font, FontStyle.Bold)
            };
            row.Controls.Add(name, 0, 0);
            row.SetColumnSpan(name, 5);

            var include = new CheckBox
            {
                Text = AppText.Get("IncludeInTrayIcon"),
                Checked = preferences.IncludeInTrayIcon,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            var individual = new CheckBox
            {
                Text = AppText.Get("IndividualTrayIcon"),
                Checked = preferences.ShowIndividualTrayIcon,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            var profile = CreateCombo();
            profile.Items.Add(new ProfileChoice(AppText.Get("DefaultProfile"), null));
            foreach (var item in _profiles.Where(item =>
                         !string.Equals(item.Id, BatteryProfileEngine.DefaultProfileId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                profile.Items.Add(new ProfileChoice(item.Name, item.Id));
            }
            profile.SelectedItem = profile.Items.Cast<ProfileChoice>()
                .FirstOrDefault(item => string.Equals(item.Id, preferences.ProfileId,
                    StringComparison.OrdinalIgnoreCase)) ?? profile.Items[0];

            var style = CreateCombo();
            style.Items.Add(new StyleChoice(AppText.Get("UseGlobalTrayIconStyle"), null));
            foreach (var (value, key) in StyleOptions())
            {
                style.Items.Add(new StyleChoice(AppText.Get(key), value));
            }
            style.SelectedItem = style.Items.Cast<StyleChoice>()
                .FirstOrDefault(item => item.Style == preferences.IndividualTrayIconStyle) ?? style.Items[0];

            row.Controls.Add(include, 0, 1);
            row.Controls.Add(individual, 1, 1);
            row.Controls.Add(profile, 3, 1);
            row.Controls.Add(style, 4, 1);
            panel.Controls.Add(row);
            _deviceRows.Add(new DeviceEditorRow(device, preferences, include, individual, profile, style));
        }
    }

    private void LoadSettings()
    {
        foreach (var (mode, key) in new[]
                 {
                     (BatteryAggregationMode.Lowest, "AggregationLowest"),
                     (BatteryAggregationMode.Highest, "AggregationHighest"),
                     (BatteryAggregationMode.Average, "AggregationAverage")
                 })
        {
            _globalAggregation.Items.Add(new EnumChoice<BatteryAggregationMode>(AppText.Get(key), mode));
        }
        _globalAggregation.SelectedItem = _globalAggregation.Items.Cast<EnumChoice<BatteryAggregationMode>>()
            .FirstOrDefault(item => item.Value == _settings.GlobalAggregationMode) ?? _globalAggregation.Items[0];

        foreach (var (style, key) in StyleOptions())
        {
            _globalIconStyle.Items.Add(new EnumChoice<TrayIconStyle>(AppText.Get(key), style));
        }
        _globalIconStyle.SelectedItem = _globalIconStyle.Items.Cast<EnumChoice<TrayIconStyle>>()
            .FirstOrDefault(item => item.Value == _settings.IconStyle) ?? _globalIconStyle.Items[0];

        foreach (var value in new[] { 10, 15, 20, 25, 30, 40, 50 })
        {
            _threshold.Items.Add(value);
        }
        if (!_threshold.Items.Contains(_settings.LowBatteryThreshold))
        {
            _threshold.Items.Add(_settings.LowBatteryThreshold);
        }
        _threshold.SelectedItem = _settings.LowBatteryThreshold;

        _lowBatteryNotifications.Checked = _settings.LowBatteryNotifications;
        _connectionNotifications.Checked = _settings.ConnectionNotifications;

        foreach (var (language, key) in new[]
                 {
                     (AppLanguage.Auto, "LanguageAuto"),
                     (AppLanguage.Russian, "LanguageRussian"),
                     (AppLanguage.English, "LanguageEnglish")
                 })
        {
            _language.Items.Add(new EnumChoice<AppLanguage>(AppText.Get(key), language));
        }
        _language.SelectedItem = _language.Items.Cast<EnumChoice<AppLanguage>>()
            .FirstOrDefault(item => item.Value == _settings.Language) ?? _language.Items[0];

        foreach (var (seconds, key) in new[]
                 {
                     (30, "Seconds30"),
                     (60, "Minute1"),
                     (300, "Minutes5")
                 })
        {
            _refreshInterval.Items.Add(new EnumChoice<int>(AppText.Get(key), seconds));
        }
        _refreshInterval.SelectedItem = _refreshInterval.Items.Cast<EnumChoice<int>>()
            .FirstOrDefault(item => item.Value == _settings.RefreshIntervalSeconds) ?? _refreshInterval.Items[1];

        _automaticUpdates.Checked = _settings.CheckForUpdatesAutomatically;
        _startWithWindows.Checked = AutoStartManager.IsEnabled;
    }

    private void SaveAndClose()
    {
        if (!ApplyChanges()) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool ApplyChanges()
    {
        _settings.GlobalAggregationMode =
            ((EnumChoice<BatteryAggregationMode>)_globalAggregation.SelectedItem!).Value;
        _settings.IconStyle = ((EnumChoice<TrayIconStyle>)_globalIconStyle.SelectedItem!).Value;
        _settings.LowBatteryThreshold = (int)_threshold.SelectedItem!;
        _settings.LowBatteryNotifications = _lowBatteryNotifications.Checked;
        _settings.ConnectionNotifications = _connectionNotifications.Checked;
        _settings.Language = ((EnumChoice<AppLanguage>)_language.SelectedItem!).Value;
        _settings.RefreshIntervalSeconds = ((EnumChoice<int>)_refreshInterval.SelectedItem!).Value;
        _settings.CheckForUpdatesAutomatically = _automaticUpdates.Checked;
        foreach (var row in _deviceRows)
        {
            row.Preferences.IncludeInTrayIcon = row.IncludeInGlobal.Checked;
            row.Preferences.ShowIndividualTrayIcon = row.IndividualTrayIcon.Checked;
            row.Preferences.ProfileId = ((ProfileChoice)row.Profile.SelectedItem!).Id;
            row.Preferences.IndividualTrayIconStyle = ((StyleChoice)row.Style.SelectedItem!).Style;
        }

        try
        {
            AutoStartManager.SetEnabled(_startWithWindows.Checked);
            UpdatedSettings = _settings.DeepClone();
            _applySettings(UpdatedSettings.DeepClone());
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, AppText.Format("AutoStartError", ex.Message),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void OpenProfileEditor()
    {
        using var editor = new BatteryProfileEditorForm(_profiles, _profileStore);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            _profiles = _profileStore.Load();
            RefreshProfileChoices();
        }
    }

    private void RefreshProfileChoices()
    {
        foreach (var row in _deviceRows)
        {
            var selectedId = row.Preferences.ProfileId;
            row.Profile.Items.Clear();
            row.Profile.Items.Add(new ProfileChoice(AppText.Get("DefaultProfile"), null));
            foreach (var profile in _profiles.Where(profile =>
                         !string.Equals(profile.Id, BatteryProfileEngine.DefaultProfileId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                row.Profile.Items.Add(new ProfileChoice(profile.Name, profile.Id));
            }
            row.Profile.SelectedItem = row.Profile.Items.Cast<ProfileChoice>()
                .FirstOrDefault(item => string.Equals(item.Id, selectedId,
                    StringComparison.OrdinalIgnoreCase)) ?? row.Profile.Items[0];
        }
    }

    private DevicePreferences GetDevicePreferences(BluetoothBatteryDevice device)
    {
        var key = GetDeviceKey(device);
        if (_settings.Devices.TryGetValue(key, out var existing) && existing is not null)
        {
            return existing;
        }

        var legacyAddressKey = string.IsNullOrWhiteSpace(device.Address)
            ? null
            : $"address:{device.Address.Trim()}";
        if (key.StartsWith("container:", StringComparison.OrdinalIgnoreCase) &&
            legacyAddressKey is not null &&
            _settings.Devices.Remove(legacyAddressKey, out var legacy) && legacy is not null)
        {
            _settings.Devices[key] = legacy;
            return legacy;
        }

        var preferences = new DevicePreferences { LastKnownName = device.Name };
        _settings.Devices[key] = preferences;
        return preferences;
    }

    private static string GetDeviceKey(BluetoothBatteryDevice device) =>
        BatteryDeviceIdentity.GetKey(device);

    private static TableLayoutPanel CreateSettingsLayout() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(14),
        ColumnCount = 2,
        RowCount = 0
    };

    private static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 12, 0)
        };
        layout.Controls.Add(caption, 0, row);
        control.Width = 260;
        control.Margin = new Padding(3, 4, 3, 4);
        layout.Controls.Add(control, 1, row);
    }

    private static ComboBox CreateCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 260
    };

    private static (TrayIconStyle Style, string Key)[] StyleOptions() =>
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

    private sealed record DeviceEditorRow(
        BluetoothBatteryDevice Device,
        DevicePreferences Preferences,
        CheckBox IncludeInGlobal,
        CheckBox IndividualTrayIcon,
        ComboBox Profile,
        ComboBox Style);

    private sealed record ProfileChoice(string Name, string? Id)
    {
        public override string ToString() => Name;
    }

    private sealed record StyleChoice(string Name, TrayIconStyle? Style)
    {
        public override string ToString() => Name;
    }

    private sealed record EnumChoice<T>(string Name, T Value)
    {
        public override string ToString() => Name;
    }
}
