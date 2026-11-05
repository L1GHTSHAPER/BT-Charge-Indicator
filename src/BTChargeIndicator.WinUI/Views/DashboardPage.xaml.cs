using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BTChargeIndicator.Models;
using BTChargeIndicator.Runtime;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BTChargeIndicator.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    private RuntimeHost? _runtime;
    private ApplicationRuntimeState _latestState = ApplicationRuntimeState.Initial;
    private IReadOnlyList<BluetoothBatteryDevice> _devices = [];
    private readonly ObservableCollection<DeviceRow> _deviceRows = [];
    private readonly HashSet<string> _selectedDeviceKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CheckBox> _categoryFilterItems = [];
    private string[] _configuredCategories = [];
    private bool _selectionMode;
    private bool _tileView = true;
    private bool _isNarrowLayout;
    private DeviceSortOrder _sortOrder = DeviceSortOrder.Connected;

    public DashboardPage()
    {
        InitializeComponent();
        DeviceList.ItemsSource = _deviceRows;
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.LanguageChanged -= ApplyLocalizedText;
        App.LanguageChanged += ApplyLocalizedText;
        _runtime = App.Runtime;
        ApplyLocalizedText();
        SetViewMode(_tileView);
        if (_runtime is null)
        {
            RuntimeStatus.Title = WinUIText.Get("RuntimeUnavailableTitle");
            RuntimeStatus.Message = WinUIText.Get("RuntimeUnavailableMessage");
            RuntimeStatus.Severity = InfoBarSeverity.Error;
            return;
        }

        _runtime.StateChanged += Runtime_StateChanged;
        ApplyState(_runtime.State);
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.LanguageChanged -= ApplyLocalizedText;
        if (_runtime is not null)
        {
            _runtime.StateChanged -= Runtime_StateChanged;
            _runtime = null;
        }
    }

    private void ApplyLocalizedText()
    {
        DashboardTitleText.Text = WinUIText.Get("DashboardTitle");
        DashboardDescriptionText.Text = WinUIText.Get("DashboardDescription");
        RefreshButton.Label = WinUIText.Get("Refresh");
        TilesButton.Label = WinUIText.Get("ViewTiles");
        ListButton.Label = WinUIText.Get("ViewList");
        SelectionButton.Label = WinUIText.Get(_selectionMode ? "CancelSelection" : "SelectDevices");
        SelectAllButton.Label = WinUIText.Get("SelectAll");
        UnselectAllButton.Label = WinUIText.Get("UnselectAll");
        InvertSelectionButton.Label = WinUIText.Get("InvertSelection");
        ActionsButton.Label = WinUIText.Get("Actions");
        FilterButton.Label = WinUIText.Get("Filter");
        ClearFiltersButton.Label = WinUIText.Get("ClearFilters");
        SortButton.Label = WinUIText.Get("SortBy");
        SortConnectedItem.Text = WinUIText.Get("SortConnected");
        SortTypeItem.Text = WinUIText.Get("SortDeviceType");
        SortNameItem.Text = WinUIText.Get("SortDeviceName");
        SortBatteryItem.Text = WinUIText.Get("SortBattery");
        HideSelectedAction.Text = WinUIText.Get("HideSelected");
        ShowSelectedAction.Text = WinUIText.Get("ShowSelected");
        ShowHiddenFilterItem.Content = WinUIText.Get("ShowHidden");
        ConnectedFilterItem.Content = WinUIText.Get("ConnectedFilter");
        DisconnectedFilterItem.Content = WinUIText.Get("DisconnectedFilter");
        DeviceTypeFilterHeader.Text = WinUIText.Get("DeviceTypeFilter");
        NoDevicesTitleText.Text = WinUIText.Get("NoDevices");
        NoDevicesHelpText.Text = WinUIText.Get("NoDevicesHelp");

        foreach (var item in _categoryFilterItems)
        {
            if (item.Tag is string category)
            {
                item.Content = category;
            }
        }

        UpdateSelectionToolbar();
        UpdateFilterToolbar();
        if (_runtime is not null)
        {
            ApplyState(_latestState);
        }
    }

    private void Runtime_StateChanged(ApplicationRuntimeState state)
    {
        DispatcherQueue.TryEnqueue(() => ApplyState(state));
    }

    private void ApplyState(ApplicationRuntimeState state)
    {
        _latestState = state;
        _devices = BatteryDeviceIdentity.Deduplicate(state.Devices);
        ConfigureCategoryFilters(_devices);
        var settings = _runtime?.LoadSettings();
        var rows = _devices.Select(device =>
            {
                DevicePreferences? preferences = null;
                var key = BatteryDeviceIdentity.GetKey(device);
                if (settings is not null)
                {
                    settings.Devices.TryGetValue(key, out preferences);
                }

                var isHidden = preferences?.IsHidden == true;
                var passesConnection = (!IsChecked(ConnectedFilterItem) && !IsChecked(DisconnectedFilterItem)) ||
                                       (IsChecked(ConnectedFilterItem) && device.IsConnected == true) ||
                                       (IsChecked(DisconnectedFilterItem) && device.IsConnected == false);
                var passesCategories = _selectedCategories.Count == 0 ||
                                       device.Categories.Any(_selectedCategories.Contains);
                if ((!IsChecked(ShowHiddenFilterItem) && isHidden) || !passesConnection || !passesCategories)
                {
                    return null;
                }

                var batteryMode = preferences?.BatteryMode ??
                    (device.Components is null ? BatteryAggregationMode.Lowest : BatteryAggregationMode.EarbudsLowest);
                var battery = BatteryAggregationEngine.Aggregate(device.Components, batteryMode, device.BatteryPercent);
                var batteryParts = new List<string>();
                if (device.Components is { } components)
                {
                    if (components.LeftPercent is int left) batteryParts.Add($"L {left}%");
                    if (components.RightPercent is int right) batteryParts.Add($"R {right}%");
                    if (components.CasePercent is int caseLevel) batteryParts.Add($"Case {caseLevel}%");
                }
                var charging = device.IsCharging == true || device.Components?.LeftCharging == true ||
                    device.Components?.RightCharging == true || device.Components?.CaseCharging == true;
                var isSelected = _selectedDeviceKeys.Contains(key);
                var category = string.Join(" ", device.Categories);
                return new DeviceRowData(
                    key,
                    preferences?.CustomName ?? device.Name,
                    category,
                    GetDeviceGlyph(device),
                    _tileView ? Visibility.Visible : Visibility.Collapsed,
                    _tileView ? Visibility.Collapsed : Visibility.Visible,
                    !_isNarrowLayout ? Visibility.Visible : Visibility.Collapsed,
                    _isNarrowLayout ? Visibility.Visible : Visibility.Collapsed,
                    battery.HasValue ? $"{battery.Value}%" : string.Empty,
                    battery.HasValue ? string.Empty : WinUIText.Get("NoBattery"),
                    battery.HasValue ? Visibility.Visible : Visibility.Collapsed,
                    battery.HasValue ? Visibility.Collapsed : Visibility.Visible,
                    device.IsConnected == true && battery.HasValue
                        ? Visibility.Visible
                        : Visibility.Collapsed,
                    battery is int level && settings is not null && level <= settings.LowBatteryThreshold
                        ? WinUIText.Get("LowBattery")
                        : string.Empty,
                    string.Join("  ·  ", batteryParts),
                    batteryParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
                    charging ? "⚡ " + WinUIText.Get("Charging") : string.Empty,
                    charging ? Visibility.Visible : Visibility.Collapsed,
                    device.IsConnected switch
                    {
                        true => WinUIText.Get("Connected"),
                        false => WinUIText.Get("Disconnected"),
                        _ => WinUIText.Get("ConnectionUnknown")
                    },
                    battery ?? 0,
                    isSelected,
                    device.IsConnected == true,
                    battery ?? 101,
                    WinUIText.Get("DeviceOptions"),
                    WinUIText.Get("RenameDevice"),
                    WinUIText.Get(isHidden ? "ShowDevice" : "HideDevice"),
                    WinUIText.Get("DeviceNotificationSettings"),
                    WinUIText.Get("DeviceTraySettings"));
            })
            .Where(row => row is not null)
            .Cast<DeviceRowData>()
            .ToArray();

        var orderedRows = (_sortOrder switch
        {
            DeviceSortOrder.Connected => rows.OrderByDescending(row => row.IsConnected)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            DeviceSortOrder.DeviceType => rows.OrderBy(row => row.DeviceType, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            DeviceSortOrder.Battery => rows.OrderBy(row => row.BatterySortValue)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
        }).ToArray();

        DeviceList.ItemsPanel = (ItemsPanelTemplate)Resources[GetItemsPanelResourceKey()];
        SyncDeviceRows(orderedRows);
        DeviceList.Visibility = orderedRows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = orderedRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (orderedRows.Length == 0)
        {
            var hiddenDevices = _devices.Count(device =>
            {
                var key = BatteryDeviceIdentity.GetKey(device);
                return settings?.Devices.TryGetValue(key, out var preferences) == true && preferences.IsHidden;
            });
            var allHidden = _devices.Count > 0 && hiddenDevices == _devices.Count && !IsChecked(ShowHiddenFilterItem);
            NoDevicesTitleText.Text = WinUIText.Get(allHidden ? "AllDevicesHidden" : "NoDevices");
            NoDevicesHelpText.Text = WinUIText.Get(_devices.Count == 0 ? "NoDevicesHelp" : "ChangeDeviceFilter");
        }

        if (state.IsScanning)
        {
            RuntimeStatus.Title = WinUIText.Get("ScanningTitle");
            RuntimeStatus.Message = WinUIText.Get("ScanningMessage");
            RuntimeStatus.Severity = InfoBarSeverity.Informational;
        }
        else if (state.Availability != BluetoothAvailability.Available)
        {
            RuntimeStatus.Title = WinUIText.Get("RuntimeUnavailableTitle");
            RuntimeStatus.Message = state.ErrorMessage ?? WinUIText.Get("RuntimeUnavailableMessage");
            RuntimeStatus.Severity = InfoBarSeverity.Warning;
        }
        else
        {
            RuntimeStatus.Title = WinUIText.Format("DevicesDiscovered", orderedRows.Length);
            RuntimeStatus.Message = state.LastUpdatedUtc is DateTimeOffset updated
                ? WinUIText.Format("LastUpdated", updated.ToLocalTime().ToString("t", WinUIText.Culture))
                : WinUIText.Get("WaitingForScan");
            RuntimeStatus.Severity = InfoBarSeverity.Success;
        }

        UpdateSelectionToolbar();
        UpdateFilterToolbar();
    }

    private void SyncDeviceRows(IReadOnlyList<DeviceRowData> desiredRows)
    {
        var desiredKeys = desiredRows.Select(row => row.DeviceKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedDeviceKeys.IntersectWith(_devices.Select(BatteryDeviceIdentity.GetKey));

        for (var index = _deviceRows.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(_deviceRows[index].DeviceKey))
            {
                _deviceRows.RemoveAt(index);
            }
        }

        for (var targetIndex = 0; targetIndex < desiredRows.Count; targetIndex++)
        {
            var data = desiredRows[targetIndex];
            var existingIndex = -1;
            for (var index = targetIndex; index < _deviceRows.Count; index++)
            {
                if (string.Equals(_deviceRows[index].DeviceKey, data.DeviceKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                _deviceRows.Insert(targetIndex, new DeviceRow(data));
                continue;
            }

            if (existingIndex != targetIndex)
            {
                _deviceRows.Move(existingIndex, targetIndex);
            }
            _deviceRows[targetIndex].Update(data);
        }
    }

    private void ConfigureCategoryFilters(IReadOnlyList<BluetoothBatteryDevice> devices)
    {
        var categories = devices.SelectMany(device => device.Categories)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _selectedCategories.IntersectWith(categories);
        if (!_configuredCategories.SequenceEqual(categories, StringComparer.OrdinalIgnoreCase))
        {
            CategoryFilterList.Children.Clear();
            _categoryFilterItems.Clear();
            _configuredCategories = categories;
            foreach (var category in categories)
            {
                var item = new CheckBox
                {
                    Content = category,
                    Tag = category,
                    IsChecked = _selectedCategories.Contains(category)
                };
                item.Click += FilterChanged_Click;
                CategoryFilterList.Children.Add(item);
                _categoryFilterItems.Add(item);
            }
        }
        else
        {
            foreach (var item in _categoryFilterItems)
            {
                if (item.Tag is string category) item.IsChecked = _selectedCategories.Contains(category);
            }
        }
    }

    private void SetViewMode(bool tiles)
    {
        _tileView = tiles;
        TilesButton.IsChecked = tiles;
        ListButton.IsChecked = !tiles;
        DeviceList.ItemsPanel = (ItemsPanelTemplate)Resources[GetItemsPanelResourceKey()];
        if (_runtime is not null)
        {
            ApplyState(_latestState);
        }
    }

    private void SortOrder_Click(object sender, RoutedEventArgs e)
    {
        _sortOrder = sender switch
        {
            ToggleMenuFlyoutItem item when item == SortConnectedItem => DeviceSortOrder.Connected,
            ToggleMenuFlyoutItem item when item == SortTypeItem => DeviceSortOrder.DeviceType,
            ToggleMenuFlyoutItem item when item == SortBatteryItem => DeviceSortOrder.Battery,
            _ => DeviceSortOrder.Name
        };
        SortConnectedItem.IsChecked = _sortOrder == DeviceSortOrder.Connected;
        SortTypeItem.IsChecked = _sortOrder == DeviceSortOrder.DeviceType;
        SortNameItem.IsChecked = _sortOrder == DeviceSortOrder.Name;
        SortBatteryItem.IsChecked = _sortOrder == DeviceSortOrder.Battery;
        ApplyState(_latestState);
    }

    private static string GetDeviceGlyph(BluetoothBatteryDevice device)
    {
        var type = string.Join(" ", device.Categories).ToLowerInvariant();
        if (type.Contains("input.gaming") || type.Contains("gamepad") || type.Contains("controller")) return "\uE7FC";
        if (type.Contains("input.mouse") || type.Contains("mouse")) return "\uE962";
        if (type.Contains("keyboard")) return "\uE765";
        if (type.Contains("headset") || type.Contains("headphone") || type.Contains("ear") || type.Contains("audio.head")) return "\uE7F6";
        if (type.Contains("communication.phone") || type.Contains("mobile") || type.Contains("phone")) return "\uE8EA";
        if (type.Contains("computer.laptop") || type.Contains("laptop")) return "\uE7F8";
        if (type.Contains("computer.desktop") || type.Contains("desktop") || device.BluetoothClassMajor == 1) return "\uE977";
        if (device.BluetoothClassMajor == 4 || type.Contains("speaker") || type.Contains("sound") || type.Contains("audio")) return "\uE7F5";
        if (type.Contains("display") || type.Contains("monitor")) return "\uE7FB";
        return "\uE702";
    }

    private static bool IsChecked(CheckBox checkBox) => checkBox.IsChecked == true;

    private void UpdateFilterToolbar()
    {
        var hasFilters = IsChecked(ShowHiddenFilterItem) || IsChecked(ConnectedFilterItem) ||
                         IsChecked(DisconnectedFilterItem) || _selectedCategories.Count > 0;
        FilterButton.Label = WinUIText.Get(hasFilters ? "FilterActive" : "Filter");
        FilterButton.Foreground = hasFilters
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            : null;
        FilterButton.Background = hasFilters
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorSecondaryBrush"]
            : null;
        ClearFiltersButton.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
        ClearFiltersButton.IsEnabled = hasFilters;
    }

    private void DashboardPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 960;
        if (_isNarrowLayout == narrow) return;
        _isNarrowLayout = narrow;
        DashboardCommandBar.OverflowButtonVisibility = narrow
            ? CommandBarOverflowButtonVisibility.Visible
            : CommandBarOverflowButtonVisibility.Collapsed;
        if (_runtime is not null) ApplyState(_latestState);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e) => UpdateFilterToolbar();

    private string GetItemsPanelResourceKey() => !_tileView
        ? "ListItemsPanel"
        : _isNarrowLayout ? "TileItemsPanelCompact" : "TileItemsPanel";

    private void UpdateSelectionToolbar()
    {
        SelectionButton.Label = WinUIText.Get(_selectionMode ? "CancelSelection" : "SelectDevices");
        var selectionVisibility = _selectionMode ? Visibility.Visible : Visibility.Collapsed;
        SelectAllButton.Visibility = selectionVisibility;
        UnselectAllButton.Visibility = selectionVisibility;
        InvertSelectionButton.Visibility = selectionVisibility;
        SelectionActionsSeparator.Visibility = selectionVisibility;
        SelectionCountText.Visibility = _selectionMode ? Visibility.Visible : Visibility.Collapsed;
        SelectionCountText.Text = WinUIText.Format("SelectedCount", _selectedDeviceKeys.Count);
        ActionsButton.IsEnabled = _selectionMode && _selectedDeviceKeys.Count > 0;
        SelectAllButton.IsEnabled = _selectionMode && _deviceRows.Count > 0;
        UnselectAllButton.IsEnabled = _selectionMode && _selectedDeviceKeys.Count > 0;
        InvertSelectionButton.IsEnabled = _selectionMode && _deviceRows.Count > 0;

        var selected = _devices.Where(device => _selectedDeviceKeys.Contains(BatteryDeviceIdentity.GetKey(device))).ToArray();
        var settings = _runtime?.LoadSettings();
        var selectedHiddenCount = selected.Count(device =>
        {
            var key = BatteryDeviceIdentity.GetKey(device);
            return settings?.Devices.TryGetValue(key, out var preferences) == true && preferences.IsHidden;
        });
        HideSelectedAction.IsEnabled = selectedHiddenCount < selected.Length;
        ShowSelectedAction.IsEnabled = selectedHiddenCount > 0;
    }

    private void TilesButton_Click(object sender, RoutedEventArgs e) => SetViewMode(true);

    private void ListButton_Click(object sender, RoutedEventArgs e) => SetViewMode(false);

    private void SelectionButton_Click(object sender, RoutedEventArgs e)
    {
        _selectionMode = !_selectionMode;
        if (!_selectionMode)
        {
            _selectedDeviceKeys.Clear();
        }
        foreach (var row in _deviceRows)
        {
            row.IsSelected = _selectionMode && _selectedDeviceKeys.Contains(row.DeviceKey);
        }
        UpdateSelectionToolbar();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _selectedDeviceKeys.Clear();
        foreach (var row in _deviceRows)
        {
            _selectedDeviceKeys.Add(row.DeviceKey);
            row.IsSelected = true;
        }
        UpdateSelectionToolbar();
    }

    private void UnselectAll_Click(object sender, RoutedEventArgs e)
    {
        _selectedDeviceKeys.Clear();
        foreach (var row in _deviceRows)
        {
            row.IsSelected = false;
        }
        UpdateSelectionToolbar();
    }

    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _deviceRows)
        {
            if (!_selectedDeviceKeys.Add(row.DeviceKey))
            {
                _selectedDeviceKeys.Remove(row.DeviceKey);
            }
            row.IsSelected = _selectedDeviceKeys.Contains(row.DeviceKey);
        }
        UpdateSelectionToolbar();
    }

    private void DeviceCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_selectionMode || IsWithinButton(e.OriginalSource as DependencyObject) ||
            sender is not FrameworkElement { DataContext: DeviceRow row })
        {
            return;
        }

        if (!_selectedDeviceKeys.Add(row.DeviceKey))
        {
            _selectedDeviceKeys.Remove(row.DeviceKey);
        }
        row.IsSelected = _selectedDeviceKeys.Contains(row.DeviceKey);
        UpdateSelectionToolbar();
        e.Handled = true;
    }

    private static bool IsWithinButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase)
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void DeviceCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceRow row })
        {
            row.IsPointerOver = true;
        }
    }

    private void DeviceCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceRow row })
        {
            row.IsPointerOver = false;
        }
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ShowHiddenFilterItem.IsChecked = false;
        ConnectedFilterItem.IsChecked = false;
        DisconnectedFilterItem.IsChecked = false;
        _selectedCategories.Clear();
        _selectedDeviceKeys.Clear();
        ApplyState(_latestState);
    }

    private void FilterChanged_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string category } categoryItem)
        {
            if (categoryItem.IsChecked == true)
            {
                _selectedCategories.Add(category);
            }
            else
            {
                _selectedCategories.Remove(category);
            }
        }
        _selectedDeviceKeys.Clear();
        ApplyState(_latestState);
    }

    private void HideSelected_Click(object sender, RoutedEventArgs e) => SetSelectedVisibility(isHidden: true);

    private void ShowSelected_Click(object sender, RoutedEventArgs e) => SetSelectedVisibility(isHidden: false);

    private void SetSelectedVisibility(bool isHidden)
    {
        if (_runtime is null)
        {
            return;
        }

        var selected = _devices
            .Where(device => _selectedDeviceKeys.Contains(BatteryDeviceIdentity.GetKey(device)))
            .ToArray();
        _runtime.SetDevicesHidden(selected, isHidden);
        _selectedDeviceKeys.Clear();
        ApplyState(_latestState);
    }

    private async void RenameDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime is null || sender is not MenuFlyoutItem { Tag: string key })
        {
            return;
        }

        var device = _devices.FirstOrDefault(item =>
            string.Equals(BatteryDeviceIdentity.GetKey(item), key, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return;
        }

        var settings = _runtime.LoadSettings();
        settings.Devices.TryGetValue(key, out var preferences);
        var nameBox = new TextBox { Text = preferences?.CustomName ?? string.Empty };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = WinUIText.Get("RenameDeviceHelp"),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            Title = WinUIText.Get("RenameDeviceTitle"),
            Content = content,
            PrimaryButtonText = WinUIText.Get("Apply"),
            CloseButtonText = WinUIText.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _runtime.UpdateDevicePreferences([
                (device, (string?)nameBox.Text, preferences?.IsHidden == true)
            ]);
            ApplyState(_latestState);
        }
    }

    private void ToggleDeviceVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime is null || sender is not MenuFlyoutItem { Tag: string key })
        {
            return;
        }

        var device = _devices.FirstOrDefault(item =>
            string.Equals(BatteryDeviceIdentity.GetKey(item), key, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return;
        }

        var settings = _runtime.LoadSettings();
        settings.Devices.TryGetValue(key, out var preferences);
        _runtime.SetDevicesHidden([device], preferences?.IsHidden != true);
        _selectedDeviceKeys.Remove(key);
        ApplyState(_latestState);
    }

    private async void DeviceNotifications_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime is null || sender is not MenuFlyoutItem { Tag: string key })
        {
            return;
        }

        var device = _devices.FirstOrDefault(item =>
            string.Equals(BatteryDeviceIdentity.GetKey(item), key, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return;
        }

        var settings = _runtime.LoadSettings();
        settings.Devices.TryGetValue(key, out var preferences);
        var connectionNotifications = new CheckBox
        {
            Content = WinUIText.Get("DeviceConnectionNotifications"),
            IsChecked = preferences?.ConnectionNotifications != false
        };
        var lowBatteryNotifications = new CheckBox
        {
            Content = WinUIText.Get("DeviceLowBatteryNotifications"),
            IsChecked = preferences?.LowBatteryNotifications != false
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(connectionNotifications);
        content.Children.Add(lowBatteryNotifications);
        var dialog = new ContentDialog
        {
            Title = WinUIText.Get("DeviceNotificationSettingsTitle"),
            Content = content,
            PrimaryButtonText = WinUIText.Get("Apply"),
            CloseButtonText = WinUIText.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _runtime.UpdateDeviceNotificationPreferences(
                device,
                lowBatteryNotifications.IsChecked == true,
                connectionNotifications.IsChecked == true);
        }
    }

    private async void DeviceTraySettings_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime is null || sender is not MenuFlyoutItem { Tag: string key }) return;
        var device = _devices.FirstOrDefault(item =>
            string.Equals(BatteryDeviceIdentity.GetKey(item), key, StringComparison.OrdinalIgnoreCase));
        if (device is null) return;

        var settings = _runtime.LoadSettings();
        settings.Devices.TryGetValue(key, out var preferences);
        var include = new CheckBox { Content = WinUIText.Get("IncludeInCombinedTrayIcon"), IsChecked = preferences?.IncludeInTrayIcon != false };
        var individual = new CheckBox { Content = WinUIText.Get("ShowIndividualTrayIcon"), IsChecked = preferences?.ShowIndividualTrayIcon == true };
        var batteryMode = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch };
        AddMode(null, "BatteryModeAutomatic");
        AddMode(BatteryAggregationMode.Left, "BatteryModeLeft");
        AddMode(BatteryAggregationMode.Right, "BatteryModeRight");
        AddMode(BatteryAggregationMode.Case, "BatteryModeCase");
        AddMode(BatteryAggregationMode.EarbudsLowest, "BatteryModeEarbudsLowest");
        AddMode(BatteryAggregationMode.EarbudsAverage, "BatteryModeEarbudsAverage");
        var selectedMode = preferences?.BatteryMode;
        batteryMode.SelectedItem = batteryMode.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, selectedMode));
        var iconStyle = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch };
        var previewMode = selectedMode ?? (device.Components is null
            ? BatteryAggregationMode.Lowest
            : BatteryAggregationMode.EarbudsLowest);
        var previewBattery = BatteryAggregationEngine.Aggregate(device.Components, previewMode, device.BatteryPercent);
        foreach (var style in Enum.GetValues<TrayIconStyle>())
        {
            var image = await WinUiTrayService.CreateStylePreviewAsync(
                previewBattery, style, settings.LowBatteryThreshold);
            var itemContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            itemContent.Children.Add(new Image { Source = image, Width = 28, Height = 28 });
            itemContent.Children.Add(new TextBlock
            {
                Text = WinUIText.Get("Style" + style),
                VerticalAlignment = VerticalAlignment.Center
            });
            iconStyle.Items.Add(new ComboBoxItem { Content = itemContent, Tag = style });
        }
        var selectedStyle = preferences?.IndividualTrayIconStyle ?? settings.IconStyle;
        iconStyle.SelectedItem = iconStyle.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, selectedStyle));
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(include);
        content.Children.Add(individual);
        content.Children.Add(new TextBlock { Text = WinUIText.Get("TrayBatteryChoice") });
        content.Children.Add(batteryMode);
        content.Children.Add(new TextBlock { Text = WinUIText.Get("IndividualTrayIconStyle") });
        content.Children.Add(iconStyle);
        var dialog = new ContentDialog
        {
            Title = WinUIText.Get("DeviceTraySettingsTitle"), Content = content,
            PrimaryButtonText = WinUIText.Get("Apply"), CloseButtonText = WinUIText.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _runtime.UpdateDeviceTrayPreferences(device, include.IsChecked == true, individual.IsChecked == true,
                batteryMode.SelectedItem is ComboBoxItem { Tag: BatteryAggregationMode mode } ? mode : null,
                iconStyle.SelectedItem is ComboBoxItem { Tag: TrayIconStyle style } ? style : null);
            ApplyState(_latestState);
        }

        void AddMode(BatteryAggregationMode? mode, string labelKey) =>
            batteryMode.Items.Add(new ComboBoxItem { Content = WinUIText.Get(labelKey), Tag = mode });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            await _runtime.RefreshAsync(refreshGattBattery: true);
        }
        catch (Exception ex)
        {
            RuntimeStatus.Title = WinUIText.Get("RefreshFailed");
            RuntimeStatus.Message = ex.Message;
            RuntimeStatus.Severity = InfoBarSeverity.Error;
        }
    }

    private sealed record DeviceRowData(
        string DeviceKey,
        string Name,
        string DeviceType,
        string DeviceGlyph,
        Visibility TileContentVisibility,
        Visibility ListContentVisibility,
        Visibility WideListContentVisibility,
        Visibility NarrowListContentVisibility,
        string BatteryPercentLabel,
        string NoBatteryLabel,
        Visibility BatteryPercentVisibility,
        Visibility NoBatteryVisibility,
        Visibility BatteryBarVisibility,
        string BatteryCondition,
        string BatteryDetailsLabel,
        Visibility BatteryDetailsVisibility,
        string ChargingLabel,
        Visibility ChargingVisibility,
        string ConnectionLabel,
        int BatteryValue,
        bool IsSelected,
        bool IsConnected,
        int BatterySortValue,
        string DeviceOptionsLabel,
        string RenameMenuLabel,
        string ToggleVisibilityMenuLabel,
        string NotificationSettingsMenuLabel,
        string TraySettingsMenuLabel);

    private sealed class DeviceRow : INotifyPropertyChanged
    {
        private DeviceRowData _data;
        private bool _isSelected;
        private bool _isPointerOver;

        public DeviceRow(DeviceRowData data)
        {
            _data = data;
            _isSelected = data.IsSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DeviceKey => _data.DeviceKey;
        public string Name => _data.Name;
        public string DeviceType => _data.DeviceType;
        public string DeviceGlyph => _data.DeviceGlyph;
        public Visibility TileContentVisibility => _data.TileContentVisibility;
        public Visibility ListContentVisibility => _data.ListContentVisibility;
        public Visibility WideListContentVisibility => _data.WideListContentVisibility;
        public Visibility NarrowListContentVisibility => _data.NarrowListContentVisibility;
        public string BatteryPercentLabel => _data.BatteryPercentLabel;
        public string NoBatteryLabel => _data.NoBatteryLabel;
        public Visibility BatteryPercentVisibility => _data.BatteryPercentVisibility;
        public Visibility NoBatteryVisibility => _data.NoBatteryVisibility;
        public Visibility BatteryBarVisibility => _data.BatteryBarVisibility;
        public string BatteryCondition => _data.BatteryCondition;
        public string BatteryDetailsLabel => _data.BatteryDetailsLabel;
        public Visibility BatteryDetailsVisibility => _data.BatteryDetailsVisibility;
        public string ChargingLabel => _data.ChargingLabel;
        public Visibility ChargingVisibility => _data.ChargingVisibility;
        public string ConnectionLabel => _data.ConnectionLabel;
        public int BatteryValue => _data.BatteryValue;
        public bool IsConnected => _data.IsConnected;
        public int BatterySortValue => _data.BatterySortValue;
        public string DeviceOptionsLabel => _data.DeviceOptionsLabel;
        public string RenameMenuLabel => _data.RenameMenuLabel;
        public string ToggleVisibilityMenuLabel => _data.ToggleVisibilityMenuLabel;
        public string NotificationSettingsMenuLabel => _data.NotificationSettingsMenuLabel;
        public string TraySettingsMenuLabel => _data.TraySettingsMenuLabel;
        public Visibility SelectionVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectionStripeVisibility =>
            _isSelected && _data.ListContentVisibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HoverVisibility => _isPointerOver ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectionVisibility));
                OnPropertyChanged(nameof(SelectionStripeVisibility));
            }
        }

        public bool IsPointerOver
        {
            get => _isPointerOver;
            set
            {
                if (_isPointerOver == value)
                {
                    return;
                }

                _isPointerOver = value;
                OnPropertyChanged(nameof(HoverVisibility));
            }
        }

        public void Update(DeviceRowData data)
        {
            _data = data;
            IsSelected = data.IsSelected;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DeviceType));
            OnPropertyChanged(nameof(DeviceGlyph));
            OnPropertyChanged(nameof(TileContentVisibility));
            OnPropertyChanged(nameof(ListContentVisibility));
            OnPropertyChanged(nameof(WideListContentVisibility));
            OnPropertyChanged(nameof(NarrowListContentVisibility));
            OnPropertyChanged(nameof(SelectionStripeVisibility));
            OnPropertyChanged(nameof(BatteryPercentLabel));
            OnPropertyChanged(nameof(NoBatteryLabel));
            OnPropertyChanged(nameof(BatteryPercentVisibility));
            OnPropertyChanged(nameof(NoBatteryVisibility));
            OnPropertyChanged(nameof(BatteryBarVisibility));
            OnPropertyChanged(nameof(BatteryCondition));
            OnPropertyChanged(nameof(BatteryDetailsLabel));
            OnPropertyChanged(nameof(BatteryDetailsVisibility));
            OnPropertyChanged(nameof(ChargingLabel));
            OnPropertyChanged(nameof(ChargingVisibility));
            OnPropertyChanged(nameof(ConnectionLabel));
            OnPropertyChanged(nameof(BatteryValue));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(BatterySortValue));
            OnPropertyChanged(nameof(DeviceOptionsLabel));
            OnPropertyChanged(nameof(RenameMenuLabel));
            OnPropertyChanged(nameof(ToggleVisibilityMenuLabel));
            OnPropertyChanged(nameof(NotificationSettingsMenuLabel));
            OnPropertyChanged(nameof(TraySettingsMenuLabel));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private enum DeviceSortOrder { Connected, DeviceType, Name, Battery }
}
