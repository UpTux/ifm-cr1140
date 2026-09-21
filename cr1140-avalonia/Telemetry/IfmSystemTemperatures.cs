// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// Reads the CR1102's on-board temperature sensors over the <c>com.ifm.Io.Temperature</c>
/// D-Bus interface (the <c>ifm_service_io</c> daemon) — the same source the CODESYS
/// <c>ifmDevice_ecomatDisplay</c> library surfaces as <c>rCore0</c> (processor core) and
/// <c>rBoard</c> (mainboard).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the CR1140/CR1141 (SoC via <c>/sys/class/thermal/thermal_zone0</c>, board via an
/// <c>lm75</c> hwmon sensor), the CR1102's ZynqMP exposes <b>no</b> thermal-zone or hwmon
/// temperature node — its core and mainboard temperatures are only available from the IO MCU
/// over D-Bus. Confirmed on a live CR1102 (2026-09-21): <c>GetTemperatureCore0</c> ≈ 42 °C,
/// <c>GetTemperatureBoard</c> ≈ 39 °C (both signed integers in whole °C).
/// </para>
/// <para>
/// Every call is fail-soft: it invokes the platform <c>gdbus</c> CLI on the system bus and
/// returns <see langword="null"/> off-device (no <c>gdbus</c> on <c>PATH</c>, no system bus,
/// or no service) — the same contract as <see cref="Cr1140.Avalonia.Leds.IfmKeyboardLeds"/>
/// and <see cref="ProcFs"/>. Each read spawns a short-lived <c>gdbus</c> process; poll at a
/// modest cadence (the CODESYS equivalent updates every 2000 ms).
/// </para>
/// </remarks>
public sealed class IfmSystemTemperatures
{
    private const string Service = "com.ifm.Io.Temperature";
    private const string ObjectPath = "/com/ifm/Io/Temperature";
    private const string Interface = "com.ifm.Io.Temperature";

    /// <summary>Processor-core temperature in whole °C (<c>rCore0</c>), or <see langword="null"/> if unavailable.</summary>
    public int? Core0() => Read("GetTemperatureCore0");

    /// <summary>Mainboard temperature in whole °C (<c>rBoard</c>), or <see langword="null"/> if unavailable.</summary>
    public int? Board() => Read("GetTemperatureBoard");

    private static int? Read(string method)
    {
        var (ok, output) = Run(method);
        if (!ok)
            return null;

        // gdbus prints e.g. "(42,)" or "(-5,)"; take the first signed integer.
        var m = Regex.Match(output, @"-?\d+");
        return m.Success && int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static (bool ok, string output) Run(string method)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gdbus",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("call");
            psi.ArgumentList.Add("--system");
            psi.ArgumentList.Add("--dest");
            psi.ArgumentList.Add(Service);
            psi.ArgumentList.Add("--object-path");
            psi.ArgumentList.Add(ObjectPath);
            psi.ArgumentList.Add("--method");
            psi.ArgumentList.Add(Interface + "." + method);

            using var p = Process.Start(psi);
            if (p is null)
                return (false, string.Empty);

            string stdout = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return (false, string.Empty);
            }

            return (p.ExitCode == 0, stdout);
        }
        catch
        {
            // gdbus missing / not permitted / no bus — treat as off-device no-op.
            return (false, string.Empty);
        }
    }
}
