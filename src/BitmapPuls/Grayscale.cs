using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BitmapPuls;

/// <summary>
/// Grayscale conversion examples demonstrating BitmapPlus access patterns.
/// Formula: Y = (77*R + 150*G + 29*B) >> 8  (integer approximation, coefficients sum to 256).
/// </summary>
public static class Grayscale
{
    private const int CR = 77, CG = 150, CB = 29;

    // -------------------------------------------------------------------------
    // 1. Safe single-pixel API — simplest, slowest (bounds + lock check per pixel)
    // -------------------------------------------------------------------------

    public static void ConvertSafe(BitmapPlus bp)
    {
        int w = bp.Width, h = bp.Height;
        byte r = 0, g = 0, b = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bp.GetPixel(x, y, ref r, ref g, ref b);
                byte gray = (byte)((CR * r + CG * g + CB * b) >> 8);
                bp.SetPixel(x, y, gray, gray, gray);
            }
    }

    // -------------------------------------------------------------------------
    // 2. Unchecked single-pixel — no validation overhead in inner loop
    // -------------------------------------------------------------------------

    public static void ConvertUnchecked(BitmapPlus bp)
    {
        int w = bp.Width, h = bp.Height;
        byte r = 0, g = 0, b = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bp.GetPixelUnchecked(x, y, ref r, ref g, ref b);
                byte gray = (byte)((CR * r + CG * g + CB * b) >> 8);
                bp.SetPixelUnchecked(x, y, gray, gray, gray);
            }
    }

    // -------------------------------------------------------------------------
    // 3. Row-buffer scalar — GetRow → scalar inner loop → SetRow
    //    Sequential access; all row data already in L1/L2 cache after GetRow.
    // -------------------------------------------------------------------------

    public static void ConvertViaRows(BitmapPlus bp)
    {
        int w = bp.Width, h = bp.Height;
        byte[] buf = new byte[w * 3];
        for (int y = 0; y < h; y++)
        {
            bp.GetRow(y, buf);
            for (int i = 0; i < w * 3; i += 3)
            {
                byte gray = (byte)((CB * buf[i] + CG * buf[i + 1] + CR * buf[i + 2]) >> 8);
                buf[i] = buf[i + 1] = buf[i + 2] = gray;
            }
            bp.SetRow(y, buf);
        }
    }

    // -------------------------------------------------------------------------
    // 4. Row-buffer + SIMD (SSSE3 + SSE4.1)
    //    GetRow → 4 pixels/iteration with deinterleaved uint16 multiply-add → SetRow.
    //
    //    Per iteration:
    //      Load 16 bytes (12 valid BGR bytes for 4 pixels + up to 4 garbage bytes)
    //      vpshufb  → extract B0-B3, G0-G3, R0-R3 into first 4 bytes each
    //      pmovzxbw → zero-extend to uint16 (SSE4.1)
    //      pmullw   → multiply by coefficient vectors (CB, CG, CR)
    //      paddw    → sum, psrlw >>8 → 4 gray uint16 values
    //      packuswb → pack to bytes
    //      vpshufb  → replicate each gray byte to 3 BGR positions → 12 output bytes
    //      Write 12 bytes as one 64-bit + one 32-bit store (no overrun into pixel i+4)
    // -------------------------------------------------------------------------

    public static unsafe void ConvertSimd(BitmapPlus bp)
    {
        int w = bp.Width, h = bp.Height;
        // 16-byte read slack: last iteration reads from (w-4)*3, needs 16 bytes,
        // so needs up to (w-4)*3+15 = w*3+3 bytes — allocate with headroom.
        byte[] buf = new byte[w * 3 + 16];
        for (int y = 0; y < h; y++)
        {
            bp.GetRow(y, buf.AsSpan(0, w * 3));
            fixed (byte* p = buf)
                ProcessRow(p, w);
            bp.SetRow(y, buf.AsSpan(0, w * 3));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ProcessRow(byte* row, int width)
    {
        int i = 0;

        if (Ssse3.IsSupported && Sse41.IsSupported)
        {
            // Deinterleave masks: extract positions 0,3,6,9 (B), 1,4,7,10 (G), 2,5,8,11 (R)
            // from a 16-byte load covering 4 BGR pixels (12 bytes) + up to 4 garbage bytes.
            var bShuf = Vector128.Create((byte)0, 3, 6, 9, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            var gShuf = Vector128.Create((byte)1, 4, 7, 10, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            var rShuf = Vector128.Create((byte)2, 5, 8, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

            // Write-back mask: gray[k] → bytes k*3, k*3+1, k*3+2; last 4 bytes zeroed.
            // Bytes 0-11 of gray8 = [g0,g1,g2,g3, g0,g1,g2,g3, ...]
            // outShuf reads: g0→pos0,1,2  g1→pos3,4,5  g2→pos6,7,8  g3→pos9,10,11
            var outShuf = Vector128.Create((byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 0x80, 0x80, 0x80, 0x80);

            var vcB = Vector128.Create((ushort)CB);
            var vcG = Vector128.Create((ushort)CG);
            var vcR = Vector128.Create((ushort)CR);

            for (; i + 4 <= width; i += 4)
            {
                // Load 16 bytes (only first 12 are valid pixels; slack in buf absorbs the rest).
                var pix = Unsafe.ReadUnaligned<Vector128<byte>>(row + i * 3);

                // Extract channels; pmovzxbw zero-extends the 4 extracted bytes to uint16.
                var b16 = Sse41.ConvertToVector128Int16(Ssse3.Shuffle(pix, bShuf)).AsUInt16();
                var g16 = Sse41.ConvertToVector128Int16(Ssse3.Shuffle(pix, gShuf)).AsUInt16();
                var r16 = Sse41.ConvertToVector128Int16(Ssse3.Shuffle(pix, rShuf)).AsUInt16();

                // gray[k] = (CB*B[k] + CG*G[k] + CR*R[k]) >> 8
                var gray16 = Sse2.ShiftRightLogical(
                    Sse2.Add(Sse2.Add(
                        Sse2.MultiplyLow(b16, vcB),
                        Sse2.MultiplyLow(g16, vcG)),
                        Sse2.MultiplyLow(r16, vcR)), 8);

                // packuswb: [g0,g1,g2,g3, g0,g1,g2,g3, ...]  (passed twice → same halves)
                var gray8 = Sse2.PackUnsignedSaturate(gray16.AsInt16(), gray16.AsInt16());

                // Replicate each gray byte to BGR triple → 12 valid + 4 zero bytes.
                var out12 = Ssse3.Shuffle(gray8, outShuf);

                // Write exactly 12 bytes: one 8-byte store + one 4-byte store.
                // Using GetElement avoids a 16-byte write that would corrupt pixel i+4.
                Unsafe.WriteUnaligned(row + i * 3,     out12.AsUInt64().GetElement(0));
                Unsafe.WriteUnaligned(row + i * 3 + 8, out12.AsUInt32().GetElement(2));
            }
        }

        // Scalar tail (also full fallback when SSSE3/SSE4.1 unavailable)
        for (; i < width; i++)
        {
            byte* p = row + i * 3;
            byte gray = (byte)((CB * p[0] + CG * p[1] + CR * p[2]) >> 8);
            p[0] = p[1] = p[2] = gray;
        }
    }
}
