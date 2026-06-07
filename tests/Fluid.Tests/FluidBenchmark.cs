using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using GorelordsBrawler.Components.Hazards.Fluid;
using Xunit;
using Xunit.Abstractions;

namespace Fluid.Tests
{
	/// <summary>
	/// Pure-CPU scaling benchmark for the PBF solver (FluidSimulation.Step).
	/// NO rendering — isolates solver cost so we can answer "how many particles
	/// can the sim sustain at 60 fps?" independent of the GPU splat pass.
	///
	/// Measures SERIAL vs PARALLEL in the SAME run (same machine state, same
	/// spawned block) so the speedup figure isn't contaminated by cross-run drift.
	///
	/// Env-gated: this is a measurement, not a correctness assertion (that lives in
	/// FluidSimulationTests). Only meaningful in Release. Normal `dotnet test` runs
	/// see it return instantly (green). To run:
	///
	///   FLUID_BENCH=1 dotnet test tests/Fluid.Tests -c Release \
	///       --filter FullyQualifiedName~FluidBenchmark
	///
	/// Results also written to %TEMP%/fluid_bench.txt.
	/// </summary>
	public class FluidBenchmark
	{
		private readonly ITestOutputHelper _output;
		public FluidBenchmark(ITestOutputHelper output) => _output = output;

		[Fact]
		public void Benchmark_Step_Cost_By_ParticleCount()
		{
			if (Environment.GetEnvironmentVariable("FLUID_BENCH") != "1")
			{
				_output.WriteLine("skipped (set FLUID_BENCH=1 to run)");
				return;
			}

			int[] counts = { 500, 1000, 1223, 2000, 3000, 5000, 8000, 10000 };
			const float dt      = 1f / 60f;
			const int   warmup  = 60;
			const int   measure = 300;
			// Realistic arena geometry: full inner-arena width so fill depth (and
			// thus per-particle neighbour density) matches the real game — a wide
			// shallow pool, NOT a narrow deep column (which would over-compress the
			// bottom layers and inflate per-particle cost).
			const float boxW = 1216f;   // InnerRight(1248) - InnerLeft(32)
			const float boxH = 800f;

			var sb = new StringBuilder();
			void Log(string s) { _output.WriteLine(s); sb.AppendLine(s); }

			Log("# Fluid PBF Step() — serial vs parallel, no rendering");
			Log($"# build : {(IsOptimized() ? "RELEASE (JIT optimized)" : "DEBUG (unoptimized)")}");
			Log($"# cores : {Environment.ProcessorCount} logical");
			Log($"# solver iters: {FluidConfig.SolverIterations}   h={FluidConfig.SmoothingRadius}   r={FluidConfig.ParticleRadius}");
			Log($"# warmup={warmup}  measure={measure}  60fps budget = 16.67 ms/frame (shared by ALL systems)");
			Log("");
			Log("     N | serial ms | parallel ms | speedup | par % of 16.67ms | par sim-fps");
			Log("-------+-----------+-------------+---------+------------------+------------");

			foreach (int n in counts)
			{
				double serialMs   = MeasureStep(n, boxW, boxH, dt, warmup, measure, parallel: false);
				double parallelMs = MeasureStep(n, boxW, boxH, dt, warmup, measure, parallel: true);

				double speedup = serialMs / parallelMs;
				double pct60   = parallelMs / (1000.0 / 60.0) * 100.0;
				double simFps  = 1000.0 / parallelMs;

				Log(
					n.ToString().PadLeft(6) + " | " +
					serialMs.ToString("F3").PadLeft(9) + " | " +
					parallelMs.ToString("F3").PadLeft(11) + " | " +
					speedup.ToString("F2").PadLeft(6) + "x | " +
					pct60.ToString("F1").PadLeft(15) + "% | " +
					simFps.ToString("F0").PadLeft(11));
			}

			Log("");
			Log("# 'par % of 16.67ms' is the sim's share of one frame at that count —");
			Log("# read across N to find the sustainable particle ceiling at 60 fps.");

			var path = Path.Combine(Path.GetTempPath(), "fluid_bench.txt");
			File.WriteAllText(path, sb.ToString());
			_output.WriteLine($"\nwrote {path}");
		}

