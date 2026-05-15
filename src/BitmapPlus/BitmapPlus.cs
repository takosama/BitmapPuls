using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BitmapPlus;

/// <summary>
/// Provides high-performance, safe pixel access to a 24bpp bitmap by locking
/// the bitmap memory and manipulating it via pointers.
/// </summary>
public sealed class BitmapPlus : IDisposable
{
    private static readonly Vector256<int> s_vthree = Vector256.Create(3);

    private readonly Bitmap _bitmap;
    private BitmapData? _bitmapData;
    private nint _ptr;
    private nint _stride;
    private int _width;
    private int _height;
    private bool _disposed;
    private ImageLockMode _lockMode;

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

    /// <summary>Gets a value indicating whether the bitmap memory is currently locked.</summary>
    public bool IsLocked => _bitmapData is not null;

    /// <summary>Gets the bitmap width in pixels.</summary>
    public int Width  { get { ThrowIfDisposed(); return _bitmap.Width; } }

    /// <summary>Gets the bitmap height in pixels.</summary>
    public int Height { get { ThrowIfDisposed(); return _bitmap.Height; } }

    /// <summary>
    /// Locks the bitmap to enable pointer-based access.
    /// </summary>
    /// <param name="lockMode">The access mode to use when locking the bitmap.</param>
    /// <exception cref="InvalidOperationException">Thrown when the bitmap is already locked.</exception>
    public void BeginAccess(ImageLockMode lockMode = ImageLockMode.ReadWrite)
    {
        ThrowIfDisposed();
        if ((lockMode & ImageLockMode.UserInputBuffer) != 0)
            throw new NotSupportedException("UserInputBuffer is not supported.");
        if (IsLocked)
            throw new InvalidOperationException("The bitmap is already locked for access.");

        _bitmapData = _bitmap.LockBits(
            new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
            lockMode,
            PixelFormat.Format24bppRgb);

        _ptr       = _bitmapData.Scan0;
        // Keep the signed stride. GDI+ sets Scan0 so that Scan0 + y*Stride
        // always yields visual row y, even for bottom-up DIBs (negative Stride).
        _stride    = _bitmapData.Stride;
        _width     = _bitmap.Width;
        _height    = _bitmap.Height;
        _lockMode  = lockMode;
    }

    /// <summary>Unlocks the bitmap if it is currently locked.</summary>
    public void EndAccess()
    {
        if (_bitmapData is not null)
        {
            _bitmap.UnlockBits(_bitmapData);
            _bitmapData = null;
            _ptr      = 0;
            _stride   = 0;
            _lockMode = default;
        }
    }

    // -------------------------------------------------------------------------
    // Safe pixel access (bounds + lock checked)
    // -------------------------------------------------------------------------

    /// <summary>Reads the pixel at the specified coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void GetPixel(int x, int y, ref byte r, ref byte g, ref byte b)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureReadable();
        ValidateCoordinates(x, y);

