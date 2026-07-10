using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// A boulder shed by the crumbling facility (docs/rockfall-proposal.md —
	/// replaced the floating drop-logs). Lifecycle:
	///
	///   FALLING — gravity until the hull rests on solid ground or another
	///             rock. Splashes (and slows) when it plunges through the
	///             acid surface; DAMAGES and shoves any player it lands on
	///             (the drop is telegraphed by the spawner's marker).
	///   RESTING — completely static. No buoyancy, no springs: whether the
	///             rock offers footing is pure geometry — its cap protrudes
	///             wherever the pile is taller than the local depth. If the
	///             rock it rests on erodes away, it falls again.
	///
	/// The sibling <see cref="ErodibleSurface"/> chews the submerged faces at
	/// the slow stone rate, so cairn islands persist about a loop before the
	/// sea reclaims them — the archipelago churns.
	/// </summary>
	public class FallingRock : Component, IUpdatable
	{
		public readonly float Width;
		public readonly float Height;

		/// <summary>World Y of the top surface of the SURVIVING hull.</summary>
		public float TopY => Entity != null ? Entity.Transform.Position.Y + HullTopLocal : 0f;

		/// <summary>World Y of the bottom of the SURVIVING hull.</summary>
		public float BottomY => Entity != null ? Entity.Transform.Position.Y + HullBottomLocal : 0f;

		/// <summary>True once the rock has come to rest (on ground or a pile).</summary>
		public bool IsResting => !_falling;

		/// <summary>Invoked just before the entity is destroyed, for spawn-count bookkeeping.</summary>
		public Action OnDestroyed;

		private float HullTopLocal    => _erodible?.SolidTopLocalY    ?? -Height * 0.5f;
		private float HullBottomLocal => _erodible?.SolidBottomLocalY ??  Height * 0.5f;

		private readonly AcidSurface _acid;
		private ErodibleSurface _erodible;

		private bool  _falling = true;
		private bool  _settling;   // support eroded away → slump, don't plunge
		private float _velocityY;
		private bool  _splashed;
		private FallingRock _restingOn;

		// The TUMBLE: visual-only spin while airborne (collision stays the
		// axis-aligned cell mask). The rate is STEERED each frame so a whole
		// number of turns completes exactly at the projected impact — the rock
		// lands aligned with its rest pose, and the leftover angular momentum
		// feeds a damped spring that tips it past upright and rocks it back.
		private float _spinRadPerSec; // seeded PREFERRED rate (sign = direction; never changes)
		private float _spinMag;       // current steered magnitude
		private float _spin;          // accumulated render angle
		private float _spinVel;       // settle-spring angular velocity (post-landing)
		private int   _steerTurns;    // the turn count the steer is committed to (0 = unplanned)

		private static readonly Collider[] _rockSensor   = new Collider[6];
		// The steer's support projection spans hull-to-ground and can cross many
		// greedy-merged erosion colliders at once — give it real headroom so the
		// true support is never silently dropped from an overfull buffer.
		private static readonly Collider[] _supportSensor = new Collider[24];
		private static readonly Collider[] _playerSensor  = new Collider[4];
		private readonly HashSet<Entity> _victims = new HashSet<Entity>();

		private static readonly Color _rockColor = new Color(96, 92, 88);

		public FallingRock(float width, float height, AcidSurface acid)
		{
			Width  = width;
			Height = height;
			_acid  = acid;
		}

		public override void OnAddedToEntity()
		{
			// Destructible stone: the boulder texture's ALPHA defines the
			// shape — the erosion mask, colliders, and render all follow the
			// same irregular silhouette (a rock-shaped rock, not a brick).
			string texPath = Height >= 128f
				? Nez.Content.Sprites.Hazards.Rock_128
				: Nez.Content.Sprites.Hazards.Rock_96;
			var texture = Entity.Scene.Content.LoadTexture(texPath, premultiplyAlpha: true);
			_erodible = Entity.AddComponent(new ErodibleSurface(
				_acid, Width, Height, _rockColor,
				AcidConfig.RockErosionPassesPerSec, texture,
				maskFromTextureAlpha: true));
			_erodible.OnFullyEroded = () => OnDestroyed?.Invoke();

			// Tumble: a random spin direction/rate per boulder — pure visual
			// while airborne (the crush check and landing use the hull AABB).
			// Seeded by spawn order so stepped-mode E2E stays deterministic.
			int seed = ++_spawnCounter;
			float t = (seed % 17) / 16f;
			float mag = AcidConfig.RockSpinMinRadPerSec
				+ t * (AcidConfig.RockSpinMaxRadPerSec - AcidConfig.RockSpinMinRadPerSec);
			_spinRadPerSec = (seed % 2 == 0) ? mag : -mag;
			_spinMag       = mag;
		}

		private static int _spawnCounter;

		public void Update()
		{
			if (!_falling)
			{
				// Post-landing: a damped spring around the rest pose, seeded
				// with the airborne angular velocity — the boulder carries its
				// momentum into the ground, tips past upright, and rocks back.
				// Semi-implicit Euler; a hitstop dt=0 freezes the wobble with
				// everything else.
				if (MathF.Abs(_spin) > 0.005f || MathF.Abs(_spinVel) > 0.05f)
				{
					float sdt = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);
					_spinVel += (-AcidConfig.RockSettleOmega * AcidConfig.RockSettleOmega * _spin
						- 2f * AcidConfig.RockSettleDamping * AcidConfig.RockSettleOmega * _spinVel) * sdt;
					_spin    += _spinVel * sdt;
					_erodible.RenderRotation = _spin;
				}
				else if (_erodible.RenderRotation != 0f)
				{
					_spin    = 0f;
					_spinVel = 0f;
					_erodible.RenderRotation = 0f;
				}

				// Re-fall if the supporting rock died (or itself fell away).
				// Solid ground never dies, so only pile rests re-check.
				if (_restingOn != null
					&& (_restingOn.Entity == null || _restingOn.Entity.IsDestroyed || !_restingOn.IsResting))
				{
					// The pile SETTLES rather than re-plunging: a gravity
					// re-fall made the collider a hydraulic piston (see
					// AcidConfig.RockSettleSinkSpeed). No re-tumble either —
					// a slumping pile doesn't cartwheel.
					_restingOn = null;
					_falling   = true;
					_settling  = true;
					_velocityY = 0f;
				}
				else
				{
					return;
				}
			}
			float dt  = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);
			var   pos = Entity.Transform.Position;

			_velocityY += GameConstants.Hazards.RockFallGravity * dt;
			float maxSpeed = _settling
				? AcidConfig.RockSettleSinkSpeed
				: GameConstants.Hazards.RockFallMaxSpeed;

			// Plunging through the liquid: one splash at the surface, then the
			// stone keeps sinking at half speed (cheap drag — reads as heavy
			// mass pushing through, not floating).
			float bodySurface = _acid.GetBodySurfaceLevelAtX(pos.X, pos.Y - Height);
			bool  inLiquid    = pos.Y + HullBottomLocal >= bodySurface;
			if (inLiquid)
			{
				if (!_splashed)
				{
					_splashed = true;
					// Scaled: a full-speed impulse threw tier-killing tsunamis
					// during the contest loop (see AcidConfig.RockSplashScale).
					_acid.Disturb(pos.X, Width, _velocityY * AcidConfig.RockSplashScale);
				}
				maxSpeed *= 0.5f;
			}

			if (!_settling)
			{
				SteerSpin(pos, maxSpeed);
				_spin += (_spinRadPerSec < 0f ? -_spinMag : _spinMag) * dt;
				_erodible.RenderRotation = _spin;
			}

			_velocityY = Math.Min(_velocityY, maxSpeed);
			pos.Y     += _velocityY * dt;

			CrushPlayersInPath(pos);

			// Rest on the tallest RESTING rock under the hull, else solid ground.
			var below = FindRestingRockBelow(pos);
			if (below != null && pos.Y + HullBottomLocal >= below.TopY)
			{
				pos.Y      = below.TopY - HullBottomLocal;
				_restingOn = below;
				Land();
			}
			else
			{
				float groundY = AcidConfig.GroundYAt(pos.X);
				if (pos.Y + HullBottomLocal >= groundY)
				{
					pos.Y      = groundY - HullBottomLocal;
					_restingOn = null;
					Land();
				}
			}

			Entity.Transform.Position = pos;
		}

		private void Land()
		{
			_falling    = false;
			_velocityY  = 0f;
			_steerTurns = 0;
			if (!_settling)
			{
				// The steer landed us within a few degrees of the rest pose;
				// fold the whole turns away and hand the airborne angular
				// velocity to the settle spring. (A slumping pile never tumbled,
				// so it gets no wobble — it just comes to rest.)
				_spin    = MathF.IEEERemainder(_spin, MathF.Tau);
				_spinVel = _spinRadPerSec < 0f ? -_spinMag : _spinMag;
			}
			// The thud — small trauma; the telegraph already warned, the land
			// confirms. (Juice guidance: shake sells mass, keep it subtle.)
			Entity.Scene.FindComponentOfType<BrawlerCamera>()?.AddShake(0.25f);
		}

		/// <summary>
		/// Adjust the tumble rate so a WHOLE number of turns completes exactly at
		/// the projected impact — the boulder must be upright at rest (erosion
		/// mask and colliders are axis-aligned), and unwinding a leftover angle
		/// after impact either rewinds the tumble or whip-spins it. Re-solved
		/// every frame: pile growth and the liquid's slower terminal speed simply
		/// refresh the estimate. Commits to a turn count and keeps it while its
		/// required rate stays inside the steer bounds (hysteresis), so the rate
		/// drifts smoothly instead of hopping between plans.
		/// </summary>
		private void SteerSpin(Vector2 pos, float maxSpeed)
		{
			float tImpact = EstimateFallTime(
				FindSupportYBelow(pos) - (pos.Y + HullBottomLocal),
				_velocityY, GameConstants.Hazards.RockFallGravity, maxSpeed);
			if (tImpact <= AcidConfig.RockSpinSteerCutoffSeconds)
			{
				return; // Too close to impact — hold the committed plan.
			}

			float dir        = _spinRadPerSec < 0f ? -1f : 1f;
			float progressed = dir * _spin; // rotation completed, measured along the spin direction

			if (_steerTurns >= 1)
			{
				float needed = (MathF.Tau * _steerTurns - progressed) / tImpact;
				if (needed >= AcidConfig.RockSpinSteerMinRadPerSec
					&& needed <= AcidConfig.RockSpinSteerMaxRadPerSec)
				{
					_spinMag = needed;
					return;
				}
				_steerTurns = 0; // Plan no longer feasible (support moved) — re-pick.
			}

			// Pick the turn count whose required rate is feasible and closest to
			// this boulder's seeded preference.
			float preferred = MathF.Abs(_spinRadPerSec);
			float bestErr   = float.MaxValue;
			for (int k = 1; k <= 5; k++)
			{
				float needed = (MathF.Tau * k - progressed) / tImpact;
				if (needed <= 0f)
				{
					continue; // Already spun past this count.
				}
				float clamped = Math.Clamp(needed,
					AcidConfig.RockSpinSteerMinRadPerSec, AcidConfig.RockSpinSteerMaxRadPerSec);
				// Infeasibility (clamp distance) dominates preference distance.
				float err = MathF.Abs(clamped - preferred) + MathF.Abs(clamped - needed) * 10f;
				if (err < bestErr)
				{
					bestErr     = err;
					_steerTurns = k;
					_spinMag    = clamped;
				}
			}
		}

		/// <summary>
		/// World Y of the surface this rock would come to rest on if nothing
		/// changes: the highest RESTING rock under the hull's footprint, else
		/// solid ground. Feeds the tumble steer's time-to-impact projection.
		/// </summary>
		private float FindSupportYBelow(Vector2 pos)
		{
			float hullBottom = pos.Y + HullBottomLocal;
			float best       = AcidConfig.GroundYAt(pos.X);
			float span       = best - hullBottom;
			if (span <= 0f)
			{
				return best;
			}

			var sensorRect = new RectangleF(
				pos.X - Width * 0.5f + 4f, hullBottom, Width - 8f, span);
			int count = Physics.OverlapRectangleAll(ref sensorRect, _supportSensor, PhysicsLayers.Platforms);
			for (int i = 0; i < count; i++)
			{
				if (_supportSensor[i].Entity == Entity)
				{
					continue;
				}
				var other = _supportSensor[i].Entity?.GetComponent<FallingRock>();
				if (other == null || !other.IsResting)
				{
					continue;
				}
				if (other.TopY >= hullBottom - 2f && other.TopY < best)
				{
					best = other.TopY;
				}
			}
			return best;
		}

		/// <summary>
		/// Time for a body at speed <paramref name="v"/> to fall <paramref name="d"/>
		/// under gravity <paramref name="g"/> with terminal speed <paramref name="vmax"/> —
		/// closed-form kinematics (accelerate, then coast at terminal).
		/// </summary>
		private static float EstimateFallTime(float d, float v, float g, float vmax)
		{
			if (d <= 0f)
			{
				return 0f;
			}
			float t1 = Math.Max(0f, (vmax - v) / g);       // time left accelerating
			float d1 = v * t1 + 0.5f * g * t1 * t1;        // distance covered accelerating
			if (d1 >= d)
			{
				return (MathF.Sqrt(v * v + 2f * g * d) - v) / g;
			}
			return t1 + (d - d1) / vmax;
		}

		/// <summary>
		/// A falling boulder hurts: damage + a sideways shove + hitstun for any
		/// player overlapping the hull, once per player per drop. The spawner's
		/// telegraph (marker + lead time) is what keeps this fair.
		/// </summary>
		private void CrushPlayersInPath(Vector2 pos)
		{
			var rect = new RectangleF(
				pos.X - Width * 0.5f,
				pos.Y + HullTopLocal,
				Width,
				Math.Max(4f, HullBottomLocal - HullTopLocal));
			int count = Physics.OverlapRectangleAll(ref rect, _playerSensor, PhysicsLayers.Player);
			for (int i = 0; i < count; i++)
			{
				var pe = _playerSensor[i].Entity;
				if (pe == null || _victims.Contains(pe))
				{
					continue;
				}
				_victims.Add(pe);

				pe.GetComponent<Health>()?.TakeDamage((int)AcidConfig.RockImpactDamage);
				var body = pe.GetComponent<PhysicsBody>();
				if (body != null)
				{
					float side = pe.Transform.Position.X >= pos.X ? 1f : -1f;
					body.Velocity = new Vector2(
						side * AcidConfig.RockImpactKnockbackX,
						AcidConfig.RockImpactKnockbackY);
				}
				pe.GetComponent<Hitstun>()?.Trigger(0.25f);
				pe.GetComponent<HitFlash>()?.Trigger();
			}
		}

		private FallingRock FindRestingRockBelow(Vector2 pos)
		{
			var sensorRect = new RectangleF(
				pos.X - Width * 0.5f + 4f,
				pos.Y + HullBottomLocal - 2f,
				Width - 8f, 6f);

			int count = Physics.OverlapRectangleAll(ref sensorRect, _rockSensor, PhysicsLayers.Platforms);
			FallingRock best = null;
			float bestTopY = float.MaxValue;
			for (int i = 0; i < count; i++)
			{
				if (_rockSensor[i].Entity == Entity)
				{
					continue;
				}
				var other = _rockSensor[i].Entity?.GetComponent<FallingRock>();
				if (other == null || !other.IsResting)
				{
					continue;
				}
				if (other.TopY < bestTopY)
				{
					bestTopY = other.TopY;
					best = other;
				}
			}
			return best;
		}
	}
}
