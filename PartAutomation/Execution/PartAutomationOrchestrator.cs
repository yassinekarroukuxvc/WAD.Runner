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

                Logger.Info("[PartOrchestrator] Importing equations from equation file.");
                _partService.UpdateEquations(equationsOutPath);
                _partService.RebuildPart();

                _partService.EnsureAllEquationsExist(wedge);

                var tolKeys = wedge.Dimensions
                    .Where(kvp => kvp.Value.Nominal.Unit == UnitKind.Millimeter)
                    .Where(kvp => !kvp.Value.Tol.IsZero)
                    .Select(kvp => kvp.Key)
                    .Distinct()
                    .ToArray();

                if (tolKeys.Length == 0)
                {
                    Logger.Info("[PartOrchestrator] No non-zero length tolerances found in WedgeData.");
                }
                else
                {
                    _partService.ApplyLengthTolerances(wedge, tolKeys);
                }

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
