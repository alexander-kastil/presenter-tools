# Presenter Tools

Draw on your screen, capture and edit screenshots, and receive AirPlay screen mirroring on Windows 10/11 (x64).

## Quick start

1. Download [PresenterTools.msi](https://github.com/alexander-kastil/presenter-tools/releases/latest/download/PresenterTools.msi) (also under **Assets** on the [Releases](https://github.com/alexander-kastil/presenter-tools/releases) page), run it and click through the installer.
2. Open **Presenter Tools** from the Start menu. The pen bar appears at the top left.
3. Press **Print Screen** or click **Capture** to take a screenshot. It opens in the library.
4. Click **AirPlay** to receive screen mirroring, and add an API key in **Settings > AI** to use Optimize.

> Note: if Windows shows "Windows protected your PC" when you start the installer, click **More info**, then **Run anyway**. The installer is new and has not built up SmartScreen reputation yet.
>
> Note: run `setup.ps1` once in an administrator PowerShell to trust the certificate that signs the app and the MSI, so Windows stops showing "Unknown publisher".

## Features

| Feature | Summary |
| --- | --- |
| [Pen bar](docs/pen-bar.md) | Floating toolbar for drawing on screen, with colors, pen sizes and modes |
| [Optimize](docs/optimize.md) | Replaces your last pen drawing with clean text or shapes using an AI provider |
| [Tray icon](docs/tray-icon.md) | Shows or hides the pen bar and opens the receiver status, settings and exit |
| [Screen capture](docs/screen-capture.md) | Captures a screen region from the pen bar or with a global shortcut |
| [Library](docs/library.md) | Shows all captures in a gallery and opens the selected capture for editing |
| [Library editing tools](docs/library-editing.md) | Annotates, crops and exports the selected capture |
| [AirPlay receiver](docs/airplay-receiver.md) | Receives AirPlay screen mirroring from devices on your local network |
| [Settings](docs/settings.md) | Sets the AI provider, AirPlay receiver, capture shortcut, save location and startup options |
| [Start with Windows](docs/start-with-windows.md) | Starts Presenter Tools at sign-in, minimized to the tray |

## License

MIT License, (c) 2026 Alexander Kastil, [integrations.at](https://integrations.at). See [LICENSE](LICENSE).

The bundled AirPlay engine, [UxPlay](https://github.com/FDH2/UxPlay), is licensed separately under GPL-3.0. See [src/UxPlay/LICENSE](src/UxPlay/LICENSE).
