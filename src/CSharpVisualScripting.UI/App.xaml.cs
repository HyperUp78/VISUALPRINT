using System.Windows;
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using CSharpVisualScripting.UI.Diagnostics;

namespace CSharpVisualScripting.UI;

public partial class App : Application
{
    static App()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var initLog = Path.Combine(baseDir, "moduleinit.log");
            File.AppendAllText(initLog, $"Module initializer (App static ctor) at {DateTime.Now:O}\n");
        }
        catch { }
    }

    public App()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var initLog = Path.Combine(baseDir, "moduleinit.log");
            File.AppendAllText(initLog, $"App constructor called at {DateTime.Now:O}\n");
            
            // Wrap InitializeComponent to catch XAML issues
            File.AppendAllText(initLog, $"About to call App.InitializeComponent at {DateTime.Now:O}\n");
            InitializeComponent();
            File.AppendAllText(initLog, $"App.InitializeComponent completed at {DateTime.Now:O}\n");
        }
        catch (Exception ex)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var initLog = Path.Combine(baseDir, "moduleinit.log");
            File.AppendAllText(initLog, $"App constructor ERROR: {ex.Message} at {DateTime.Now:O}\n{ex.StackTrace}\n");
        }
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var startupLog = Path.Combine(baseDir, "startup.log");
        var debugLog = Path.Combine(baseDir, "debug.log");

        try
            {
                LoadThemeResources();
                DiagLogger.Startup("App.OnStartup entered");
                File.WriteAllText(startupLog, $"Application starting at {DateTime.Now:O}\n");

            // Add global exception handling
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

                File.AppendAllText(startupLog, "Exception handlers registered\n");
                DiagLogger.Startup("Exception handlers registered");

            base.OnStartup(e);

            try
            {
                    File.AppendAllText(startupLog, "Creating MainWindow...\n");
                    DiagLogger.Startup("Creating MainWindow");
                var win = new MainWindow();
                this.MainWindow = win;
                win.Show();
                    File.AppendAllText(startupLog, "MainWindow shown successfully\n");
                    DiagLogger.Startup("MainWindow shown");
            }
            catch (Exception createEx)
            {
                var msg = $"MainWindow creation error: {createEx.Message}\n{createEx.StackTrace}\n";
                File.AppendAllText(debugLog, msg);
                MessageBox.Show($"Failed to initialize UI: {createEx.Message}\n\nSee debug.log for details.",
                    "GREENPRINTS - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Startup error: {ex.Message}\n{ex.StackTrace}\n";
            File.AppendAllText(startupLog, errorMsg);
            MessageBox.Show($"Startup Error: {ex.Message}\n\nSee startup.log for details",
                "GREENPRINTS - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
        }
    }

    private void LoadThemeResources()
    {
        var resourceUris = new[]
        {
            "pack://application:,,,/MahApps.Metro;component/styles/controls.xaml",
            "pack://application:,,,/MahApps.Metro;component/styles/fonts.xaml",
            "pack://application:,,,/MahApps.Metro;component/styles/themes/dark.teal.xaml",
            "pack://application:,,,/Nodify;component/Themes/Dark.xaml"
        };

        foreach (var resourceUri in resourceUris)
        {
            try
            {
                var dictionary = new ResourceDictionary { Source = new Uri(resourceUri, UriKind.Absolute) };
                Resources.MergedDictionaries.Add(dictionary);
                DiagLogger.Startup($"Loaded resource dictionary: {resourceUri}");
            }
            catch (Exception ex)
            {
                DiagLogger.Error($"Failed to load resource dictionary {resourceUri}: {ex}");
            }
        }
    }
    
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        File.AppendAllText(logFile, $"Dispatcher exception: {e.Exception.Message}\n{e.Exception.StackTrace}\n");
        MessageBox.Show($"Unhandled Exception: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}",
            "GREENPRINTS - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
    
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        if (e.ExceptionObject is Exception ex)
        {
            File.AppendAllText(logFile, $"Fatal error: {ex.Message}\n{ex.StackTrace}\n");
            MessageBox.Show($"Fatal Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "GREENPRINTS - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Verbose early exception tracing (may be noisy but invaluable for crashes)
        var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        try { File.AppendAllText(logFile, $"FirstChance: {e.Exception.GetType().FullName}: {e.Exception.Message}\n"); }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        try { File.AppendAllText(logFile, $"UnobservedTaskException: {e.Exception}\n"); }
        catch { }
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var startupLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
        try { File.AppendAllText(startupLog, $"Application exiting with code {e.ApplicationExitCode} at {DateTime.Now:O}\n"); }
        catch { }
        base.OnExit(e);
    }
}
