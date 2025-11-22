namespace WAD.Runner.Application;

public static class Logger
{
    public static void Info(string message)
    {
        Write(message, ConsoleColor.Gray);
    }

    public static void Success(string message)
    {
        Write(message, ConsoleColor.Green);
    }

    public static void Warn(string message)
    {
        Write(message, ConsoleColor.Yellow);
    }

    public static void Error(string message)
    {
        Write(message, ConsoleColor.Red);
    }
    public static bool WarnAndReturnFalse(string message)
    {
        Write(message, ConsoleColor.DarkYellow);
        return false;
    }
    public static bool ErrorAndReturnFalse(string message)
    {
        Write(message, ConsoleColor.DarkRed);
        return false;
    }
    public static T WarnAndReturnNull<T>(string message) where T : class
    {
        Warn(message);
        return null;
    }

    public static void Blue(string message)
    {
        Write(message, ConsoleColor.Blue);
    }

    private static void Write(string message, ConsoleColor color)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Console.ForegroundColor = color;
        Console.WriteLine($"[{timestamp}] {message}");
        Console.ResetColor();
    }
}