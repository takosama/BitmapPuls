using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BitmapPuls.Benchmarks;

// Must be set before any System.Drawing type is loaded (enables libgdiplus on Linux)
AppContext.SetSwitch("System.Drawing.EnableUnixSupport", true);

var config = DefaultConfig.Instance
    .AddJob(Job.MediumRun.WithToolchain(InProcessEmitToolchain.Instance));

BenchmarkRunner.Run<BitmapPlusBenchmarks>(config);
