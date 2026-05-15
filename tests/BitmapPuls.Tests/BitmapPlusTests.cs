using System;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace BitmapPuls.Tests;

public sealed class BitmapPlusTests : IDisposable
{
    private readonly Bitmap _bitmap;

    public BitmapPlusTests()
    {
        _bitmap = new Bitmap(4, 4, PixelFormat.Format24bppRgb);
    }

    [Fact]
    public void BeginAccess_Throws_WhenAlreadyLocked()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        var exception = Assert.Throws<InvalidOperationException>(() => sut.BeginAccess());
        Assert.Contains("already locked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPixel_ReturnsValuesWrittenBySetPixel()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();
        sut.SetPixel(2, 1, 10, 20, 30);

        byte r = 0, g = 0, b = 0;
        sut.GetPixel(2, 1, ref r, ref g, ref b);

        Assert.Equal(10, r);
        Assert.Equal(20, g);
        Assert.Equal(30, b);
    }

    [Fact]
    public void GetPixel_Throws_ForOutOfRangeCoordinates()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        byte r = 0, g = 0, b = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetPixel(-1, 0, ref r, ref g, ref b));
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetPixel(0, 4, ref r, ref g, ref b));
    }

    [Fact]
    public void SetPixel_Throws_ForOutOfRangeCoordinates()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        Assert.Throws<ArgumentOutOfRangeException>(() => sut.SetPixel(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.SetPixel(0, 4, 0, 0, 0));
    }

    [Fact]
    public void Constructor_Throws_ForNonFormat24bppRgbBitmap()
    {
        using var bitmap32 = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        Assert.Throws<ArgumentException>(() => new BitmapPlus(bitmap32));
    }

    [Fact]
    public void AfterDispose_BeginAccess_Throws()
    {
        var sut = new BitmapPlus(_bitmap);
        sut.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sut.BeginAccess());
    }

    [Fact]
    public void AfterDispose_GetSetPixel_Throws()
    {
        var sut = new BitmapPlus(_bitmap);
        sut.Dispose();

        byte r = 0, g = 0, b = 0;
        Assert.Throws<ObjectDisposedException>(() => sut.GetPixel(0, 0, ref r, ref g, ref b));
        Assert.Throws<ObjectDisposedException>(() => sut.SetPixel(0, 0, 0, 0, 0));
    }

    [Fact]
    public void EndAccess_IsSafe_WhenNotLocked()
    {
        using var sut = new BitmapPlus(_bitmap);

        sut.EndAccess();
    }

    [Fact]
    public void Dispose_ReleasesLock()
    {
        var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();
        sut.Dispose();

        Assert.False(sut.IsLocked);
    }

    [Fact]
    public void SetRow_GetRow_RoundTrip()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        // BGR layout: [B, G, R] per pixel
        byte[] written = new byte[4 * 3];
        for (int x = 0; x < 4; x++)
        {
            written[x * 3]     = (byte)(x * 10);
            written[x * 3 + 1] = (byte)(x * 10 + 1);
            written[x * 3 + 2] = (byte)(x * 10 + 2);
        }

        sut.SetRow(2, written);

        byte[] read = new byte[4 * 3];
        sut.GetRow(2, read);

        Assert.Equal(written, read);
    }

    [Fact]
    public void SetRow_GetRow_DoNotAffectOtherRows()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        byte[] row = new byte[4 * 3];
        Array.Fill(row, (byte)0xFF);
        sut.SetRow(1, row);

        byte[] otherRow = new byte[4 * 3];
        sut.GetRow(0, otherRow);

        Assert.All(otherRow, b => Assert.Equal(0, b));
    }

    [Fact]
    public void GetRow_Throws_ForOutOfRangeY()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        byte[] buf = new byte[4 * 3];
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetRow(-1, buf));
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetRow(4, buf));
    }

    [Fact]
    public void SetRow_Throws_ForOutOfRangeY()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        byte[] buf = new byte[4 * 3];
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.SetRow(-1, buf));
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.SetRow(4, buf));
    }

    [Fact]
    public void Fill_SetsAllPixelsToGivenColor()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();
        sut.Fill(255, 128, 64);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                byte r = 0, g = 0, b = 0;
                sut.GetPixel(x, y, ref r, ref g, ref b);
                Assert.Equal(255, r);
                Assert.Equal(128, g);
                Assert.Equal(64, b);
            }
        }
    }

    [Fact]
    public void GetPixelUnchecked_ReturnsValuesWrittenBySetPixelUnchecked()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();
        sut.SetPixelUnchecked(1, 2, 11, 22, 33);

        byte r = 0, g = 0, b = 0;
        sut.GetPixelUnchecked(1, 2, ref r, ref g, ref b);

        Assert.Equal(11, r);
        Assert.Equal(22, g);
        Assert.Equal(33, b);
    }

    [Fact]
    public void GetPixels_ReturnsValuesWrittenBySetPixels()
    {
        using var sut = new BitmapPlus(_bitmap);
        sut.BeginAccess();

        int[] xs  = new[] { 0, 1, 2, 3, 0, 1, 2, 3, 0 };
        int[] ys  = new[] { 0, 0, 0, 0, 1, 1, 1, 1, 2 };
        byte[] wrs = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
        byte[] wgs = new byte[] { 11, 21, 31, 41, 51, 61, 71, 81, 91 };
        byte[] wbs = new byte[] { 12, 22, 32, 42, 52, 62, 72, 82, 92 };

        sut.SetPixels(xs, ys, wrs, wgs, wbs);

        byte[] rs = new byte[xs.Length];
        byte[] gs = new byte[xs.Length];
        byte[] bs = new byte[xs.Length];
        sut.GetPixels(xs, ys, rs, gs, bs);

        Assert.Equal(wrs, rs);
        Assert.Equal(wgs, gs);
        Assert.Equal(wbs, bs);
    }

    public void Dispose()
    {
        _bitmap.Dispose();
    }
}
