# BitmapPuls

`System.Drawing.Bitmap` の 24bpp ピクセルに対して、`LockBits` + ポインタ演算 + AVX2 SIMD を組み合わせた高速アクセスを提供する C# ライブラリです。

> **動作環境**: Windows（`System.Drawing.Common` は .NET 6 以降の Linux/macOS では公式サポート外）。  
> Linux でビルド・テストする場合は `libgdiplus` と `System.Drawing.Common` 6.x が必要です。

---

## 特徴

| 機能 | 説明 |
|------|------|
| 安全な単体ピクセルアクセス | `GetPixel` / `SetPixel` — 範囲チェック・ロック確認付き |
| 検証省略の高速アクセス | `GetPixelUnchecked` / `SetPixelUnchecked` — 信頼済みループ向け |
| AVX2 バッチランダムアクセス | `GetPixels` — `GatherVector256` で 8 ピクセルを並列ロード |
| スカラーバッチ書き込み | `SetPixels` — AVX2 に scatter 命令がないためスカラー unchecked ループ |
| AVX2 行単位コピー | `GetRow` / `SetRow` — 32 バイト単位 SIMD ロード/ストア |
| AVX2 全面塗り潰し | `Fill` — LCM(3,32)=96 バイトパターンで BGR ずれなし |
| 負ストライド対応 | ボトムアップ DIB の `Stride < 0` を `Math.Abs` で吸収 |

---

## プロジェクト構成

```
BitmapPuls.sln
├── src/BitmapPuls                 # ライブラリ本体 (net6.0)
├── tests/BitmapPuls.Tests         # xUnit 単体テスト (net8.0)
└── benchmarks/BitmapPuls.Benchmarks  # BenchmarkDotNet ベンチマーク (net8.0)
```

---

## クイックスタート

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using BitmapPuls;

// 1. ビットマップを用意（Format24bppRgb のみ対応）
using var bitmap = new Bitmap(1920, 1080, PixelFormat.Format24bppRgb);
using var bp = new BitmapPlus(bitmap);

// 2. ロックしてアクセス開始
bp.BeginAccess();

// 単体ピクセル（範囲チェックあり）
bp.SetPixel(100, 200, r: 255, g: 128, b: 0);
byte r = 0, g = 0, b = 0;
bp.GetPixel(100, 200, ref r, ref g, ref b);

// 検証省略（自前で範囲を保証できる内側ループで使用）
bp.SetPixelUnchecked(100, 200, 255, 128, 0);
bp.GetPixelUnchecked(100, 200, ref r, ref g, ref b);

// バッチランダムアクセス（AVX2 gather）
int[]  xs = { 0, 10, 500, 1000 };
int[]  ys = { 0, 10, 200,  500 };
byte[] rs = new byte[xs.Length];
byte[] gs = new byte[xs.Length];
byte[] bs = new byte[xs.Length];
bp.GetPixels(xs, ys, rs, gs, bs);

// 行単位
byte[] row = new byte[1920 * 3]; // BGR 順
bp.GetRow(0, row);
bp.SetRow(0, row);

// 全面塗り潰し
bp.Fill(r: 255, g: 0, b: 0);

// 3. ロック解除（Dispose でも自動解除）
bp.EndAccess();
```

---

## API リファレンス

### コンストラクタ

```csharp
BitmapPlus(Bitmap bitmap)
```

- `bitmap` が `null` の場合は `ArgumentNullException`
- `PixelFormat.Format24bppRgb` 以外の場合は `ArgumentException`

### ライフサイクル

| メソッド | 説明 |
|----------|------|
| `BeginAccess(ImageLockMode lockMode = ReadWrite)` | ビットマップをロックしてポインタアクセスを開始 |
| `EndAccess()` | ロックを解除（未ロック時は何もしない） |
| `Dispose()` | `EndAccess` を呼んでリソース解放 |
| `IsLocked` | 現在ロック中かどうか |

### 単体ピクセル（安全）

```csharp
void GetPixel(int x, int y, ref byte r, ref byte g, ref byte b)
void SetPixel(int x, int y, byte r, byte g, byte b)
```

`BeginAccess` が必要。範囲外で `ArgumentOutOfRangeException`、未ロックで `InvalidOperationException`、Dispose 後で `ObjectDisposedException`。

### 単体ピクセル（unchecked）

```csharp
void GetPixelUnchecked(int x, int y, ref byte r, ref byte g, ref byte b)
void SetPixelUnchecked(int x, int y, byte r, byte g, byte b)
```

一切の検証を省略。呼び出し側が BeginAccess 済み・範囲内であることを保証すること。

### バッチランダムアクセス

```csharp
void GetPixels(ReadOnlySpan<int> xs, ReadOnlySpan<int> ys,
               Span<byte> rs, Span<byte> gs, Span<byte> bs)

void SetPixels(ReadOnlySpan<int> xs, ReadOnlySpan<int> ys,
               ReadOnlySpan<byte> rs, ReadOnlySpan<byte> gs, ReadOnlySpan<byte> bs)
