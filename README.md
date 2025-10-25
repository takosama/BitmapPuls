# BitmapPuls

C# で Bitmap にポインタを使用して高速にアクセスするためのヘルパークラスです。

## 特徴

- `Bitmap.LockBits` を利用した高速な 24bpp ピクセルアクセス
- 64bit 環境でも安全に動作する `IntPtr` ベースのポインタ管理
- 範囲チェックとロック状態チェックによる安全な API
- xUnit ベースの自動テスト

## プロジェクト構成

```
BitmapPuls.sln
├── src/BitmapPuls          # ライブラリ本体
└── tests/BitmapPuls.Tests  # 単体テスト
```

## ビルドとテスト

.NET SDK をインストール済みの環境で次のコマンドを実行してください。

```bash
dotnet restore
dotnet test
```
