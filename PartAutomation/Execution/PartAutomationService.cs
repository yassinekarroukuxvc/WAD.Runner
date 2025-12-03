// PartAutomation/Execution/PartAutomationService.cs
using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;                       // Logger
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Rules;
using WAD.Runner.PartAutomation.SolidWorks;
// NOTE: SwAppHost is no longer used. You can delete PartAutomation/SolidWorks/SwAppHost.cs.

namespace WAD.Runner.PartAutomation.Execution
{
    /// <summary>
    /// Implements part automation against a SolidWorks instance provided by the caller via Attach(swApp).
    /// This class does NOT start or quit SolidWorks; it only opens/saves/closes documents.
    /// </summary>
    public sealed class PartAutomationService : IPartAutomationService
    {
        private SldWorks? _sw;            // provided via Attach
        private PartEditor? _editor;       // wrapper around ModelDoc2 operations

        /// <summary>Provide a running SolidWorks instance. Must be called before other methods.</summary>
        public void Attach(SldWorks swApp)
        {
            _sw = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _editor = new PartEditor(_sw);
            Logger.Info("[PartAutomationService] Attached to SolidWorks.");
        }

        public void OpenPart(string partPath)
        {
            EnsureAttached();
            Logger.Info($"[PartAutomationService] OpenPart → '{partPath}'");
            _editor!.Open(partPath);
            Logger.Success("[PartAutomationService] Part opened.");
        }

        public void ActivateConfiguration(WedgeSubclass subclass, DrawingType drawingType)
        {
            EnsureAttached();
            var cfg =
                subclass switch
                {
                    WedgeSubclass.PGB when drawingType == DrawingType.Overlay => "PGB_OVERLAY",
                    WedgeSubclass.PGB when drawingType == DrawingType.Customer => "PGB_CUSTOMER_DRAWING",
                    WedgeSubclass.PGB => "PGB_PRODUCTION",
                    _ when drawingType == DrawingType.Overlay => "FG_OVERLAY",
                    _ when drawingType == DrawingType.Customer => "FG_CUSTOMER_DRAWING",
                    _ => "FG_PRODUCTION_DRAWING"
                };

            Logger.Info($"[PartAutomationService] ActivateConfiguration → {cfg} (subclass={subclass}, type={drawingType})");
            _editor!.ActivateConfiguration(cfg);
            Logger.Success("[PartAutomationService] Configuration activated.");
        }

        public void UpdateEquations(string equationFilePath)
        {
            EnsureAttached();
            Logger.Info($"[PartAutomationService] UpdateEquations → '{equationFilePath}'");
            _editor!.UpdateEquations(equationFilePath);
            Logger.Success("[PartAutomationService] Equations updated.");
        }

        public void EnsureAllEquationsExist(WedgeData wedge)
        {
            EnsureAttached();
            Logger.Info("[PartAutomationService] EnsureAllEquationsExist → start");
            EquationUpdater.EnsureAllEquationsExist(_editor!.Model, wedge);
            Logger.Success("[PartAutomationService] EnsureAllEquationsExist → done");
        }

        public void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys)
        {
            EnsureAttached();
            var keysList = string.Join(", ", keys is null ? Array.Empty<string>() : System.Linq.Enumerable.Select(keys, k => k.Value));
            Logger.Info($"[PartAutomationService] ApplyLengthTolerances → [{keysList}]");
            _editor!.ApplyLengthTolerances(wedge, keys ?? Array.Empty<DimensionKey>());
            Logger.Success("[PartAutomationService] Tolerances applied.");
        }

        public void ApplyPostRules(WedgeData wedge, DrawingType drawingType)
        {
            EnsureAttached();
            Logger.Info($"[PartAutomationService] ApplyPostRules → drawingType={drawingType}");

            var editor = _editor!;
            var engraving = wedge.Marking?.Text
                            ?? (wedge.Properties.TryGetValue("Marking", out var s) ? s : null);

            Logger.Info($"[PartAutomationService] Engraving text → '{(engraving ?? "(null)")}'");
            editor.SetEngraving(engraving);

            // Engraving toggle:
            //  - Production + Customer → turn engraving ON
            //  - Overlay → do nothing (keep whatever suppression state exists)
            if (drawingType == DrawingType.Production || drawingType == DrawingType.Customer)
            {
                BasicPartRules.ApplyEngravingToggle(editor);
            }

            BasicPartRules.ApplyTipGuard(editor, wedge);
            BasicPartRules.ApplyVrMinMax(editor, wedge);
            BasicPartRules.ApplyVwTolDims(editor, wedge);
            BasicPartRules.ApplyOverlayVwWToggle(editor, wedge, overlay: drawingType == DrawingType.Overlay);

            editor.Rebuild();
            Logger.Success("[PartAutomationService] Post rules applied.");
        }


        public void RebuildPart()
        {
            EnsureAttached();
            Logger.Info("[PartAutomationService] RebuildPart");
            _editor!.Rebuild();
            Logger.Success("[PartAutomationService] Rebuild complete.");
        }

        public void SaveAndClose()
        {
            Logger.Info("[PartAutomationService] SaveAndClose → start");
            try
            {
                _editor?.Save();
                _editor?.Close();
                Logger.Success("[PartAutomationService] Part saved and closed.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[PartAutomationService] Save/Close warning: {ex.Message}");
            }
            finally
            {
                // We do NOT own SolidWorks lifetime. Just release editor references.
                _editor = null;
                _sw = null;
                Logger.Info("[PartAutomationService] Cleanup complete (editor detached).");
            }
        }

        // --- helpers ---
        private void EnsureAttached()
        {
            if (_sw is null || _editor is null)
                throw new InvalidOperationException("Not attached. Call Attach(swApp) before using the service.");
        }
    }
}
