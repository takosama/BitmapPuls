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

    public void Dispose()
    {
        _bitmap.Dispose();
    }
}
