using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BitmapPuls.Benchmarks;

// Must be set before any System.Drawing type is loaded (enables libgdiplus on Linux)
AppContext.SetSwitch("System.Drawing.EnableUnixSupport", true);

var config = ManualConfig.CreateEmpty()
    .AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
    .AddExporter(MarkdownExporter.GitHub)
    .AddLogger(ConsoleLogger.Default)
    .AddColumnProvider(DefaultColumnProviders.Instance)
    .AddDiagnoser(MemoryDiagnoser.Default);

BenchmarkRunner.Run<BitmapPlusBenchmarks>(config);
