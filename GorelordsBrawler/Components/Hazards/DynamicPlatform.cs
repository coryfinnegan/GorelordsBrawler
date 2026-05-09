using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// Platform that either falls from the sky (drop-in) or is spawned at the
	/// acid surface (TMX float-out).
	///
	/// Drop-in lifecycle:
	///   FALLING  — gravity until the bottom edge reaches another platform or the acid surface
	///   FLOATING — underdamped spring; equilibrium is lower platform top or acid surface
	///   BURNING  — alpha fade, then Entity.Destroy()
	///
	/// TMX lifecycle (skipFall = true):
	///   Starts in FLOATING directly. AcidPhaseManager drives StartBurning().
	/// </summary>
	public class DynamicPlatform : Component, IUpdatable
	{
		public readonly float Width;
		public readonly float Height;

		/// <summary>World Y of the top surface of this platform.</summary>
		public float TopY => Entity != null ? Entity.Transform.Position.Y - Height * 0.5f : 0f;

		/// <summary>Invoked just before the entity is destroyed, for spawn-count bookkeeping.</summary>
		public Action OnDestroyed;

		private readonly float       _burnDuration;
		private readonly AcidSurface _acid;
		private readonly float       _autoBurnDelay;  // -1 = no auto-burn (TMX managed externally)

		private PrototypeSpriteRenderer _renderer;
		private bool  _isBurning;
		private float _burnTimer;

		private bool  _isFloating;
		private float _velocityY;
		private float _angularVelocity;
		private float _autoBurnAccum;

		private DynamicPlatform _platformBelow;

		private static readonly Collider[] _playerSensor   = new Collider[8];
		private static readonly Collider[] _platformSensor = new Collider[4];
		private HashSet<Entity> _prevContacts = new HashSet<Entity>();
		private HashSet<Entity> _currContacts = new HashSet<Entity>();

		private static readonly Color _baseColor = new Color(139, 90, 43);
		private const float MaxVelocity = 600f;

		/// <param name="autoBurnDelay">
		/// Seconds after landing before auto-burn begins. Pass -1 (default) for TMX platforms
		/// whose burn is triggered externally by AcidPhaseManager.
		/// </param>
		public DynamicPlatform(float width, float height, float burnDuration,
			AcidSurface acid, bool skipFall = false, float autoBurnDelay = -1f)
		{
			Width          = width;
			Height         = height;
			_burnDuration  = burnDuration;
			_acid          = acid;
			_isFloating    = skipFall;
			_autoBurnDelay = autoBurnDelay;
		}

		/// <summary>Set initial vertical velocity before the first Update fires.</summary>
		public void Initialize(float initialVelocityY)
		{
			_velocityY = initialVelocityY;
		}

		public override void OnAddedToEntity()
		{
			_renderer       = Entity.AddComponent(new PrototypeSpriteRenderer(Width, Height));
			_renderer.Color = _baseColor;

			var collider = Entity.AddComponent(new BoxCollider(Width, Height));
			collider.PhysicsLayer = PhysicsLayers.Platforms;
			collider.ShouldColliderScaleAndRotateWithTransform = false;
		}

		public void StartBurning()
		{
			if (_isBurning) return;
			_isBurning = true;
			_burnTimer = _burnDuration;
		}

		/// <summary>External velocity impulse — used when another platform lands on top of this one.</summary>
		public void ApplyImpulse(float verticalImpulse, float angularImpulse)
		{
			_velocityY       += verticalImpulse;
			_angularVelocity += angularImpulse;
		}

		public void Update()
		{
			float dt = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);

			if (_isBurning)
			{
				_burnTimer -= dt;
				float t = Math.Max(0f, _burnTimer / _burnDuration);
				_renderer.Color = Color.Lerp(Color.Transparent, _baseColor, t);
				if (_burnTimer <= 0)
				{
					OnDestroyed?.Invoke();
					Entity.Destroy();
					return;
				}
			}

			var pos = Entity.Transform.Position;

			if (!_isFloating)
				UpdateFall(ref pos, dt);
			else
				UpdateFloat(ref pos, dt);

			Entity.Transform.Position = pos;
		}

		// ── Fall phase ────────────────────────────────────────────────────────

		private void UpdateFall(ref Vector2 pos, float dt)
		{
			_velocityY += GameConstants.Hazards.PlatformFallGravity * dt;
			_velocityY  = Math.Min(_velocityY, GameConstants.Hazards.PlatformFallMaxSpeed);
			pos.Y      += _velocityY * dt;

			// Check for a floating platform below before checking the acid surface (stacking).
			var lower = FindPlatformBelow(pos);
			if (lower != null && pos.Y + Height * 0.5f >= lower.TopY)
			{
				LandOnPlatform(lower, ref pos);
				return;
			}

			float surface = _acid.GetSurfaceLevelInRange(
				pos.X - Width * 0.5f, pos.X + Width * 0.5f);
			if (pos.Y + Height * 0.5f >= surface)
			{
				_isFloating = true;
				pos.Y       = surface - Height * 0.5f;
				_acid.Disturb(pos.X, Width, _velocityY);
			}
		}

		private DynamicPlatform FindPlatformBelow(Vector2 pos)
		{
			// Thin sensor strip at the bottom edge of the falling platform.
			var sensorRect = new RectangleF(
				pos.X - Width * 0.5f,
				pos.Y + Height * 0.5f - 2f,
				Width, 4f);

			int count = Physics.OverlapRectangleAll(ref sensorRect, _platformSensor, PhysicsLayers.Platforms);
			DynamicPlatform best = null;
			float bestTopY = float.MaxValue;

			for (int i = 0; i < count; i++)
			{
				if (_platformSensor[i].Entity == Entity) continue;
				var other = _platformSensor[i].Entity?.GetComponent<DynamicPlatform>();
				// Only land on a platform that is already floating (not also falling).
				if (other == null || !other._isFloating) continue;
				if (other.TopY < bestTopY)
				{
					bestTopY = other.TopY;
					best = other;
				}
			}
			return best;
		}

		private void LandOnPlatform(DynamicPlatform lower, ref Vector2 pos)
		{
			_isFloating    = true;
			_platformBelow = lower;
			pos.Y          = lower.TopY - Height * 0.5f;

			// Transfer a fraction of the impact velocity into the lower platform.
			float offsetX = pos.X - lower.Entity.Transform.Position.X;
			float impulse = _velocityY * GameConstants.Hazards.PlatformImpactFactor;
			lower.ApplyImpulse(impulse, impulse * offsetX / (lower.Width * 4f));

			_velocityY = 0f;

			lower.OnDestroyed += OnPlatformBelowDestroyed;
		}

		private void OnPlatformBelowDestroyed()
		{
			if (_platformBelow != null)
				_platformBelow.OnDestroyed -= OnPlatformBelowDestroyed;
			_platformBelow = null;
			// Platform now falls/floats on the acid directly.
		}

		// ── Float phase (underdamped spring, ζ ≈ 0.5 → 1–2 visible bounces) ─

		private void UpdateFloat(ref Vector2 pos, float dt)
		{
			float equilibriumY;
			if (_platformBelow != null)
			{
				// Stacked: sit on top of the lower platform.
				equilibriumY = _platformBelow.TopY - Height * 0.5f;
			}
			else
			{
				equilibriumY = _acid.GetSurfaceLevelInRange(
					pos.X - Width * 0.5f - AcidSurface.CellSize,
					pos.X + Width * 0.5f + AcidSurface.CellSize) - Height * 0.5f;
			}

			float displacement = pos.Y - equilibriumY;
			float force        = -GameConstants.Hazards.SpringK  * displacement
			                     - GameConstants.Hazards.Damping * _velocityY;
			_velocityY += force * dt;
			_velocityY  = MathHelper.Clamp(_velocityY, -MaxVelocity, MaxVelocity);
			pos.Y      += _velocityY * dt;

			// Angular tilt (visual only — collider stays axis-aligned)
			float rotDeg = Entity.Transform.RotationDegrees;
			_angularVelocity += (-GameConstants.Hazards.AngularSpringK * rotDeg
			                     - GameConstants.Hazards.AngularDamping * _angularVelocity) * dt;
			rotDeg += _angularVelocity * dt;
			rotDeg  = MathHelper.Clamp(rotDeg,
				-GameConstants.Hazards.MaxTiltDegrees,
				 GameConstants.Hazards.MaxTiltDegrees);
			Entity.Transform.RotationDegrees = rotDeg;

			CheckPlayerContacts(pos);

			// Drop-in auto-burn: count from first landing moment.
			if (_autoBurnDelay >= 0 && !_isBurning)
			{
				_autoBurnAccum += dt;
				if (_autoBurnAccum >= _autoBurnDelay)
					StartBurning();
			}
		}

		// ── Player contact / landing impulse ─────────────────────────────────

		private void CheckPlayerContacts(Vector2 pos)
		{
			var tmp = _prevContacts;
			_prevContacts = _currContacts;
			_currContacts = tmp;
			_currContacts.Clear();

			float topY = pos.Y - Height * 0.5f;
			var sensor = new RectangleF(pos.X - Width * 0.5f, topY - 3f, Width, 6f);
			int count  = Physics.OverlapRectangleAll(ref sensor, _playerSensor, PhysicsLayers.Player);

			for (int i = 0; i < count; i++)
			{
				var pe   = _playerSensor[i].Entity;
				var body = pe?.GetComponent<PhysicsBody>();
				if (body == null || !body.Grounded) continue;
				_currContacts.Add(pe);
			}

			foreach (var pe in _currContacts)
			{
				if (!_prevContacts.Contains(pe))
					ApplyLandingImpulse(pe, pos);
			}
		}

		private void ApplyLandingImpulse(Entity player, Vector2 pos)
		{
			float offsetX     = player.Transform.Position.X - pos.X;
			_velocityY       += GameConstants.Hazards.LandingImpulse;
			_angularVelocity += GameConstants.Hazards.LandingImpulse * offsetX / (Width * 4f);
			_acid.Disturb(player.Transform.Position.X, 24f, 100f);
		}
	}
}
