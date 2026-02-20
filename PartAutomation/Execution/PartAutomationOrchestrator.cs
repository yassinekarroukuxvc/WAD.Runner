// PartAutomation/Execution/PartAutomationOrchestrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.PartAutomation.Common;
using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Jobs;
using WAD.Runner.PartAutomation.Rules.Equations;
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Execution
{
    /// <summary>
    /// Thin orchestrator:
    ///  - Prepare output paths + copy templates
    ///  - (Optional) write equations.txt for traceability
    ///  - Open part + activate configuration
    ///  - Re-point EquationMgr.FilePath to the JOB equations file (avoid template linkage)
    ///  - Run macro-style pipeline via PartAutomationService.RunMacroStyle(...)
    ///  - Save/close (always in finally)
    /// </summary>
    public sealed class PartAutomationOrchestrator
    {
        private readonly IPartAutomationService _partService;

        public PartAutomationOrchestrator(IPartAutomationService partService)
        {
            _partService = partService ?? throw new ArgumentNullException(nameof(partService));
        }

        public Task<string> RunAsync(PartJobRequest job, SldWorks swApp, CancellationToken ct)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));

            ct.ThrowIfCancellationRequested();

            var plan = PathPlanner.Build(
                article: (job.ArticleNumber ?? "UNKNOWN").Trim(),
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot)
                    ? Path.Combine("Resources", "Out")
                    : job.OutputRoot!,
                fileBase: job.FileBase
            );

            var modPartPath = Path.GetFullPath(plan.PartPath);
            var equationsOutPath = Path.GetFullPath(plan.EquationsPath);

            if (string.IsNullOrWhiteSpace(job.PartTemplatePath) || !File.Exists(job.PartTemplatePath))
                throw new FileNotFoundException($"Part template not found: {job.PartTemplatePath}");

            if (string.IsNullOrWhiteSpace(job.EquationTemplatePath) || !File.Exists(job.EquationTemplatePath))
                throw new FileNotFoundException($"Equation template not found: {job.EquationTemplatePath}");

            // 1) Copy templates (job-local artifacts)
            TemplatePreparer.CopyTemplate(job.PartTemplatePath!, modPartPath, overwrite: true);
            TemplatePreparer.CopyTemplate(job.EquationTemplatePath!, equationsOutPath, overwrite: true);

            // Ensure equations file is writable (traceability write)
            var eqAttrs = File.GetAttributes(equationsOutPath);
            if ((eqAttrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(equationsOutPath, eqAttrs & ~FileAttributes.ReadOnly);
                Logger.Info($"[PartOrchestrator] Cleared read-only on equations file: {equationsOutPath}");
            }

            // 2) Compute effective dims + (optional) write equations.txt for traceability
            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension>? effectiveDims = null;

            if (job.WedgeData is not null)
            {
                IEquationInputNormalizer normalizer =
                    job.WedgeType switch
                    {
                        WedgeType.OSG7 => new Osg7EquationInputNormalizer(),
                        _ => new NoOpEquationInputNormalizer()
                    };

                effectiveDims = normalizer.Normalize(job.WedgeData, job.DrawingType);

                // Optional artifact: write the "effective" equations file (job traceability)
                EquationUpdater.UpdateEquationFile(equationsOutPath, effectiveDims, job.WedgeData, job.DrawingType);
            }
            else
            {
                Logger.Warn("[PartOrchestrator] No WedgeData provided; equations.txt remains as template.");
            }

            // 3) Open / activate / run macro pipeline
            _partService.Attach(swApp);

            try
            {
                ct.ThrowIfCancellationRequested();

                _partService.OpenPart(modPartPath);
                _partService.ActivateConfiguration(job.Subclass, job.DrawingType);

                // IMPORTANT:
                // Ensure the GENERATED part points to the JOB equations file (NOT the template path).
                // We are NOT importing from the file here (no UpdateValuesFromExternalEquationFile).
                // This only fixes the "linked to template equations.txt" symptom when opening the saved part later.
                ForceEquationFileLinkToJobPath(_partService.Model, equationsOutPath);

                // 4) Run macro-style pipeline (single entry point)
                if (job.WedgeData is not null && effectiveDims is not null)
                {
                    var wedge = job.WedgeData;

                    var tolKeys = wedge.Dimensions
                        .Where(kvp => kvp.Value?.Nominal.Unit == UnitKind.Millimeter)
                        .Where(kvp => kvp.Value is not null && !kvp.Value.Tol.IsZero)
                        .Select(kvp => kvp.Key)
                        .Distinct()
                        .ToArray();

                    _ = _partService.RunMacroStyle(
                        wedgeType: job.WedgeType,
                        wedge: wedge,
                        drawingType: job.DrawingType,
                        effectiveDims: effectiveDims,
                        toleranceKeys: tolKeys
                    );

                    /*
                    DO NOT do this in macro pipeline:
                    _partService.UpdateEquations(equationsOutPath);
                    _partService.ApplyPostRules(...);
                    */
                }
                else
                {
                    Logger.Warn("[PartOrchestrator] No WedgeData/effectiveDims; skipping macro pipeline.");
                    _partService.RebuildPart(); // minimal sync so file is stable
                }

                Logger.Success($"[PartOrchestrator] Done → {modPartPath}");
                return Task.FromResult(modPartPath);
            }
            finally
            {
                // 5) Save/close (always)
                try
                {
                    _partService.SaveAndClose();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[PartOrchestrator] SaveAndClose warning: {ex.Message}");
                }
            }
        }

        private static void ForceEquationFileLinkToJobPath(ModelDoc2 model, string equationsOutPath)
        {
            try
            {
                if (model is null) return;
                if (string.IsNullOrWhiteSpace(equationsOutPath)) return;

                var eqMgr = (EquationMgr)model.GetEquationMgr();

                var prev = eqMgr.FilePath ?? string.Empty;
                if (!string.Equals(prev, equationsOutPath, StringComparison.OrdinalIgnoreCase))
                {
                    eqMgr.FilePath = equationsOutPath;
                    Logger.Info($"[PartOrchestrator] EquationMgr.FilePath set to job file: {equationsOutPath}");
                }
                else
                {
                    Logger.Info("[PartOrchestrator] EquationMgr.FilePath already points to job file.");
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: upsert pipeline still works even if SW refuses the path.
                Logger.Warn($"[PartOrchestrator] Failed to set EquationMgr.FilePath (ignored): {ex.Message}");
            }
        }
    }
}
