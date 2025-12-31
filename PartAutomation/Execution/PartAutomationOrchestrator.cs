// PartAutomation/Execution/PartAutomationOrchestrator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.PartAutomation.Common;
using WAD.Runner.PartAutomation.Execution;           // PartAutomationService
using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Jobs;
using WAD.Runner.PartAutomation.SolidWorks;

using WAD.Runner.PartAutomation.Rules.Equations;

namespace WAD.Runner.PartAutomation.Execution
{
    public sealed class PartAutomationOrchestrator
    {
        private readonly IPartAutomationService _partService;

        public PartAutomationOrchestrator(IPartAutomationService partService)
        {
            _partService = partService ?? throw new ArgumentNullException(nameof(partService));
        }

        public async Task<string> RunAsync(PartJobRequest job, SldWorks swApp, CancellationToken ct)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));

            var plan = PathPlanner.Build(
                article: (job.ArticleNumber ?? "UNKNOWN").Trim(),
                subclass: job.Subclass,
                drawingType: job.DrawingType,
                outputRoot: string.IsNullOrWhiteSpace(job.OutputRoot) ? Path.Combine("Resources", "Out") : job.OutputRoot!,
                fileBase: job.FileBase
            );

            var modPartPath = Path.GetFullPath(plan.PartPath);
            var equationsOutPath = Path.GetFullPath(plan.EquationsPath);

            if (string.IsNullOrWhiteSpace(job.PartTemplatePath) || !File.Exists(job.PartTemplatePath))
                throw new FileNotFoundException($"Part template not found: {job.PartTemplatePath}");

            if (string.IsNullOrWhiteSpace(job.EquationTemplatePath) || !File.Exists(job.EquationTemplatePath))
                throw new FileNotFoundException($"Equation template not found: {job.EquationTemplatePath}");

            TemplatePreparer.CopyTemplate(job.PartTemplatePath!, modPartPath, overwrite: true);
            TemplatePreparer.CopyTemplate(job.EquationTemplatePath!, equationsOutPath, overwrite: true);

            var eqAttrs = File.GetAttributes(equationsOutPath);
            if ((eqAttrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(equationsOutPath, eqAttrs & ~FileAttributes.ReadOnly);
                Logger.Info($"[PartOrchestrator] Cleared read-only on equations file: {equationsOutPath}");
            }

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

                EquationUpdater.UpdateEquationFile(equationsOutPath, effectiveDims, job.WedgeData, job.DrawingType);
            }
            else
            {
                Logger.Warn("[PartOrchestrator] No WedgeData provided; equations.txt remains as template.");
            }

            _partService.Attach(swApp);

            _partService.OpenPart(modPartPath);
            _partService.ActivateConfiguration(job.Subclass, job.DrawingType);

            if (job.WedgeData is not null)
            {
                var wedge = job.WedgeData;
                var dims = effectiveDims ?? wedge.Dimensions;

                if (job.WedgeType == WedgeType.OSG7)
                {
                    if (_partService is PartAutomationService svc)
                    {
                        Logger.Info("[PartOrchestrator] OSG7 → using direct EquationMgr upsert (no equation file import).");
                        EquationUpdater.UpsertEquationsInModel(svc.Model, dims, wedge, job.DrawingType);
                    }
                    else
                    {
                        Logger.Warn("[PartOrchestrator] OSG7 → cannot access Model. Falling back to equation-file import.");
                        _partService.UpdateEquations(equationsOutPath);
                    }
                }
                else
                {
                    Logger.Info("[PartOrchestrator] Non-OSG7 → using equation-file import (default CKVD behavior).");
                    _partService.UpdateEquations(equationsOutPath);
                    _partService.RebuildPart();
                }

                _partService.EnsureAllEquationsExist(wedge);

                var tolKeys = new[]
                {
                    new DimensionKey("TL"),
                    new DimensionKey("TD"),
                    new DimensionKey("TDF"),
                    new DimensionKey("FL"),
                    new DimensionKey("W")
                };

                _partService.ApplyLengthTolerances(wedge, tolKeys);
                _partService.ApplyPostRules(job.WedgeType, wedge, job.DrawingType);
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