		/// <summary>
		/// Cross-check that parallel and serial produce the SAME simulation, not
		/// just a faster one. Steps both from identical state and asserts every
		/// final position matches within float tolerance. A data race would make
		/// the parallel result diverge (order-dependent writes) and fail this.
		/// Runs in normal `dotnet test` (not env-gated) so it guards every build.
		/// </summary>
		[Fact]
		public void Parallel_And_Serial_Produce_Identical_Result()
		{
			const float boxW = 600f, boxH = 600f, dt = 1f / 60f;
			const int n = 1500, steps = 200;   // > ParallelThreshold so parallel path is exercised

			var serial = MakeAndStep(n, boxW, boxH, dt, steps, parallel: false);
			var par    = MakeAndStep(n, boxW, boxH, dt, steps, parallel: true);

			Assert.Equal(serial.Count, par.Count);
			for (int i = 0; i < serial.Count; i++)
			{
				// Float summation order differs across partitions, so allow a small
				// epsilon — but positions must track tightly over 200 steps or a real
				// race (not just reordering) is present.
				Assert.True(MathF.Abs(serial.Px[i] - par.Px[i]) < 0.5f,
					$"Px[{i}] diverged: serial={serial.Px[i]} parallel={par.Px[i]}");
				Assert.True(MathF.Abs(serial.Py[i] - par.Py[i]) < 0.5f,
					$"Py[{i}] diverged: serial={serial.Py[i]} parallel={par.Py[i]}");
			}
		}

		private static FluidSimulation MakeAndStep(int n, float boxW, float boxH,
			float dt, int steps, bool parallel)
		{
			var sim = new FluidSimulation(n + 16, 0, 0, boxW, boxH) { ParallelEnabled = parallel };
			var box = new FluidCollider(0, boxW, 0, boxH);
			SpawnBlock(sim, n, boxW, boxH);
			for (int i = 0; i < steps; i++)
			{
				sim.Step(dt, box);
			}
			return sim;
		}

		/// <summary>
		/// Spawn n particles as a floor-anchored packed block, warm up, then time
		/// `measure` Step()s. Returns mean ms/step. `parallel` toggles the solver
		/// threading so both modes are measured on identical state in one run.
		/// </summary>
		private static double MeasureStep(int n, float boxW, float boxH, float dt,
			int warmup, int measure, bool parallel)
		{
			var sim = new FluidSimulation(n + 16, 0, 0, boxW, boxH) { ParallelEnabled = parallel };
			var box = new FluidCollider(0, boxW, 0, boxH);
			SpawnBlock(sim, n, boxW, boxH);

			for (int i = 0; i < warmup; i++)
			{
				sim.Step(dt, box);
			}

			var sw = Stopwatch.StartNew();
			for (int i = 0; i < measure; i++)
			{
				sim.Step(dt, box);
			}
			sw.Stop();
			return sw.Elapsed.TotalMilliseconds / measure;
		}

		private static void SpawnBlock(FluidSimulation sim, int n, float boxW, float boxH)
		{
			float spacing = 2f * FluidConfig.ParticleRadius;
			int cols = (int)(boxW / spacing) - 1;
			int spawned = 0;
			for (int row = 0; spawned < n; row++)
			{
				for (int col = 0; col < cols && spawned < n; col++)
				{
					sim.Spawn(spacing * (col + 1), boxH - spacing * (row + 1) - 1f, 0f, 0f);
					spawned++;
				}
			}
		}

		/// <summary>True if the SUT assembly was JIT-optimized (Release).</summary>
		private static bool IsOptimized()
		{
			var attrs = typeof(FluidSimulation).Assembly
				.GetCustomAttributes(typeof(DebuggableAttribute), false);
			if (attrs.Length == 0)
			{
				return true;
			}
			return !((DebuggableAttribute)attrs[0]).IsJITOptimizerDisabled;
		}
	}
}
