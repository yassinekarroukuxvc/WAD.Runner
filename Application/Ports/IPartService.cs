using System;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;


namespace WAD.Runner.Application.Ports
{
    /// <summary>
    /// Thin, resilient adapter over SolidWorks Part operations.
    /// Keep it small and stable; all rule logic lives elsewhere.
    /// Length units are METERS for system values; angles are DEGREES.
    /// </summary>
    public interface IPartService : IDisposable
    {
        // Lifecycle
        void OpenPart(string partPath);
        bool ActivateConfiguration(WedgeSubclass subclass, DrawingType drawingType);
        void Rebuild(bool force = false); // coalesce when possible
        void Save(bool close = false);
        void Unlock();


        // Setters
        /// <summary>Set a dimension by its full SW name (e.g., "VW_LTOL@FG_Wed_VW"). Value in METERS for lengths.</summary>
        bool SetDimensionValueByFullName(string fullName, double systemValueMeters, bool rebuild = false);
        /// <summary>Set or update a custom property on the active part (e.g., "Engraving").</summary>
        bool SetCustomProperty(string name, string value);


        // Suppression / visibility
        bool SetFeatureSuppressed(string featureName, bool suppress);
        bool SetSketchSuppressed(string sketchName, bool suppress);
        bool SetFolderSuppressed(string folderName, bool suppress, bool includeChildren = true);


        // Equations
        /// <summary>Load from an external equations.txt and push values into the part.</summary>
        bool UpdateEquationsFromFile(string equationFilePath);


        // Native access (needed by EquationMgr ensure pass)
        ModelDoc2 GetNativeModel();
    }
}