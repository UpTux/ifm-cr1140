using System.Runtime.InteropServices;

namespace Cr1140.Avalonia.Output;

/// <summary>
/// Pure, allocation-free pixel rotator: copies a source framebuffer into a destination
/// framebuffer, rotating it by a <see cref="DisplayRotation"/>. No Avalonia dependency,
/// so it is host-testable with plain byte buffers (mirrors the pure <c>CpuSampler</c>/
/// <c>KeyGestureDetector</c> ethos of this package).
/// </summary>
/// <remarks>
/// Whole pixels are moved (no format conversion, no scaling). Both buffers must share the
/// same pixel format; only 32 bpp (4 bytes/pixel) and 16 bpp (2 bytes/pixel) are supported.
/// The destination is the physical framebuffer (its width/height and row stride); the
/// source is the logical, rotated-out image Avalonia rendered. For
/// <see cref="DisplayRotation.Clockwise90"/> / <see cref="DisplayRotation.Clockwise270"/>
/// the source is portrait (its width equals the destination height).
/// </remarks>
public static class FramebufferRotator
{
    /// <summary>Rotate <paramref name="source"/> into <paramref name="destination"/>.</summary>
    /// <param name="source">Source (logical) pixels, row-major, top-left origin.</param>
    /// <param name="sourceStrideBytes">Bytes per source row (may exceed width × bpp).</param>
    /// <param name="destination">Destination (physical framebuffer) pixels.</param>
    /// <param name="destinationStrideBytes">Bytes per destination row (framebuffer line length).</param>
    /// <param name="destinationWidth">Destination width in pixels.</param>
    /// <param name="destinationHeight">Destination height in pixels.</param>
    /// <param name="bytesPerPixel">4 (32 bpp) or 2 (16 bpp).</param>
    /// <param name="rotation">Clockwise rotation to apply.</param>
    public static void Rotate(
        ReadOnlySpan<byte> source, int sourceStrideBytes,
        Span<byte> destination, int destinationStrideBytes,
        int destinationWidth, int destinationHeight,
        int bytesPerPixel, DisplayRotation rotation)
    {
        switch (bytesPerPixel)
        {
            case 4:
                RotateCore(
                    MemoryMarshal.Cast<byte, uint>(source), sourceStrideBytes / 4,
                    MemoryMarshal.Cast<byte, uint>(destination), destinationStrideBytes / 4,
                    destinationWidth, destinationHeight, rotation);
                break;
            case 2:
                RotateCore(
                    MemoryMarshal.Cast<byte, ushort>(source), sourceStrideBytes / 2,
                    MemoryMarshal.Cast<byte, ushort>(destination), destinationStrideBytes / 2,
                    destinationWidth, destinationHeight, rotation);
                break;
            default:
                throw new NotSupportedException($"Unsupported bytes per pixel: {bytesPerPixel}");
        }
    }

    // Strides are in elements of T (pixels). w/h are the destination (physical) dimensions;
    // the source (logical) dimensions are (w, h) for None/180 and (h, w) for the 90/270 cases.
    private static void RotateCore<T>(
        ReadOnlySpan<T> src, int srcStride,
        Span<T> dst, int dstStride,
        int w, int h, DisplayRotation rotation) where T : unmanaged
    {
        switch (rotation)
        {
            case DisplayRotation.None:
                for (int y = 0; y < h; y++)
                    src.Slice(y * srcStride, w).CopyTo(dst.Slice(y * dstStride, w));
                break;

            case DisplayRotation.Clockwise180:
                // P[py][px] = L[h-1-py][w-1-px]
                for (int py = 0; py < h; py++)
                {
                    int drow = py * dstStride;
                    int srow = (h - 1 - py) * srcStride;
                    for (int px = 0; px < w; px++)
                        dst[drow + px] = src[srow + (w - 1 - px)];
                }
                break;

            case DisplayRotation.Clockwise90:
                // Logical is portrait (LW=h, LH=w). P[py][px] = L[w-1-px][py]
                for (int py = 0; py < h; py++)
                {
                    int drow = py * dstStride;
                    for (int px = 0; px < w; px++)
                        dst[drow + px] = src[(w - 1 - px) * srcStride + py];
                }
                break;

            case DisplayRotation.Clockwise270:
                // Logical is portrait (LW=h, LH=w). P[py][px] = L[px][h-1-py]
                for (int py = 0; py < h; py++)
                {
                    int drow = py * dstStride;
                    for (int px = 0; px < w; px++)
                        dst[drow + px] = src[px * srcStride + (h - 1 - py)];
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "Unknown rotation");
        }
    }
}
