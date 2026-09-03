namespace Cr1140.Avalonia.Output;

/// <summary>
/// The clockwise rotation applied to the rendered image before it is written to the
/// framebuffer, so a CR1140/CR1141 panel can be physically mounted in any of the four
/// orientations and still show an upright UI.
/// </summary>
/// <remarks>
/// Pick the value that makes the UI appear upright for how the display is mounted.
/// <see cref="Clockwise90"/> and <see cref="Clockwise270"/> swap the logical resolution
/// Avalonia lays out against (the native 800×480 landscape framebuffer becomes a 480×800
/// portrait surface). The enum values are the clockwise angle in degrees.
/// </remarks>
public enum DisplayRotation
{
    /// <summary>No rotation — render in the framebuffer's native landscape orientation.</summary>
    None = 0,

    /// <summary>Rotate the rendered image 90° clockwise (portrait); logical size is swapped.</summary>
    Clockwise90 = 90,

    /// <summary>Rotate the rendered image 180° (upside-down landscape).</summary>
    Clockwise180 = 180,

    /// <summary>Rotate the rendered image 270° clockwise / 90° counter-clockwise (portrait); logical size is swapped.</summary>
    Clockwise270 = 270
}
