using System.Drawing;
using System.Drawing.Imaging;
using BenchmarkDotNet.Attributes;
namespace BitmapPuls.Benchmarks;

[MemoryDiagnoser]
public class BitmapPlusBenchmarks
{
    [Params(64, 512, 1920)]
    public int Width;

    [Params(64, 512, 1080)]
    public int Height;

    private Bitmap _bitmap = null!;
    private BitmapPlus _sut = null!;
    private byte[] _rowBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        _sut = new BitmapPlus(_bitmap);
        _sut.BeginAccess();
        _rowBuffer = new byte[Width * 3];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sut.EndAccess();
        _sut.Dispose();
        _bitmap.Dispose();
    }

    [Benchmark]
    public void GetPixel_Center()
    {
        byte r = 0, g = 0, b = 0;
        _sut.GetPixel(Width / 2, Height / 2, ref r, ref g, ref b);
    }

    [Benchmark]
    public void SetPixel_Center()
    {
        _sut.SetPixel(Width / 2, Height / 2, 128, 64, 32);
    }

    [Benchmark]
    public void GetRow_Middle()
    {
        _sut.GetRow(Height / 2, _rowBuffer);
    }

    [Benchmark]
    public void SetRow_Middle()
    {
        _sut.SetRow(Height / 2, _rowBuffer);
    }

    [Benchmark]
    public void Fill_Entire()
    {
        _sut.Fill(128, 64, 32);
    }

    [Benchmark]
    public void GetPixel_AllPixels()
    {
        byte r = 0, g = 0, b = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _sut.GetPixel(x, y, ref r, ref g, ref b);
    }

    [Benchmark]
    public void SetPixel_AllPixels()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _sut.SetPixel(x, y, 128, 64, 32);
    }

    [Benchmark]
    public void GetRow_AllRows()
    {
        for (int y = 0; y < Height; y++)
            _sut.GetRow(y, _rowBuffer);
    }

    [Benchmark]
    public void SetRow_AllRows()
    {
        for (int y = 0; y < Height; y++)
            _sut.SetRow(y, _rowBuffer);
    }
}
