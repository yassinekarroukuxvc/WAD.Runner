// DrawingAutomation/Rules/Common/IDrawingCleanupRunner.cs
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Rules.Common
{
    /// <summary>
    /// Contract for wedge-type-specific drawing cleanup runners (annotation cleanup, note cleanup, etc.).
    ///
    /// Pattern:
    /// - Each wedge type can have its own runner (COB, OSG7, ...).
    /// - Pipeline / executors can call runners uniformly.
    /// </summary>
    public interface IDrawingCleanupRunner
    {
        WedgeType AppliesTo { get; }

        /// <summary>
        /// Apply cleanup to an already-open drawing.
        /// Implementations should catch exceptions internally unless you want hard failure.
        /// </summary>
        void TryApply(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            DrawingRun run,
            DrawingData drawingData,
            bool activateEachView = true);
    }
}