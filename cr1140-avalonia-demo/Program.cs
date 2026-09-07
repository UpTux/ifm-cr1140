using Avalonia;
using Cr1140.Avalonia.Diagnostics;
using Cr1140.Avalonia.Emulator;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Output;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.LinuxFramebuffer;

namespace Cr1140.AvaloniaDemo;

internal static class Program
{
    public static IKeypadInput Keypad = null!;
    public static FrameStatsRecorder FrameStats = null!;
    public static bool PerfStartVisible;

    // Emulator-mode state (null in device mode); consumed by App to build the bezel window.
    public static WindowKeypadInput? EmulatorKeypad;
    public static EmulatedDevice? EmulatorDevice;
    public static EmulatorOptions? EmulatorOptions;

    [STAThread]
    public static int Main(string[] args)
    {
        FrameStats = new FrameStatsRecorder();
        PerfStartVisible = args.Any(a => a == "--perf")
            || string.Equals(Environment.GetEnvironmentVariable("CR1140_PERF"), "1", StringComparison.OrdinalIgnoreCase);

        // Display rotation lets the panel be mounted in any orientation. Configure via
        // `--rotate=90|180|270` or the CR1140_ROTATE env var; default is no rotation.
        var rotation = ParseRotation(args, Environment.GetEnvironmentVariable("CR1140_ROTATE"));

        // Desktop emulator: run the same app in a CR1140 device-bezel window on a dev
        // host (physical keyboard + on-screen keypad) for a fast edit/run loop without
        // publishing to the panel. Auto-selected off Linux (macOS/Windows); force it on
        // a Linux desktop with `--emulator` or CR1140_EMULATOR=1. On the device (Linux,
        // no flag) the default stays the real fbdev/DRM output path below.
        var emulator = args.Any(a => a == "--emulator")
            || string.Equals(Environment.GetEnvironmentVariable("CR1140_EMULATOR"), "1", StringComparison.OrdinalIgnoreCase)
            || !OperatingSystem.IsLinux();

        return emulator ? RunEmulator(args, rotation) : RunDevice(args, rotation);
    }

    /// <summary>Desktop emulator entry: the same App hosted in the device bezel window.</summary>
    private static int RunEmulator(string[] args, DisplayRotation rotation)
    {
        var keypad = new WindowKeypadInput();
        Keypad = keypad;
        EmulatorKeypad = keypad;
        EmulatorDevice = new EmulatedDevice();
        EmulatorOptions = new EmulatorOptions { Rotation = rotation };

        try
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Restore the redirected sysfs roots and clean up the seeded temp tree.
            EmulatorDevice.Dispose();
            keypad.Dispose();
        }
    }

    /// <summary>On-device entry: evdev keypad + rotating fbdev/DRM output via StartLinuxDirect.</summary>
    private static int RunDevice(string[] args, DisplayRotation rotation)
    {
        // Pick device node from first non-option arg, or default
        var deviceNode = (args.Length > 0 && !args[0].StartsWith("--"))
            ? args[0]
            : "/dev/input/event1";

        // Output backend. DRM/KMS (`RotatingDrmOutput`) is the default: tear-free,
        // double-buffered page-flip on `/dev/dri/card0` (override with `--card=…` /
        // CR1140_CARD). Force the single-buffered fbdev backend with `--fbdev` or
        // CR1140_OUTPUT=fbdev. If DRM init fails (no device / not master), fall back
        // to fbdev so the panel still comes up.
        var forceFbdev = args.Any(a => a == "--fbdev")
            || string.Equals(Environment.GetEnvironmentVariable("CR1140_OUTPUT"), "fbdev", StringComparison.OrdinalIgnoreCase);
        var fbdev = Environment.GetEnvironmentVariable("FRAMEBUFFER") ?? "/dev/fb0";

        var evdev = new EvdevKeypadInput(deviceNode);
        Keypad = evdev;

        IOutputBackend output;
        if (forceFbdev)
        {
            output = new RotatingFbdevOutput(fbdev, rotation, 1.0, FrameStats);
        }
        else
        {
            var card = ParseOption(args, "--card=") ?? Environment.GetEnvironmentVariable("CR1140_CARD");
            try
            {
                output = new RotatingDrmOutput(card, rotation, 1.0, FrameStats);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[cr1140] DRM output unavailable ({ex.Message}); falling back to fbdev '{fbdev}'.");
                output = new RotatingFbdevOutput(fbdev, rotation, 1.0, FrameStats);
            }
        }

        // Cap the render/present rate (software Skia => every frame is CPU work). Default 60
        // (Avalonia platform default); lower it via `--fps=24` or CR1140_FPS to free CPU.
        var fps = ParseFps(args, Environment.GetEnvironmentVariable("CR1140_FPS"));
        var app = BuildAvaloniaApp();
        if (fps > 0)
            app = app.With(new LinuxFramebufferPlatformOptions { Fps = fps });

        return app.StartLinuxDirect(args, output, evdev);
    }

    private static string? ParseOption(string[] args, string flag)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(flag, StringComparison.Ordinal))
                return arg.Substring(flag.Length);
        }
        return null;
    }

    private static DisplayRotation ParseRotation(string[] args, string? env)
    {
        var value = env;
        foreach (var arg in args)
        {
            const string flag = "--rotate=";
            if (arg.StartsWith(flag, StringComparison.Ordinal))
                value = arg.Substring(flag.Length);
        }

        return value switch
        {
            "90" => DisplayRotation.Clockwise90,
            "180" => DisplayRotation.Clockwise180,
            "270" => DisplayRotation.Clockwise270,
            _ => DisplayRotation.None,
        };
    }

    private static int ParseFps(string[] args, string? env)
    {
        var value = ParseOption(args, "--fps=") ?? env;
        return int.TryParse(value, out var fps) && fps > 0 ? fps : 60;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
