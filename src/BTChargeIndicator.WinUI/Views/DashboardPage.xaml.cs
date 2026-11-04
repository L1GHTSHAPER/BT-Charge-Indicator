using BTChargeIndicator.Models;
using BTChargeIndicator.Runtime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BTChargeIndicator.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    private RuntimeHost? _runtime;

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        _runtime = App.Runtime;
        if (_runtime is null)
        {
            RuntimeStatus.Title = "Bluetooth runtime unavailable";
            RuntimeStatus.Message = "Restart the application to initialize Bluetooth monitoring.";
            RuntimeStatus.Severity = InfoBarSeverity.Error;
            return;
        }

        _runtime.StateChanged += Runtime_StateChanged;
        ApplyState(_runtime.State);
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_runtime is not null)
        {
            _runtime.StateChanged -= Runtime_StateChanged;
            _runtime = null;
        }
    }

    private void Runtime_StateChanged(ApplicationRuntimeState state)
    {
        DispatcherQueue.TryEnqueue(() => ApplyState(state));
    }

    private void ApplyState(ApplicationRuntimeState state)
    {
        var rows = state.Devices.Select(device => new DeviceRow(
            device.Name,
            device.BatteryPercent is int battery ? $"{battery}%" : "Not reported",
            device.IsConnected switch
            {
                true => "Connected",
                false => "Disconnected",
                _ => "Connection state unknown"
            },
            device.BatteryPercent ?? 0,
            !device.BatteryPercent.HasValue,
            device.BatterySource.ToString())).ToArray();

        DeviceList.ItemsSource = rows;
        EmptyState.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (state.IsScanning)
        {
            RuntimeStatus.Title = "Scanning for Bluetooth devices";
            RuntimeStatus.Message = "Reading paired devices and available battery services...";
            RuntimeStatus.Severity = InfoBarSeverity.Informational;
        }
        else if (state.Availability != BluetoothAvailability.Available)
        {
            RuntimeStatus.Title = state.Availability.ToString();
            RuntimeStatus.Message = state.ErrorMessage ?? "Bluetooth is not currently available.";
            RuntimeStatus.Severity = InfoBarSeverity.Warning;
        }
        else
        {
            RuntimeStatus.Title = $"{rows.Length} device(s) discovered";
            RuntimeStatus.Message = state.LastUpdatedUtc is DateTimeOffset updated
                ? $"Last updated {updated.ToLocalTime():t}"
                : "Waiting for the first device scan...";
            RuntimeStatus.Severity = InfoBarSeverity.Success;
        }
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
            RuntimeStatus.Title = "Refresh failed";
            RuntimeStatus.Message = ex.Message;
            RuntimeStatus.Severity = InfoBarSeverity.Error;
        }
    }

    private sealed record DeviceRow(
        string Name,
        string BatteryLabel,
        string ConnectionLabel,
        int BatteryValue,
        bool IsBatteryUnknown,
        string SourceLabel);
}
