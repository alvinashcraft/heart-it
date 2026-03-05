using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HeartIt.Services;

namespace HeartIt;

public partial class MainWindow : Window
{
    private readonly TeamsReactionService _teamsService = new();
    private readonly DispatcherTimer _statusTimer;
    private HwndSource? _hwndSource;

    // Win32 global hotkey support
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    // Hotkey IDs
    private const int HK_LIKE = 1;
    private const int HK_LOVE = 2;
    private const int HK_APPLAUSE = 3;
    private const int HK_LAUGH = 4;
    private const int HK_SURPRISED = 5;

    // Virtual key codes for 1-5
    private const uint VK_1 = 0x31;
    private const uint VK_2 = 0x32;
    private const uint VK_3 = 0x33;
    private const uint VK_4 = 0x34;
    private const uint VK_5 = 0x35;

    public MainWindow()
    {
        InitializeComponent();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        UpdateStatus();
        SourceInitialized += OnSourceInitialized;
        Closed += OnWindowClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);

        var hwnd = new WindowInteropHelper(this).Handle;
        uint mods = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        RegisterHotKey(hwnd, HK_LIKE, mods, VK_1);
        RegisterHotKey(hwnd, HK_LOVE, mods, VK_2);
        RegisterHotKey(hwnd, HK_APPLAUSE, mods, VK_3);
        RegisterHotKey(hwnd, HK_LAUGH, mods, VK_4);
        RegisterHotKey(hwnd, HK_SURPRISED, mods, VK_5);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        for (int id = HK_LIKE; id <= HK_SURPRISED; id++)
            UnregisterHotKey(hwnd, id);
        _hwndSource?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var reactionType = wParam.ToInt32() switch
            {
                HK_LIKE => ReactionType.Like,
                HK_LOVE => ReactionType.Love,
                HK_APPLAUSE => ReactionType.Applause,
                HK_LAUGH => ReactionType.Laugh,
                HK_SURPRISED => ReactionType.Surprised,
                _ => (ReactionType?)null,
            };

            if (reactionType.HasValue)
            {
                handled = true;
                _ = FireReactionAsync(reactionType.Value);
            }
        }
        return IntPtr.Zero;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private async void OnReactionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string reactionName
            && Enum.TryParse<ReactionType>(reactionName, out var reactionType))
        {
            await FireReactionAsync(reactionType);
        }
    }

    private async Task FireReactionAsync(ReactionType reactionType)
    {
        SetButtonsEnabled(false);

        var (success, message) = await _teamsService.SendReactionAsync(reactionType);

        if (!success)
            MessageBox.Show(message, "Heart It", MessageBoxButton.OK, MessageBoxImage.Warning);

        SetButtonsEnabled(true);
        UpdateStatus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void SetButtonsEnabled(bool enabled)
    {
        BtnLike.IsEnabled = enabled;
        BtnHeart.IsEnabled = enabled;
        BtnClap.IsEnabled = enabled;
        BtnLaugh.IsEnabled = enabled;
        BtnSurprised.IsEnabled = enabled;
    }

    private void UpdateStatus()
    {
        var (found, title) = _teamsService.CheckTeamsStatus();
        StatusIndicator.Fill = found
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
            : new SolidColorBrush(Color.FromRgb(244, 67, 54));
        StatusToolTip.Text = found ? $"Meeting: {title}" : title;
    }
}