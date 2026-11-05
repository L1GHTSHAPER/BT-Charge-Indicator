# WinUI 3 host

The WinUI host composes the shared Core ApplicationRuntime with the existing Windows Bluetooth scanner. Scanner and protocol-reader source files are linked into BTChargeIndicator.Platform.Windows for this extraction step; protocol behavior is not rewritten.

The dashboard displays live device snapshots, connection state, battery values, reading source, Bluetooth availability, and last scan time. Manual refresh requests a GATT battery refresh through the same runtime. There is no mock device data and no second scan scheduler.

The existing WinForms executable remains buildable and retains its tray behavior. Each executable process owns one runtime instance when launched independently.

## Build

From the repository root on Windows with the .NET 10 SDK and Windows App SDK build prerequisites:

```powershell
dotnet restore .\src\BTChargeIndicator.WinUI\BTChargeIndicator.WinUI.csproj
dotnet build .\src\BTChargeIndicator.WinUI\BTChargeIndicator.WinUI.csproj -c Release --no-restore
```

The project currently targets unpackaged WinUI 3. Portable self-contained publishing and MSIX packaging remain separate deployment tasks.
