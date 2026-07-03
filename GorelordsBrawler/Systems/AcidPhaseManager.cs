using System;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// The acid match timeline (Phase C of docs/acid-arena-design-proposal.md):
	/// a looping, intensifying state machine.
	///
	///   Calm → Rise → Scramble → Surge → Drain ─┐
	///     ▲                                     │  Loop++ (escalation)
	///     └────────────── Rise ◄────────────────┘
	///
	/// …until <see cref="AcidConfig.TimeCapSeconds"/>, when the Surge phase
	/// diverts to the terminal STORM-flood (FinalFlood): the pour raises the
	/// standing surface to just under the MID tiers while recurring storm
	/// crests break over them and claw the TOP tiers down — the round resolves
	/// because the last footing crumbles, with Phase-B depth lethality ruling
	/// the water. Escalation is explicit per-loop curves (AcidConfig): each
	/// loop the rise reaches higher, POURS harder, surges come sooner and hit
	/// harder — the "crazier and crazier" macro-panic; the Phase-B swim escape
	/// is the per-knock-in micro counter-play.
	///
	/// Rise transitions fire when the MEASURED standing surface reaches the
	/// ceiling (<see cref="AcidSurface.AtFillCap"/> — closed-loop fill; the
	/// geometric count remains the safety stop). Never the full-width
	/// volumetric estimate, which is geometry-blind for a basin-shaped pool —
	/// the Phase-A lesson. All durations run through
	/// <see cref="AcidConfig.TimeScale"/> so DebugFastAcid compresses the loop
	/// by the same ×4 it boosts the pour.
	/// </summary>
	public class AcidPhaseManager : SceneComponent
	{
		public enum AcidPhase
		{
			Calm,
			Rise,
			Scramble,
			Surge,
			Drain,
			FinalFlood,
		}

		public AcidPhase Phase { get; private set; } = AcidPhase.Calm;

		/// <summary>Completed Rise→Drain cycles — drives the escalation curves.</summary>
		public int Loop { get; private set; }

		public bool IsDraining => Phase == AcidPhase.Drain;

		private readonly AcidSurface _acid;

		private float _phaseElapsed;
		private float _totalElapsed;
		private float _nextSurgeIn;
		private int   _surgesThisCycle;
		private float _nextCrestIn;   // storm-crest cadence (FinalFlood only)
		private float _fillHold;      // how long AtFillCap has held continuously (Rise)
		private bool  _tellArmed;     // telegraph fired for the upcoming surge/crest
		private bool  _riseStarted;   // valves opened (Rise begins with a telegraph rumble)

		public AcidPhaseManager(AcidSurface acid)
		{
			_acid = acid;
		}

		public override void Update()
		{
			float dt = Time.DeltaTime;
			float ts = AcidConfig.TimeScale();
			_phaseElapsed += dt;
			_totalElapsed += dt;

			switch (Phase)
			{
				case AcidPhase.Calm:
				{
					float delay = AppSettings.DebugFastAcid ? 3f : GameConstants.Hazards.AcidStartDelay;
					if (_phaseElapsed >= delay)
					{
						EnterRise();
					}
					break;
				}

				case AcidPhase.Rise:
				{
					// Rise opens on a telegraph (Brinstar's shake-before-the-
					// acid): the rumble/boil plays out first, THEN the valves
					// open. Phase D — every dynamic beat announces itself.
					if (!_riseStarted)
					{
						if (!_acid.TellActive)
						{
							_acid.Activate();
							_riseStarted = true;
						}
						break;
					}

					// The closed-loop fill's "surface reached the ceiling" must
					// HOLD before the handoff — one frame's reading can be a
					// wave transient even through the percentile probe, and a
					// premature handoff truncates the rising beat.
					if (_acid.AtFillCap)
					{
						_fillHold += dt;
						if (_fillHold >= AcidConfig.FillHoldSeconds * ts)
						{
							EnterScramble();
						}
					}
					else
					{
						_fillHold = 0f;
					}
					break;
				}

				case AcidPhase.Scramble:
				{
					if (_phaseElapsed >= AcidConfig.ScrambleDurationSeconds * ts)
					{
						EnterSurge();
					}
					break;
				}

				case AcidPhase.Surge:
				{
					_nextSurgeIn -= dt;
					// Arm the telegraph one lead ahead of every wave (Phase D):
					// the boil/pulse/rumble build, THEN the surge lands.
					if (!_tellArmed && _nextSurgeIn <= AcidConfig.SurgeTellSeconds * ts)
					{
						_acid.BeginTell(MathF.Max(_nextSurgeIn, 0.01f));
						_tellArmed = true;
					}
					if (_nextSurgeIn <= 0f)
					{
						_acid.TriggerSurge(AcidConfig.SurgeStrengthFor(Loop));
						_surgesThisCycle++;
						_nextSurgeIn = AcidConfig.SurgeIntervalFor(Loop) * ts;
						_tellArmed   = false;

						if (_surgesThisCycle >= AcidConfig.SurgesPerCycle)
						{
							// The time cap diverts the loop at its natural beat:
							// after a surge volley, instead of relief… the flood.
							if (_totalElapsed >= AcidConfig.TimeCapSeconds * ts)
							{
								EnterFinalFlood();
							}
							else
							{
								EnterDrain();
							}
						}
					}
					break;
				}

				case AcidPhase.Drain:
				{
					if (_acid.ParticleCount <= AcidConfig.ParticleCapForCeiling(AcidConfig.DrainCeilingY))
					{
						Loop++;
						EnterRise();
					}
					break;
				}

				case AcidPhase.FinalFlood:
				{
					// Terminal STORM: the pour submerges the MID tiers while
					// crests break above the sea on a fixed cadence, clawing at
					// the last refuges. No drain, no relief — but every crest
					// still telegraphs (Phase D), so the endgame stays readable.
					_nextCrestIn -= dt;
					if (!_tellArmed && _nextCrestIn <= AcidConfig.SurgeTellSeconds * ts)
					{
						_acid.BeginTell(MathF.Max(_nextCrestIn, 0.01f));
						_tellArmed = true;
					}
					if (_nextCrestIn <= 0f)
					{
						_acid.TriggerSurge(AcidConfig.StormSurgeStrength);
						_nextCrestIn = AcidConfig.StormCrestIntervalSeconds * ts;
						_tellArmed   = false;
					}
					break;
				}
			}
		}

		private void EnterRise()
		{
			Phase         = AcidPhase.Rise;
			_phaseElapsed = 0f;
			_fillHold     = 0f;
			_riseStarted  = false;
			_acid.Draining = false;
			// Ceiling AND flow escalate together: later loops pour a much larger
			// volume (lip → lapped tier is ~3× loop 0), so the flow scales to
			// keep every rise ~30 s — a stalling rise reads as a broken valve.
			// The valves OPEN only after the telegraph rumble (Rise case above).
			_acid.SetInletFlow(AcidConfig.InletFlowFor(Loop));
			_acid.SetFillCeiling(AcidConfig.RiseCeilingFor(Loop));
			_acid.BeginTell(AcidConfig.RiseTellSeconds * AcidConfig.TimeScale());
		}

		private void EnterScramble()
		{
			// Pour stays on: it tops the pool back up to the cap as splash
			// particles despawn off-map. (The log spawner is NOT phase-gated —
			// ArenaScene starts it the moment the acid dissolves the LOW tier
			// pair, per the functional-test decision: platforms arrive because
			// the acid ate the first footing, not because a timer elapsed.)
			Phase         = AcidPhase.Scramble;
			_phaseElapsed = 0f;
		}

		private void EnterSurge()
		{
			Phase            = AcidPhase.Surge;
			_phaseElapsed    = 0f;
			_surgesThisCycle = 0;
			// The first wave IS telegraphed: phase entry arms the tell and the
			// surge lands one lead later — entering Surge reads as "it's
			// winding up", not an instant ambush.
			_nextSurgeIn = AcidConfig.SurgeTellSeconds * AcidConfig.TimeScale();
			_acid.BeginTell(_nextSurgeIn);
			_tellArmed   = true;
		}

		private void EnterDrain()
		{
			Phase         = AcidPhase.Drain;
			_phaseElapsed = 0f;
			_acid.StopPour();
			// Duration-targeted: the sluice rate derives from the live surplus
			// so the relief beat is ~9 s every loop (a fixed rate made loop 0's
			// drain a 2.7 s blink). Duration is time-scaled here, like every
			// other phase timer.
			_acid.BeginDrain(AcidConfig.DrainCeilingY,
				AcidConfig.DrainDurationSeconds * AcidConfig.TimeScale());
		}

		private void EnterFinalFlood()
		{
			Phase         = AcidPhase.FinalFlood;
			_phaseElapsed = 0f;
			_acid.Draining = false;
			// Storm: hardest pour of the match up to the mid-submerging ceiling.
			// The first crest is telegraphed like every other (phase-entry tell,
			// wave one lead later); the pour itself starts immediately — the
			// rising sea IS the storm's announcement.
			_acid.SetInletFlow(AcidConfig.InletFlowFor(Loop) * AcidConfig.StormFlowMult);
			_acid.SetFillCeiling(AcidConfig.StormCeilingY);
			_acid.Activate();
			_nextCrestIn = AcidConfig.SurgeTellSeconds * AcidConfig.TimeScale();
			_acid.BeginTell(_nextCrestIn);
			_tellArmed   = true;
		}
	}
}
