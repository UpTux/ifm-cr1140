using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cr1140.AvaloniaDemo.ViewModels;
using Cr1140.AvaloniaDemo.Views;
using Cr1140.Avalonia.Systemd;

namespace Cr1140.AvaloniaDemo;

public partial class App : Application
{
    private SystemdWatchdog? _watchdog;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var vm = new MainViewModel(Program.Keypad);

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = vm
            };
        }

        // Mirror the CODESYS liveness model without CODESYS: sd_notify READY=1 plus a
        // UI-thread WATCHDOG=1 heartbeat (unit is Type=notify + WatchdogSec=). No-op off systemd.
        _watchdog = new SystemdWatchdog();
        _watchdog.Start();

        base.OnFrameworkInitializationCompleted();
    }
}
