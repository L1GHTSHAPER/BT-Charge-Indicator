using System.Runtime.InteropServices;
using BTChargeIndicator.Models;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BTChargeIndicator.WinUI;

// Uses the Windows Shell notification icon API directly, so the WinUI app does
// not need to load the WPF/Windows Forms UI frameworks.
internal sealed class WinUiTrayService : IDisposable
{
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint CallbackMessage = 0x8001;
    private const uint WmLeftDoubleClick = 0x0203;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmCommand = 0x0111;
    private const int WmClose = 0x0010;
    private const int WmNcDestroy = 0x0082;
    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint MenuOpen = 100;
    private const uint MenuRefresh = 101;
    private const uint MenuExit = 102;
    private static readonly UIntPtr SubclassId = new(1);

    private readonly SettingsStore _settingsStore = new();
    private readonly Action _activate;
    private readonly Action _exit;
    private readonly Dictionary<string, uint> _deviceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _idKeys = [];
    private readonly Dictionary<uint, IntPtr> _icons = [];
    private readonly Dictionary<uint, Action> _menuActions = [];
    private readonly List<IntPtr> _menuBitmaps = [];
    private readonly NativeWindowProc _windowProc;
    private IReadOnlyList<BluetoothBatteryDevice> _devices = [];
    private IntPtr _window;
    private uint _nextId = 2;
    private bool _disposed;
    private bool _allowClose;
    private uint _nextMenuCommand = 1000;

    internal static async Task<BitmapImage> CreateStylePreviewAsync(int? battery, TrayIconStyle style, int threshold)
    {
        const int size = 32;
        var pixels = IconPixels.RenderPixels(battery, style, threshold, size);
        var rgba = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            rgba[i * 4] = (byte)(pixel >> 16);
            rgba[i * 4 + 1] = (byte)(pixel >> 8);
            rgba[i * 4 + 2] = (byte)pixel;
            rgba[i * 4 + 3] = (byte)(pixel >> 24);
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, size, size, 96, 96, rgba);
        await encoder.FlushAsync();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    public WinUiTrayService(Action activate, Action exit)
    {
        _activate = activate;
        _exit = exit;
        _windowProc = WindowProc;
    }

    public void AttachWindow(Window window)
    {
        _window = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowSubclass(_window, _windowProc, SubclassId, IntPtr.Zero);
        UpdateIcon(1, "BT Charge Indicator", null, TrayIconStyle.Percentage, 20);
    }

    public void Refresh(IReadOnlyList<BluetoothBatteryDevice> devices)
    {
        if (_disposed) return;
        _devices = BatteryDeviceIdentity.Deduplicate(devices);
        var settings = _settingsStore.Load();
        var eligible = _devices.Where(device =>
        {
            var key = BatteryDeviceIdentity.GetKey(device);
            return !settings.Devices.TryGetValue(key, out var prefs) || prefs.IncludeInTrayIcon;
        }).Select(device => EffectiveBattery(device, settings)).ToArray();
        var combined = BatteryAggregationEngine.AggregateValues(eligible, settings.GlobalAggregationMode);
        var preferredDevice = _devices.FirstOrDefault(device => string.Equals(
            BatteryDeviceIdentity.GetKey(device), settings.PreferredTrayDeviceKey, StringComparison.OrdinalIgnoreCase));
        var mainBattery = preferredDevice is null ? combined : EffectiveBattery(preferredDevice, settings);
        var mainTip = preferredDevice is null ? "BT Charge Indicator" : (settings.Devices.GetValueOrDefault(
            BatteryDeviceIdentity.GetKey(preferredDevice))?.CustomName ?? preferredDevice.Name);
        UpdateIcon(1, mainTip + (mainBattery is int mainLevel ? $" · {mainLevel}%" : ""),
            mainBattery, settings.IconStyle, settings.LowBatteryThreshold);

        var active = new HashSet<uint> { 1 };
        foreach (var device in _devices)
        {
            var key = BatteryDeviceIdentity.GetKey(device);
            settings.Devices.TryGetValue(key, out var prefs);
            if (prefs?.ShowIndividualTrayIcon != true) continue;
            if (!_deviceIds.TryGetValue(key, out var id))
            {
                id = _nextId++;
                _deviceIds[key] = id;
                _idKeys[id] = key;
            }
            active.Add(id);
            var percent = EffectiveBattery(device, settings);
            var name = (prefs.CustomName ?? device.Name) + (percent is int value ? $" · {value}%" : " · —");
            UpdateIcon(id, name, percent, prefs.IndividualTrayIconStyle ?? settings.IconStyle, settings.LowBatteryThreshold);
        }

        foreach (var id in _icons.Keys.Where(id => !active.Contains(id)).ToArray()) RemoveIcon(id);
    }

