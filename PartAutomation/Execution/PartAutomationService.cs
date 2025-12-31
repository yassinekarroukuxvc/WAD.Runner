// PartAutomation/Execution/PartAutomationService.cs
using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Rules;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Execution
{
    public sealed class PartAutomationService : IPartAutomationService
    {
        private SldWorks? _sw;
        private PartEditor? _editor;

        public ModelDoc2 Model
        {
            get
            {
                EnsureAttached();
                return _editor!.Model;
            }
        }
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
                    WedgeSubclass.PGB => "PGB_DRAWING",
                    _ when drawingType == DrawingType.Overlay => "FG_OVERLAY",
                    _ when drawingType == DrawingType.Customer => "FG_CUSTOMER_DRAWING",
                    _ => "FG_PRODUCTION_DRAWING"
                };

            Logger.Info($"[PartAutomationService] ActivateConfiguration → {cfg}");
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
            var keysList = string.Join(", ",
                keys is null ? Array.Empty<string>() : System.Linq.Enumerable.Select(keys, k => k.Value));

            Logger.Info($"[PartAutomationService] ApplyLengthTolerances → [{keysList}]");
            _editor!.ApplyLengthTolerances(wedge, keys ?? Array.Empty<DimensionKey>());
            Logger.Success("[PartAutomationService] Tolerances applied.");
        }

        public void ApplyPostRules(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType)
        {
            EnsureAttached();
            Logger.Info($"[PartAutomationService] ApplyPostRules → wedgeType={wedgeType}, drawingType={drawingType}");

            var editor = _editor!;

            var engraving =
                wedge.Marking?.Text ??
                (wedge.Properties.TryGetValue("Marking", out var s) ? s : null);

            Logger.Info($"[PartAutomationService] Engraving text → '{engraving ?? "(null)"}'");
            editor.SetEngraving(engraving);

            switch (wedgeType)
            {
                case WedgeType.CKVD:
                    Logger.Info("[PartAutomationService] Applying CKVD rules.");
                    BasicPartRules.ApplyCkvdRules(editor, wedge, drawingType);
                    break;

                case WedgeType.COB:
                    Logger.Info("[PartAutomationService] Applying COB rules.");
                    BasicPartRules.ApplyCobRules(editor, wedge, drawingType);
                    break;

                case WedgeType.OSG7:
                    Logger.Info("[PartAutomationService] Applying OSG7 rules.");
                    BasicPartRules.ApplyOsg7Rules(editor, wedge, drawingType);
                    break;

                default:
                    Logger.Warn("[PartAutomationService] Unknown wedge type; applying fallback.");
                    if (drawingType is DrawingType.Production or DrawingType.Customer)
                        BasicPartRules.ApplyEngravingToggle(editor);

                    editor.Rebuild();
                    Logger.Success("[PartAutomationService] Post rules applied (fallback).");
                    break;
            }
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
                _editor = null;
                _sw = null;
                Logger.Info("[PartAutomationService] Cleanup complete.");
            }
        }

        private void EnsureAttached()
        {
            if (_sw is null || _editor is null)
                throw new InvalidOperationException("Not attached. Call Attach(swApp) first.");
        }
    }
}
