using System.Windows;
using System.Windows.Threading;
using DayZModManager.App.Services;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.IO;
using DayZModManager.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DayZModManager.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            RegisterGlobalExceptionHandlers();

            var fileSystem = new PhysicalFileSystem();
            var dataDirectoryProvider = new DataDirectoryProvider(fileSystem);
            dataDirectoryProvider.Initialize();

            _services = BuildServiceProvider(fileSystem, dataDirectoryProvider);

            var viewModel = _services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();
            _ = viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start DayZ Mod Manager:\n\n{ex.Message}",
                "DayZ Mod Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServiceProvider(IFileSystem fileSystem, IDataDirectoryProvider dataDirectoryProvider)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<IDataDirectoryProvider>(dataDirectoryProvider);
        services.AddSingleton<IJunctionOperations, JunctionOperations>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IModOrderStore, ModOrderStore>();
        services.AddSingleton<ITypesConfigStore, TypesConfigStore>();
        services.AddSingleton<IBatchFileService, BatchFileService>();
        services.AddSingleton<IModDiscoveryService, ModDiscoveryService>();
        services.AddSingleton<IJunctionService, JunctionService>();
        services.AddSingleton<IServerConfigService, ServerConfigService>();
        services.AddSingleton<IEconomyCoreService, EconomyCoreService>();
        services.AddSingleton<ITypesService, TypesService>();
        services.AddSingleton<ISaveGameService, SaveGameService>();
        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<IApplyService, ApplyService>();
        services.AddSingleton<IDialogService, UiDialogService>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IModOrderStore>(),
            sp.GetRequiredService<ITypesConfigStore>(),
            sp.GetRequiredService<IApplyService>(),
            sp.GetRequiredService<IModDiscoveryService>(),
            sp.GetRequiredService<IMapService>(),
            sp.GetRequiredService<ITypesService>(),
            sp.GetRequiredService<ISaveGameService>(),
            sp.GetRequiredService<IServerConfigService>(),
            sp.GetRequiredService<IBatchFileService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IProcessLauncher>(),
            dataDirectoryProvider));

        return services.BuildServiceProvider();
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        var app = Current;
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}",
            "DayZ Mod Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        string message = (e.ExceptionObject as Exception)?.Message
            ?? e.ExceptionObject?.ToString()
            ?? "Unknown error";

        Dispatcher? dispatcher = Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(() =>
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{message}",
                    "DayZ Mod Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
    }
}