        byte* p = (byte*)_ptr + (nint)y * _stride + (nint)x * 3;
        b = p[0]; g = p[1]; r = p[2];
    }

    /// <summary>Writes the pixel at the specified coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureWritable();
        ValidateCoordinates(x, y);

        byte* p = (byte*)_ptr + (nint)y * _stride + (nint)x * 3;
        p[0] = b; p[1] = g; p[2] = r;
    }

    // -------------------------------------------------------------------------
    // Unchecked pixel access — skip all validation for trusted inner loops
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads the pixel without bounds or lock checks.
    /// The caller must ensure BeginAccess has been called and coordinates are in range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void GetPixelUnchecked(int x, int y, ref byte r, ref byte g, ref byte b)
    {
        byte* p = (byte*)_ptr + (nint)y * _stride + (nint)x * 3;
        b = p[0]; g = p[1]; r = p[2];
    }

    /// <summary>
    /// Writes the pixel without bounds or lock checks.
    /// The caller must ensure BeginAccess has been called and coordinates are in range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void SetPixelUnchecked(int x, int y, byte r, byte g, byte b)
    {
        byte* p = (byte*)_ptr + (nint)y * _stride + (nint)x * 3;
        p[0] = b; p[1] = g; p[2] = r;
    }

    // -------------------------------------------------------------------------
    // Batch random access — AVX2 gather (GetPixels) / unchecked scatter (SetPixels)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads N pixels at arbitrary coordinates using AVX2 GatherVector256 where available.
    /// 8 memory accesses are issued in parallel per SIMD iteration, hiding random-access latency.
    /// All spans must have at least <c>xs.Length</c> elements.
    /// </summary>
    public unsafe void GetPixels(
        ReadOnlySpan<int> xs, ReadOnlySpan<int> ys,
        Span<byte> rs, Span<byte> gs, Span<byte> bs)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureReadable();

        int count = xs.Length;
        if (ys.Length < count || rs.Length < count || gs.Length < count || bs.Length < count)
            throw new ArgumentException("All spans must have at least xs.Length elements.");

        for (int k = 0; k < count; k++)
            ValidateCoordinates(xs[k], ys[k]);

        // GatherVector256 reads 4 bytes per pixel to extract BGR.
        // When |stride| == width*3 (no row padding), the 4th byte of the last pixel in
        // memory falls outside the locked region. For positive stride that pixel is
        // (width-1, height-1); for negative stride it is (width-1, 0) because y=0 is
        // the high-memory end. Check per 8-element block so only unsafe blocks fall
        // back to scalar — safe blocks behind an unsafe one still use gather.
        bool hasUnsafePixel = Math.Abs((int)_stride) == _width * 3;
        int unsafeX = _width - 1;
        int unsafeY = (_stride > 0) ? _height - 1 : 0;

        fixed (int* pxs = xs, pys = ys)
        fixed (byte* prs = rs, pgs = gs, pbs = bs)
        {
            int i = 0;

            if (Avx2.IsSupported && count >= 8)
            {
                var vstride = Vector256.Create((int)_stride);
                int* buf    = stackalloc int[8];

                for (; i + 8 <= count; )
                {
                    bool blockUnsafe = false;
                    if (hasUnsafePixel)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            if (pxs[i + j] == unsafeX && pys[i + j] == unsafeY)
                            { blockUnsafe = true; break; }
                        }
                    }

                    if (blockUnsafe)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            byte* p = (byte*)_ptr + (nint)pys[i+j] * _stride + (nint)pxs[i+j] * 3;
                            pbs[i+j] = p[0]; pgs[i+j] = p[1]; prs[i+j] = p[2];
                        }
                        i += 8;
                        continue;
                    }

                    var xvec = Avx.LoadVector256(pxs + i);
                    var yvec = Avx.LoadVector256(pys + i);

                    var offsets = Avx2.Add(
                        Avx2.MultiplyLow(yvec, vstride),
                        Avx2.MultiplyLow(xvec, s_vthree));

                    var gathered = Avx2.GatherVector256((int*)_ptr, offsets, 1);

                    Avx.Store(buf, gathered);
                    for (int j = 0; j < 8; j++)
                    {
                        int px = buf[j];
                        pbs[i+j] = (byte) px;
                        pgs[i+j] = (byte)(px >>  8);
                        prs[i+j] = (byte)(px >> 16);
                    }
                    i += 8;
                }
            }

            for (; i < count; i++)
            {
                byte* p = (byte*)_ptr + (nint)pys[i] * _stride + (nint)pxs[i] * 3;
                pbs[i] = p[0]; pgs[i] = p[1]; prs[i] = p[2];
            }
        }
    }

    /// <summary>
    /// Writes N pixels at arbitrary coordinates.
    /// AVX2 has no scatter instruction, so this uses unchecked scalar writes in a tight loop.
    /// All spans must have at least <c>xs.Length</c> elements.
    /// </summary>
    public unsafe void SetPixels(
        ReadOnlySpan<int> xs, ReadOnlySpan<int> ys,
        ReadOnlySpan<byte> rs, ReadOnlySpan<byte> gs, ReadOnlySpan<byte> bs)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureWritable();

        int count = xs.Length;
        if (ys.Length < count || rs.Length < count || gs.Length < count || bs.Length < count)
            throw new ArgumentException("All spans must have at least xs.Length elements.");

        for (int k = 0; k < count; k++)
            ValidateCoordinates(xs[k], ys[k]);

        fixed (int* pxs = xs, pys = ys)
        fixed (byte* prs = rs, pgs = gs, pbs = bs)
        {
            for (int i = 0; i < count; i++)
            {
                byte* p = (byte*)_ptr + (nint)pys[i] * _stride + (nint)pxs[i] * 3;
                p[0] = pbs[i]; p[1] = pgs[i]; p[2] = prs[i];
            }
        }
    }

    // -------------------------------------------------------------------------
    // Bulk operations (Fill, GetRow, SetRow)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fills the entire bitmap with the specified color.
    /// Uses AVX2 Vector256 SIMD writes when hardware support is available.
    /// </summary>
    public unsafe void Fill(byte r, byte g, byte b)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureWritable();

        int rowBytes = _width * 3;
        byte* basePtr = (byte*)_ptr;

        if (Avx2.IsSupported && rowBytes >= 96)
        {
            // 96 = LCM(3, 32): 32 complete BGR pixels with no pattern shift per store triplet
            byte* pattern = stackalloc byte[96];
            for (int i = 0; i < 96; i++)
                pattern[i] = (i % 3) switch { 0 => b, 1 => g, _ => r };

            var v0 = Unsafe.ReadUnaligned<Vector256<byte>>(pattern);
            var v1 = Unsafe.ReadUnaligned<Vector256<byte>>(pattern + 32);
            var v2 = Unsafe.ReadUnaligned<Vector256<byte>>(pattern + 64);

            for (int y = 0; y < _height; y++)
            {
                byte* row = basePtr + (nint)y * _stride;
                int i = 0;
                while (i + 96 <= rowBytes)
                {
                    Avx2.Store(row + i,      v0);
                    Avx2.Store(row + i + 32, v1);
                    Avx2.Store(row + i + 64, v2);
                    i += 96;
                }
                while (i < rowBytes) { row[i] = b; row[i+1] = g; row[i+2] = r; i += 3; }
            }
        }
        else
        {
            for (int y = 0; y < _height; y++)
            {
                byte* row = basePtr + (nint)y * _stride;
                for (int x = 0; x < _width; x++)
                { row[x*3] = b; row[x*3+1] = g; row[x*3+2] = r; }
            }
        }
    }

    /// <summary>
    /// Reads a row of pixels into <paramref name="bgrBuffer"/> using AVX2 Vector256 loads where available.
    /// Buffer layout: index <c>x*3</c>=B, <c>x*3+1</c>=G, <c>x*3+2</c>=R.
    /// </summary>
    public unsafe void GetRow(int y, Span<byte> bgrBuffer)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureReadable();
        if ((uint)y >= (uint)_height) throw new ArgumentOutOfRangeException(nameof(y));

        int rowBytes = _width * 3;
        if (bgrBuffer.Length < rowBytes)
            throw new ArgumentException($"Buffer must hold at least {rowBytes} bytes.", nameof(bgrBuffer));

        byte* src = (byte*)_ptr + (nint)y * _stride;

        if (Avx2.IsSupported)
            fixed (byte* dst = bgrBuffer)
                CopyRowAvx2(src, dst, rowBytes);
        else
            new Span<byte>(src, rowBytes).CopyTo(bgrBuffer);
    }

    /// <summary>
    /// Writes a row of pixels from <paramref name="bgrBuffer"/> using AVX2 Vector256 stores where available.
    /// Buffer layout: index <c>x*3</c>=B, <c>x*3+1</c>=G, <c>x*3+2</c>=R.
    /// </summary>
    public unsafe void SetRow(int y, ReadOnlySpan<byte> bgrBuffer)
    {
        ThrowIfDisposed();
        EnsureLocked();
        EnsureWritable();
        if ((uint)y >= (uint)_height) throw new ArgumentOutOfRangeException(nameof(y));

        int rowBytes = _width * 3;
        if (bgrBuffer.Length < rowBytes)
            throw new ArgumentException($"Buffer must hold at least {rowBytes} bytes.", nameof(bgrBuffer));

        byte* dst = (byte*)_ptr + (nint)y * _stride;

        if (Avx2.IsSupported)
            fixed (byte* src = bgrBuffer)
                CopyRowAvx2(src, dst, rowBytes);
        else
            bgrBuffer[..rowBytes].CopyTo(new Span<byte>(dst, rowBytes));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static unsafe void CopyRowAvx2(byte* src, byte* dst, int rowBytes)
    {
        int i = 0;
        while (i + 32 <= rowBytes)
        {
            Avx2.Store(dst + i, Unsafe.ReadUnaligned<Vector256<byte>>(src + i));
            i += 32;
        }
        while (i < rowBytes) { dst[i] = src[i]; i++; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureLocked()
    {
        if (!IsLocked)
            throw new InvalidOperationException("BeginAccess must be called before manipulating pixels.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureReadable()
    {
        if ((_lockMode & ImageLockMode.ReadOnly) == 0)
            throw new InvalidOperationException("Bitmap is locked as write-only.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureWritable()
    {
        if ((_lockMode & ImageLockMode.WriteOnly) == 0)
            throw new InvalidOperationException("Bitmap is locked as read-only.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            throw new ArgumentOutOfRangeException(
                paramName: x < 0 || x >= _width ? nameof(x) : nameof(y),
                actualValue: $"({x}, {y})",
                message: $"Coordinates ({x}, {y}) are outside the bitmap bounds ({_width}, {_height}).");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BitmapPlus));
    }

    public void Dispose()
    {
        if (_disposed) return;
        EndAccess();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
