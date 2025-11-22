using Microsoft.Extensions.Logging;
using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WAD.Runner.Solidworks.Adapters;

public interface ISwSessionFactory
{
    SwSession Create(bool visible);
}