```

全スパンの長さは `xs.Length` 以上が必要。`GetPixels` は AVX2 が利用可能な場合 `GatherVector256` で 8 ピクセルずつ並列ロード。

### 行単位

```csharp
void GetRow(int y, Span<byte> bgrBuffer)   // bgrBuffer.Length >= Width * 3
void SetRow(int y, ReadOnlySpan<byte> bgrBuffer)
```

バッファレイアウト: `[B, G, R, B, G, R, ...]`（x=0 から順）。

### 全面塗り潰し

```csharp
void Fill(byte r, byte g, byte b)
```

---

## ビルド・テスト

```bash
dotnet restore
dotnet build -c Release
dotnet test tests/BitmapPuls.Tests/ -c Release
```

---

## ベンチマーク

```bash
dotnet run --project benchmarks/BitmapPuls.Benchmarks/ -c Release
```

<!-- BENCHMARK_RESULTS_START -->
環境: .NET 8.0.26, X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI, ShortRun(3 warmup / 3 iter)

### Grayscale 変換（全ピクセル）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| `ConvertSafe` (GetPixel/SetPixel) | 532 µs | 1,092 µs | 1,980 µs | 4,135 µs |
| `ConvertUnchecked` | 404 µs | 893 µs | 1,542 µs | 3,223 µs |
| `ConvertViaRows` (行バッファ+スカラー) | 367 µs | 932 µs | 1,662 µs | 3,463 µs |
| `ConvertSimd` (SSSE3+SSE4.1) | **125 µs** | **316 µs** | **566 µs** | **1,305 µs** |

> SIMD 版は Safe 版の約 **3.2×** 高速 (1920×1080)。  
> ViaRows と Unchecked がほぼ同速なのはメモリ帯域がボトルネックのため。


環境: .NET 8.0.26, X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI, ShortRun(3 warmup / 3 iter)

### 単体ピクセル（中央1点）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| GetPixel (safe) | 1.41 ns | 1.42 ns | 1.40 ns | 1.38 ns |
| SetPixel (safe) | 1.54 ns | 1.58 ns | 1.67 ns | 1.22 ns |
| GetPixelUnchecked | 0.23 ns | 0.21 ns | 0.08 ns | ~0 ns |
| SetPixelUnchecked | 0.20 ns | 0.04 ns | 0.22 ns | 0.15 ns |

### 行単位（1行）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| GetRow | 32.5 ns | 35.7 ns | 94.6 ns | 96.5 ns |
| SetRow | 33.7 ns | 34.6 ns | 94.1 ns | 90.7 ns |

### バッチランダムアクセス（256ピクセル）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| GetPixels (AVX2 gather) | 536 ns | 505 ns | 538 ns | 551 ns |
| SetPixels (scalar unchecked) | 335 ns | 344 ns | 322 ns | 331 ns |

> 256ピクセルあたり 約 2.1 ns/pixel (Get) / 1.3 ns/pixel (Set)

### 全面操作（全ピクセル走査）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| Fill | 15.5 µs | 38.8 µs | 134.1 µs | 334.8 µs |
| GetPixel_AllPixels | 369 µs | 787 µs | 1,371 µs | 2,926 µs |
| SetPixel_AllPixels | 426 µs | 919 µs | 1,730 µs | 3,485 µs |
| GetRow_AllRows | 15.2 µs | 39.3 µs | 111.3 µs | 273.6 µs |
| SetRow_AllRows | 22.8 µs | 45.1 µs | 151.4 µs | 341.2 µs |

> `GetRow_AllRows` vs `GetPixel_AllPixels` (1920×1080): **273 µs vs 2,926 µs → 約10.7倍高速**
<!-- BENCHMARK_RESULTS_END -->

---

## 設計メモ

### なぜ `nint` でポインタをキャッシュするか

`BitmapData.Scan0` は `IntPtr` だが、毎回キャストすると JIT が余分な符号拡張命令を挿入する可能性がある。`nint _ptr` に一度格納しておくことで JIT はレジスタに保持しやすくなる。

### AVX2 Gather のしくみ

```
offset[k] = y[k] * stride + x[k] * 3
gathered  = GatherVector256((int*)_ptr, offsets, scale=1)
// → 8 つの 4 バイト値を並列ロード（BGR + 次ピクセルの B を含む）
B = (byte)(gathered[k])
G = (byte)(gathered[k] >> 8)
R = (byte)(gathered[k] >> 16)
```

ランダムアクセスでもメモリアクセスを並列化してレイテンシを隠蔽する。

### なぜ Fill に 96 バイトパターンを使うか

BGR は 3 バイト周期、AVX2 ストアは 32 バイト単位。LCM(3, 32) = 96 なので、96 バイトごとに BGR パターンの位相が揃う。3 回の 32 バイトストアを 1 セットとして扱うことで、ストアごとに値を入れ替える必要がなくなる。

### 負ストライド

Windows の DIB はボトムアップ格納の場合に `Stride < 0` になる。`Math.Abs(_bitmapData.Stride)` で常に正の値を保持し、ポインタ演算を単純化している。
