using Microsoft.UI.Xaml.Controls;
using BTChargeIndicator.WinUI.Views;

namespace BTChargeIndicator.WinUI;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        InitializeComponent();
        App.LanguageChanged += ApplyLanguage;
        Closed += (_, _) => App.LanguageChanged -= ApplyLanguage;
        ApplyLanguage();
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        NavigateTo("Dashboard");
    }

    private void ApplyLanguage()
    {
        DashboardNavigationItem.Content = WinUIText.Get("NavDashboard");
        SettingsNavigationItem.Content = WinUIText.Get("NavSettings");
    }

    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string pageTag)
        {
            NavigateTo(pageTag);
        }
    }

    private void NavigateTo(string pageTag)
    {
        var pageType = pageTag switch
        {
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
