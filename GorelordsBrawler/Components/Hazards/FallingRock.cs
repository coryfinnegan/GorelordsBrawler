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

		private static readonly Collider[] _rockSensor   = new Collider[6];
		private static readonly Collider[] _playerSensor = new Collider[4];
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
			// Destructible stone: erosion carves the submerged faces through a
			// stable texture (proportional mapping — same treatment as tiers).
			string texPath = Height >= 128f
				? Nez.Content.Sprites.Hazards.Rock_128
				: Nez.Content.Sprites.Hazards.Rock_96;
			var texture = Entity.Scene.Content.LoadTexture(texPath);
			_erodible = Entity.AddComponent(new ErodibleSurface(
				_acid, Width, Height, _rockColor,
				AcidConfig.RockErosionPassesPerSec, texture));
			_erodible.OnFullyEroded = () => OnDestroyed?.Invoke();
		}

		public void Update()
		{
			if (!_falling)
			{
				// Re-fall if the supporting rock died (or itself fell away).
				// Solid ground never dies, so only pile rests re-check.
				if (_restingOn != null
					&& (_restingOn.Entity == null || _restingOn.Entity.IsDestroyed || !_restingOn.IsResting))
				{
					// The pile SETTLES rather than re-plunging: a gravity
					// re-fall made the collider a hydraulic piston (see
					// AcidConfig.RockSettleSinkSpeed).
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
			_falling   = false;
			_velocityY = 0f;
			// The thud — small trauma; the telegraph already warned, the land
			// confirms. (Juice guidance: shake sells mass, keep it subtle.)
			Entity.Scene.FindComponentOfType<BrawlerCamera>()?.AddShake(0.25f);
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
