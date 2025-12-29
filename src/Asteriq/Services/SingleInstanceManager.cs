using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Asteriq.Services;

/// <summary>
/// Manages single-instance application behavior.
/// Ensures only one instance of Asteriq runs at a time, and activates the existing instance if launched again.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Global\\Asteriq_SingleInstance_Mutex";

    // Custom Windows message for activation request
    private const int WM_SHOW_WINDOW = 0x0400 + 1; // WM_USER + 1

    private readonly Mutex _mutex;
    private readonly bool _isFirstInstance;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ShowWindow constants
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Custom Windows message ID for activation requests.
    /// MainForm should override WndProc to handle this message.
    /// </summary>
    public static int ActivationMessage => WM_SHOW_WINDOW;

    public SingleInstanceManager()
    {
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);
    }

    /// <summary>
    /// Check if this is the first instance of the application.
    /// </summary>
    public bool IsFirstInstance => _isFirstInstance;

    /// <summary>
    /// Attempt to activate an existing instance of the application.
    /// Returns true if an existing instance was found and activated.
    /// </summary>
    public bool ActivateExistingInstance()
    {
        // Find the main window by enumerating all Asteriq processes
        var currentProcess = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(currentProcess.ProcessName);

        foreach (var process in processes)
        {
            // Skip our own process
            if (process.Id == currentProcess.Id)
                continue;

            // Try to find the main window for this process
            IntPtr mainWindowHandle = process.MainWindowHandle;

            // If MainWindowHandle is zero, enumerate all windows for this process
            if (mainWindowHandle == IntPtr.Zero)
            {
                mainWindowHandle = FindMainWindow(process.Id);
            }

            if (mainWindowHandle != IntPtr.Zero)
            {
                // Window found - restore it if minimized
                if (IsIconic(mainWindowHandle))
                {
                    ShowWindow(mainWindowHandle, SW_RESTORE);
                }

                // Send custom message to show the window (handles tray minimized case)
                SendMessage(mainWindowHandle, WM_SHOW_WINDOW, IntPtr.Zero, IntPtr.Zero);

                // Bring to foreground
                SetForegroundWindow(mainWindowHandle);

                process.Dispose();
                return true;
            }

            process.Dispose();
        }

        return false;
    }

    /// <summary>
    /// Find the main window for a process by enumerating all windows.
    /// Finds the window even if it's hidden (e.g., minimized to tray).
    /// </summary>
    private static IntPtr FindMainWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowProcessId);

            // Find any window owned by this process
            // Don't check IsWindowVisible - we want to find hidden windows too (system tray)
            if (windowProcessId == processId)
            {
                result = hWnd;
                return false; // Stop enumeration
            }

            return true; // Continue enumeration
        }, IntPtr.Zero);

        return result;
    }

    public void Dispose()
    {
        if (_isFirstInstance)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
