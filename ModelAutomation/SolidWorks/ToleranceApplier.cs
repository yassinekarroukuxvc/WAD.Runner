using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public sealed class ToleranceApplier
    {
        private readonly ModelDoc2 _swModel;

        public ToleranceApplier(ModelDoc2 swModel)
        {
            _swModel = swModel ?? throw new ArgumentNullException(nameof(swModel));
        }

        public void Apply(TolerancePlan plan)
        {
            if (plan is null || plan.Count == 0)
            {
                Logger.Info("[ToleranceApplier] No tolerance updates to apply.");
                return;
            }

            Logger.Info($"[ToleranceApplier] Applying {plan.Count} tolerance updates...");

            int ok = 0, fail = 0;

            foreach (var upd in plan.NonEmpty())
            {
                if (string.IsNullOrWhiteSpace(upd.TargetDimensionName))
                {
                    fail++;
                    continue;
                }

                try
                {
                    var dimObj = _swModel.Parameter(upd.TargetDimensionName) as Dimension;
                    if (dimObj == null)
                    {
                        Logger.Warn($"[ToleranceApplier] Missing dimension: '{upd.TargetDimensionName}'");
                        fail++;
                        continue;
                    }

                    var sysVal = ToSystemValue(upd.Value, upd.Unit);
                    dimObj.SystemValue = sysVal;

                    Logger.Info($"[ToleranceApplier] Set {upd.TargetDimensionName} = {upd.Value} ({upd.Unit})");
                    ok++;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[ToleranceApplier] Failed: '{upd.TargetDimensionName}' → {ex.Message}");
                    fail++;
                }
            }

            Logger.Success($"[ToleranceApplier] Done. ok={ok}, fail={fail}");
        }

        private static double ToSystemValue(decimal value, ToleranceUnit unit)
        {
            return unit switch
            {
                ToleranceUnit.LengthMm => (double)(value / 1000m),
                ToleranceUnit.AngleDeg => (double)(value * (decimal)Math.PI / 180m),
                _ => (double)value
            };
        }
    }
}
