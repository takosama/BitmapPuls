# Cross-platform buffer contract checks

Run `dotnet run --project tests/BufferValidation.Regression/BufferValidation.Regression.csproj -c Release` with .NET 8.

This project links the production PixelBufferValidation helper and checks all six coordinate/output overlap directions, short buffers, empty input and safe disjoint prefixes. It does not initialize System.Drawing or test GDI+ bitmap memory. The existing BitmapPlus.Tests project also contains six public GetPixels overlap cases with zero-filled pixels; run `dotnet test tests/BitmapPlus.Tests/BitmapPlus.Tests.csproj -c Release` on a supported Windows environment for integration coverage.
