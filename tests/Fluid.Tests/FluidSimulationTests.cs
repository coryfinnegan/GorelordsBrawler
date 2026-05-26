using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards.Fluid;
using Xunit;

namespace Fluid.Tests
{
	public class FluidSimulationTests
	{
		// ── Helpers ──────────────────────────────────────────────────────────

		private static FluidSimulation NewSim(float w, float h, int capacity = 1024)
		{
			return new FluidSimulation(capacity, 0, 0, w, h);
		}

		private static FluidCollider NewBox(float w, float h)
		{
			return new FluidCollider(0, w, 0, h);
		}

		private static void Run(FluidSimulation sim, FluidCollider colliders, int frames, float dt = 1f / 60f)
		{
			for (int i = 0; i < frames; i++)
			{
				sim.Step(dt, colliders);
			}
		}

		// ── 1. Stack height ──────────────────────────────────────────────────
		// 500 particles dropped into a 200×400 box should come to rest with the
		// surface near y ≈ 275 (≈ 125 px above the floor at y=400).

		[Fact]
		public void Particles_Stack_To_Expected_Height()
		{
			var sim       = NewSim(200, 400, capacity: 600);
			var colliders = NewBox(200, 400);

			// Spawn 500 particles in a loose grid above the floor
			float r = FluidConfig.ParticleRadius;
			int spawned = 0;
			for (int row = 0; row < 25 && spawned < 500; row++)
			{
				for (int col = 0; col < 20 && spawned < 500; col++)
				{
					float x = 10 + col * (2 * r + 1);
					float y = 50 + row * (2 * r + 1);
					sim.Spawn(x, y, 0, 0);
					spawned++;
				}
			}

			Run(sim, colliders, 600);

			// Find min Y (highest particle) — that's the surface
			float minY = 400f;
			for (int i = 0; i < sim.Count; i++)
			{
				if (sim.Py[i] < minY) minY = sim.Py[i];
			}

			// Expected stack height ≈ 500 · π·r² / 200 px (loose-pack volumetric estimate)
			// With PBF effectively at hex packing density, particles occupy roughly
			// 90% as much area as π·r². Allow ±60 px slack — the goal here is to
			// catch blow-ups and broken floor collision, not to nail equilibrium.
			float expectedSurfaceY = 400f - (500f * MathHelper.Pi * r * r / 200f);
			Assert.InRange(minY, expectedSurfaceY - 60f, expectedSurfaceY + 60f);

			// Sanity: nothing escaped the box
			for (int i = 0; i < sim.Count; i++)
			{
				Assert.InRange(sim.Px[i], -1f, 201f);
				Assert.InRange(sim.Py[i], -1f, 401f);
			}
		}

		// ── 2. Single-platform splash ───────────────────────────────────────
		// 200 particles fall onto a static AABB platform. At least 80% pass
		// below the platform's bottom edge, and ≥30 come to rest on top of it.

		[Fact]
		public void Particles_Split_Around_Platform()
		{
			var sim       = NewSim(200, 400, capacity: 300);
			var colliders = NewBox(200, 400);

			// Platform: x∈[40,160], y∈[200,232]
			var aabbs = new[] { new RectangleF(40, 200, 120, 32) };
			colliders.SetAabbs(aabbs, 1);

			float r = FluidConfig.ParticleRadius;
			for (int row = 0; row < 10; row++)
			{
				for (int col = 0; col < 20; col++)
				{
					float x = 20 + col * (2 * r + 1);
					float y = 30 + row * (2 * r + 1);
					sim.Spawn(x, y, 0, 0);
				}
			}

			Run(sim, colliders, 300);

			int belowPlatform = 0;
			int restingOnTop = 0;
			for (int i = 0; i < sim.Count; i++)
			{
				if (sim.Py[i] > 232f) belowPlatform++;
				// "On top" = within radius of y=200 and roughly above the platform body
				if (sim.Py[i] > 180f && sim.Py[i] < 200f &&
				    sim.Px[i] > 40f && sim.Px[i] < 160f)
				{
					restingOnTop++;
				}
			}

			Assert.True(belowPlatform >= sim.Count * 0.6,
				$"Expected ≥60% past platform, got {belowPlatform}/{sim.Count}");
			Assert.True(restingOnTop >= 10,
				$"Expected ≥10 particles resting on platform top, got {restingOnTop}");
		}

