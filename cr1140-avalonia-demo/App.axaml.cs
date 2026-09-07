using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cr1140.AvaloniaDemo.ViewModels;
using Cr1140.AvaloniaDemo.Views;
using Cr1140.Avalonia.Emulator;
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
        var mainView = new MainView { DataContext = vm };

        switch (ApplicationLifetime)
        {
            // Desktop emulator: host the app inside the CR1140 device bezel window
            // (keyboard + on-screen keypad, live status LED / keypad backlight / dimming).
            case IClassicDesktopStyleApplicationLifetime desktop:
                // Caption each on-screen keypad button with the live soft-key it triggers,
                // so the emulator keys follow the footer layout as it toggles.
                Program.EmulatorOptions!.KeyCaptions = vm.CaptionForKey;
                desktop.MainWindow = Cr1140Emulator.BuildWindow(
                    mainView, Program.EmulatorKeypad!, Program.EmulatorDevice!, Program.EmulatorOptions);
                break;

            // On-device: embedded single-view over the LinuxFramebuffer/DRM surface.
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = mainView;
                break;
        }

        // Mirror the CODESYS liveness model without CODESYS: sd_notify READY=1 plus a
        // UI-thread WATCHDOG=1 heartbeat (unit is Type=notify + WatchdogSec=). No-op off systemd.
        _watchdog = new SystemdWatchdog();
        _watchdog.Start();

        base.OnFrameworkInitializationCompleted();
    }
}
