using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using BTChargeIndicator.Localization;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BTChargeIndicator.Services;

internal static class DesktopNotificationService
{
    private const string AppUserModelId = "L1GHTSHAPER.BTChargeIndicator";
    private const string AppIdRegistryPath = @"Software\Classes\AppUserModelId\L1GHTSHAPER.BTChargeIndicator";

    private static readonly PropertyKey AppUserModelIdKey = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    private static bool _registrationAttempted;

    public static void Initialize()
    {
        try
        {
            EnsureRegistered();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Windows toast registration failed.", ex);
        }
    }

    public static bool TryShow(
        string title,
        string message,
        string actionLabel,
        string actionUri,
        bool allowSnooze = false)
    {
        try
        {
            EnsureRegistered();

            var document = new XmlDocument();
            document.LoadXml(BuildToastXml(title, message, actionLabel, actionUri, allowSnooze));

            ToastNotificationManager
                .CreateToastNotifier(AppUserModelId)
                .Show(new ToastNotification(document));
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Windows toast notification failed.", ex);
            return false;
        }
    }

    internal static string BuildToastXml(
        string title, string message, string actionLabel, string actionUri, bool allowSnooze = false)
    {
        var escapedActionLabel = Escape(actionLabel);
        var escapedActionUri = Escape(actionUri);
        var dismiss = Escape(AppText.Get("Dismiss"));
        var actions = allowSnooze
            ? $"""
              <actions>
                <input id="snoozeTime" type="selection" defaultInput="60">
                  <selection id="15" content="{Escape(AppText.Get("Snooze15"))}" />
                  <selection id="60" content="{Escape(AppText.Get("Snooze60"))}" />
                  <selection id="180" content="{Escape(AppText.Get("Snooze180"))}" />
                </input>
                <action content="{Escape(AppText.Get("Snooze"))}" arguments="snooze" activationType="system" hint-inputId="snoozeTime" />
                <action content="{escapedActionLabel}" arguments="{escapedActionUri}" activationType="protocol" />
                <action content="{dismiss}" arguments="dismiss" activationType="system" />
              </actions>
              """
            : $"""
              <actions>
                <action content="{escapedActionLabel}" arguments="{escapedActionUri}" activationType="protocol" />
                <action content="{dismiss}" arguments="dismiss" activationType="system" />
              </actions>
              """;
        return $"""
               <toast duration="short">
                 <visual>
                   <binding template="ToastGeneric">
                     <text>{Escape(title)}</text>
                     <text>{Escape(message)}</text>
                   </binding>
                 </visual>
                 {actions}
               </toast>
               """;
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static void EnsureRegistered()
    {
        if (_registrationAttempted)
        {
            return;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(AppText.Get("AppPathError"));

        using (var key = Registry.CurrentUser.CreateSubKey(AppIdRegistryPath))
        {
            if (key is not null)
            {
                key.SetValue("DisplayName", "BT Charge Indicator", RegistryValueKind.String);
                key.SetValue("IconUri", executablePath, RegistryValueKind.String);
                key.SetValue("IconBackgroundColor", "0", RegistryValueKind.String);
            }
        }

        CreateOrUpdateStartMenuShortcut(executablePath);
        _registrationAttempted = true;
    }

    private static void CreateOrUpdateStartMenuShortcut(string executablePath)
    {
        var programsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcutPath = Path.Combine(programsDirectory, "BT Charge Indicator.lnk");
        var shellLink = (IShellLinkW)(object)new ShellLink();
        try
        {
            shellLink.SetPath(executablePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);
            shellLink.SetDescription("Bluetooth battery indicator");
            shellLink.SetIconLocation(executablePath, 0);

            var propertyStore = (IPropertyStore)shellLink;
            using (var appId = new PropVariant(AppUserModelId))
            {
                var appIdKey = AppUserModelIdKey;
                propertyStore.SetValue(ref appIdKey, appId);
                propertyStore.Commit();
            }

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int maxPath, IntPtr findData, uint flags);
        void GetIdList(out IntPtr idList);
        void SetIdList(IntPtr idList);
        void GetDescription(IntPtr name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(IntPtr arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCommand(out int showCommand);
        void SetShowCommand(int showCommand);
        void GetIconLocation(IntPtr iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, [Out] PropVariant value);
        void SetValue(ref PropertyKey key, [In] PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private sealed class PropVariant : IDisposable
    {
        [FieldOffset(0)]
        private readonly ushort _valueType;

        [FieldOffset(8)]
        private readonly IntPtr _pointerValue;

        public PropVariant(string value)
        {
            _valueType = 31;
            _pointerValue = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            PropVariantClear(this);
            GC.SuppressFinalize(this);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear([In, Out] PropVariant propVariant);
}
