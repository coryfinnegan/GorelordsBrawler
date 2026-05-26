using System;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using Microsoft.Xna.Framework;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Unit tests for <see cref="CameraShake"/> — the trauma math behind BrawlerCamera's shake.
///
/// These lock in the fix for the static-camera drift bug: BrawlerCamera composes its position
/// as <c>Position = basePosition + CameraShake.Advance(...)</c> every frame. The regression we
/// guard against is the old <c>Position += offset</c> accumulation that let a locked (Static)
/// view random-walk away from map center, since the base was never recomputed there.
/// </summary>
public class CameraShakeTests
{
	private const float MaxOffset = GameConstants.Combat.MaxShakeOffset;
	private const float Decay     = GameConstants.Combat.ShakeDecay;
	private const float Dt        = 1f / 60f;

	// rng stand-ins for Nez.Random.NextFloat() — picks the extreme corners of the offset range.
	private static float MaxUnit() => 1f;   // (1*2 - 1) =  1  → +max magnitude
	private static float MinUnit() => 0f;   // (0*2 - 1) = -1  → -max magnitude

	[Fact]
	public void NoTrauma_ProducesZeroOffset()
	{
		var shake = new CameraShake();

		Assert.False(shake.IsActive);
		Assert.Equal(Vector2.Zero, shake.Advance(Dt, MaxOffset, Decay, MaxUnit));
	}

	[Fact]
	public void StaticView_ReturnsExactlyToBase_AfterShakeSettles()
	{
		var basePosition = new Vector2(640f, 360f); // arbitrary "map center"
		var shake = new CameraShake();
		shake.AddTrauma(1f);

		var position = basePosition;
		var rng = new Random(12345);

		// Drive until the shake fully settles (cap iterations so a bug can't hang the test).
		for (int i = 0; i < 1000 && shake.IsActive; i++)
		{
			var offset = shake.Advance(Dt, MaxOffset, Decay, () => (float)rng.NextDouble());
			position = basePosition + offset;
		}

		Assert.False(shake.IsActive);
		// One more frame past settle: offset must be exactly zero, so position == base bit-for-bit.
		position = basePosition + shake.Advance(Dt, MaxOffset, Decay, () => (float)rng.NextDouble());
		Assert.Equal(basePosition, position);
	}

	[Fact]
	public void RepeatedShakes_DoNotAccumulateDrift()
	{
		// The core regression test: a Static camera that shakes many times over a match must
		// always come back to exactly the same base. With the old `+=` this random-walked away.
		var basePosition = new Vector2(640f, 360f);
		var shake = new CameraShake();
		var rng = new Random(999);

		for (int burst = 0; burst < 20; burst++)
		{
			shake.AddTrauma(1f);

			var position = basePosition;
			for (int i = 0; i < 1000 && shake.IsActive; i++)
			{
				var offset = shake.Advance(Dt, MaxOffset, Decay, () => (float)rng.NextDouble());
				position = basePosition + offset;
			}

			Assert.False(shake.IsActive);
			Assert.Equal(basePosition, basePosition + shake.Advance(Dt, MaxOffset, Decay, () => (float)rng.NextDouble()));
		}
	}

	[Fact]
	public void Offset_NeverExceedsMaxOffset()
	{
		var shake = new CameraShake();
		shake.AddTrauma(1f);

		var rng = new Random(7);
		for (int i = 0; i < 1000 && shake.IsActive; i++)
		{
			var offset = shake.Advance(Dt, MaxOffset, Decay, () => (float)rng.NextDouble());
			Assert.True(Math.Abs(offset.X) <= MaxOffset + 1e-4f, $"X offset {offset.X} exceeded max {MaxOffset}");
			Assert.True(Math.Abs(offset.Y) <= MaxOffset + 1e-4f, $"Y offset {offset.Y} exceeded max {MaxOffset}");
		}
	}

	[Fact]
	public void FullTrauma_FirstFrame_HitsMaxMagnitude()
	{
		var shake = new CameraShake();
		shake.AddTrauma(1f);

		// trauma² * maxOffset at trauma=1 → exactly maxOffset, signed by the rng corner.
		var offset = shake.Advance(Dt, MaxOffset, Decay, MaxUnit);
		Assert.Equal(MaxOffset, offset.X, precision: 3);
		Assert.Equal(MaxOffset, offset.Y, precision: 3);
	}

	[Fact]
	public void AddTrauma_ClampsToOne()
	{
		var shake = new CameraShake();
		shake.AddTrauma(0.8f);
		shake.AddTrauma(0.8f); // would be 1.6 unclamped

		// At clamped trauma=1, magnitude is exactly maxOffset (not 1.6² × maxOffset).
		var offset = shake.Advance(Dt, MaxOffset, Decay, MaxUnit);
		Assert.Equal(MaxOffset, offset.X, precision: 3);
	}

	[Fact]
	public void Decay_ScalesWithDt_LargerDtSettlesFaster()
	{
		int FramesToSettle(float dt)
		{
			var shake = new CameraShake();
			shake.AddTrauma(1f);
			int frames = 0;
			while (shake.IsActive && frames < 10000)
			{
				shake.Advance(dt, MaxOffset, Decay, MaxUnit);
				frames++;
			}
			return frames;
		}

		// Trauma decays by Decay*dt per frame, so a larger timestep clears it in fewer frames.
		Assert.True(FramesToSettle(Dt * 2f) < FramesToSettle(Dt),
			"A larger dt should settle the shake in fewer frames (decay is dt-scaled).");
	}

	[Fact]
	public void ReShake_DuringDecay_RaisesTraumaAgain()
	{
		var shake = new CameraShake();
		shake.AddTrauma(1f);

		// Let it decay partway down.
		for (int i = 0; i < 5; i++)
		{
			shake.Advance(Dt, MaxOffset, Decay, MaxUnit);
		}
		var partial = shake.Advance(Dt, MaxOffset, Decay, MaxUnit); // smaller than max now

		// A fresh hit re-fills trauma → next offset is back to full magnitude.
		shake.AddTrauma(1f);
		var refreshed = shake.Advance(Dt, MaxOffset, Decay, MaxUnit);

		Assert.True(refreshed.X > partial.X,
			$"Re-shake should restore magnitude: refreshed={refreshed.X} vs partial={partial.X}");
		Assert.Equal(MaxOffset, refreshed.X, precision: 3);
	}
}
