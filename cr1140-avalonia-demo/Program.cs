using System;
using Avalonia;
using Avalonia.LinuxFramebuffer;
using Cr1140.AvaloniaDemo.Input;

namespace Cr1140.AvaloniaDemo;

internal static class Program
{
    public static EvdevKeypadInput Keypad = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        // Pick device node from first non-option arg, or default
        var deviceNode = (args.Length > 0 && !args[0].StartsWith("--"))
            ? args[0]
            : "/dev/input/event1";

        Keypad = new EvdevKeypadInput(deviceNode);

        return BuildAvaloniaApp().StartLinuxFbDev(
            args,
            Environment.GetEnvironmentVariable("FRAMEBUFFER") ?? "/dev/fb0",
            1.0,
            Keypad
        );
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
