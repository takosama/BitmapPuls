using System;
using System.Runtime.InteropServices;

namespace BitmapPlus;

// Separate from GDI+ so the safety contract can be regression-tested on every OS.
internal static class PixelBufferValidation
{
    internal static void ValidateGetPixels(
        ReadOnlySpan<int> xs, ReadOnlySpan<int> ys,
        Span<byte> rs, Span<byte> gs, Span<byte> bs)
    {
        int count = xs.Length;
        if (ys.Length < count || rs.Length < count || gs.Length < count || bs.Length < count)
            throw new ArgumentException("All spans must have at least xs.Length elements.");

        // int coordinates may alias byte outputs through MemoryMarshal.AsBytes.
        // Only consumed prefixes matter; reject before touching any pixel/output.
        ReadOnlySpan<byte> xBytes = MemoryMarshal.AsBytes(xs);
        ReadOnlySpan<byte> yBytes = MemoryMarshal.AsBytes(ys[..count]);
        if (xBytes.Overlaps(rs[..count]) || xBytes.Overlaps(gs[..count]) || xBytes.Overlaps(bs[..count]) ||
            yBytes.Overlaps(rs[..count]) || yBytes.Overlaps(gs[..count]) || yBytes.Overlaps(bs[..count]))
            throw new ArgumentException("Pixel outputs must not overlap coordinate inputs.");
    }
}
