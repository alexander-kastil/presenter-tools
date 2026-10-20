---
paths:
  - "src/PresenterTools/**"
  - "src/PresenterTools.Setup/**"
---

# Presenter Tools desktop (src/PresenterTools)

- Owner agent: `wpf-expert`.
- Stack: WPF plus WinForms (`UseWindowsForms` for the tray `NotifyIcon`) on `net10.0-windows`, x64, `TreatWarningsAsErrors`, no Fluent theme. Solution `src/PresenterTools.slnx`.
- Tests: `src/PresenterTools.Tests` (xUnit, FluentAssertions), run with `dotnet test src/PresenterTools.Tests/PresenterTools.Tests.csproj -c Release` (Release, so it runs beside the locked Debug build). `Sta.cs` hosts one STA thread and one `Application` for the whole run, PerMonitorV2 like the app; `TempSettings` points `AppSettings.FilePathOverride` at a temp file so no test reads or writes the real `settings.json`. The tests briefly show real toolbar and editor windows without taking focus.
- Version: `<Version>` in `src/PresenterTools/PresenterTools.csproj`; the wixproj's `AppVersion` defaults to the same value; release tags are `v<major>.<minor>.<patch>`.
- Running app: do not stop the user's instance unless they say so. It locks `src/PresenterTools/bin/Debug/net10.0-windows/PresenterTools.exe`; compile-check with a Release build or a build into `%TEMP%` instead.
- Lifecycle: `App.xaml` has `ShutdownMode="OnExplicitShutdown"`; `App.xaml.cs` holds a named mutex `Local\integrations.at.PresenterTools` (a second instance exits, nothing activates the first) and skips showing the toolbar when started with `--autostart`.
- Tray: `TrayIcon.cs` (left-click toggles the pen bar; menu: show or hide toolbar, receiver status, start or stop receiver, "Start with Windows (minimized)", "Settings...", Exit). The status window hides on close.
- Autostart: `Autostart.cs` writes HKCU Run `PresenterTools` = `"<ProcessPath>" --autostart`; the state is the registry value, never `settings.json`. `LegacyMigration.cs` renames an old `ScreenTools` value.
- Settings: `%APPDATA%\PresenterTools\settings.json` (`AppSettings.cs`, `JsonNode`); the API key and AirPlay PIN and password are DPAPI-encrypted with a `dpapi:` prefix. Logs and keys live in `%LOCALAPPDATA%\PresenterTools`. The settings window is code-only (`SettingsWindow.cs`).
- Installer: `dotnet build src/PresenterTools.Setup/PresenterTools.Setup.wixproj -c Release` stages the UxPlay engine through MSYS2 (`C:\msys64\usr\bin\bash.exe`, override with `-p:MsysBash=`), publishes self-contained win-x64, signs when the certificate thumbprint is in `Cert:\CurrentUser\My`, and copies the MSI to `setup/PresenterTools.msi`. CI: `.github/workflows/release.yml`; signing and release upload: `sign-release.ps1`.
- User docs: `docs/` (`tray-icon.md`, `start-with-windows.md`, `settings.md` and the feature pages). A behaviour change to a documented feature is reported to the owner; docs are written only when asked.
