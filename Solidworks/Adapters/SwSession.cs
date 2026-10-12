using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// One WAD-owned SolidWorks process. All SolidWorks COM calls must occur on
/// the application's automation STA, including shutdown.
///
/// The native Visible=false path has hung inside drawing Save3 on some
/// installations. The Offscreen strategy deliberately keeps SolidWorks in its
/// WORKING VISIBLE EXECUTION MODE while moving only WAD's own frame outside
/// the virtual display and (optionally) out of the taskbar.
///
/// This is visual concealment, not a claim of officially supported headless
/// drawing automation. Desktop prompts/add-ins can still interfere.
/// </summary>
public sealed class SwSession : IAsyncDisposable, IDisposable
{
    private readonly ILogger _log;
    private readonly string _hiddenMode;
    private readonly bool _hideTaskbar;
    private bool _disposed;

    private nint _ownedHwnd;
    private bool _haveOriginalWindowState;
    private Rect _originalRect;
    private nint _originalExStyle;
    private bool _offscreenApplied;

    public SldWorks App { get; }
    public int ProcessId { get; }

    public SwSession(
        ILogger log,
        bool visible,
        string? hiddenMode = "Offscreen",
        bool hideTaskbar = true)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _hiddenMode = string.IsNullOrWhiteSpace(hiddenMode)
            ? "Offscreen"
            : hiddenMode.Trim();
        _hideTaskbar = hideTaskbar;

        if (!_hiddenMode.Equals("Offscreen", StringComparison.OrdinalIgnoreCase) &&
            !_hiddenMode.Equals("Native", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unrecognized SolidWorks hidden mode '{_hiddenMode}'. Use Offscreen or Native.",
                nameof(hiddenMode));
        }

        // COM activation can attach to an existing instance. Do not hijack
        // a user-owned SolidWorks installation or kill it by process name.
        var existing = Process.GetProcessesByName("SLDWORKS");
        try
        {
            if (existing.Length != 0)
                throw new InvalidOperationException(
                    "WAD.Runner needs an isolated SolidWorks process. A SLDWORKS.exe " +
                    "is already running. Close the previous automation instance (or " +
                    "use a dedicated automation workstation) before starting a job.");
        }
        finally
        {
            foreach (var process in existing) process.Dispose();
        }

        var type = Type.GetTypeFromProgID("SldWorks.Application", throwOnError: true)!;
        App = (SldWorks)Activator.CreateInstance(type)!;
        try
        {
            ProcessId = App.GetProcessID();
            if (ProcessId <= 0)
                throw new InvalidOperationException("SolidWorks returned an invalid process ID.");

            ApplyVisibility(visible);
        }
        catch
        {
            try { App.ExitApp(); } catch { }
            try { Marshal.FinalReleaseComObject(App); } catch { }
            throw;
        }

