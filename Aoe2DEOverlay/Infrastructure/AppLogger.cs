using System.IO;
using System.Globalization;
using System.Text;

namespace Aoe2DEOverlay;

public static class AppLogger
{
    private static readonly object Gate = new();
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AoE2MinimalOverlay");
    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");
    public static string LogPath { get; } = Path.Combine(LogDirectory, $"overlay-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash or disturb the overlay.
        }
    }
}

