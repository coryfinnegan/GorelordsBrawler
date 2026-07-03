using System;
using GorelordsBrawler.Components.Hazards.Fluid;
using Xunit;

namespace Fluid.Tests
{
	/// <summary>
	/// Pins <see cref="FluidConfig.EffectiveParticleArea"/> — the MEASURED
	/// standing area per particle that every particles↔fill-height conversion
	/// (pour caps, inlet flow, pre-fill packing) is derived from.
	///
	/// Backstory: the caps originally assumed hex packing at 2r (55.4 px²), but
	/// the dynamic solver settles ~2× denser, so every fill ceiling landed ~half
	/// its intended height above the basin lip — in a real-speed match the acid
	/// never reached a single platform (2026-07-01 timeline capture). This test
	/// settles a pool headlessly, re-measures the standing density, and fails if
	/// the constant drifts from reality — e.g. after retuning SolverIterations,
	/// SCorr, or gravity. If it fails: re-measure, update the constant, re-tune
	/// AcidConfig's ceilings/flows against the new value.
	/// </summary>
	public class FluidCalibrationTests
	{
		/// <summary>
		/// The pinning test: settles one canonical pool (arena-width, flood-ish
		/// depth) and asserts the measured density matches the constant. ±15% —
		/// generous for surface-layer noise, tight enough that a real solver
		/// retune (which moves density far more) trips it.
		/// </summary>
		[Fact]
		public void Settled_Pool_Stands_At_The_Calibrated_Density()
		{
			float measuredArea = MeasureStandingArea(boxW: 1216f, targetDepth: 160f);

			Assert.True(
				Math.Abs(measuredArea - FluidConfig.EffectiveParticleArea)
					<= FluidConfig.EffectiveParticleArea * 0.15f,
				$"Measured standing area {measuredArea:F1} px²/particle drifted from the " +
				$"calibrated {FluidConfig.EffectiveParticleArea} px² (±15%). Re-measure with " +
				"FLUID_CALIB=1 (Measure_Standing_Density_Across_Geometries), update " +
				"FluidConfig.EffectiveParticleArea, then re-tune AcidConfig ceilings/flows.");

			// Guard against 'correcting' the constant back to hex-pack theory:
			// the settled pool is decisively denser than the 2r hex ideal.
			float hexIdeal = 4f * FluidConfig.ParticleRadius * FluidConfig.ParticleRadius * 0.8660254f;
			Assert.True(measuredArea < hexIdeal * 0.75f,
				$"Settled density ({measuredArea:F1} px²) unexpectedly near the hex-pack ideal " +
				$"({hexIdeal:F1} px²) — the calibration story changed; re-derive the caps.");
		}

		/// <summary>
		/// The re-measurement harness (env-gated like FluidBenchmark): prints the
		/// settled density across representative pool geometries so a solver
		/// retune can pick the new EffectiveParticleArea with eyes open.
		/// Run: FLUID_CALIB=1 dotnet test tests/Fluid.Tests --filter FluidCalibration
		/// </summary>
		[Fact]
		public void Measure_Standing_Density_Across_Geometries()
		{
			if (Environment.GetEnvironmentVariable("FLUID_CALIB") != "1")
			{
				return;
			}

			var sb = new System.Text.StringBuilder();
			sb.AppendLine("boxW x depth -> px²/particle");
			foreach (var (w, d) in new[] { (1216f, 80f), (1216f, 160f), (1216f, 220f), (384f, 190f), (256f, 120f) })
			{
				sb.AppendLine($"{w,6:F0} x {d,4:F0} -> {MeasureStandingArea(w, d):F1}");
			}
			var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fluid_calibration.txt");
			System.IO.File.WriteAllText(path, sb.ToString());
		}

		/// <summary>
		/// Settle a floor-anchored pool in a boxW-wide vessel and return the
		/// measured standing area (px² per particle). Surface = 2nd-percentile
		/// particle Y — ignores stray splash particles, sits at the bulk top.
		/// </summary>
		private static float MeasureStandingArea(float boxW, float targetDepth)
		{
			const float boxH = 800f;
			const float dt = 1f / 60f;
			const int settleSteps = 360;   // 6 s of sim time — fully at rest

			int n = (int)(boxW * targetDepth / FluidConfig.EffectiveParticleArea);

			var sim = new FluidSimulation(n + 16, 0, 0, boxW, boxH);
			var box = new FluidCollider(0, boxW, 0, boxH);

			// Spawn as a floor-anchored loose block (2r grid); the solver pulls
			// it to its true rest density during settling.
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

			for (int i = 0; i < settleSteps; i++)
			{
				sim.Step(dt, box);
			}

			var ys = new float[sim.Count];
			for (int i = 0; i < sim.Count; i++)
			{
				ys[i] = sim.Py[i];
			}
			Array.Sort(ys);
			float surfaceY = ys[Math.Max(0, (int)(sim.Count * 0.02f))];

			return boxW * (boxH - surfaceY) / sim.Count;
		}
	}
}