        _log.LogInformation(
            "[SW-Session] WAD-owned PID={Pid}, requestedVisible={Requested}, " +
            "SolidWorks.Visible={Actual}, strategy={Strategy}",
            ProcessId, visible, App.Visible, visible ? "Visible" : _hiddenMode);
    }

    public void ApplyVisibility(bool visible)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SwSession));

        // WAD retains process ownership for both visible and background jobs.
        App.UserControl = false;

        if (visible)
        {
            RestoreWindowIfNeeded();
            SetKeepInvisible(false);
            App.UserControlBackground = false;
            App.Visible = true;
            _log.LogInformation("[SW-Visibility] PID={Pid}: visible mode", ProcessId);
            return;
        }

        if (_hiddenMode.Equals("Native", StringComparison.OrdinalIgnoreCase))
        {
            RestoreWindowIfNeeded();
            App.UserControlBackground = true;
            App.Visible = false;
            SetKeepInvisible(true);
            if (App.Visible)
                throw new InvalidOperationException("SolidWorks refused native invisible mode.");

            _log.LogInformation(
                "[SW-Visibility] PID={Pid}: NATIVE hidden mode, SolidWorks.Visible=false", ProcessId);
            return;
        }

        ApplyOffscreenVisibleMode();
    }

    private void ApplyOffscreenVisibleMode()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SolidWorks offscreen mode requires Windows.");

        // Work around Save3 hanging with native Visible=false. We first place
        // the main frame offscreen, and ONLY THEN run SOLIDWORKS with Visible=true.
        // Avoid UserControlBackground=true: it can turn visibility off again.
        App.UserControlBackground = false;
        App.Visible = false;
        SetKeepInvisible(false);

        var frame = App.Frame() as Frame
            ?? throw new InvalidOperationException("SolidWorks frame unavailable for offscreen mode.");
        var hwndValue = frame.GetHWndx64(); // x64-safe; NEVER use GetHWnd() here.
        if (hwndValue == 0)
            throw new InvalidOperationException("SolidWorks returned a null frame HWND.");
        var hwnd = new IntPtr(hwndValue);

        _ = GetWindowThreadProcessId(hwnd, out var hwndPid);
        if (hwndPid != (uint)ProcessId)
            throw new InvalidOperationException(
                $"Refusing to move an unowned window. Frame PID={hwndPid}, owned PID={ProcessId}.");

        if (!GetWindowRect(hwnd, out var original))
            ThrowWin32("Could not determine SolidWorks frame bounds");

        if (!_haveOriginalWindowState)
        {
            _ownedHwnd = hwnd;
            _originalRect = original;
            _originalExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            _haveOriginalWindowState = true;
        }

        if (_hideTaskbar)
        {
            var current = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var updated = (current | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            if (updated != current)
            {
                _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(updated));
                if (!SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER |
                        SWP_NOACTIVATE | SWP_FRAMECHANGED))
                    ThrowWin32("Unable to apply SolidWorks tool-window style");
            }
        }

        var xVirtual = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var yVirtual = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var widthVirtual = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var heightVirtual = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        // Off the bottom-right of the ENTIRE multi-monitor virtual desktop.
        // The window stays visible to Windows and SolidWorks internally.
        var offscreenX = checked(xVirtual + widthVirtual + 10000);
        var offscreenY = checked(yVirtual + heightVirtual + 10000);
        MoveOffscreen(hwnd, offscreenX, offscreenY);

        App.Visible = true;
        // SolidWorks can change its frame placement when Visible is set.
        // Ensure it stays outside all monitors after changing the COM flag.
        MoveOffscreen(hwnd, offscreenX, offscreenY);

        if (!App.Visible || !IsWindowVisible(hwnd))
            throw new InvalidOperationException(
                "Offscreen mode failed: SolidWorks frame is not visible internally. " +
                "Native hidden Save3 may hang. Refusing to run in that state.");

        if (!GetWindowRect(hwnd, out var actual))
            ThrowWin32("Unable to verify offscreen SolidWorks location");
        var virtualRight = xVirtual + widthVirtual;
        var virtualBottom = yVirtual + heightVirtual;
        if (actual.Left < virtualRight && actual.Right > xVirtual &&
            actual.Top < virtualBottom && actual.Bottom > yVirtual)
        {
            throw new InvalidOperationException(
                "SolidWorks frame remained onscreen; refusing to claim hidden operation.");
        }

        _offscreenApplied = true;
        _log.LogInformation(
            "[SW-Visibility] PID={Pid}: OFFSCREEN strategy active. " +
            "SolidWorks.Visible={Visible}, HWND visible={HwndVisible}, " +
            "frame=({X},{Y}), taskbarConceal={HideTaskbar}. " +
            "This intentionally differs from native Visible=false.",
            ProcessId, App.Visible, IsWindowVisible(hwnd),
            actual.Left, actual.Top, _hideTaskbar);
    }

    private static void MoveOffscreen(nint hwnd, int x, int y)
    {
        if (!SetWindowPos(hwnd, nint.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
            ThrowWin32("Could not move SolidWorks frame offscreen");
    }

    private void SetKeepInvisible(bool value)
    {
        var frame = App.Frame() as Frame;
        if (frame is null)
        {
            if (value) throw new InvalidOperationException("SolidWorks Frame is unavailable.");
            return;
        }
        frame.KeepInvisible = value;
    }

    private void RestoreWindowIfNeeded()
    {
        if (!_haveOriginalWindowState || !_offscreenApplied)
            return;

        if (_ownedHwnd == nint.Zero)
            return;

        try
        {
            _ = SetWindowLongPtr(_ownedHwnd, GWL_EXSTYLE, _originalExStyle);
            _ = SetWindowPos(_ownedHwnd, nint.Zero,
                _originalRect.Left, _originalRect.Top, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        finally
        {
            _offscreenApplied = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            try { App.CloseAllDocuments(true); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[SW-Session] CloseAllDocuments failed PID={Pid}", ProcessId);
            }
            try { SetKeepInvisible(false); } catch { }
            try { App.ExitApp(); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[SW-Session] ExitApp failed PID={Pid}", ProcessId);
            }
        }
        finally
        {
            try { Marshal.FinalReleaseComObject(App); } catch { }
            try
            {
                using var process = Process.GetProcessById(ProcessId);
                if (!process.WaitForExit(3000))
                {
                    _log.LogWarning("[SW-Session] Owned PID={Pid} still running; stopping it.", ProcessId);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[SW-Session] Shutdown verification failed PID={Pid}", ProcessId);
            }
            _log.LogInformation("[SW-Session] Owned PID={Pid} shutdown complete.", ProcessId);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static void ThrowWin32(string reason) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), reason);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
