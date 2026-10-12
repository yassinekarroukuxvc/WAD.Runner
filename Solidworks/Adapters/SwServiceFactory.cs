
using System;
using System.ComponentModel;
using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// Creates a new owned SolidWorks session for CLI/API jobs.
///
/// Before creating a new session:
/// 1. Detect existing SLDWORKS.exe processes.
/// 2. Request normal shutdown.
/// 3. Force termination if necessary.
/// 4. Verify all old processes have exited.
/// 5. Start a fresh SolidWorks session.
///
/// WARNING:
/// Forced termination can discard unsaved SolidWorks changes.
/// Intended for a dedicated automation workstation.
/// </summary>
public sealed class SwServiceFactory : ISwSessionFactory
{
    private static readonly object StartupLock = new();

    private const int GracefulShutdownTimeoutMs = 8000;
    private const int ForcedShutdownTimeoutMs = 15000;

    private readonly ILogger<SwSession> _logger;
    private readonly IConfiguration? _configuration;

    public SwServiceFactory(
        ILogger<SwSession> sessLog,
        IConfiguration? configuration = null)
    {
        _logger = sessLog;
        _configuration = configuration;
    }

    /// <summary>
    /// Creates a fresh SolidWorks session.
    /// Closes any previously running SolidWorks process first.
    /// </summary>
    public SwSession Create(bool visible)
    {
        lock (StartupLock)
        {
            // Close previous SolidWorks instances BEFORE
            // creating the new COM automation session.
            CloseExistingSolidWorksSessions();

            _logger.LogInformation(
                "[SW-Startup] Creating fresh SolidWorks session. " +
                "Visible={Visible}",
                visible);

            return new SwSession(
                _logger,
                visible,
                _configuration?[
                    "Runner:SolidWorks:HiddenMode"
                ] ?? "Offscreen",
                _configuration?.GetValue<bool?>(
                    "Runner:SolidWorks:OffscreenHideTaskbar"
                ) ?? true);
        }
    }

    /// <summary>
    /// Closes all existing SolidWorks processes.
    ///
    /// Attempts normal shutdown first.
    /// If SolidWorks refuses to close or is unresponsive,
    /// forcefully terminates the process.
    /// </summary>
    private void CloseExistingSolidWorksSessions()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "SolidWorks automation requires Windows.");
        }

        Process[] processes =
            Process.GetProcessesByName("SLDWORKS");

        try
        {
            if (processes.Length == 0)
            {
                _logger.LogInformation(
                    "[SW-Startup] No existing SolidWorks process found.");
            }
            else
            {
                _logger.LogWarning(
                    "[SW-Startup] Found {Count} existing " +
                    "SolidWorks process(es). Closing before startup.",
                    processes.Length);
            }

            foreach (Process process in processes)
            {
                CloseSolidWorksProcess(process);
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        // Verify no old SolidWorks process remains.
        Process[] remaining =
            Process.GetProcessesByName("SLDWORKS");

        try
        {
            if (remaining.Length > 0)
            {
                throw new InvalidOperationException(
                    "[SW-Startup] Cannot start a fresh SolidWorks " +
                    "session because an existing SLDWORKS.exe " +
                    "process is still running.");
            }
        }
        finally
        {
            foreach (Process process in remaining)
            {
                process.Dispose();
            }
        }

        _logger.LogInformation(
            "[SW-Startup] Existing SolidWorks sessions cleared. " +
            "Ready to start new session.");
    }

    /// <summary>
    /// Closes a single SolidWorks process.
    /// </summary>
    private void CloseSolidWorksProcess(Process process)
    {
        int pid = process.Id;

        if (!IsProcessRunning(process))
        {
            return;
        }

        _logger.LogWarning(
            "[SW-Startup] Closing existing SolidWorks PID={Pid}.",
            pid);

        bool gracefulCloseRequested = false;

        try
        {
            // Ask SolidWorks to close normally.
            gracefulCloseRequested = process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
            // Process may have exited already.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[SW-Startup] Could not request normal shutdown " +
                "for PID={Pid}.",
                pid);
        }

        if (gracefulCloseRequested)
        {
            if (process.WaitForExit(GracefulShutdownTimeoutMs))
            {
                _logger.LogInformation(
                    "[SW-Startup] SolidWorks PID={Pid} " +
                    "closed normally.",
                    pid);

                return;
            }
        }

        if (!IsProcessRunning(process))
        {
            return;
        }

        // Normal shutdown failed or no visible window exists.
        // Force termination.
        _logger.LogWarning(
            "[SW-Startup] SolidWorks PID={Pid} still running. " +
            "Force terminating process.",
            pid);

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
            when (!IsProcessRunning(process))
        {
            // Process exited between checking and killing.
            return;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"[SW-Startup] Unable to terminate SolidWorks PID={pid}. " +
                "Check process permissions.",
                ex);
        }

        if (!process.WaitForExit(ForcedShutdownTimeoutMs))
        {
            throw new InvalidOperationException(
                $"[SW-Startup] SolidWorks PID={pid} did not exit " +
                "after forced termination.");
        }

        _logger.LogInformation(
            "[SW-Startup] SolidWorks PID={Pid} terminated successfully.",
            pid);
    }

    /// <summary>
    /// Safely checks whether a process is still running.
    /// </summary>
    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
