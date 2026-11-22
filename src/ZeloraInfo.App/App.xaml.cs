// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.IO;
using System.Windows;
using System.Windows.Threading;
using ZeloraInfo.App.Services;
using ZeloraInfo.App.ViewModels;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App;

public partial class App : Application
{
    private const string InstanceName = "SeroTeknoloji.ZeloraInfo";
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;

    public static AppServices Services { get; private set; } = null!;
    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch brings the running window (possibly hidden in the tray) to front.
        // After an elevation restart the old process may still be shutting down, so wait briefly for it.
        _instanceMutex = new Mutex(true, $@"Local\{InstanceName}", out var createdNew);
        if (!createdNew)
        {
            var acquired = false;
            if (e.Args.Contains("--elevated"))
            {
                try { acquired = _instanceMutex.WaitOne(TimeSpan.FromSeconds(6)); }
                catch (AbandonedMutexException) { acquired = true; }
            }
            if (!acquired)
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting($@"Local\{InstanceName}.Show");
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }
                Shutdown();
                return;
            }
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceName}.Show");
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
            {
                if (IsExiting) return;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true, Name = "ZeloraInfo.SingleInstance" };
        listener.Start();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };

        Services = new AppServices();
        var settings = Services.Settings.Load();
        var args = e.Args;

        // Optional: always elevate so S.M.A.R.T. is available (the user can decline UAC and continue restricted).
        if (settings.General.AlwaysRunAsAdministrator && !Services.Privileges.IsAdministrator
            && !args.Contains("--elevated") && !args.Contains("--no-elevate"))
        {
            var forward = string.Join(' ', args.Where(a => a != "--elevated").Append("--elevated"));
            if (Services.Privileges.TryRestartElevated(forward))
            {
                ReleaseInstance();
                Shutdown();
                return;
            }
        }

        Services.Theme.Apply(settings.General.Theme);
        Services.Localization.Apply(settings.General.Language);

        _viewModel = new MainViewModel(Services);
        _window = new MainWindow(_viewModel, Services);
        MainWindow = _window;

        // Tray: every page reachable from the context menu.
        Services.Tray.SetPages(_viewModel.NavGroups.SelectMany(g => g.Items.Select(i => (i.Key, (string?)g.HeaderKey))).ToList());
        Services.Tray.OpenRequested += (_, _) => ShowMainWindow();
        Services.Tray.ExitRequested += (_, _) => ExitApplication();
        Services.Tray.NavigateRequested += (_, key) =>
        {
            ShowMainWindow();
            _viewModel.Navigate(key);
        };
        Services.Localization.LanguageChanged += (_, _) => Services.Tray.RefreshTexts();

        _viewModel.Navigate(settings.General.StartPage);

        var startMinimized = args.Contains("--minimized") || settings.General.StartMinimized;
        var trayCapable = settings.General.MinimizeToTray || settings.General.CloseAction == CloseAction.MinimizeToTray;
        if (startMinimized && trayCapable)
        {
            Services.Tray.IsVisible = true;
        }
        else
        {
            _window.Show();
            if (startMinimized) _window.WindowState = WindowState.Minimized;
        }

        if (settings.Benchmark.CleanupOrphanedFilesOnStartup)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Services.Benchmark.CleanupOrphanedTestFiles(BenchmarkCleanupRoots(settings));
                }
                catch (Exception ex)
                {
                    LogError(ex);
                }
            });
        }
    }

    /// <summary>Roots scanned for orphaned benchmark files: every ready fixed/removable drive + the last custom target.</summary>
    public static IEnumerable<string> BenchmarkCleanupRoots(AppSettings settings)
    {
        var roots = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) roots.Add(d.RootDirectory.FullName);
            }
            catch (IOException)
            {
            }
        }
        if (!string.IsNullOrWhiteSpace(settings.Benchmark.LastTargetPath)) roots.Add(settings.Benchmark.LastTargetPath);
        return roots;
    }

    public void ShowMainWindow()
    {
        if (_window is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    public void HideToTray()
    {
        if (_window is null) return;
        Services.Tray.IsVisible = true;
        _window.Hide();
        Services.Tray.ShowRunningHint();
    }

    public void ExitApplication()
    {
        if (IsExiting) return;
        IsExiting = true;
        try
        {
            _viewModel?.Shutdown();
            Services.Dispose();
            _window?.Close();
        }
        finally
        {
            ReleaseInstance();
            Shutdown();
        }
    }

    /// <summary>Starts an elevated copy (UAC) and exits this one cleanly. Shows a hint if the user declines.</summary>
    public static void RestartElevated()
    {
        var app = (App)Current;
        app.ReleaseInstance();
        if (Services.Privileges.TryRestartElevated())
        {
            app.ExitApplication();
            return;
        }
        // Declined: re-acquire single-instance ownership and continue in restricted mode.
        app._instanceMutex = new Mutex(true, $@"Local\{InstanceName}", out _);
        Services.Ui.ShowInfo(LanguageCatalog.Get("Priv.UacDeclined"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services?.Tray.Dispose(); // never leave a ghost tray icon behind
        }
        catch
        {
        }
        base.OnExit(e);
    }

    /// <summary>Releases the single-instance mutex early (before an elevated restart or on exit).</summary>
    public void ReleaseInstance()
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned (already released).
        }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        if (IsExiting) _showSignal?.Set(); // wake the listener so it can observe IsExiting and stop
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(LanguageCatalog.Format("Common.UnexpectedError", e.Exception.Message), "Zelora Info",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void LogError(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeloraInfo", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
