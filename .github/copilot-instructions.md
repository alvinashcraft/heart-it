# Copilot Instructions for HeartIt

## Project Overview

HeartIt is a WPF desktop app (.NET 10) that sends reactions in Microsoft Teams meetings via global hotkeys and UI Automation. It runs in the system tray and uses Win32 interop for hotkey registration and mouse/keyboard simulation.

## Architecture

- **Code-behind pattern** — no MVVM, no ViewModels, no data binding beyond styling. Event handlers in `MainWindow.xaml.cs` call into service classes directly.
- **Single service class** — `TeamsReactionService` handles all Teams interaction (process detection, UI Automation traversal, coordinate-based clicking).
- **Win32 interop throughout** — P/Invoke for `RegisterHotKey`, `SetForegroundWindow`, `SetCursorPos`, `mouse_event`, `keybd_event`. HwndSource message hook for `WM_HOTKEY`.
- **Packaging project** — `HeartIt.Package` is a `.wapproj` (Windows Application Packaging Project) that produces MSIX. It requires MSBuild, not `dotnet build`.
- **Theming** — uses WPF Fluent (`ThemeMode="System"` in `App.xaml`) plus a pair of custom resource dictionaries in `HeartIt/Themes/` (`Light.xaml` and `Dark.xaml`). `App.xaml.cs` listens to `SystemEvents.UserPreferenceChanged` and swaps the active dictionary on system theme changes. All custom brushes are referenced via `DynamicResource` so they re-resolve at runtime.

## Code Style

- **File-scoped namespaces**: `namespace HeartIt;`
- **Nullable reference types**: enabled — use `?` annotations for nullable references
- **Implicit usings**: enabled — don't add `using System;` etc.
- **Target-typed new**: prefer `new()` over `new ClassName()`
- **Switch expressions**: use for mapping enums and constants
- **Pattern matching**: use `is` patterns for type checks and null guards

## Naming Conventions

- **Private fields**: `_camelCase` (e.g., `_teamsService`, `_hwndSource`, `_busy`)
- **Win32 constants**: `UPPER_CASE` (e.g., `MOD_CONTROL`, `WM_HOTKEY`, `VK_1`)
- **Async methods**: `VerbAsync` suffix (e.g., `SendReactionAsync`, `FireReactionAsync`)
- **Event handlers**: `On` prefix (e.g., `OnReactionClick`, `OnWindowClosed`)
- **Enums**: PascalCase values (e.g., `ReactionType.Applause`)
- **XAML elements**: PascalCase with type prefix (e.g., `BtnLike`, `StatusIndicator`)

## Async & Threading

- **Async event handlers**: `async void` is acceptable only in event handlers — nowhere else
- **Fire-and-forget**: use `_ = FireReactionAsync(...)` with intentional discard to suppress warnings
- **Background work**: use `Task.Run` to move UI Automation calls off the UI thread
- **Concurrency guard**: use `Interlocked.CompareExchange` for non-blocking busy flags — not locks
- **UI thread polling**: use `DispatcherTimer` for periodic status checks
- **Deliberate delays**: `Thread.Sleep` is intentional in automation sequences for realistic interaction timing — don't replace with `Task.Delay` inside `Task.Run` blocks

## Error Handling

- **Return tuples**: methods return `(bool Success, string Message)` — callers display the message
- **Wide outer try-catch**: top-level methods catch all exceptions and return friendly messages
- **Silent inner catches**: UI Automation tree traversal may throw; catch and continue gracefully
- **Validation before action**: check coordinates, bounds, NaN/infinity before sending clicks
- **No exceptions for control flow**: return `(false, "reason")` instead of throwing

## Win32 / P/Invoke

- Use `[LibraryImport]` or `[DllImport]` with explicit `SetLastError` when needed
- Define structs with `[StructLayout(LayoutKind.Sequential)]` for interop
- Constants should be well-documented with inline comments explaining the Win32 meaning

## MSIX Packaging

- The packaging project (`HeartIt.Package/HeartIt.Package.wapproj`) is legacy MSBuild format
- Build with: `msbuild HeartIt.Package/HeartIt.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Never /restore`
- Target platforms: **x64** and **ARM64** only
- The `Publisher` in `Package.appxmanifest` must match the signing certificate subject
- See `docs/dev-instructions.md` for full build and signing details

## What Not To Do

- Don't introduce MVVM, DI containers, or abstractions unless explicitly requested — the app is intentionally simple
- Don't remove `Thread.Sleep` from automation sequences — the delays are required for Teams UI to respond
- Don't use `async void` outside of event handlers
- Don't add `using` directives for namespaces covered by implicit usings
