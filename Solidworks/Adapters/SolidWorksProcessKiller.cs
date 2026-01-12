// Solidworks/Adapters/SolidWorksProcessKiller.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using WAD.Runner.Application; // Logger

namespace WAD.Runner.Solidworks.Adapters;

public static class SolidWorksProcessKiller
{
    /// <summary>
    /// Force-terminates all SolidWorks-related processes to avoid
    /// "journal file could not be created" / stale instances after automation.
    ///
    /// WARNING:
    /// - This will close any user-open SolidWorks sessions.
    /// - Use only in automation contexts.
    /// </summary>
    public static void KillAll(bool killVbaServer = true, int waitMs = 8000)
    {
        Logger.Warn("[SW-Kill] Terminating SolidWorks instances before automation...");

        // Kill SolidWorks main process
        KillByProcessName("SLDWORKS");

        // Optional: VBA macro server (often lingers)
        if (killVbaServer)
            KillByProcessName("swvbaserver");

        // Optional: eDrawings / viewers rarely, but can hold locks in some setups
        // KillByProcessName("eDrawings");

        WaitForExit(waitMs);

        Logger.Warn("[SW-Kill] SolidWorks process cleanup completed.");
    }

    private static void KillByProcessName(string name)
    {
        try
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length == 0)
            {
                Logger.Info($"[SW-Kill] No processes found: {name}.exe");
                return;
            }

            foreach (var p in procs)
            {
                try
                {
                    Logger.Warn($"[SW-Kill] Killing {name}.exe (PID={p.Id})...");
                    p.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[SW-Kill] Failed to kill {name}.exe (PID={p.Id}): {ex.Message}");
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SW-Kill] KillByProcessName('{name}') failed: {ex.Message}");
        }
    }

    private static void WaitForExit(int waitMs)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < waitMs)
        {
            var stillRunning =
                Process.GetProcessesByName("SLDWORKS").Any() ||
                Process.GetProcessesByName("swvbaserver").Any();

            if (!stillRunning)
            {
                Logger.Info("[SW-Kill] Confirmed no SolidWorks processes are running.");
                return;
            }

            Thread.Sleep(250);
        }

        var leftSw = Process.GetProcessesByName("SLDWORKS").Length;
        var leftVba = Process.GetProcessesByName("swvbaserver").Length;

        Logger.Warn($"[SW-Kill] Timeout waiting for exit. Remaining: SLDWORKS={leftSw}, swvbaserver={leftVba}");
    }
}
