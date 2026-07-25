using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	/// <summary>
	/// The acid escape: while submerged, a jump press is a REAL jump — full
	/// character JumpSpeed, at any depth, with the normal hold-to-rise /
	/// release-to-short-hop contract. Combined with the buoyancy that
	/// <see cref="SubmersionFeel"/> writes onto the <see cref="PhysicsBody"/>
	/// (the body floats to the surface with no input at all), this is the
	/// Smash Bros. water model (ssbwiki.com/Swimming): swimming can't trap
	/// you, and the jump button always means "get out".
	///
	/// This replaced the mash-to-stroke + near-surface-breach design, which
	/// functional testing found inescapable in practice: strokes never set
	/// JumpHeld so the 3.5× short-hop gravity reduced each press to ~9 px of
	/// rise, and the breach gate depended on a depth reading the threshold-1
	/// spray query kept corrupting. The acid's threat is the depth-scaled DPS
	/// (ContactHazard) — deep dunks melt fast — not the exit being luck-gated.
	///
	/// Wired in <see cref="Scenes.ArenaScene"/> alongside <see cref="SubmersionFeel"/>
	/// (not CharacterFactory) because the acid dependency only exists at scene level.
	/// Reads <see cref="SubmersionFeel.IsSubmerged"/>, refreshed earlier in the
	/// frame (SubmersionFeel.UpdateOrder = -10).
	///
	/// IMPORTANT: JumpAbility suppresses its own ground-/air-jump while submerged
	/// (it checks the same SubmersionFeel), so the jump button belongs exclusively
	/// to this component underwater. Because it early-outs, the JumpHeld
	/// release/apex bookkeeping it normally does is mirrored here for the
	/// frames a jump arc spends inside the acid.
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
			if (_body == null || _submersion == null || _movement == null)
			{
				return;
			}

			// Only act while actually in the acid. On dry land JumpAbility owns
			// the button (it gates itself off while submerged).
			if (!_submersion.IsSubmerged)
			{
				return;
			}

			// Mirror JumpAbility's JumpHeld bookkeeping while it is gated off:
			// releasing the button (or passing the apex) must end the held-rise
			// state even when those frames happen underwater, or the arc after
			// exiting the acid would ignore a short-hop release.
			if (_body.JumpHeld && !_input.Jump.IsDown)
			{
				_body.JumpHeld = false;
			}
			if (_body.JumpHeld && _body.Velocity.Y >= 0)
			{
				_body.JumpHeld = false;
			}

			// Stunned fighters can't act — same rule the other abilities follow,
			// and it keeps a knock-in juggle honest (you eat the first bite before
			// you can start clawing out). Buoyancy still floats a stunned body.
			if (_hitstun != null && _hitstun.IsActive)
			{
				return;
			}

			if (_input.Jump.IsPressed)
			{
				// A full jump, from any depth. Buoyancy never brakes an ascent
				// faster than its rise cap, so the launch speed carries through
				// the remaining water and out — no aerial action is consumed,
				// the water banks it (SubmersionFeel), so a follow-up press in
				// the air is still a real double jump.
				_body.Velocity.Y = -_movement.JumpSpeed;
				_body.JumpHeld   = true;
				_input.Jump.ConsumeBuffer();
			}
		}
	}
}
