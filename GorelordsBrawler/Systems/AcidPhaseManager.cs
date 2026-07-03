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
					if (_nextSurgeIn <= 0f)
					{
						_acid.TriggerSurge(AcidConfig.SurgeStrengthFor(Loop));
						_surgesThisCycle++;
						_nextSurgeIn = AcidConfig.SurgeIntervalFor(Loop) * ts;

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
					// Terminal STORM: the pour holds the ceiling just under the
					// MID tiers while crests break above it on a fixed cadence —
					// each one wets (and so ERODES) the mid tiers hard and the
					// top tiers a little, so the last refuges crumble and the
					// round must resolve. No drain, no relief.
					_nextCrestIn -= dt;
					if (_nextCrestIn <= 0f)
					{
						_acid.TriggerSurge(AcidConfig.StormSurgeStrength);
						_nextCrestIn = AcidConfig.StormCrestIntervalSeconds * ts;
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
			_acid.Draining = false;
			// Ceiling AND flow escalate together: later loops pour a much larger
			// volume (lip → lapped tier is ~3× loop 0), so the flow scales to
			// keep every rise ~30 s — a stalling rise reads as a broken valve.
			_acid.SetInletFlow(AcidConfig.InletFlowFor(Loop));
			_acid.SetFillCeiling(AcidConfig.RiseCeilingFor(Loop));
			_acid.Activate();
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
			_nextSurgeIn     = 0f;   // first surge fires on the next tick — a visible phase-entry beat
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
			// Storm: hardest pour of the match up to the budget-honest ceiling,
			// first crest immediately (the phase-entry beat), then on cadence.
			_acid.SetInletFlow(AcidConfig.InletFlowFor(Loop) * AcidConfig.StormFlowMult);
			_acid.SetFillCeiling(AcidConfig.StormCeilingY);
			_acid.Activate();
			_nextCrestIn = 0f;
		}
	}
}