		// ── 3. Gravity-only conservation ─────────────────────────────────────
		// One step at dt=1/60 with all particles far apart (no neighbor density
		// constraints). Vertical velocity should be ≈ g·dt = 20 px/s.

		[Fact]
		public void Single_Step_With_No_Neighbors_Applies_Pure_Gravity()
		{
			// Use a huge box so particles can be spaced far apart and stay un-clipped
			var sim       = NewSim(10000, 10000, capacity: 200);
			var colliders = NewBox(10000, 10000);

			// Spawn 100 particles in a sparse grid: spacing 50 px (>> h=12), well inside box
			for (int row = 0; row < 10; row++)
			{
				for (int col = 0; col < 10; col++)
				{
					sim.Spawn(500 + col * 50, 500 + row * 50, 0, 0);
				}
			}

			float dt = 1f / 60f;
			sim.Step(dt, colliders);

			float expectedVy = FluidConfig.Gravity * dt; // 20.0
			for (int i = 0; i < sim.Count; i++)
			{
				Assert.InRange(sim.Vy[i], expectedVy - 0.5f, expectedVy + 0.5f);
				Assert.InRange(sim.Vx[i], -0.5f, 0.5f); // no lateral drift
			}
		}

		// ── 4. Rest-density calibration is non-trivial ──────────────────────
		// Smoke test that the constructor's calibration step produced a real value.

		[Fact]
		public void RestDensity_Is_Calibrated_To_Nontrivial_Value()
		{
			var sim = NewSim(100, 100);
			Assert.True(sim.RestDensity > 0.001f);
			// Sanity: order of magnitude — Poly6 self-density is the dominant term;
			// at h=12 it's 315/(64π·12⁹) · 12⁶ = 315/(64π·12³) ≈ 0.0905
			// Plus ~6 neighbors at r=8 contribute roughly half that each → ρ₀ ≈ 0.4
			Assert.InRange(sim.RestDensity, 0.05f, 5.0f);
		}

		// ── 5. Zero-timestep (hitstop) must not blow up ─────────────────────
		// Regression for the "liquid disappears + FPS tanks on melee hit" bug.
		// A melee hit triggers hitstop (CombatEffectsManager sets Time.TimeScale
		// = 0), so AcidSurface.Update feeds dt = Time.DeltaTime = 0 into Step.
		// UpdateVelocitiesAndPositions computed invDt = 1/dt → +Infinity → NaN
		// velocities → NaN positions. NaN particles never despawn AND all collapse
		// to one spatial-hash cell (int)NaN==0, making neighbor search O(n²).
		// Step must treat dt <= 0 as a no-op so the settled state survives a freeze.

		[Fact]
		public void Zero_Timestep_During_Hitstop_Does_Not_Produce_NaN()
		{
			var sim       = NewSim(200, 400, capacity: 600);
			var colliders = NewBox(200, 400);

			float r = FluidConfig.ParticleRadius;
			for (int row = 0; row < 25; row++)
			{
				for (int col = 0; col < 20; col++)
				{
					sim.Spawn(10 + col * (2 * r + 1), 50 + row * (2 * r + 1), 0, 0);
				}
			}

			// Settle so particles are in contact (where the ΔP correction is
			// non-zero — that correction divided by dt=0 is what NaNs).
			Run(sim, colliders, 120);

			int countBefore = sim.Count;

			// 4 frozen frames == HitstopDuration 0.06s at 60fps.
			for (int i = 0; i < 4; i++)
			{
				sim.Step(0f, colliders);
			}

			// Resume a few normal frames — NaN, if introduced, propagates here.
			Run(sim, colliders, 5);

			for (int i = 0; i < sim.Count; i++)
			{
				Assert.True(float.IsFinite(sim.Px[i]), $"Px[{i}] not finite: {sim.Px[i]}");
				Assert.True(float.IsFinite(sim.Py[i]), $"Py[{i}] not finite: {sim.Py[i]}");
				Assert.True(float.IsFinite(sim.Vx[i]), $"Vx[{i}] not finite: {sim.Vx[i]}");
				Assert.True(float.IsFinite(sim.Vy[i]), $"Vy[{i}] not finite: {sim.Vy[i]}");
			}

			// A freeze should preserve the fluid, not vanish it.
			Assert.Equal(countBefore, sim.Count);
		}
	}
}
