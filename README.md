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

1. Clone the repository
2. Open the solution in Visual Studio 2022 or later
3. Build the project
4. Run `HeartIt.exe`

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

## Building from Source

```bash
git clone https://github.com/yourusername/heart-it.git
cd heart-it
dotnet build
dotnet run --project HeartIt/HeartIt.csproj
```

## Technology Stack

- .NET 10.0
- WPF (Windows Presentation Foundation)
- Windows UI Automation
- Win32 API for global hotkeys

## License

[Add your license here]

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
