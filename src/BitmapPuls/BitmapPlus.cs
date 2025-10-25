using System;
using System.Drawing;
using System.Drawing.Imaging;

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
        _stride = _bitmapData.Stride;
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
    }
}
