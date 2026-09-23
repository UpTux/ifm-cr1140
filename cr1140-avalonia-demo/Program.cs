using Avalonia;
using Cr1140.Avalonia.Diagnostics;
using Cr1140.Avalonia.Emulator;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Output;
using Cr1140.Avalonia.Devices;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.LinuxFramebuffer;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.LinuxFramebuffer.Input.EvDev;

namespace Cr1140.AvaloniaDemo;

internal static class Program
{
    public static DeviceProfile Profile = DeviceProfiles.Cr1140;
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

        // Device profile selection: configure panel dimensions, touch, and keypad defaults.
        // `--device cr1102|cr1140|cr1141` (case-insensitive, with or without "CR" prefix)
        // or CR1140_DEVICE env var. Default is CR1140.
        var deviceName = ParseOption(args, "--device=") ?? Environment.GetEnvironmentVariable("CR1140_DEVICE");
        var profile = DeviceProfiles.ByName(deviceName ?? "") ?? DeviceProfiles.Cr1140;
        Profile = profile;

        // Display rotation lets the panel be mounted in any orientation. Configure via
        // `--rotate=90|180|270` or the CR1140_ROTATE env var; default is no rotation.
        var rotation = ParseRotation(args, Environment.GetEnvironmentVariable("CR1140_ROTATE"));

        // Desktop emulator: run the same app in a device-bezel window on a dev host
        // (physical keyboard + on-screen keypad) for a fast edit/run loop without
        // publishing to the panel. Auto-selected off Linux (macOS/Windows); force it on
        // a Linux desktop with `--emulator` or CR1140_EMULATOR=1. On the device (Linux,
        // no flag) the default stays the real fbdev/DRM output path below.
        var emulator = args.Any(a => a == "--emulator")
            || string.Equals(Environment.GetEnvironmentVariable("CR1140_EMULATOR"), "1", StringComparison.OrdinalIgnoreCase)
            || !OperatingSystem.IsLinux();

        return emulator ? RunEmulator(args, profile, rotation) : RunDevice(args, profile, rotation);
    }

    /// <summary>Desktop emulator entry: the same App hosted in the device bezel window.</summary>
    private static int RunEmulator(string[] args, DeviceProfile profile, DisplayRotation rotation)
    {
        var keypad = new WindowKeypadInput();
        Keypad = keypad;
        EmulatorKeypad = keypad;
        EmulatorDevice = new EmulatedDevice(profile);
        EmulatorOptions = EmulatorOptions.ForDevice(profile);
        EmulatorOptions.Rotation = rotation;

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
    private static int RunDevice(string[] args, DeviceProfile profile, DisplayRotation rotation)
    {
        // Keypad: use an explicit evdev node if passed as the first non-option arg, otherwise
        // auto-discover via the profile (the CR1102 finds its "PDM3 virtual keyboard" nodes;
        // the CR1140/CR1141 use /dev/input/event1).
        var explicitNode = (args.Length > 0 && !args[0].StartsWith("--"))
            ? args[0]
            : null;

        var evdev = explicitNode != null
            ? new EvdevKeypadInput(explicitNode)
            : EvdevKeypadInput.ForDevice(profile);
        Keypad = evdev;

        // Output backend. DRM/KMS (`RotatingDrmOutput`) is the default: tear-free,
        // double-buffered page-flip. With no `--card=` it auto-detects the connected KMS
        // display card (card1 on the CR1102; card0 on the CR1140/CR1141). Force the
        // single-buffered fbdev backend with `--fbdev` or CR1140_OUTPUT=fbdev. If DRM init
        // fails (no device / not master), fall back to fbdev so the panel still comes up.
        var forceFbdev = args.Any(a => a == "--fbdev")
            || string.Equals(Environment.GetEnvironmentVariable("CR1140_OUTPUT"), "fbdev", StringComparison.OrdinalIgnoreCase);
        var fbdev = Environment.GetEnvironmentVariable("FRAMEBUFFER") ?? "/dev/fb0";

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

        // Input backend. On a touch panel (CR1102) compose Avalonia's stock touchscreen
        // backend (EvDevBackend over the PCAP node) with the physical keypad; keypad-only
        // SKUs (CR1140/CR1141) use the keypad backend alone.
        IInputBackend input = evdev;
        if (profile.HasTouch)
        {
            var touchPath = ResolveTouchPath(profile);
            if (touchPath != null)
            {
                var touch = new EvDevBackend(new EvDevDeviceDescription[]
                {
                    new EvDevTouchScreenDeviceDescription { Path = touchPath },
                });
                input = new CompositeInputBackend(touch, evdev);
            }
        }

        // Cap the render/present rate (software Skia => every frame is CPU work). Default 60
        // (Avalonia platform default); lower it via `--fps=24` or CR1140_FPS to free CPU.
        var fps = ParseFps(args, Environment.GetEnvironmentVariable("CR1140_FPS"));
        var app = BuildAvaloniaApp();
        if (fps > 0)
            app = app.With(new LinuxFramebufferPlatformOptions { Fps = fps });

        return app.StartLinuxDirect(args, output, input);
    }

    /// <summary>Resolve the touchscreen evdev node: discover by the profile's device name, else its path.</summary>
    private static string? ResolveTouchPath(DeviceProfile profile)
    {
        if (!string.IsNullOrEmpty(profile.TouchDeviceName))
        {
            var found = EvdevKeypadInput.DiscoverByName(profile.TouchDeviceName);
            if (found.Count > 0)
                return found[0];
        }
        return profile.TouchDevicePath;
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
