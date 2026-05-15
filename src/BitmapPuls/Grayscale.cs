using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BitmapPuls;

/// <summary>
/// Grayscale conversion implementations that demonstrate the different BitmapPlus access patterns.
/// Formula: Y = (77*R + 150*G + 29*B) >> 8  (integer approximation, coefficients sum to 256).
/// </summary>
public static class Grayscale
{
    private const int CR = 77, CG = 150, CB = 29;

    // -------------------------------------------------------------------------
    // 1. Safe single-pixel API  — simplest, slowest (bounds + lock check every pixel)
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
    // 2. Unchecked single-pixel  — no validation, tight inner loop
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
    // 3. Row-buffer scalar  — GetRow → scalar conversion → SetRow
    //    One LockBits-level copy per row; sequential reads hide latency.
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
    // 4. Row-buffer + AVX2  — GetRow → SIMD inner loop → SetRow
    //    Processes 8 pixels (24 bytes) per AVX2 iteration using integer
    //    multiply-add on deinterleaved channel bytes, scalar tail for remainder.
    // -------------------------------------------------------------------------

    public static unsafe void ConvertAvx2(BitmapPlus bp)
    {
        int w = bp.Width, h = bp.Height;
        // Allocate with 32-byte tail slack so the SIMD loop can over-read safely.
        byte[] buf = new byte[w * 3 + 32];
        for (int y = 0; y < h; y++)
        {
            bp.GetRow(y, buf.AsSpan(0, w * 3));
            fixed (byte* p = buf)
                ProcessRowAvx2(p, w);
            bp.SetRow(y, buf.AsSpan(0, w * 3));
        }
    }

    // Processes `width` BGR pixels starting at `row`.
    // AVX2 path: 8 pixels per iteration via deinterleaved uint16 multiply-add.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ProcessRowAvx2(byte* row, int width)
    {
        int i = 0;

        if (Avx2.IsSupported)
        {
            // Shuffle masks (applied per 128-bit lane) to deinterleave
            // 8 BGR pixels (24 bytes across two 128-bit lanes) into separate channels.
            //
            // Lane 0 byte layout (pixels 0-4 + B5):
            //   [B0,G0,R0, B1,G1,R1, B2,G2,R2, B3,G3,R3, B4,G4,R4, B5]
            // Lane 1 byte layout (G5,R5, pixels 6-7):
            //   [G5,R5, B6,G6,R6, B7,G7,R7, 0,0,0,0,0,0,0,0]
            //
            // Each shuffle zero-masks (0x80) positions it doesn't extract,
            // then OR-ing the two results combines all 8 values per channel
            // into the first 8 bytes of the 256-bit register.

            // B channel: lane0[0,3,6,9,12,15], lane1[2]→pos6, lane1[5]→pos7
            var bMask0 = Vector256.Create(
                (byte)0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                      0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            var bMask1 = Vector256.Create(
                (byte)0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                      2, 5, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

            // G channel: lane0[1,4,7,10,13], lane1[0,3,6]
            var gMask0 = Vector256.Create(
                (byte)1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                      0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            var gMask1 = Vector256.Create(
                (byte)0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                      0, 3, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

            // R channel: lane0[2,5,8,11,14], lane1[1,4,7]
            var rMask0 = Vector256.Create(
                (byte)2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                      0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            var rMask1 = Vector256.Create(
                (byte)0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
                      1, 4, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

            // Coefficient vectors (uint16 for MultiplyLow)
            var vcB = Vector256.Create((ushort)CB);
            var vcG = Vector256.Create((ushort)CG);
            var vcR = Vector256.Create((ushort)CR);

            // Write-back shuffle: replicate each of 8 gray bytes to 3 positions.
            // gray[k] → positions k*3, k*3+1, k*3+2 within each 128-bit lane.
            // We write 8 pixels in two passes (4 per lane) to stay within lane boundaries.
            var toRgbLane = Vector128.Create(
                (byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 0x80, 0x80, 0x80, 0x80);

            for (; i + 8 <= width; i += 8)
            {
                // Load 24 bytes (8 pixels) into a 256-bit register.
                // The upper 8 bytes of lane 1 are garbage but masked away.
                var data = Unsafe.ReadUnaligned<Vector256<byte>>(row + i * 3);

                // Deinterleave channels to bytes 0-7 of the result vector.
                var b8 = Avx2.Or(Avx2.Shuffle(data, bMask0), Avx2.Shuffle(data, bMask1));
                var g8 = Avx2.Or(Avx2.Shuffle(data, gMask0), Avx2.Shuffle(data, gMask1));
                var r8 = Avx2.Or(Avx2.Shuffle(data, rMask0), Avx2.Shuffle(data, rMask1));

                // Expand first 8 bytes of each to uint16 (use lower 128-bit lane).
                var b16 = Avx2.ConvertToVector256Int16(b8.GetLower()).AsUInt16();
                var g16 = Avx2.ConvertToVector256Int16(g8.GetLower()).AsUInt16();
                var r16 = Avx2.ConvertToVector256Int16(r8.GetLower()).AsUInt16();

                // Weighted sum, shift >> 8 → 8 gray values as uint16.
                var gray16 = Avx2.ShiftRightLogical(
                    Avx2.Add(Avx2.Add(
                        Avx2.MultiplyLow(b16, vcB),
                        Avx2.MultiplyLow(g16, vcG)),
                        Avx2.MultiplyLow(r16, vcR)), 8);

                // Pack uint16 → uint8 (saturate), gives 8 gray bytes in lower 8 of each lane.
                var gray8 = Avx2.PackUnsignedSaturate(gray16.AsInt16(), gray16.AsInt16());
                // After PackUnsignedSaturate: [g0..g7, g0..g7 | g0..g7, g0..g7]
                // We need the first 8 bytes of lane 0.
                var grayLane = gray8.GetLower(); // 16 bytes, first 8 are our gray values

                // Replicate each gray byte to BGR triple and store 24 bytes.
                // Process 4 pixels per 128-bit shuffle to stay within lane.
                var lo4 = Sse2.Shuffle(grayLane.AsInt32(), 0b01_00_01_00).AsByte(); // duplicate low 4 grays
                var hi4 = Sse2.Shuffle(grayLane.AsInt32(), 0b11_10_11_10).AsByte();
                var out0 = Ssse3.Shuffle(lo4, toRgbLane); // pixels 0-3 → 12 bytes
                var out1 = Ssse3.Shuffle(hi4, toRgbLane); // pixels 4-7 → 12 bytes

                Unsafe.WriteUnaligned(row + i * 3,      out0);
                Unsafe.WriteUnaligned(row + i * 3 + 12, out1);
            }
        }

        // Scalar tail
        for (; i < width; i++)
        {
            byte* p = row + i * 3;
            byte gray = (byte)((CB * p[0] + CG * p[1] + CR * p[2]) >> 8);
            p[0] = p[1] = p[2] = gray;
        }
    }
}
