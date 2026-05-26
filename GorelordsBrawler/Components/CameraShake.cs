using System;
using Microsoft.Xna.Framework;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Trauma-based screen-shake math, separated from <see cref="BrawlerCamera"/> so the
	/// "shake oscillates around a fixed base and returns to exactly zero" invariant can be
	/// unit-tested without a live Nez Camera (same seam pattern as CombatMath/Hurtbox).
	///
	/// Trauma rises on impact (clamped to 1) and decays linearly. The per-frame offset
	/// magnitude scales with trauma² (Squirrel Eiserloh, "Juicing Your Cameras With Math"),
	/// so shake falls off quickly and feels punchy rather than lingering.
	/// </summary>
	public struct CameraShake
	{
		private float _trauma;

		public readonly bool IsActive => _trauma > 0f;

		public void AddTrauma(float intensity)
		{
			_trauma = Math.Min(_trauma + intensity, 1f);
		}

		/// <summary>
		/// Advances trauma by <paramref name="dt"/> and returns the displacement to ADD to the
		/// base position this frame. Returns <see cref="Vector2.Zero"/> once trauma is spent, so a
		/// caller doing <c>position = base + offset</c> settles exactly on its base (no drift).
		/// <paramref name="nextUnit"/> must yield values in [0,1) (e.g. Nez.Random.NextFloat()).
		/// </summary>
		public Vector2 Advance(float dt, float maxOffset, float decay, Func<float> nextUnit)
		{
			if (_trauma <= 0f)
			{
				return Vector2.Zero;
			}

			// Offset is computed from the CURRENT trauma, then trauma decays — so the frame
			// that spends the last trauma still shakes, and the next frame returns zero.
			var magnitude = _trauma * _trauma * maxOffset;
			var offset = new Vector2(
				(nextUnit() * 2f - 1f) * magnitude,
				(nextUnit() * 2f - 1f) * magnitude);

			_trauma -= decay * dt;
			if (_trauma < 0f)
			{
				_trauma = 0f;
			}

			return offset;
		}
	}
}
