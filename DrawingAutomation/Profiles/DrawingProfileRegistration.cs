namespace WAD.Runner.DrawingAutomation.Profiles;

public readonly record struct DrawingProfileRegistration(
    RegisteredDrawingProfileKey Key,
    DrawingProfile Profile);
