using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;

namespace BTChargeIndicator.Services;

internal static class DualSenseBatteryService
{
    private const string ContainerIdProperty = "System.Devices.ContainerId";

    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort GamepadUsageId = 0x05;
    private const ushort SonyVendorId = 0x054C;
    private const ushort DualSenseProductId = 0x0CE6;
    private const ushort DualSenseEdgeProductId = 0x0DF2;

    private const byte BluetoothFullReportId = 0x31;
    private const byte TruncatedReportId = 0x01;
    private const byte CalibrationFeatureReportId = 0x05;
    private const int BluetoothReportLength = 78;
    private const int UsbReportLength = 64;
    private const int BluetoothBatteryOffset = 54;
    private const int MaximumBatteryLevel = 10;
    private const int ChargeCompleteState = 2;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    private static readonly string[] RequestedProperties = [ContainerIdProperty];

    public static async Task<IReadOnlyList<DualSenseBatteryReading>> FindReadingsAsync()
    {
        try
        {
            var selector = HidDevice.GetDeviceSelector(GenericDesktopUsagePage, GamepadUsageId);
            var devices = await DeviceInformation.FindAllAsync(
                selector,
                RequestedProperties,
                DeviceInformationKind.DeviceInterface);

            var reads = devices.Select(TryReadDeviceAsync);
            return (await Task.WhenAll(reads))
                .OfType<DualSenseBatteryReading>()
                .ToArray();
        }
        catch
        {
            // HID support is a best-effort fallback and must not break the main Bluetooth scan.
            return [];
        }
    }

    internal static int? ParseBluetoothBatteryPercent(ReadOnlySpan<byte> report)
    {
        if (report.Length <= BluetoothBatteryOffset || report[0] != BluetoothFullReportId)
        {
            return null;
        }

        var power = report[BluetoothBatteryOffset];
        var level = power & 0x0F;
        var state = (power & 0xF0) >> 4;

        if (level > MaximumBatteryLevel)
        {
            return null;
        }

        return state == ChargeCompleteState ? 100 : level * 10;
    }

    private static async Task<DualSenseBatteryReading?> TryReadDeviceAsync(DeviceInformation device)
    {
        using var handle = OpenDevice(device.Id);
        if (handle.IsInvalid || !IsSupportedDualSense(handle))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(handle, FileAccess.Read, BluetoothReportLength, isAsync: true);
            var buffer = new byte[BluetoothReportLength];
            var bytesRead = await ReadReportAsync(stream, buffer, TimeSpan.FromMilliseconds(800));
            if (bytesRead <= 0)
            {
                return null;
            }

            var battery = ParseBluetoothBatteryPercent(buffer.AsSpan(0, bytesRead));
            if (battery.HasValue)
            {
                return new DualSenseBatteryReading(ReadContainerId(device), battery.Value);
            }

            // Bluetooth starts with a short 0x01 report. Requesting calibration switches the
            // controller to its full 0x31 report, which contains the battery status byte.
            if (buffer[0] != TruncatedReportId || bytesRead == UsbReportLength || !EnableFullBluetoothReports(handle))
            {
                return null;
            }

            NativeMethods.HidD_FlushQueue(handle);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                bytesRead = await ReadReportAsync(stream, buffer, TimeSpan.FromMilliseconds(250));
                if (bytesRead <= 0)
                {
                    return null;
                }

                battery = ParseBluetoothBatteryPercent(buffer.AsSpan(0, bytesRead));
                if (battery.HasValue)
                {
                    return new DualSenseBatteryReading(ReadContainerId(device), battery.Value);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A connected controller normally reports every few milliseconds; a timeout means
            // this interface is not currently producing usable DualSense reports.
        }
        catch (IOException)
        {
            // The controller may disconnect while a report is being read.
        }
        catch (UnauthorizedAccessException)
        {
            // Another driver may have opened this HID collection exclusively.
        }

        return null;
    }

    private static async Task<int> ReadReportAsync(FileStream stream, byte[] buffer, TimeSpan timeout)
    {
        Array.Clear(buffer);
        using var cancellation = new CancellationTokenSource(timeout);
        return await stream.ReadAsync(buffer.AsMemory(), cancellation.Token);
    }

    private static SafeFileHandle OpenDevice(string path)
    {
        var handle = NativeMethods.CreateFile(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            return handle;
        }

        handle.Dispose();
        return NativeMethods.CreateFile(
            path,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);
    }

    private static bool IsSupportedDualSense(SafeFileHandle handle)
    {
        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        return NativeMethods.HidD_GetAttributes(handle, ref attributes) &&
               attributes.VendorId == SonyVendorId &&
               attributes.ProductId is DualSenseProductId or DualSenseEdgeProductId;
    }

    private static bool EnableFullBluetoothReports(SafeFileHandle handle)
    {
        var featureReport = new byte[BluetoothReportLength];
        featureReport[0] = CalibrationFeatureReportId;
        return NativeMethods.HidD_GetFeature(handle, featureReport, featureReport.Length);
    }

    private static Guid? ReadContainerId(DeviceInformation device)
    {
        if (!device.Properties.TryGetValue(ContainerIdProperty, out var value) || value is null)
        {
            return null;
        }

        if (value is Guid guid && guid != Guid.Empty)
        {
            return guid;
        }

        return Guid.TryParse(value.ToString(), out guid) && guid != Guid.Empty ? guid : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetFeature(SafeFileHandle device, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_FlushQueue(SafeFileHandle device);
    }
}

internal sealed record DualSenseBatteryReading(Guid? ContainerId, int BatteryPercent);
