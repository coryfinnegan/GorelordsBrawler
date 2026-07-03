using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// Drop-in log platform.
	///
	/// Lifecycle:
	///   FALLING  — gravity until the hull bottom reaches another platform, the
	///              acid surface, or dry ground
	///   FLOATING — Archimedes buoyancy on the SURVIVING hull (see below); the
	///              sibling ErodibleSurface chews the wetted wood until nothing
	///              is left and the entity destroys itself
	///
	/// Buoyancy reads the surviving-hull extent from the ErodibleSurface, not
	/// the nominal Height: with the nominal constant, a bottom-eaten log kept
	/// floating at full-hull depth — feeding fresh wood to the waterline
	/// non-stop (logs died in ~5 s) and visually hovering ABOVE the water once
	/// its wet rows were gone. Hull-tracked, the log rides lower as it thins
	/// and the visible wood always crosses the waterline.
	/// </summary>
	public class DynamicPlatform : Component, IUpdatable
	{
		public readonly float Width;
		public readonly float Height;

		/// <summary>World Y of the top surface of the SURVIVING hull.</summary>
		public float TopY => Entity != null ? Entity.Transform.Position.Y + HullTopLocal : 0f;

		// Surviving-hull extent (local to the entity center), nominal until the
		// erodible sibling exists / has eaten something.
		private float HullTopLocal    => _erodible?.SolidTopLocalY    ?? -Height * 0.5f;
		private float HullBottomLocal => _erodible?.SolidBottomLocalY ??  Height * 0.5f;
		private float HullHeight      => Math.Max(4f, _erodible?.SolidHeight ?? Height);

		/// <summary>Invoked just before the entity is destroyed, for spawn-count bookkeeping.</summary>
		public Action OnDestroyed;

		private readonly AcidSurface _acid;

		private ErodibleSurface _erodible;

		private bool  _isFloating;
		private float _velocityY;
		private float _angularVelocity;

		// Exponentially smoothed end-point surface samples (two-point float).
		// Raw per-frame samples off a churning particle pool jitter by whole
		// cells; feeding them straight into the spring made logs "bounce
		// everywhere" (functional-test bug). NaN = not yet seeded.
		private float _surfSmoothL = float.NaN;
		private float _surfSmoothR = float.NaN;
		private const float SurfaceSmoothing = 0.12f;   // per-frame lerp factor

		private DynamicPlatform _platformBelow;

		private static readonly Collider[] _playerSensor   = new Collider[8];
		private static readonly Collider[] _platformSensor = new Collider[4];
		private HashSet<Entity> _prevContacts = new HashSet<Entity>();
		private HashSet<Entity> _currContacts = new HashSet<Entity>();

		private static readonly Color _baseColor = new Color(139, 90, 43);
		private const float MaxVelocity = 600f;

		public DynamicPlatform(float width, float height, AcidSurface acid, bool skipFall = false)
		{
			Width       = width;
			Height      = height;
			_acid       = acid;
			_isFloating = skipFall;
		}

		/// <summary>Set initial vertical velocity before the first Update fires.</summary>
		public void Initialize(float initialVelocityY)
		{
			_velocityY = initialVelocityY;
		}

		public override void OnAddedToEntity()
		{
			// Swiss-cheese erosion + per-cell collision, at the LOG rate: a
			// floater keeps fresh hull at the waterline forever (no waterline
			// self-limit like a static tier), so its rate is ~4× slower to give
			// the late-game footing a ~20 s life (AcidConfig).
			_erodible = Entity.AddComponent(new ErodibleSurface(
				_acid, Width, Height, _baseColor,
				AcidConfig.LogErosionPassesPerSec));
			_erodible.OnFullyEroded = () => OnDestroyed?.Invoke();
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

			// No timed burn: the sibling ErodibleSurface eats the log cell-by-cell
			// where the acid laps it, fires OnFullyEroded → OnDestroyed, and
			// self-destructs. This component just handles fall + float.

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
			if (lower != null && pos.Y + HullBottomLocal >= lower.TopY)
			{
				LandOnPlatform(lower, ref pos);
				return;
			}

			// Splash-robust landing check: scan for the surface AT OR BELOW the
			// falling log (per-column local query). The old range query returned
			// the TOPMOST wet cell — a stray spray droplet high in the air would
			// read as "the surface", and the log would land on it mid-flight.
			// Clamped by the SOLID GROUND at this column: a dry column otherwise
			// reads "surface = map bottom" and the log tunnels through the banks.
			float surface = MathHelper.Min(
				_acid.GetLocalSurfaceLevelAtX(pos.X, pos.Y),
				Constants.AcidConfig.GroundYAt(pos.X));
			if (pos.Y + HullBottomLocal >= surface)
			{
				_isFloating = true;
				pos.Y       = surface - HullBottomLocal;
				_acid.Disturb(pos.X, Width, _velocityY);
				// The splash absorbs most of the fall energy — keep only a
				// fraction of the plunge velocity for the float spring, so the
				// log dips, bobs once, and settles instead of rocketing back out.
				_velocityY *= GameConstants.Hazards.WaterEntryVelocityRetention;
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
			pos.Y          = lower.TopY - HullBottomLocal;

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
			// All equilibria are written against the SURVIVING hull: as the acid
			// eats the bottom rows the hull bottom rises in local space, so the
			// log settles deeper (less freeboard) instead of hovering where the
			// eaten wood used to be.
			float hullBottom = HullBottomLocal;
			float hullH      = HullHeight;
			float targetTiltDeg = 0f;

			if (_platformBelow != null)
			{
				// Stacked on another platform: stiff flat spring to its top.
				float eqY  = _platformBelow.TopY - hullBottom;
				float disp = pos.Y - eqY;
				float force = -GameConstants.Hazards.SpringK  * disp
				              - GameConstants.Hazards.Damping * _velocityY;
				_velocityY += force * dt;
			}
			else
			{
				// FREE-surface samples just OUTSIDE each hull end — spray landing
				// on the log's own back doesn't poison the query, and the hull's
				// own displacement doesn't distort the reading (around-the-hull
				// sampling, per the boat-water model). Acid surface and solid
				// ground are sampled SEPARATELY: ground decides where a log rests
				// on a dry bank; the acid decides the waterline it floats at.
				float endOffset = Width * 0.5f + 10f;
				float ceiling   = pos.Y - Height;
				float acidL   = _acid.GetLocalSurfaceLevelAtX(pos.X - endOffset, ceiling);
				float acidR   = _acid.GetLocalSurfaceLevelAtX(pos.X + endOffset, ceiling);
				float groundY = MathHelper.Min(
					Constants.AcidConfig.GroundYAt(pos.X - endOffset),
					Constants.AcidConfig.GroundYAt(pos.X + endOffset));

				if (float.IsNaN(_surfSmoothL))
				{
					_surfSmoothL = acidL;
					_surfSmoothR = acidR;
				}
				_surfSmoothL = MathHelper.Lerp(_surfSmoothL, acidL, SurfaceSmoothing);
				_surfSmoothR = MathHelper.Lerp(_surfSmoothR, acidR, SurfaceSmoothing);
				float waterline = (_surfSmoothL + _surfSmoothR) * 0.5f;

				// Floating when the acid would hold the log ABOVE the ground it
				// would otherwise rest on (the buoyant equilibrium sits higher
				// than the dry-rest equilibrium).
				float restDepth   = hullH * GameConstants.Hazards.BuoyancyRestFraction;
				float buoyantEqY  = waterline + restDepth - hullBottom;   // center Y at rest, hull partly under
				float groundEqY   = groundY - hullBottom;
				bool  floating    = buoyantEqY < groundEqY - 1f;

				if (floating)
				{
					// ── Archimedes buoyancy ──────────────────────────────────
					// Buoyant force ∝ submerged depth; gravity balances it at
					// restDepth submerged, so the log settles PARTIALLY UNDER the
					// waterline (it crosses the hull) instead of perched on top.
					// Damped so it dips on entry, bobs once, and comes to rest.
					float depth = (pos.Y + hullBottom) - waterline;   // >0 = hull bottom below surface
					float k     = GameConstants.Hazards.BuoyancyGravity / restDepth;
					float accel = k * (restDepth - depth)
					            - GameConstants.Hazards.BuoyancyDamping * _velocityY;
					_velocityY += accel * dt;

					// Tilt rides the acid slope while afloat.
					targetTiltDeg = MathHelper.ToDegrees(
						MathF.Atan2(_surfSmoothR - _surfSmoothL, endOffset * 2f));
				}
				else
				{
					// Rest flat ON dry ground (stiff spring to the ground top).
					float disp = pos.Y - groundEqY;
					float force = -GameConstants.Hazards.SpringK  * disp
					              - GameConstants.Hazards.Damping * _velocityY;
					_velocityY += force * dt;
				}

				targetTiltDeg = MathHelper.Clamp(targetTiltDeg,
					-GameConstants.Hazards.MaxTiltDegrees,
					 GameConstants.Hazards.MaxTiltDegrees);
			}

			_velocityY = MathHelper.Clamp(_velocityY, -MaxVelocity, MaxVelocity);
			pos.Y     += _velocityY * dt;

			// Angular spring toward the SURFACE SLOPE (not toward level) —
			// visual only, the collider stays axis-aligned.
			float rotDeg = Entity.Transform.RotationDegrees;
			_angularVelocity += (GameConstants.Hazards.AngularSpringK * (targetTiltDeg - rotDeg)
			                     - GameConstants.Hazards.AngularDamping * _angularVelocity) * dt;
			rotDeg += _angularVelocity * dt;
			rotDeg  = MathHelper.Clamp(rotDeg,
				-GameConstants.Hazards.MaxTiltDegrees,
				 GameConstants.Hazards.MaxTiltDegrees);
			Entity.Transform.RotationDegrees = rotDeg;

			CheckPlayerContacts(pos);
		}

		// ── Player contact / landing impulse ─────────────────────────────────

		private void CheckPlayerContacts(Vector2 pos)
		{
			var tmp = _prevContacts;
			_prevContacts = _currContacts;
			_currContacts = tmp;
			_currContacts.Clear();

			float topY = pos.Y + HullTopLocal;
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
