// ModelAutomation/SolidWorks/ModelDimensionApplier.cs
using System;
using System.Collections.Generic;

using WAD.Runner.Application; // Logger

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;
using DomDrawingType = WAD.Runner.DataManagement.Domain.Wedge.DrawingType;
using DomWedgeType = WAD.Runner.DataManagement.Domain.Wedge.WedgeType;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public enum DimensionApplyMode
    {
        /// <summary>Write dimensions to equations.txt then import file into model.</summary>
        EquationFilePrimary = 0,

        /// <summary>Upsert equations directly in model EquationMgr (no file required).</summary>
        DirectEquationMgrPrimary = 1
    }

    public sealed record DimensionApplyResult(
        bool Success,
        string MethodUsed,
        string? Error = null
    );

    /// <summary>
    /// Applies effective dimensions to either:
    /// - equations file (primary) + model import, OR
    /// - model EquationMgr directly (alternate).
    ///
    /// IMPORTANT: This class does NOT rebuild. Orchestrator will do a single rebuild at the end.
    /// </summary>
    public sealed class ModelDimensionApplier
    {
        private readonly DimensionApplyMode _mode;
        private readonly bool _fallbackToAlternate;

        public ModelDimensionApplier(
            DimensionApplyMode mode = DimensionApplyMode.EquationFilePrimary,
            bool fallbackToAlternate = false)
        {
            _mode = mode;
            _fallbackToAlternate = fallbackToAlternate;
        }

        public DimensionApplyResult Apply(
            ModelEditor editor,
            string equationsOutPath,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            if (editor is null) throw new ArgumentNullException(nameof(editor));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            // equationsOutPath may be unused for Direct mode, but validate for file mode
            if (_mode == DimensionApplyMode.EquationFilePrimary && string.IsNullOrWhiteSpace(equationsOutPath))
                throw new ArgumentException("equationsOutPath is required for EquationFilePrimary.", nameof(equationsOutPath));

            return _mode switch
            {
                DimensionApplyMode.EquationFilePrimary =>
                    ApplyByEquationFile(editor, equationsOutPath, effectiveDims, wedge, wedgeType, drawingType),

                DimensionApplyMode.DirectEquationMgrPrimary =>
                    ApplyByDirectUpsert(editor, effectiveDims, wedge, wedgeType, drawingType),

                _ => throw new NotSupportedException($"Unsupported apply mode: {_mode}")
            };
        }

        private DimensionApplyResult ApplyByEquationFile(
            ModelEditor editor,
            string equationsOutPath,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            try
            {
                Logger.Info("[ModelDimensionApplier] ApplyByEquationFile → start");

                // 1) Write computed dimensions into equations file
                EquationUpdater.UpdateEquationFile(
                    equationsOutPath,
                    effectiveDims,
                    wedge,
                    wedgeType,
                    drawingType);

                // 2) Import file into model (NO rebuild here)
                editor.ImportEquationsFromFile(equationsOutPath);

                Logger.Success("[ModelDimensionApplier] ApplyByEquationFile → done");
                return new DimensionApplyResult(true, MethodUsed: "EquationFilePrimary");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ModelDimensionApplier] ApplyByEquationFile failed: {ex.GetType().Name}: {ex.Message}");

                if (!_fallbackToAlternate)
                    return new DimensionApplyResult(false, MethodUsed: "EquationFilePrimary", Error: ex.Message);

                Logger.Warn("[ModelDimensionApplier] Fallback enabled → trying DirectEquationMgr upsert...");
                return ApplyByDirectUpsert(
                    editor,
                    effectiveDims,
                    wedge,
                    wedgeType,
                    drawingType,
                    primaryFailed: ex);
            }
        }

        private DimensionApplyResult ApplyByDirectUpsert(
            ModelEditor editor,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType,
            Exception? primaryFailed = null)
        {
            try
            {
                Logger.Info("[ModelDimensionApplier] ApplyByDirectUpsert → start");

                // Upsert equations directly into model (NO rebuild here by default; orchestrator should rebuild once)
                EquationUpdater.UpsertEquationsInModel(
                    editor.Model,
                    effectiveDims,
                    wedge,
                    wedgeType,
                    drawingType,
                    rebuild: false);

                Logger.Success("[ModelDimensionApplier] ApplyByDirectUpsert → done");
                return new DimensionApplyResult(true, MethodUsed: "DirectEquationMgrPrimary");
            }
            catch (Exception ex)
            {
                var msg = primaryFailed is null
                    ? ex.Message
                    : $"Primary failed: {primaryFailed.Message} | Alternate failed: {ex.Message}";

                Logger.Error($"[ModelDimensionApplier] ApplyByDirectUpsert failed: {ex.GetType().Name}: {ex.Message}");
                return new DimensionApplyResult(false, MethodUsed: "DirectEquationMgrPrimary", Error: msg);
            }
        }
    }
}