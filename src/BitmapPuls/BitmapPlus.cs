using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BitmapPuls;

/// <summary>
/// Provides high-performance, safe pixel access to a 24bpp bitmap by locking
/// the bitmap memory and manipulating it via pointers.
/// </summary>
public sealed class BitmapPlus : IDisposable
{
    private readonly Bitmap _bitmap;
    private BitmapData? _bitmapData;
    private IntPtr _scan0 = IntPtr.Zero;
    private int _stride;
    private bool _disposed;

    public BitmapPlus(Bitmap bitmap)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        if (_bitmap.PixelFormat != PixelFormat.Format24bppRgb)
        {
            throw new ArgumentException(
                $"Bitmap must use Format24bppRgb, but got {_bitmap.PixelFormat}.",
                nameof(bitmap));
        }
    }

    /// <summary>
    /// Gets a value indicating whether the bitmap memory is currently locked.
    /// </summary>
    public bool IsLocked => _bitmapData is not null;

    /// <summary>
    /// Locks the bitmap to enable pointer-based access.
    /// </summary>
    /// <param name="lockMode">The access mode to use when locking the bitmap.</param>
    /// <exception cref="InvalidOperationException">Thrown when the bitmap is already locked.</exception>
    public void BeginAccess(ImageLockMode lockMode = ImageLockMode.ReadWrite)
    {
        ThrowIfDisposed();

        if (IsLocked)
        {
            throw new InvalidOperationException("The bitmap is already locked for access.");
        }

        _bitmapData = _bitmap.LockBits(
            new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
            lockMode,
            PixelFormat.Format24bppRgb);
        _scan0 = _bitmapData.Scan0;
        _stride = Math.Abs(_bitmapData.Stride);
    }

    /// <summary>
    /// Unlocks the bitmap if it is currently locked.
    /// </summary>
    public void EndAccess()
    {
        if (_bitmapData is not null)
        {
            _bitmap.UnlockBits(_bitmapData);
            _bitmapData = null;
            _scan0 = IntPtr.Zero;
            _stride = 0;
        }
    }

    /// <summary>
    /// Reads the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The horizontal pixel coordinate.</param>
    /// <param name="y">The vertical pixel coordinate.</param>
    /// <param name="r">Receives the red component.</param>
    /// <param name="g">Receives the green component.</param>
    /// <param name="b">Receives the blue component.</param>
    public unsafe void GetPixel(int x, int y, ref byte r, ref byte g, ref byte b)
    {
        ThrowIfDisposed();
        EnsureLocked();
        ValidateCoordinates(x, y);

        byte* basePtr = (byte*)_scan0;
        byte* pixelPtr = basePtr + (y * _stride) + (x * 3);

        b = pixelPtr[0];
        g = pixelPtr[1];
        r = pixelPtr[2];
    }

    /// <summary>
    /// Writes the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The horizontal pixel coordinate.</param>
    /// <param name="y">The vertical pixel coordinate.</param>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public unsafe void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        ThrowIfDisposed();
        EnsureLocked();
        ValidateCoordinates(x, y);

        byte* basePtr = (byte*)_scan0;
        byte* pixelPtr = basePtr + (y * _stride) + (x * 3);

        pixelPtr[0] = b;
        pixelPtr[1] = g;
        pixelPtr[2] = r;
    }

    /// <summary>
    /// Fills the entire bitmap with the specified color.
    /// Uses AVX2 Vector256 SIMD writes when hardware support is available.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public unsafe void Fill(byte r, byte g, byte b)
    {
        ThrowIfDisposed();
        EnsureLocked();

        int width = _bitmap.Width;
        int height = _bitmap.Height;
        byte* basePtr = (byte*)_scan0;
        int rowBytes = width * 3;

        if (Avx2.IsSupported && rowBytes >= 96)
        {
            // 96 bytes = LCM(3, 32): covers 32 complete BGR pixels with no pattern shift between stores
            byte* pattern = stackalloc byte[96];
            for (int i = 0; i < 96; i++)
            {
                pattern[i] = (i % 3) switch { 0 => b, 1 => g, _ => r };
            }

            Vector256<byte> v0 = Unsafe.ReadUnaligned<Vector256<byte>>(pattern);
            Vector256<byte> v1 = Unsafe.ReadUnaligned<Vector256<byte>>(pattern + 32);
            Vector256<byte> v2 = Unsafe.ReadUnaligned<Vector256<byte>>(pattern + 64);

            for (int y = 0; y < height; y++)
            {
                byte* row = basePtr + (y * _stride);
                int i = 0;

                while (i + 96 <= rowBytes)
                {
                    Avx2.Store(row + i,      v0);
                    Avx2.Store(row + i + 32, v1);
                    Avx2.Store(row + i + 64, v2);
                    i += 96;
                }

                while (i < rowBytes)
                {
                    row[i]     = b;
                    row[i + 1] = g;
                    row[i + 2] = r;
                    i += 3;
                }
            }
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                byte* row = basePtr + (y * _stride);
                for (int x = 0; x < width; x++)
                {
                    row[x * 3]     = b;
                    row[x * 3 + 1] = g;
                    row[x * 3 + 2] = r;
                }
            }
        }
    }

    /// <summary>
    /// Reads a row of pixels into <paramref name="bgrBuffer"/> using AVX2 Vector256 loads where available.
    /// The buffer receives raw BGR bytes: index <c>x*3</c>=B, <c>x*3+1</c>=G, <c>x*3+2</c>=R.
    /// </summary>
    /// <param name="y">The row index.</param>
    /// <param name="bgrBuffer">Destination buffer; must hold at least <c>Width * 3</c> bytes.</param>
    public unsafe void GetRow(int y, Span<byte> bgrBuffer)
    {
        ThrowIfDisposed();
        EnsureLocked();
        if ((uint)y >= (uint)_bitmap.Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        int rowBytes = _bitmap.Width * 3;
        if (bgrBuffer.Length < rowBytes)
            throw new ArgumentException($"Buffer must hold at least {rowBytes} bytes.", nameof(bgrBuffer));

        byte* src = (byte*)_scan0 + (y * _stride);

        if (Avx2.IsSupported)
        {
            fixed (byte* dst = bgrBuffer)
            {
                int i = 0;
                while (i + 32 <= rowBytes)
                {
                    Avx2.Store(dst + i, Unsafe.ReadUnaligned<Vector256<byte>>(src + i));
                    i += 32;
                }
                while (i < rowBytes)
                {
                    dst[i] = src[i];
                    i++;
                }
            }
        }
        else
        {
            new Span<byte>(src, rowBytes).CopyTo(bgrBuffer);
        }
    }

    /// <summary>
    /// Writes a row of pixels from <paramref name="bgrBuffer"/> using AVX2 Vector256 stores where available.
    /// The buffer must supply raw BGR bytes: index <c>x*3</c>=B, <c>x*3+1</c>=G, <c>x*3+2</c>=R.
    /// </summary>
    /// <param name="y">The row index.</param>
    /// <param name="bgrBuffer">Source buffer; must hold at least <c>Width * 3</c> bytes.</param>
    public unsafe void SetRow(int y, ReadOnlySpan<byte> bgrBuffer)
    {
        ThrowIfDisposed();
        EnsureLocked();
        if ((uint)y >= (uint)_bitmap.Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        int rowBytes = _bitmap.Width * 3;
        if (bgrBuffer.Length < rowBytes)
            throw new ArgumentException($"Buffer must hold at least {rowBytes} bytes.", nameof(bgrBuffer));

        byte* dst = (byte*)_scan0 + (y * _stride);

        if (Avx2.IsSupported)
        {
            fixed (byte* src = bgrBuffer)
            {
                int i = 0;
                while (i + 32 <= rowBytes)
                {
                    Avx2.Store(dst + i, Unsafe.ReadUnaligned<Vector256<byte>>(src + i));
                    i += 32;
                }
                while (i < rowBytes)
                {
                    dst[i] = src[i];
                    i++;
                }
            }
        }
        else
        {
            bgrBuffer[..rowBytes].CopyTo(new Span<byte>(dst, rowBytes));
        }
    }

    private void EnsureLocked()
    {
        if (!IsLocked)
        {
            throw new InvalidOperationException("BeginAccess must be called before manipulating pixels.");
        }
    }

    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)_bitmap.Width || (uint)y >= (uint)_bitmap.Height)
        {
            throw new ArgumentOutOfRangeException($"Coordinates ({x}, {y}) are outside the bitmap bounds ({_bitmap.Width}, {_bitmap.Height}).");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BitmapPlus));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        EndAccess();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
