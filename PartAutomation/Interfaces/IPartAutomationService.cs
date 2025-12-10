using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;

namespace WAD.Runner.PartAutomation.Interfaces;

public interface IPartAutomationService
{
    void Attach(SldWorks swApp);
    void OpenPart(string partPath);
    void ActivateConfiguration(WedgeSubclass subclass, DrawingType drawingType);
    void UpdateEquations(string equationFilePath);
    void EnsureAllEquationsExist(WedgeData wedge);
    void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys);

    void ApplyPostRules(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType);

    void SaveAndClose();
    void RebuildPart();
}
