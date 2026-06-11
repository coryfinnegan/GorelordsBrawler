using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	/// <summary>
	/// Phase B escape mechanic: while submerged in acid, mashing JUMP claws the
	/// fighter toward the surface, and a press NEAR the surface leaps them out.
	/// Two regimes by depth:
	///
	///   • DEEP (depth &gt; <see cref="GameConstants.Hazards.SwimBreachDepth"/>):
	///     each PRESS is one stroke — upward velocity set to
	///     <see cref="GameConstants.Hazards.SwimStrokeImpulse"/>, clamped at
	///     <see cref="GameConstants.Hazards.SwimMaxRiseSpeed"/> so a buffered hold
	///     can't accumulate. Deliberately a frantic mash, not a held thrust.
	///
	///   • NEAR-SURFACE (depth ≤ breach depth): the press is a BREACH — a full
	///     jump out of the water at the character's own JumpSpeed, with JumpHeld
	///     semantics (hold to sustain the arc, release to short-hop, exactly like
	///     a ground jump). Without this the exit is luck-gated: a cresting body
	///     bobs in a ~2-frame dry window (short-hop gravity slams it back under),
	///     so mash presses land on wet frames and read as feeble strokes — found
	///     by the DeepKnockIn E2E frame trace. Standard water-exit design
	///     (Mario / Terraria: the surface jump is a real jump).
	///
	/// This is the counterpart to depth-scaled acid damage (ContactHazard): the
	/// deeper and more hurt you are, the more strokes it takes to reach breach
	/// range and the faster the acid bites — the exit always exists but the
	/// window shrinks. "Deadly but escapable."
	///
	/// Wired in <see cref="Scenes.ArenaScene"/> alongside <see cref="SubmersionFeel"/>
	/// (not CharacterFactory) because the acid dependency only exists at scene level.
	/// Reads <see cref="SubmersionFeel.IsSubmerged"/>/<see cref="SubmersionFeel.SubmergedDepth"/>,
	/// refreshed earlier in the frame (SubmersionFeel.UpdateOrder = -10).
	///
	/// IMPORTANT: JumpAbility suppresses its own ground-/air-jump while submerged
	/// (it checks the same SubmersionFeel), so the jump button belongs exclusively
	/// to this component underwater — no double-apply, no double-jumping out.
	/// </summary>
	public class SwimAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody    _body;
		private Hitstun        _hitstun;
		private SubmersionFeel _submersion;
		private MovementStats  _movement;

		public SwimAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body       = Entity.GetComponent<PhysicsBody>();
			_hitstun    = Entity.GetComponent<Hitstun>();
			_submersion = Entity.GetComponent<SubmersionFeel>();
			_movement   = Entity.GetComponent<MovementStats>();
		}

		public void Update()
		{
			if (_body == null || _submersion == null)
			{
				return;
			}

			// Only swim while actually in the acid. On dry land JumpAbility owns
			// the button (it gates itself off while submerged).
			if (!_submersion.IsSubmerged)
			{
				return;
			}

			// Stunned fighters can't act — same rule the other abilities follow,
			// and it keeps a knock-in juggle honest (you eat the first bite before
			// you can start clawing out).
			if (_hitstun != null && _hitstun.IsActive)
			{
				return;
			}

			if (_input.Jump.IsPressed)
			{
				if (_submersion.SubmergedDepth <= GameConstants.Hazards.SwimBreachDepth
					&& _movement != null)
				{
					// BREACH — leap out of the water at full jump strength. JumpHeld
					// gives the normal hold-to-rise / release-to-short-hop contract,
					// so a player who commits to the escape press clears the basin
					// lip, while a pure panic-masher pops shorter arcs.
					_body.Velocity.Y = -_movement.JumpSpeed;
					_body.JumpHeld   = true;
				}
				else
				{
					// STROKE — set (don't add) upward velocity, never reducing an
					// already-faster ascent: Min on the signed Y (up is negative)
					// keeps the strongest upward motion. Clamped so repeated strokes
					// plateau at a sane climb rate.
					_body.Velocity.Y = MathHelper.Min(
						_body.Velocity.Y, -GameConstants.Hazards.SwimStrokeImpulse);
					if (_body.Velocity.Y < -GameConstants.Hazards.SwimMaxRiseSpeed)
					{
						_body.Velocity.Y = -GameConstants.Hazards.SwimMaxRiseSpeed;
					}
				}

				_input.Jump.ConsumeBuffer();
			}
		}
	}
}
