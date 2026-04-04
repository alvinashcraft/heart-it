# HeartIt

A Windows desktop application that enables quick reactions in Microsoft Teams using global keyboard shortcuts.

## Features

- **Global Hotkeys**: Send Teams reactions from anywhere on your system without switching windows
- **Multiple Reactions**: Support for Like, Love, Applause, Laugh, and Surprised reactions
- **System Tray Integration**: Runs quietly in the background with easy access from the system tray
- **Automatic Teams Detection**: Automatically finds and interacts with your Microsoft Teams window

## Requirements

- Windows OS
- .NET 10.0
- Microsoft Teams (classic or new Teams app)

## Installation

### From winget (coming soon)

```bash
winget install HeartIt
```

### From GitHub Releases

1. Download the latest `.msixbundle` from [Releases](../../releases)
2. Double-click to install (you may need to install the signing certificate first for sideloaded packages)

### Building from Source

```bash
git clone https://github.com/yourusername/heart-it.git
cd heart-it
dotnet build
dotnet run --project HeartIt/HeartIt.csproj
```

### Building the MSIX Package

The packaging project uses the Windows Application Packaging Project format. To build the MSIX locally:

```powershell
msbuild HeartIt.Package/HeartIt.Package.wapproj /p:Configuration=Release /p:Platform=x64 /restore
```

To create a release, push a version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This triggers the GitHub Actions workflow that builds MSIX packages for x64 and ARM64 and creates an `.msixbundle`.

## Usage

Once the application is running, use the following keyboard shortcuts while in a Teams meeting:

- **Ctrl+Alt+1**: Like 👍
- **Ctrl+Alt+2**: Love ❤️
- **Ctrl+Alt+3**: Applause 👏
- **Ctrl+Alt+4**: Laugh 😂
- **Ctrl+Alt+5**: Surprised 😮

The application runs in the system tray. Right-click the tray icon to exit.

## How It Works

HeartIt uses Windows UI Automation to interact with the Microsoft Teams application. When you press a hotkey, the app:

1. Locates the active Teams meeting window
2. Activates the Teams window
3. Triggers the reaction flyout
4. Selects the appropriate reaction
5. Returns focus to your previous window

## Technology Stack

- .NET 10.0
- WPF (Windows Presentation Foundation)
- Windows UI Automation
- Win32 API for global hotkeys

## License

[Add your license here]

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