    public void SettingsChanged() => Refresh(_devices);

    public void ShowMainWindow()
    {
        if (_window != IntPtr.Zero)
        {
            ShowWindow(_window, 9); // SW_RESTORE also unhides a minimized or hidden window.
            SetForegroundWindow(_window);
        }
        _activate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _icons.Keys.ToArray()) RemoveIcon(id);
        if (_window != IntPtr.Zero) RemoveWindowSubclass(_window, _windowProc, SubclassId);
        _window = IntPtr.Zero;
    }

    private void UpdateIcon(uint id, string tip, int? percent, TrayIconStyle style, int threshold)
    {
        if (_window == IntPtr.Zero) return;
        var icon = IconPixels.Create(percent, style, threshold, _window);
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _window,
            Id = id,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            Icon = icon,
            Tip = Truncate(tip, 127)
        };
        var operation = _icons.ContainsKey(id) ? NimModify : NimAdd;
        if (Shell_NotifyIcon(operation, ref data))
        {
            if (_icons.Remove(id, out var old)) DestroyIcon(old);
            _icons[id] = icon;
        }
        else DestroyIcon(icon);
    }

    private void RemoveIcon(uint id)
    {
        if (_window != IntPtr.Zero)
        {
            var data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = id };
            Shell_NotifyIcon(NimDelete, ref data);
        }
        if (_icons.Remove(id, out var icon)) DestroyIcon(icon);
        if (_idKeys.Remove(id, out var key)) _deviceIds.Remove(key);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData)
    {
        if (message == WmClose && !_allowClose)
        {
            ShowWindow(hwnd, 0); // SW_HIDE; keep monitoring and notification icons alive.
            return IntPtr.Zero;
        }
        if (message == CallbackMessage)
        {
            var eventCode = unchecked((uint)lParam.ToInt64());
            if (eventCode == WmLeftDoubleClick)
            {
                ShowMainWindow();
                return IntPtr.Zero;
            }
            if (eventCode == WmRightButtonUp)
            {
                ShowTrayMenu(hwnd, unchecked((uint)wParam.ToInt64()));
                return IntPtr.Zero;
            }
        }
        if (message == WmCommand)
        {
            var command = unchecked((uint)wParam.ToInt64()) & 0xFFFF;
            if (_menuActions.Remove(command, out var action))
            {
                action();
                return IntPtr.Zero;
            }
            switch (command)
            {
                case MenuOpen: ShowMainWindow(); return IntPtr.Zero;
                case MenuRefresh: _ = App.Runtime?.RefreshAsync(refreshGattBattery: true); return IntPtr.Zero;
                case MenuExit: _allowClose = true; _exit(); return IntPtr.Zero;
            }
        }
        if (message == WmNcDestroy)
        {
            _window = IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowTrayMenu(IntPtr hwnd, uint iconId)
    {
        _menuActions.Clear();
        foreach (var bitmap in _menuBitmaps) DeleteObject(bitmap);
        _menuBitmaps.Clear();
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, new UIntPtr(MenuOpen), WinUIText.Get("TrayOpen"));
            AppendMenu(menu, 0, new UIntPtr(MenuRefresh), WinUIText.Get("TrayRefresh"));
            var settings = _settingsStore.Load();
            if (iconId == 1)
            {
                AddPreferredDeviceMenu(menu, settings);
                var preferred = _devices.FirstOrDefault(device => string.Equals(
                    BatteryDeviceIdentity.GetKey(device), settings.PreferredTrayDeviceKey, StringComparison.OrdinalIgnoreCase));
                var preferredBattery = preferred is null
                    ? BatteryAggregationEngine.AggregateValues(_devices.Where(device =>
                    {
                        var key = BatteryDeviceIdentity.GetKey(device);
                        return !settings.Devices.TryGetValue(key, out var prefs) || prefs.IncludeInTrayIcon;
                    }).Select(device => EffectiveBattery(device, settings)), settings.GlobalAggregationMode)
                    : EffectiveBattery(preferred, settings);
                AddStyleMenu(menu, settings.IconStyle, null, preferredBattery, settings.LowBatteryThreshold);
            }
            else if (_idKeys.TryGetValue(iconId, out var deviceKey))
            {
                var device = _devices.FirstOrDefault(item => string.Equals(
                    BatteryDeviceIdentity.GetKey(item), deviceKey, StringComparison.OrdinalIgnoreCase));
                if (device is not null && settings.Devices.TryGetValue(deviceKey, out var preferences))
                {
                    AddBatteryModeMenu(menu, device, preferences.BatteryMode, settings);
                    AddStyleMenu(menu, preferences.IndividualTrayIconStyle ?? settings.IconStyle,
                        device, EffectiveBattery(device, settings), settings.LowBatteryThreshold);
                }
            }
            AppendMenu(menu, 0x0800, UIntPtr.Zero, null); // MF_SEPARATOR
            AppendMenu(menu, 0, new UIntPtr(MenuExit), WinUIText.Get("TrayExit"));
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            TrackPopupMenu(menu, 0x0002, point.X, point.Y, 0, hwnd, IntPtr.Zero); // TPM_RIGHTBUTTON
            PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
            foreach (var bitmap in _menuBitmaps) DeleteObject(bitmap);
            _menuBitmaps.Clear();
            _menuActions.Clear();
        }
    }

    private void AddPreferredDeviceMenu(IntPtr root, AppSettings settings)
    {
        var submenu = CreatePopupMenu();
        AppendMenu(root, 0x10 | 0x100, new UIntPtr(unchecked((ulong)submenu.ToInt64())), WinUIText.Get("TrayPreferredDevice"));
        AddMenuCommand(submenu, settings.PreferredTrayDeviceKey is null, WinUIText.Get("TrayAllDevices"),
            () => SaveSettings(value => value.PreferredTrayDeviceKey = null));
        AppendMenu(submenu, 0x0800, UIntPtr.Zero, null);
        foreach (var device in _devices)
        {
            var key = BatteryDeviceIdentity.GetKey(device);
            settings.Devices.TryGetValue(key, out var preferences);
            var name = preferences?.CustomName ?? device.Name;
            var percent = EffectiveBattery(device, settings);
            AddMenuCommand(submenu, string.Equals(settings.PreferredTrayDeviceKey, key, StringComparison.OrdinalIgnoreCase),
                $"{name} ({(percent is int level ? $"{level}%" : "—")})",
                () => SaveSettings(value => value.PreferredTrayDeviceKey = key));
        }
    }

    private void AddBatteryModeMenu(IntPtr root, BluetoothBatteryDevice device, BatteryAggregationMode? currentMode, AppSettings settings)
    {
        var submenu = CreatePopupMenu();
        AppendMenu(root, 0x10 | 0x100, new UIntPtr(unchecked((ulong)submenu.ToInt64())), WinUIText.Get("TrayBatteryChoice"));
        var modes = new (BatteryAggregationMode? Mode, string Key)[]
        {
            (null, "BatteryModeAutomatic"), (BatteryAggregationMode.Left, "BatteryModeLeft"),
            (BatteryAggregationMode.Right, "BatteryModeRight"), (BatteryAggregationMode.Case, "BatteryModeCase"),
            (BatteryAggregationMode.EarbudsLowest, "BatteryModeEarbudsLowest"),
            (BatteryAggregationMode.EarbudsAverage, "BatteryModeEarbudsAverage")
        };
        foreach (var (mode, keyName) in modes)
        {
            AddMenuCommand(submenu, currentMode == mode, WinUIText.Get(keyName), () =>
                UpdateDevicePreferences(device, value => value.BatteryMode = mode));
        }
    }

    private void AddStyleMenu(IntPtr root, TrayIconStyle current, BluetoothBatteryDevice? device,
        int? battery, int threshold)
    {
        var submenu = CreatePopupMenu();
        AppendMenu(root, 0x10 | 0x100, new UIntPtr(unchecked((ulong)submenu.ToInt64())), WinUIText.Get("TrayIconStyle"));
        foreach (var style in Enum.GetValues<TrayIconStyle>())
        {
            var command = _nextMenuCommand++;
            var label = WinUIText.Get("Style" + style);
            AppendMenu(submenu, 0, new UIntPtr(command), label);
            var bitmap = IconPixels.CreateMenuBitmap(battery, style, threshold, 20);
            _menuBitmaps.Add(bitmap);
            var item = new MenuItemInfo
            {
                Size = (uint)Marshal.SizeOf<MenuItemInfo>(),
                Mask = 0x80 | 0x01,
                State = style == current ? 0x08u : 0u,
                Bitmap = bitmap
            };
            SetMenuItemInfo(submenu, command, false, ref item);
            _menuActions[command] = device is null
                ? () => SaveSettings(value => value.IconStyle = style)
                : () => UpdateDevicePreferences(device, value => value.IndividualTrayIconStyle = style);
        }
    }

    private void AddMenuCommand(IntPtr menu, bool isChecked, string text, Action action)
    {
        var command = _nextMenuCommand++;
        AppendMenu(menu, 0, new UIntPtr(command), text);
        CheckMenuItem(menu, command, isChecked ? 0x08u : 0x00u);
        _menuActions[command] = action;
    }

    private void SaveSettings(Action<AppSettings> update)
    {
        var settings = _settingsStore.Load();
        update(settings);
        _settingsStore.Save(settings);
        SettingsChanged();
    }

    private void UpdateDevicePreferences(BluetoothBatteryDevice device, Action<DevicePreferences> update)
    {
        var settings = _settingsStore.Load();
        var key = BatteryDeviceIdentity.GetKey(device);
        settings.Devices.TryGetValue(key, out var preferences);
        preferences ??= new DevicePreferences();
        preferences.LastKnownName = device.Name;
        update(preferences);
        settings.Devices[key] = preferences;
        _settingsStore.Save(settings);
        SettingsChanged();
    }

    private static int? EffectiveBattery(BluetoothBatteryDevice device, AppSettings settings)
    {
        settings.Devices.TryGetValue(BatteryDeviceIdentity.GetKey(device), out var prefs);
        var mode = prefs?.BatteryMode ?? (device.Components is null ? BatteryAggregationMode.Lowest : BatteryAggregationMode.EarbudsLowest);
        return BatteryAggregationEngine.Aggregate(device.Components, mode, device.BatteryPercent);
    }

    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit];

    private delegate IntPtr NativeWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, NativeWindowProc callback, UIntPtr subclassId, IntPtr referenceData);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, NativeWindowProc callback, UIntPtr subclassId);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr item, string? text);
    [DllImport("user32.dll")] private static extern uint CheckMenuItem(IntPtr menu, uint item, uint check);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMenuItemInfo(IntPtr menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint Size;
        public uint Mask;
        public uint Type;
        public uint State;
        public uint Id;
        public IntPtr SubMenu;
        public IntPtr CheckedBitmap;
        public IntPtr UncheckedBitmap;
        public UIntPtr ItemData;
        public IntPtr TypeData;
        public uint CharacterCount;
        public IntPtr Bitmap;
    }

    private static class IconPixels
    {
        private static readonly string[] Digits = ["111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"];

        public static IntPtr Create(int? rawPercent, TrayIconStyle style, int threshold, IntPtr hwnd)
        {
            var dpi = hwnd == IntPtr.Zero ? 96u : GetDpiForWindow(hwnd);
            var size = Math.Clamp(GetSystemMetricsForDpi(49, dpi), 16, 64);
            var pixels = RenderPixels(rawPercent, style, threshold, size);
            var colorBitmap = CreateDibBitmap(pixels, size);
            var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            var iconInfo = new IconInfo { IsIcon = true, MaskBitmap = mask, ColorBitmap = colorBitmap };
            var icon = CreateIconIndirect(ref iconInfo);
            DeleteObject(colorBitmap);
            DeleteObject(mask);
            return icon;
        }

        public static IntPtr CreateMenuBitmap(int? percent, TrayIconStyle style, int threshold, int size) =>
            CreateDibBitmap(RenderPixels(percent, style, threshold, size), size);

        public static uint[] RenderPixels(int? rawPercent, TrayIconStyle style, int threshold, int size)
        {
            var percent = rawPercent is int raw ? Math.Clamp(raw, 0, 100) : (int?)null;
            var color = percent switch
            {
                null => 0xFF69707A,
                int value when value <= threshold => 0xFFDC3737,
                int value when value <= Math.Min(threshold + 20, 100) => 0xFFEB9123,
                _ => 0xFF209E5C
            };
            var scale = size / 16f;
            var pixels = new uint[size * size];
            if (style is TrayIconStyle.Ring or TrayIconStyle.SegmentedRing or TrayIconStyle.Gauge)
                DrawRing(pixels, size, percent, color, scale,
                    segmented: style == TrayIconStyle.SegmentedRing,
                    gauge: style == TrayIconStyle.Gauge);
            else if (style == TrayIconStyle.Bars)
                DrawBars(pixels, size, percent, color, scale);
            else if (style is TrayIconStyle.Battery or TrayIconStyle.VerticalBattery)
                DrawBattery(pixels, size, percent, color, scale);
            else if (style == TrayIconStyle.Minimal)
            {
                DrawDigits(pixels, size, percent, color, scale);
            }
            else if (style == TrayIconStyle.Capsule)
            {
                DrawCapsule(pixels, size, percent, color, scale);
            }
            else if (style == TrayIconStyle.Gradient)
            {
                Array.Fill(pixels, color);
                DrawDigits(pixels, size, percent, 0xFFFFFFFF, scale);
            }
            else
            {
                Array.Fill(pixels, 0xFF1C222D);
                Fill(pixels, size, 0, size - Scale(2, scale), size, size, color);
                DrawDigits(pixels, size, percent, 0xFFFFFFFF, scale);
            }

            return pixels;
        }

        private static IntPtr CreateDibBitmap(uint[] pixels, int size)
        {
            var screen = GetDC(IntPtr.Zero);
            try
            {
                var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = size, Height = -size, Planes = 1, BitCount = 32, Compression = BiRgb } };
                var bitmap = CreateDIBSection(screen, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
                Marshal.Copy(pixels.Select(pixel => unchecked((int)pixel)).ToArray(), 0, bits, pixels.Length);
                return bitmap;
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }

        private static void DrawDigits(uint[] p, int size, int? value, uint color, float scale)
        {
            if (value is null)
            {
                var y = size / 2 - Scale(1, scale) / 2;
                Fill(p, size, Scale(4, scale), y, Scale(5, scale), y + Scale(2, scale), color);
                Fill(p, size, Scale(7, scale), y, Scale(8, scale), y + Scale(2, scale), color);
                Fill(p, size, Scale(10, scale), y, Scale(11, scale), y + Scale(2, scale), color);
                return;
            }
            var text = value.Value.ToString();
            var glyphScale = Math.Max(1, (int)Math.Floor(size / (text.Length * 4f - 1)));
            var width = text.Length * 3 * glyphScale + (text.Length - 1) * glyphScale;
            var x0 = (size - width) / 2;
            var y0 = (size - 5 * glyphScale) / 2;
            for (var n = 0; n < text.Length; n++)
            {
                var glyph = Digits[text[n] - '0'];
                for (var i = 0; i < 15; i++) if (glyph[i] == '1')
                {
                    var x = x0 + n * 4 * glyphScale + i % 3 * glyphScale;
                    var y = y0 + i / 3 * glyphScale;
                    Fill(p, size, x, y, x + glyphScale, y + glyphScale, color);
                }
            }
        }

        private static void DrawBattery(uint[] p, int size, int? value, uint color, float scale)
        {
            Fill(p, size, Scale(4, scale), Scale(4, scale), size - Scale(4, scale), size - Scale(2, scale), 0xFFFFFFFF);
            Fill(p, size, Scale(7, scale), Scale(2, scale), Scale(9, scale), Scale(4, scale), 0xFFFFFFFF);
            Fill(p, size, Scale(5, scale), Scale(5, scale), size - Scale(5, scale), size - Scale(3, scale), 0xFF535E6F);
            if (value is int level)
            {
                var bottom = size - Scale(3, scale);
                var fillHeight = (size - Scale(8, scale)) * level / 100;
                Fill(p, size, Scale(6, scale), bottom - fillHeight, size - Scale(6, scale), bottom, color);
            }
        }

        private static void DrawBars(uint[] p, int size, int? value, uint color, float scale)
        {
            for (var i = 0; i < 5; i++)
            {
                var height = Scale(3 + i * 2, scale);
                var active = value is int level && level >= (i + 1) * 20;
                var x = Scale(2 + i * 3, scale);
                Fill(p, size, x, size - Scale(2, scale) - height, x + Scale(2, scale), size - Scale(2, scale), active ? color : 0xFF535E6F);
            }
        }

        private static void DrawRing(uint[] p, int size, int? value, uint color, float scale, bool segmented, bool gauge)
        {
            var level = value ?? 0;
            var center = (size - 1) / 2f;
            var outer = size * 0.47f;
            var inner = size * 0.32f;
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var dx = x - center; var dy = y - center; var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= outer && distance >= inner)
                {
                    var angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 450) % 360;
                    if (gauge && angle > 270) continue;
                    if (segmented && ((int)angle / 12) % 2 == 1) continue;
                    if (value is not null && angle <= level * 3.6) Set(p, size, x, y, color); else Set(p, size, x, y, 0xFF535E6F);
                }
            }
            DrawDigits(p, size, value, 0xFFFFFFFF, scale);
        }

        private static void DrawCapsule(uint[] pixels, int size, int? percent, uint color, float scale)
        {
            var left = Scale(2, scale); var right = size - left;
            var top = Scale(5, scale); var bottom = size - top;
            for (var y = top; y < bottom; y++)
            {
                var inset = y < top + Scale(2, scale) || y >= bottom - Scale(2, scale) ? Scale(2, scale) : 0;
                Fill(pixels, size, left + inset, y, right - inset, y + 1, 0xFF535E6F);
                if (percent is int level)
                {
                    var fillRight = left + (right - left - inset * 2) * level / 100;
                    Fill(pixels, size, left + inset, y, fillRight, y + 1, color);
                }
            }
            DrawDigits(pixels, size, percent, 0xFFFFFFFF, scale);
        }

        private static int Scale(int value, float scale) => Math.Max(1, (int)Math.Round(value * scale));
        private static void Fill(uint[] p, int size, int x1, int y1, int x2, int y2, uint color)
        {
            for (var y = Math.Max(y1, 0); y < Math.Min(y2, size); y++) for (var x = Math.Max(x1, 0); x < Math.Min(x2, size); x++) Set(p, size, x, y, color);
        }
        private static void Set(uint[] p, int size, int x, int y, uint color) { if ((uint)x < size && (uint)y < size) p[y * size + x] = color; }

        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint SizeImage; public int XPelsPerMeter; public int YPelsPerMeter; public uint ClrUsed; public uint ClrImportant; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }
        [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint XHotspot; public uint YHotspot; public IntPtr MaskBitmap; public IntPtr ColorBitmap; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPixel, IntPtr bits);
        [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    }
}
