using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    private RuntimeHost? _runtime;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.LanguageChanged -= ApplyLocalizedText;
        App.LanguageChanged += ApplyLocalizedText;
        ApplyLocalizedText();

        _runtime = App.Runtime;
        if (_runtime is not { } runtime)
        {
            SaveStatus.Text = WinUIText.Get("RuntimeUnavailable");
            return;
        }

        SelectItem(RefreshIntervalComboBox, runtime.RefreshIntervalSeconds);
        SelectItem(LowBatteryThresholdComboBox, runtime.LowBatteryThreshold);
        SelectItem(LanguageComboBox, (int)runtime.Language);
        var settings = runtime.LoadSettings();
        LowBatteryNotificationsToggle.IsOn = settings.LowBatteryNotifications;
        ConnectionNotificationsToggle.IsOn = settings.ConnectionNotifications;
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.LanguageChanged -= ApplyLocalizedText;
        if (_runtime is not null)
        {
            _runtime = null;
        }
    }

    private void ApplyLocalizedText()
    {
        SettingsTitleText.Text = WinUIText.Get("SettingsTitle");
        SettingsDescriptionText.Text = WinUIText.Get("SettingsDescription");
        MonitoringTitleText.Text = WinUIText.Get("Monitoring");
        RefreshIntervalTitleText.Text = WinUIText.Get("RefreshInterval");
        RefreshHelpText.Text = WinUIText.Get("RefreshHelp");
        LowBatteryThresholdTitleText.Text = WinUIText.Get("LowBatteryThreshold");
        LowBatteryThresholdDescriptionText.Text = WinUIText.Get("LowBatteryThresholdHelp");
        GeneralTitleText.Text = WinUIText.Get("General");
        LanguageTitleText.Text = WinUIText.Get("Language");
        LanguageDescriptionText.Text = WinUIText.Get("LanguageHelp");
        NotificationsTitleText.Text = WinUIText.Get("Notifications");
        LowBatteryNotificationsTitleText.Text = WinUIText.Get("LowBatteryNotifications");
        LowBatteryNotificationsDescriptionText.Text = WinUIText.Get("LowBatteryNotificationsHelp");
        ConnectionNotificationsTitleText.Text = WinUIText.Get("ConnectionNotifications");
        ConnectionNotificationsDescriptionText.Text = WinUIText.Get("ConnectionNotificationsHelp");
        SaveButton.Content = WinUIText.Get("Save");

        SetItemLabels(RefreshIntervalComboBox, "Every30Seconds", "EveryMinute", "Every5Minutes");
        SetItemLabels(LowBatteryThresholdComboBox,
            "Battery10", "Battery15", "Battery20", "Battery25", "Battery30", "Battery40", "Battery50");
        SetItemLabels(LanguageComboBox, "LanguageAuto", "LanguageRussian", "LanguageEnglish");
    }

    private static void SetItemLabels(ComboBox comboBox, params string[] keys)
    {
        var selectedIndex = comboBox.SelectedIndex;
        for (var index = 0; index < comboBox.Items.Count && index < keys.Length; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item)
            {
                item.Content = WinUIText.Get(keys[index]);
            }
        }

        if (selectedIndex >= 0 && selectedIndex < comboBox.Items.Count)
        {
            RefreshSelectionDisplay(comboBox);
        }
    }

    private static void SelectItem(ComboBox comboBox, int value)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var itemSeconds) &&
                itemSeconds == value)
            {
                comboBox.SelectedIndex = index;
                RefreshSelectionDisplay(comboBox);
                break;
            }
        }
    }

    private static void RefreshSelectionDisplay(ComboBox comboBox)
    {
        var selectedIndex = comboBox.SelectedIndex;
        if (selectedIndex < 0)
        {
            return;
        }

        comboBox.SelectedIndex = -1;
        comboBox.SelectedIndex = selectedIndex;
        comboBox.UpdateLayout();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Runtime is not { } runtime ||
            RefreshIntervalComboBox.SelectedItem is not ComboBoxItem intervalItem ||
            !int.TryParse(intervalItem.Tag?.ToString(), out var seconds) ||
            LowBatteryThresholdComboBox.SelectedItem is not ComboBoxItem thresholdItem ||
            !int.TryParse(thresholdItem.Tag?.ToString(), out var threshold) ||
            LanguageComboBox.SelectedItem is not ComboBoxItem languageItem ||
            !int.TryParse(languageItem.Tag?.ToString(), out var languageValue) ||
            !Enum.IsDefined((AppLanguage)languageValue))
        {
            SaveStatus.Text = WinUIText.Get("ChooseSettings");
            return;
        }

        try
        {
            runtime.UpdateLowBatteryThreshold(threshold);
            runtime.UpdateNotificationSettings(
                LowBatteryNotificationsToggle.IsOn,
                ConnectionNotificationsToggle.IsOn);
            runtime.UpdateLanguage((AppLanguage)languageValue);
            App.ApplyLanguage((AppLanguage)languageValue);
            await runtime.UpdateRefreshIntervalAsync(seconds);
            SaveStatus.Text = WinUIText.Get("SettingsSaved");
        }
        catch (Exception ex)
        {
            SaveStatus.Text = WinUIText.Format("SettingsFailed", ex.Message);
        }
    }

}
