using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public enum LedgeState
	{
		None,
		Hanging,
		Climbing
	}

	/// <summary>
	/// Detects platform ledges, snaps the character to a hang position, and handles
	/// input during hang (climb, drop, jump-off, attack-off) and climb completion.
	/// </summary>
	public class LedgeHangAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody _body;
		private CharacterStats _stats;
		private MovementStats _movement;
		private Hitstun _hitstun;
		private CombatController _combat;
		private BoxCollider _collider;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;

		// Cached distance from entity center to sprite top (character's visual hands).
		// Computed once from frame height * SpriteScale - bodyHalfH.
		// Used so ledge detection triggers when the character's visible hands reach the
		// platform edge, not when the smaller physics collider top reaches it.
		private float _handOffset;

		private LedgeState _state;
		private float _hangTimer;
		private int _ledgeDirection;       // +1 = hanging on left edge (facing right), -1 = hanging on right edge (facing left)
		private Vector2 _climbTarget;      // where to teleport after climb completes
		private float _regrabCooldown;     // prevents immediate re-grab after release
		private Collider _lastGrabbedEdge; // the platform collider we last grabbed
		private float _lastEdgeX;          // X coord of the grabbed edge

		/// <summary>True while hanging on a ledge (not climbing).</summary>
		public bool IsHanging => _state == LedgeState.Hanging;

		/// <summary>True while hanging or climbing (any ledge interaction).</summary>
		public bool IsOnLedge => _state != LedgeState.None;

		/// <summary>True while the climb animation is playing.</summary>
		public bool IsClimbing => _state == LedgeState.Climbing;

		/// <summary>Direction the character faces while on the ledge (+1 right, -1 left).</summary>
		public int LedgeDirection => _ledgeDirection;

		public LedgeHangAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_stats = Entity.GetComponent<CharacterStats>();
			_movement = Entity.GetComponent<MovementStats>();
			_hitstun = Entity.GetComponent<Hitstun>();
			_collider = Entity.GetComponent<BoxCollider>();
			_animator = Entity.GetComponent<SpriteAnimator>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_handOffset = ComputeHandOffset();
		}

		// Returns the distance from entity center up to the character's visual hands
		// (sprite top). Uses the first locomotion animation frame height * SpriteScale.
		// Falls back to bodyHalfH so detection still works without a sprite.
		private float ComputeHandOffset()
		{
			float bodyHalfH = _stats != null ? _stats.BodyHeight / 2f : 24f;

			if (_animator == null || _spriteData == null)
				return bodyHalfH;

			float scale = _spriteData.SpriteScale;
			string prefix = _spriteData.AnimPrefix ?? "";

			// Prefer a locomotion-sized animation so attack atlases (different frame sizes) don't skew the result
			string[] candidateKeys = string.IsNullOrEmpty(prefix)
				? new[] { "LedgeIdle", "Idle" }
				: new[] { $"{prefix}_LedgeIdle", $"{prefix}_Idle" };

			float gripY = _spriteData.LedgeGripPixelFromTop;

			foreach (var key in candidateKeys)
			{
				if (_animator.Animations.TryGetValue(key, out var anim) && anim.Sprites.Length > 0)
				{
					float frameH = anim.Sprites[0].SourceRect.Height;
					return (frameH - gripY) * scale - bodyHalfH;
				}
			}

			return bodyHalfH;
		}

		public void Update()
		{
			// Lazy resolve: CombatController may be added after this component
			if (_combat == null)
			{
				_combat = Entity.GetComponent<CombatController>();
			}

			_regrabCooldown -= Time.DeltaTime;

			switch (_state)
			{
				case LedgeState.None:
					TryDetectLedge();
					break;
				case LedgeState.Hanging:
					UpdateHanging();
					break;
				case LedgeState.Climbing:
					// Climbing is driven by LocomotionAnimator — just hold position
					break;
			}
		}

		/// <summary>
		/// Called by LocomotionAnimator when the climb animation completes.
		/// Teleports the character on top of the platform and resumes physics.
		/// </summary>
		public void OnClimbAnimationCompleted()
		{
			if (_state != LedgeState.Climbing)
			{
				return;
			}

			RestoreLocalOffset();
			Entity.Transform.Position = _climbTarget;
			_body.SuspendPhysics = false;
			_body.Velocity = Vector2.Zero;
			_body.Grounded = true;
			_body.HasAerialAction = true;
			_state = LedgeState.None;
		}

		/// <summary>
		/// Force-releases from ledge. Called by RespawnHandler on death.
		/// </summary>
		public void ForceRelease()
		{
			if (_state == LedgeState.None)
			{
				return;
			}

			RestoreLocalOffset();
			_body.SuspendPhysics = false;
			_state = LedgeState.None;
			_regrabCooldown = GameConstants.Ledge.RegrabCooldown;
		}

		private void RestoreLocalOffset()
		{
			if (_animator != null)
				_animator.LocalOffset = new Vector2(0, _stats != null ? _stats.BodyHeight / 2f : 24f);
		}

		private void TryDetectLedge()
		{
			// Only detect when airborne
			if (_body.Grounded)
			{
				return;
			}

			// Don't grab during hitstun
			if (_hitstun != null && _hitstun.IsActive)
			{
				return;
			}

			// Don't grab during attacks
			if (_combat != null && _combat.IsAttacking)
			{
				return;
			}

			if (_collider == null)
			{
				return;
			}

			var grabRangeX = GameConstants.Ledge.GrabRangeX;
			var grabRangeY = GameConstants.Ledge.GrabRangeY;
			float bodyHalfH = _stats != null ? _stats.BodyHeight / 2f : 24f;

			// Search rect covers the full vertical range where the character's hands
			// (_handOffset above center) could be near a platform top
			var searchRect = new RectangleF(
				Entity.Transform.Position.X - grabRangeX - 20f,
				Entity.Transform.Position.Y - _handOffset - grabRangeY,
				(grabRangeX + 20f) * 2f,
				_handOffset + bodyHalfH + grabRangeY);

			var platforms = Physics.BoxcastBroadphase(searchRect, PhysicsLayers.Platforms);

			float charX = Entity.Transform.Position.X;
			// Use the sprite's visual top (hands) for vertical proximity, not the collider top
			float charHandsY = Entity.Transform.Position.Y - _handOffset;

			Collider bestPlatform = null;
			float bestDist = float.MaxValue;
			int bestDirection = 0;
			float bestEdgeX = 0;

			foreach (var platform in platforms)
			{
				// Skip if on regrab cooldown for this specific platform
				if (_regrabCooldown > 0 && platform == _lastGrabbedEdge)
				{
					continue;
				}

				var bounds = platform.Bounds;
				float platTop = bounds.Top;
				float platLeft = bounds.Left;
				float platRight = bounds.Right;

				// Skip walls and ground — only grab floating platforms
				// Walls are tall and thin, ground spans the full width.
				// A grabbable platform is relatively short (< 4 body heights).
				float platHeight = bounds.Height;
				if (platHeight > (_stats != null ? _stats.BodyHeight * 4f : 200f))
				{
					continue;
				}

				// Character's visual hands must be within grabRangeY of the platform top
				float vertDist = Math.Abs(charHandsY - platTop);
				if (vertDist > grabRangeY)
				{
					continue;
				}

				// Check left edge — character is to the left of the platform
				float distToLeft = Math.Abs(charX - platLeft);
				if (charX < platLeft && distToLeft < grabRangeX)
				{
					float totalDist = distToLeft + vertDist;
					if (totalDist < bestDist)
					{
						bestDist = totalDist;
						bestPlatform = platform;
						bestDirection = 1; // face right (toward platform)
						bestEdgeX = platLeft;
					}
				}

				// Check right edge — character is to the right of the platform
				float distToRight = Math.Abs(charX - platRight);
				if (charX > platRight && distToRight < grabRangeX)
				{
					float totalDist = distToRight + vertDist;
					if (totalDist < bestDist)
					{
						bestDist = totalDist;
						bestPlatform = platform;
						bestDirection = -1; // face left (toward platform)
						bestEdgeX = platRight;
					}
				}
			}

			if (bestPlatform == null)
			{
				return;
			}

			// Snap entity center below the platform so the physics collider fits naturally.
			// Then shift LocalOffset so the sprite's visual top (hands) aligns with the edge.
			float hangOffsetX = GameConstants.Ledge.HangOffsetX;
			float hangX = bestEdgeX - bestDirection * hangOffsetX;
			float hangY = bestPlatform.Bounds.Top + bodyHalfH;

			Entity.Transform.Position = new Vector2(hangX, hangY);
			// Shift sprite down so the character's visual hands appear at the platform edge
			if (_animator != null)
				_animator.LocalOffset = new Vector2(0, _handOffset);

			_body.Velocity = Vector2.Zero;
			_body.SuspendPhysics = true;
			_body.FacingDirection = bestDirection;
			_body.Grounded = false;
			_body.FastFalling = false;

			_state = LedgeState.Hanging;
			_hangTimer = 0f;
			_ledgeDirection = bestDirection;
			_lastGrabbedEdge = bestPlatform;
			_lastEdgeX = bestEdgeX;

			// Compute climb target: on top of the platform, inward from the edge
			float climbX = bestEdgeX + bestDirection * (_stats != null ? _stats.BodyWidth / 2f : 8f);
			float climbY = bestPlatform.Bounds.Top - bodyHalfH;
			_climbTarget = new Vector2(climbX, climbY);
		}

		private void UpdateHanging()
		{
			_hangTimer += Time.DeltaTime;

			// Timeout — auto-drop, no aerial action (punishing)
			if (_hangTimer >= GameConstants.Ledge.HangTimeout)
			{
				Release(grantAerialAction: false);
				return;
			}

			int dirX = _input.MoveX.Value;
			int dirY = _input.MoveY != null ? _input.MoveY.Value : 0;

			// Grace period: ignore directional input briefly after grab to prevent
			// instant climb/drop when the player is holding toward the platform
			if (_hangTimer >= GameConstants.Ledge.HangGracePeriod)
			{
				// UP or TOWARD the platform — climb up
				if (dirY < 0 || dirX == _ledgeDirection)
				{
					_state = LedgeState.Climbing;
					return;
				}

				// DOWN or AWAY from the platform — punishing drop
				if (dirY > 0 || (dirX != 0 && dirX == -_ledgeDirection))
				{
					Release(grantAerialAction: false);
					return;
				}
			}

			// JUMP — release + jump (better option, grants aerial action)
			if (_input.Jump.IsPressed)
			{
				_body.HasAerialAction = true;
				Release(grantAerialAction: true);
				_body.Velocity.Y = -_movement.JumpSpeed;
				_body.JumpHeld = true;
				_input.Jump.ConsumeBuffer();
				return;
			}

			// ATTACK — release + aerial attack
			if (_input.Attack.IsPressed)
			{
				Release(grantAerialAction: true);
				// The attack will be picked up by CombatController on the next frame
				// since we've released and the attack buffer is active
				return;
			}
		}

		private void Release(bool grantAerialAction)
		{
			RestoreLocalOffset();
			_body.SuspendPhysics = false;
			_body.HasAerialAction = grantAerialAction;
			_state = LedgeState.None;
			_regrabCooldown = GameConstants.Ledge.RegrabCooldown;
		}
	}
}
