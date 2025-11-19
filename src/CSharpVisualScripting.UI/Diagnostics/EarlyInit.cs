using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace CSharpVisualScripting.UI.Diagnostics;

public static class EarlyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(baseDir, "moduleinit.log");
            File.AppendAllText(path, $"ModuleInitializer invoked at {DateTime.Now:O}\n");
        }
        catch { }
    }
}
