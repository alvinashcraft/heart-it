using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace HeartIt.Services;

public enum ReactionType
{
    Like,
    Love,
    Applause,
    Laugh,
    Surprised
}

public class TeamsReactionService
{
    private static readonly string[] TeamsProcessNames = ["ms-teams", "Teams"];

    // Reaction order in the flyout (left to right)
    private static readonly ReactionType[] FlyoutOrder =
        [ReactionType.Like, ReactionType.Love, ReactionType.Applause, ReactionType.Laugh, ReactionType.Surprised];

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const byte VK_ESCAPE = 0x1B;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    // Concurrency guard — prevents overlapping reaction sends
    private int _busy;

    // Cached meeting window handle so Escape goes to the right window
    private IntPtr _lastMeetingHwnd;

    public async Task<(bool Success, string Message)> SendReactionAsync(ReactionType reaction)
    {
        // Prevent overlapping calls (from held keys or rapid clicks)
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return (true, "Already sending a reaction…");

        try
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var processIds = GetTeamsProcessIds();
                    if (processIds.Count == 0)
                        return (false, "Teams is not running.");

                    // Step 1 — if we know the meeting window, dismiss any open flyout
                    if (_lastMeetingHwnd != IntPtr.Zero && IsWindow(_lastMeetingHwnd))
                    {
                        SetForegroundWindow(_lastMeetingHwnd);
                        Thread.Sleep(150);
                        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        Thread.Sleep(300);
                    }

                    // Step 2 — find meeting window via React button in the UIA tree
                    var (meetingHwnd, reactRect, windowRect) = FindMeetingAndReactButton(processIds);
                    if (meetingHwnd == IntPtr.Zero)
                        return (false, "No active Teams meeting found (could not locate React button).");

                    _lastMeetingHwnd = meetingHwnd;

                    // Step 3 — bring the meeting window to the foreground
                    SetForegroundWindow(meetingHwnd);
                    await Task.Delay(250);

                    // Step 4 — click the React button
                    var reactCenter = CenterOf(reactRect);
                    var safetyCheck = ValidateClick(reactCenter, windowRect, "React button");
                    if (!safetyCheck.Safe)
                        return (false, safetyCheck.Reason);

                    ClickAt(reactCenter);
                    await Task.Delay(600);

                    // Step 5 — click the specific reaction by coordinate offset
                    int index = Array.IndexOf(FlyoutOrder, reaction);
                    if (index < 0)
                        return (false, $"Unknown reaction: {reaction}");

                    const double btnWidth = 44;

                    double flyoutLeft = reactRect.X + reactRect.Width / 2 - btnWidth / 2;
                    double reactionY = reactRect.Bottom + 30;

                    var reactionPoint = new Point(
                        flyoutLeft + btnWidth * index + btnWidth / 2,
                        reactionY);

                    var reactionSafety = ValidateClick(reactionPoint, windowRect, $"{reaction} reaction");
                    if (!reactionSafety.Safe)
                        return (false, reactionSafety.Reason +
                            $"\n\nDebug: React button at ({reactRect.X:F0},{reactRect.Y:F0}) size ({reactRect.Width:F0}x{reactRect.Height:F0})" +
                            $"\nWindow at ({windowRect.X:F0},{windowRect.Y:F0}) size ({windowRect.Width:F0}x{windowRect.Height:F0})" +
                            $"\nCalculated reaction click: ({reactionPoint.X:F0},{reactionPoint.Y:F0})");

                    ClickAt(reactionPoint);
                    return (true, $"{reaction} sent!");
                }
                catch (Exception ex)
                {
                    return (false, $"Error: {ex.Message}");
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public (bool Found, string Title) CheckTeamsStatus()
    {
        try
        {
            var pids = GetTeamsProcessIds();
            if (pids.Count == 0)
                return (false, "Teams is not running");

            var (hwnd, _, _) = FindMeetingAndReactButton(pids);
            if (hwnd == IntPtr.Zero)
                return (false, "No active meeting found");

            try
            {
                var el = AutomationElement.FromHandle(hwnd);
                return (true, el.Current.Name ?? "Meeting");
            }
            catch
            {
                return (true, "Meeting");
            }
        }
        catch
        {
            return (false, "Error checking Teams status");
        }
    }

    // ───────────────────────── finding elements ─────────────────────────

    private static List<int> GetTeamsProcessIds()
    {
        var ids = new List<int>();
        foreach (var name in TeamsProcessNames)
            foreach (var proc in Process.GetProcessesByName(name))
                ids.Add(proc.Id);
        return ids;
    }

    private static (IntPtr Hwnd, Rect ReactRect, Rect WindowRect) FindMeetingAndReactButton(List<int> processIds)
    {
        var root = AutomationElement.RootElement;

        foreach (var pid in processIds)
        {
            AutomationElementCollection windows;
            try
            {
                windows = root.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
            }
            catch { continue; }

            foreach (AutomationElement window in windows)
            {
                try
                {
                    var hwnd = new IntPtr(window.Current.NativeWindowHandle);
                    if (hwnd == IntPtr.Zero) continue;

                    if (!GetWindowRect(hwnd, out var wr)) continue;
                    var windowRect = new Rect(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
                    if (windowRect.Width < 200 || windowRect.Height < 200) continue;

                    var reactRect = FindReactButtonRect(window, windowRect);
                    if (reactRect is not null)
                        return (hwnd, reactRect.Value, windowRect);
                }
                catch { }
            }
        }

        return (IntPtr.Zero, Rect.Empty, Rect.Empty);
    }

    private static Rect? FindReactButtonRect(AutomationElement parent, Rect windowRect)
    {
        AutomationElementCollection elements;
        try
        {
            elements = parent.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        }
        catch { return null; }

        foreach (AutomationElement el in elements)
        {
            try
            {
                var name = el.Current.Name;
                if (name is null) continue;
                if (!name.Contains("React", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Send", StringComparison.OrdinalIgnoreCase)) continue;

                var rect = el.Current.BoundingRectangle;
                if (!IsRectValid(rect, windowRect)) continue;

                return rect;
            }
            catch { }
        }

        return null;
    }

    // ───────────────────────── safety ─────────────────────────

    private static bool IsRectValid(Rect rect, Rect windowRect)
    {
        if (rect.IsEmpty) return false;
        if (double.IsInfinity(rect.X) || double.IsInfinity(rect.Y)) return false;
        if (double.IsNaN(rect.X) || double.IsNaN(rect.Y)) return false;
        if (rect.Width <= 0 || rect.Height <= 0) return false;
        if (rect.Width > 500 || rect.Height > 500) return false;

        if (rect.X < windowRect.Left - 20 || rect.Y < windowRect.Top - 20) return false;
        if (rect.Right > windowRect.Right + 20 || rect.Bottom > windowRect.Bottom + 20) return false;

        // Reject top-right corner (window chrome)
        if (rect.X > windowRect.Right - 150 && rect.Y < windowRect.Top + 50) return false;

        return true;
    }

    private static (bool Safe, string Reason) ValidateClick(Point pt, Rect windowRect, string label)
    {
        if (double.IsInfinity(pt.X) || double.IsInfinity(pt.Y) ||
            double.IsNaN(pt.X) || double.IsNaN(pt.Y))
            return (false, $"Aborted clicking {label}: coordinates are invalid ({pt.X}, {pt.Y}).");

        if (pt.X < windowRect.Left || pt.Y < windowRect.Top ||
            pt.X > windowRect.Right || pt.Y > windowRect.Bottom)
            return (false,
                $"Aborted clicking {label}: target ({pt.X:F0}, {pt.Y:F0}) is outside the " +
                $"Teams window ({windowRect.Left:F0},{windowRect.Top:F0})–({windowRect.Right:F0},{windowRect.Bottom:F0}).");

        if (pt.X > windowRect.Right - 150 && pt.Y < windowRect.Top + 50)
            return (false,
                $"Aborted clicking {label}: target ({pt.X:F0}, {pt.Y:F0}) is in the " +
                $"window-controls danger zone.");

        return (true, string.Empty);
    }

    // ───────────────────────── clicking ─────────────────────────

    private static Point CenterOf(Rect rect) =>
        new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static void ClickAt(Point pt)
    {
        SetCursorPos((int)pt.X, (int)pt.Y);
        Thread.Sleep(30);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(15);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }
}
