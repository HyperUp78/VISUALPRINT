using System;
using System.IO;
using System.Threading;

namespace CSharpVisualScripting.UI.Diagnostics;

public static class DiagLogger
{
    private static readonly object _sync = new object();
    private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
    private static string LogPath(string name) => Path.Combine(BaseDir, name);

    public static void Info(string message) => Write("debug.log", "INFO", message);
    public static void Error(string message) => Write("debug.log", "ERROR", message);
    public static void Startup(string message) => Write("startup.log", "STARTUP", message);

    private static void Write(string file, string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:O} [{level}] [T{Thread.CurrentThread.ManagedThreadId}] {message}\n";
            var path = LogPath(file);
            lock (_sync)
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fs) { AutoFlush = true };
                writer.Write(line);
            }
        }
        catch
        {
            // Suppress logging exceptions
        }
    }
}
