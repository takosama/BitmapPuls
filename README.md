# BitmapPlus

`System.Drawing.Bitmap` の 24bpp ピクセルに対して、`LockBits` + ポインタ演算 + AVX2 SIMD を組み合わせた高速アクセスを提供する C# ライブラリです。

---

## 対応範囲

| 項目 | 内容 |
|------|------|
| 対応 PixelFormat | `Format24bppRgb` のみ |
| 想定 OS | Windows（`System.Drawing.Common` は .NET 7 以降 Windows 専用） |
| 所有権 | `BitmapPlus` は渡された `Bitmap` を `Dispose` しない |
| スレッドセーフ | 非対応（同一インスタンスへの同時アクセス不可） |

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
| 負ストライド対応 | `BitmapData.Stride` の符号を保持し `Scan0 + y × Stride + x × 3` で top-down / bottom-up を同一式で扱う |

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
using BitmapPlus;

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
| `ConvertSafe` (GetPixel/SetPixel) | 632 µs | 1,353 µs | 2,326 µs | 5,026 µs |
| `ConvertUnchecked` | 465 µs | 982 µs | 1,768 µs | 3,637 µs |
| `ConvertViaRows` (行バッファ+スカラー) | 426 µs | 952 µs | 1,649 µs | 3,521 µs |
| `ConvertSimd` (SSSE3+SSE4.1) | **155 µs** | **343 µs** | **643 µs** | **1,380 µs** |

> SIMD 版は Safe 版の約 **3.6×** 高速 (1920×1080)。  
> ViaRows と Unchecked がほぼ同速なのはメモリ帯域がボトルネックのため。

### 単体ピクセル（中央1点）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| GetPixel (safe) | 1.33 ns | 1.30 ns | 1.33 ns | 1.33 ns |
| SetPixel (safe) | 1.36 ns | 1.39 ns | 1.39 ns | 1.39 ns |
| GetPixelUnchecked | 0.34 ns | 0.57 ns | 0.39 ns | 0.36 ns |
| SetPixelUnchecked | 0.39 ns | 0.31 ns | 0.39 ns | 0.38 ns |

### 行単位（1行）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| GetRow | 26.2 ns | 26.3 ns | 121.2 ns | 118.6 ns |
| SetRow | 27.8 ns | 26.6 ns | 121.3 ns | 120.2 ns |

### バッチランダムアクセス（256ピクセル）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| GetPixels (AVX2 gather) | 857 ns | 823 ns | 861 ns | 843 ns |
| SetPixels (scalar unchecked) | 578 ns | 616 ns | 554 ns | 607 ns |

> 256ピクセルあたり 約 3.3 ns/pixel (Get) / 2.3 ns/pixel (Set)

### 全面操作（全ピクセル走査）

| メソッド | 512×512 | 512×1080 | 1920×512 | 1920×1080 |
|---------|--------:|---------:|---------:|----------:|
| Fill | 24.6 µs | 42.1 µs | 151 µs | 254 µs |
| GetPixel_AllPixels | 466 µs | 985 µs | 1,750 µs | 3,597 µs |
| SetPixel_AllPixels | 428 µs | 942 µs | 2,136 µs | 4,497 µs |
| GetRow_AllRows | 15.5 µs | 41.1 µs | 88.7 µs | 212 µs |
| SetRow_AllRows | 18.3 µs | 61.4 µs | 140 µs | 307 µs |

> `GetRow_AllRows` vs `GetPixel_AllPixels` (1920×1080): **212 µs vs 3,597 µs → 約17.0倍高速**
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

`BitmapData.Stride` は符号に画像方向の情報を持つため符号付きのまま保持する。GDI+ が `Scan0` を末尾行に調整済みなので `Scan0 + y × Stride + x × 3` で視覚行 y が top-down / bottom-up のどちらでも常に正しく引ける。
