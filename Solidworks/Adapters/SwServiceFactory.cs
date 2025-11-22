using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.Solidworks.Adapters;

namespace WAD.Runner.SolidWorks.Adapters;
public sealed class SwServiceFactory : ISwSessionFactory
{
    private readonly ILogger<SwSession> _sessLog;
    public SwServiceFactory(ILogger<SwSession> sessLog) => _sessLog = sessLog;

    public SwSession Create(bool visible) => new SwSession(_sessLog, visible);

    // Keep helper to open drawings if you like, but require a provided SldWorks:
    //public (ModelDoc2 model, DrawingDoc drawing) OpenDrawing(SldWorks app, string path) { /* unchanged, but no _app */ }
}