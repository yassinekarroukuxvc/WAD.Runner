using Microsoft.Extensions.Logging;
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.Solidworks.Adapters;

public sealed class SwSession : IAsyncDisposable, IDisposable
{
    public SldWorks App { get; }
    private readonly ILogger _log;

    public SwSession(ILogger log, bool visible)
    {
        var progId = "SldWorks.Application";
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)!;
        App = (SldWorks)Activator.CreateInstance(type)!;
        App.Visible = visible;
        _log = log;
        _log.LogInformation("SolidWorks session started. Visible={Visible}", visible);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        try { App.ExitApp(); } catch { /* ignore */ }
        try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(App); } catch { /* ignore */ }
        _log.LogInformation("SolidWorks session closed.");
        await ValueTask.CompletedTask;
    }
}
