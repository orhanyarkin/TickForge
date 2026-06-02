using System.Reflection;
using BenchmarkDotNet.Running;

// Run all benchmarks (or filter with, e.g., `-- --filter *Depth*`).
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
