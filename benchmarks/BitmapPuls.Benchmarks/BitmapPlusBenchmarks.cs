using System.Drawing;
using System.Drawing.Imaging;
using BenchmarkDotNet.Attributes;

namespace BitmapPuls.Benchmarks;

[MemoryDiagnoser]
public class BitmapPlusBenchmarks
{
    [Params(512, 1920)]
    public int Width;

    [Params(512, 1080)]
    public int Height;

    private Bitmap _bitmap = null!;
    private BitmapPlus _sut = null!;
    private byte[] _rowBuffer = null!;
    private int[] _batchXs = null!;
    private int[] _batchYs = null!;
    private byte[] _batchRs = null!, _batchGs = null!, _batchBs = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        _sut = new BitmapPlus(_bitmap);
        _sut.BeginAccess();
        _rowBuffer = new byte[Width * 3];

        // 256 random pixel coordinates for batch benchmarks
        var rng = new Random(42);
        const int N = 256;
        _batchXs = new int[N];
        _batchYs = new int[N];
        _batchRs = new byte[N];
        _batchGs = new byte[N];
        _batchBs = new byte[N];
        for (int i = 0; i < N; i++)
        {
            _batchXs[i] = rng.Next(Width);
            _batchYs[i] = rng.Next(Height);
            _batchRs[i] = (byte)rng.Next(256);
            _batchGs[i] = (byte)rng.Next(256);
            _batchBs[i] = (byte)rng.Next(256);
        }
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

    // --- Unchecked single-pixel ---

    [Benchmark]
    public void GetPixelUnchecked_Center()
    {
        byte r = 0, g = 0, b = 0;
        _sut.GetPixelUnchecked(Width / 2, Height / 2, ref r, ref g, ref b);
    }

    [Benchmark]
    public void SetPixelUnchecked_Center()
    {
        _sut.SetPixelUnchecked(Width / 2, Height / 2, 128, 64, 32);
    }

    // --- Batch random access (256 random coordinates) ---

    [Benchmark]
    public void GetPixels_256Random()
    {
        _sut.GetPixels(_batchXs, _batchYs, _batchRs, _batchGs, _batchBs);
    }

    [Benchmark]
    public void SetPixels_256Random()
    {
        _sut.SetPixels(_batchXs, _batchYs, _batchRs, _batchGs, _batchBs);
    }

    // --- Grayscale conversion ---

    [Benchmark]
    public void Grayscale_Safe()
    {
        Grayscale.ConvertSafe(_sut);
    }

    [Benchmark]
    public void Grayscale_Unchecked()
    {
        Grayscale.ConvertUnchecked(_sut);
    }

    [Benchmark]
    public void Grayscale_ViaRows()
    {
        Grayscale.ConvertViaRows(_sut);
    }

    [Benchmark]
    public void Grayscale_Avx2()
    {
        Grayscale.ConvertAvx2(_sut);
    }
}
