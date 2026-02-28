using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI;

public partial class MainForm
{
    protected override void WndProc(ref Message m)
    {
        // Handle single-instance activation request
        if (m.Msg == SingleInstanceManager.ActivationMessage)
        {
            ShowAndActivateWindow();
            return;
        }

        // Handle WM_NCCALCSIZE to remove title bar but keep resize borders
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // Return 0 to use entire window rectangle as client area
            // This removes the title bar but keeps resize borders
            m.Result = IntPtr.Zero;
            return;
        }

        // Intercept maximize/restore commands and handle manually
        if (m.Msg == WM_SYSCOMMAND)
        {
            int command = (int)m.WParam & 0xFFF0;
            if (command == SC_MAXIMIZE)
            {
                MaximizeWindow();
                return;  // Prevent Windows from handling it
            }
            else if (command == SC_RESTORE && _isManuallyMaximized)
            {
                // Only intercept restore if we're in our manual maximized state
                // Otherwise let Windows handle restore from minimize
                RestoreWindow();
                return;  // Prevent Windows from handling it
            }
        }
        else if (m.Msg == WM_NCHITTEST)
        {
            var result = HitTest(PointToClient(Cursor.Position));
            if (result != HTCLIENT)
            {
                m.Result = (IntPtr)result;
                return;
            }
        }
        else if (m.Msg == WM_ENTERSIZEMOVE)
        {
            // User started resizing or moving - suppress renders during drag
            _isResizing = true;
        }
        else if (m.Msg == WM_EXITSIZEMOVE)
        {
            // Resize/move finished - mark dirty and resume rendering
            _isResizing = false;
            _backgroundDirty = true;  // Background needs regeneration at new size
            MarkDirty();
        }
        else if (m.Msg == WM_SIZE)
        {
            // Window size changed - mark dirty for redraw
            _backgroundDirty = true;  // Background needs regeneration at new size
            MarkDirty();
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Show and activate the window, restoring from minimized or tray state if needed.
    /// </summary>
    private void ShowAndActivateWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        BringToFront();
    }

    /// <summary>
    /// Mark the canvas as dirty, requiring a redraw on next animation tick
    /// </summary>
    private void MarkDirty()
    {
        _isDirty = true;
    }

    private void MaximizeWindow()
    {
        if (_isManuallyMaximized) return;

        // Store current bounds for restore
        _restoreBounds = new Rectangle(Location, Size);

        // Get the working area of the screen the window is currently on
        var screen = Screen.FromHandle(Handle);
        var workingArea = screen.WorkingArea;

        // Use SetWindowPos with SWP_NOCOPYBITS to prevent ghost window
        // This moves and resizes in one atomic operation without copying old pixels
        _isManuallyMaximized = true;
        SetWindowPos(
            Handle,
            IntPtr.Zero,
            workingArea.X,
            workingArea.Y,
            workingArea.Width,
            workingArea.Height,
            SWP_NOCOPYBITS | SWP_NOZORDER | SWP_FRAMECHANGED
        );
    }

    private void RestoreWindow()
    {
        if (!_isManuallyMaximized) return;

        // Restore to previous bounds using SetWindowPos to prevent ghost window
        _isManuallyMaximized = false;
        SetWindowPos(
            Handle,
            IntPtr.Zero,
            _restoreBounds.X,
            _restoreBounds.Y,
            _restoreBounds.Width,
            _restoreBounds.Height,
            SWP_NOCOPYBITS | SWP_NOZORDER | SWP_FRAMECHANGED
        );
    }

    private int HitTest(Point clientPoint)
    {
        bool left = clientPoint.X < ResizeBorder;
        bool right = clientPoint.X >= ClientSize.Width - ResizeBorder;
        bool top = clientPoint.Y < ResizeBorder;
        bool bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;

        // Title bar area for dragging (but not over buttons or tabs)
        if (clientPoint.Y < TitleBarHeight)
        {
            // Exclude window controls area
            if (clientPoint.X >= ClientSize.Width - 120)
            {
                return HTCLIENT;
            }
            // Exclude tab area - _tabsStartX is in scaled canvas space, convert to physical pixels
            float canvasScale = FUIRenderer.CanvasScaleFactor;
            if (_tabsStartX > 0 &&
                clientPoint.X >= _tabsStartX * canvasScale &&
                clientPoint.Y >= 36 * canvasScale && clientPoint.Y <= 66 * canvasScale)
            {
                return HTCLIENT;
            }
            return HTCAPTION;
        }

        return HTCLIENT;
    }

}

