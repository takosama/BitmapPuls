using System.Runtime.InteropServices;
using BitmapPlus;

int checks = 0;
for (int coordinate = 0; coordinate < 2; coordinate++)
for (int channel = 0; channel < 3; channel++)
{
    int[] xs = new int[2], ys = new int[2];
    byte[] r = new byte[2], g = new byte[2], b = new byte[2];
    Span<byte> alias = MemoryMarshal.AsBytes((coordinate == 0 ? xs : ys).AsSpan()).Slice(4, 2);
    bool rejected = false;
    try
    {
        PixelBufferValidation.ValidateGetPixels(xs, ys,
            channel == 0 ? alias : r, channel == 1 ? alias : g, channel == 2 ? alias : b);
    }
    catch (ArgumentException) { rejected = true; }
    if (!rejected || xs.Any(x => x != 0) || ys.Any(y => y != 0)) throw new Exception("Overlap not rejected before mutation");
    checks++;
}
PixelBufferValidation.ValidateGetPixels(new int[2], new int[2], new byte[2], new byte[2], new byte[2]); checks++;
PixelBufferValidation.ValidateGetPixels([], [], [], [], []); checks++;
// Non-consumed tails may share storage without causing coordinate mutation.
int[] coordinates = new int[4];
Span<byte> tail = MemoryMarshal.AsBytes(coordinates.AsSpan()).Slice(8, 2);
PixelBufferValidation.ValidateGetPixels(coordinates.AsSpan(0, 2), new int[2], tail, new byte[2], new byte[2]); checks++;
for (int shortBuffer = 0; shortBuffer < 4; shortBuffer++)
{
    bool rejected = false;
    try { PixelBufferValidation.ValidateGetPixels(new int[2], new int[shortBuffer == 0 ? 1 : 2],
        new byte[shortBuffer == 1 ? 1 : 2], new byte[shortBuffer == 2 ? 1 : 2], new byte[shortBuffer == 3 ? 1 : 2]); }
    catch (ArgumentException) { rejected = true; }
    if (!rejected) throw new Exception("Short buffer accepted");
    checks++;
}
Console.WriteLine($"PASS {checks} buffer validation cases (production helper; no GDI+ rendering).");
