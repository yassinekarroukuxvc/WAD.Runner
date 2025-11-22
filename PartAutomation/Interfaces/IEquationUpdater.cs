// PartAutomation/Interfaces/IEquationUpdater
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.PartAutomation.Interfaces;

public interface IEquationUpdater
{
    bool UpdateFromFile(ModelDoc2 model, string equationFilePath, bool rebuildAfter = true);
    bool UpdateFromText(ModelDoc2 model, string equationsText, bool rebuildAfter = true);
}
