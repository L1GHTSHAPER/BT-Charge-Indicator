# WinUI 3 host

This project is the new presentation host for the BT Charge Indicator redesign.

## Current status

The first milestone establishes a separate WinUI 3 executable with a Fluent NavigationView shell and Dashboard/Settings page placeholders. It references the shared Core project but does not yet start Bluetooth acquisition or create a second scan loop.

The dashboard intentionally displays no mock device data. Runtime and Windows platform-service integration will be added in the next milestone. The existing WinForms executable remains the tested application and fallback during this transition.

## Build

From the repository root on Windows with the .NET 10 SDK and Windows App SDK build prerequisites:

```powershell
dotnet restore .\src\BTChargeIndicator.WinUI\BTChargeIndicator.WinUI.csproj
dotnet build .\src\BTChargeIndicator.WinUI\BTChargeIndicator.WinUI.csproj -c Release --no-restore
```

The project currently targets unpackaged WinUI 3. Self-contained portable publishing and MSIX packaging are separate deployment tasks and are not enabled by this shell scaffold.
