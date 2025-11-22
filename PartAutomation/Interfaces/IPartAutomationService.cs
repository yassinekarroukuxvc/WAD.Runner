// PartAutomation/Interfaces/IPartAutomationService.cs
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.PartAutomation.Interfaces;

public interface IPartAutomationService
{
    void Attach(SldWorks swApp);
    void OpenPart(string partPath);
    void ActivateConfiguration(WedgeSubclass subclass, DrawingType drawingType);
    void UpdateEquations(string equationFilePath);
    void EnsureAllEquationsExist(WedgeData wedge); // NEW hook for safety
    void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys);
    void ApplyPostRules(WedgeData wedge, DrawingType drawingType);
    void SaveAndClose();
    void RebuildPart();
}
