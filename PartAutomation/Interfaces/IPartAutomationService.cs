using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.PartAutomation.Rules;

namespace WAD.Runner.PartAutomation.Interfaces;

public interface IPartAutomationService
{
    ModelDoc2 Model { get; }
    void Attach(SldWorks swApp);
    void OpenPart(string partPath);
    void ActivateConfiguration(WedgeSubclass subclass, DrawingType drawingType);
    void UpdateEquations(string equationFilePath);
    void EnsureAllEquationsExist(WedgeData wedge);
    void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys);

    void ApplyPostRules(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType);

    void SaveAndClose();
    void RebuildPart();
    void UpsertGlobalsFromEffectiveDims(
    IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims,
    WedgeData wedge,
    DrawingType drawingType,
    double eps = 1e-6);

    public FeatureTogglePlan RunMacroStyle(
            WedgeType wedgeType,
            WedgeData wedge,
            DrawingType drawingType,
            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims,
            IEnumerable<DimensionKey> toleranceKeys,
            double eps = 1e-6);

}
