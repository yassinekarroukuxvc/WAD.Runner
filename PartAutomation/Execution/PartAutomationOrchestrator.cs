// PartAutomation/Execution/PartAutomationOrchestrator.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;                        // Logger
using WAD.Runner.DataManagement.Domain.Dimensions;  // DimensionKey
using WAD.Runner.DataManagement.Domain.Wedge;       // WedgeSubclass, DrawingType, WedgeData
using WAD.Runner.PartAutomation.Interfaces;         // IPartAutomationService
using WAD.Runner.PartAutomation.Jobs;               // PartJobRequest
using WAD.Runner.PartAutomation.Common;             // PathPlanner, TemplatePreparer
using WAD.Runner.PartAutomation.SolidWorks;         // EquationUpdater

namespace WAD.Runner.PartAutomation.Execution
{
    public sealed class PartAutomationOrchestrator
    {
        private readonly IPartAutomationService _partService;

        public PartAutomationOrchestrator(IPartAutomationService partService)
        {
            _partService = partService;
        }

        public async Task<string> RunAsync(PartJobRequest job, SldWorks swApp, CancellationToken ct)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));

            // ---- Plan paths using PathPlanner ----
            var plan = PathPlanner.Build(
                article: (job.ArticleNumber ?? "UNKNOWN").Trim(),
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot) ? Path.Combine("Resources", "Out") : job.OutputRoot!,
                fileBase: job.FileBase
            );

            var modPartPath = Path.GetFullPath(plan.PartPath);
            var equationsOutPath = Path.GetFullPath(plan.EquationsPath);

            // ---- Validate templates ----
            if (string.IsNullOrWhiteSpace(job.PartTemplatePath) || !File.Exists(job.PartTemplatePath))
                throw new FileNotFoundException($"Part template not found: {job.PartTemplatePath}");
            if (string.IsNullOrWhiteSpace(job.EquationTemplatePath) || !File.Exists(job.EquationTemplatePath))
                throw new FileNotFoundException($"Equation template not found: {job.EquationTemplatePath}");

            // ---- Copy templates into work dir (robust, handles locks/RO) ----
            TemplatePreparer.CopyTemplate(job.PartTemplatePath!, modPartPath, overwrite: true);
            TemplatePreparer.CopyTemplate(job.EquationTemplatePath!, equationsOutPath, overwrite: true);

            // Ensure equations file is not read-only (some source controls/templates mark RO)
            var eqAttrs = File.GetAttributes(equationsOutPath);
            if ((eqAttrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(equationsOutPath, eqAttrs & ~FileAttributes.ReadOnly);
                Logger.Info($"[PartOrchestrator] Cleared read-only on equations file: {equationsOutPath}");
            }

            // ---- MATERIALIZE WedgeData → equations.txt BEFORE importing into SW ----
            if (job.WedgeData is not null)
            {
                var before = File.GetLastWriteTimeUtc(equationsOutPath);
                EquationUpdater.UpdateEquationFile(equationsOutPath, job.WedgeData, job.DrawingType); // ★ minimal fix
                var after = File.GetLastWriteTimeUtc(equationsOutPath);
                Logger.Info($"[PartOrchestrator] equations.txt timestamp: {before:o} → {after:o}");
            }
            else
            {
                Logger.Warn("[PartOrchestrator] No WedgeData provided; equations.txt remains as template.");
            }

            // ---- Attach to SolidWorks and run pipeline ----
            _partService.Attach(swApp);

            _partService.OpenPart(modPartPath);
            _partService.ActivateConfiguration(job.Subclass, job.DrawingType);

            // Import the (now-updated) equations file into the model
            _partService.UpdateEquations(equationsOutPath);

            // ----- Wedge-driven operations -----
            if (job.WedgeData is not null)
            {
                var wedge = job.WedgeData;

                // 1) ensure all variables exist in the model (safety)
                _partService.EnsureAllEquationsExist(wedge);

                // 2) tolerances
                var tolKeys = new[]
                {
                    new DimensionKey("TL"),
                    new DimensionKey("TD"),
                    new DimensionKey("TDF"),
                    new DimensionKey("FL"),
                    new DimensionKey("W")
                };
                _partService.ApplyLengthTolerances(wedge, tolKeys);

                // 3) post-rules (tip guard, vw/w toggle, vr min/max, etc.)
                _partService.ApplyPostRules(job.WedgeType, job.WedgeData, job.DrawingType);
            }
            else
            {
                Logger.Warn("[PartOrchestrator] No WedgeData; skipping EnsureAllEquations/Tolerances/PostRules.");
            }

            _partService.RebuildPart();
            _partService.SaveAndClose();

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            Logger.Success($"[PartOrchestrator] Done → {modPartPath}");
            return modPartPath;
        }
    }
}
