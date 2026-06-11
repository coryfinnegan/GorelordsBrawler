using System.Threading.Tasks;
using GorelordsBrawler.E2E.Tests.Pages;
using Shouldly;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E coverage for Acid Arena Phase B: depth-scaled lethality and the swim escape,
/// driven through the scripted-input device in deterministic stepped mode.
///
/// Geometry these tests lean on (arena1.tmx "The Sump" + FutureAxe.json):
///   • Basin channel x∈[448,832], floor top y=736, pre-filled pool surface ≈ y 660s.
///   • Players are staged at x=560 — inside the basin but OFF the inlet column
///     (x≈640) so the pour never lands on the actor mid-test.
///   • FutureAxe: BodyHeight 48 (feet = y+24), JumpSpeed 800, MaxHp 120.
///
/// The JumpSpeed matters: a swim stroke sets vy ≈ -230, while the double-jump-bypass
/// bug (JumpAbility's air-jump firing underwater) would set vy = -800. The 3.5× gap
/// makes "is the jump button stroking or jumping?" a robust oracle, not a threshold game.
///
/// Depth truths come from the per-player oracles (submerged / submergedDepth), never
/// from assumed pool geometry — the settled surface is wherever the PBF says it is.
/// </summary>
[Trait("Category", "E2E")]
public class AcidPhaseBE2ETests
{
	// Staging spot: basin interior, clear of the inlet column. Teleport places the
	// CENTER; feet land near the floor and the body settles onto it (grounded —
	// which also refreshes the aerial action, a load-bearing detail for the full
	// escape test).
	private const float StageX = 560f;
	private const float DeepY  = 700f;

	/// <summary>Teleport player 0 into the deep basin and settle until the depth oracle reads deep.</summary>
	private static async Task<GameStateSnapshot> StageDeepSubmersionAsync(ArenaPage arena)
	{
		await arena.TeleportAsync(player: 0, StageX, DeepY);
		// Settle: sink the last few px and let the occupancy grid + oracle catch up.
		return await arena.StepUntilAsync(s => s.Players[0].SubmergedDepth >= 50, maxFrames: 240, batch: 2);
	}

	// ── Depth-scaled lethality ───────────────────────────────────────────────────

	[SkippableFact]
	public async Task DeepSubmersion_MeltsAtAmplifiedRate_NotTheSurfaceChip()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var deep = await StageDeepSubmersionAsync(arena);
		int hpBefore = deep.Players[0].Hp;

		// ACT — exactly 60 fixed frames (1.0 s) of deep soaking, no input.
		await arena.StepAsync(60);
		var after = await arena.StateAsync();

		// ASSERT — the depth multiplier must be live: at ~60-90 px depth the melt is
		// ~45-58 dps. The flat surface chip (9 dps) would lose ≤ 10 HP in this window,
		// so the lower bound cleanly separates "curve works" from "curve dead".
		int drop = hpBefore - after.Players[0].Hp;
		drop.ShouldBeGreaterThanOrEqualTo(30, "deep acid should melt (depth multiplier), not chip at the flat surface rate");
		drop.ShouldBeLessThanOrEqualTo(80, "deep melt should stay in the tuned fast-melt band, not insta-kill");
		after.Players[0].SubmergedDepth.ShouldBeGreaterThanOrEqualTo(40, "guard: the measurement must have happened at depth");
	}

	// ── Swim stroke vs the double-jump bypass ────────────────────────────────────

	[SkippableFact]
	public async Task JumpPress_Underwater_IsASwimStroke_NotTheDoubleJump()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		await StageDeepSubmersionAsync(arena);
		var player = arena.Player(0);

		// ACT — press, then poll a few frames for the stroke to land. The scripted
		// device applies input with a frame of latency (the established harness
		// pattern is "press → step 2 → read"), so a single-frame read races it.
		// StepUntil also turns "no stroke ever fired" into a crisp timeout failure.
		await player.PressJumpAsync();
		var atStroke = await arena.StepUntilAsync(s => s.Players[0].Vy < -150, maxFrames: 5, batch: 1);
		await player.ReleaseJumpAsync();

		// Let the single stroke play out fully.
		await arena.StepAsync(25);
		var later = await player.StateAsync();

		// ASSERT — stroke sets vy ≈ -230 (SwimStrokeImpulse); the bypass bug would
		// read vy ≈ -800 (FutureAxe JumpSpeed via the air-jump branch). The poll
		// above caught the FIRST frame with upward motion, so a real double jump
		// cannot hide from this band check.
		atStroke.Players[0].Vy.ShouldBeGreaterThan(-400, "vy at jump-speed magnitude means JumpAbility air-jumped underwater — the bypass bug");

		// And one stroke from the deep must NOT be an exit — escape demands mashing.
		later.Submerged.ShouldBeTrue("a single stroke from the deep should not pop the player out of the acid");
	}

	// ── The mash ─────────────────────────────────────────────────────────────────

	[SkippableFact]
	public async Task MashingJump_ClawsThePlayerUpOutOfTheAcid()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var deep = await StageDeepSubmersionAsync(arena);
		var player = arena.Player(0);
		int startY     = deep.Players[0].Y;
		int startDepth = deep.Players[0].SubmergedDepth;

		// ACT — frantic mash: press 2 frames, release 3 (12 strokes/s). Each press is
		// an input EDGE, which is what SwimAbility strokes on.
		PlayerSnapshot final = deep.Players[0];
		bool surfaced = false;
		for (int stroke = 0; stroke < 20 && !surfaced; stroke++)
		{
			await player.PressJumpAsync();
			await arena.StepAsync(2);
			await player.ReleaseJumpAsync();
			await arena.StepAsync(3);
			final = await player.StateAsync();
			surfaced = !final.Submerged;
		}

		// ASSERT
		surfaced.ShouldBeTrue($"mashing jump should claw the player to the surface (still at depth {final.SubmergedDepth} after 20 strokes)");
		final.Y.ShouldBeLessThan(startY - 30, "the mash should produce real upward progress");
		final.Hp.ShouldBeGreaterThan(0, $"the escape must be survivable from full HP (started depth {startDepth})");
	}

	// ── The full design promise: deadly but escapable ────────────────────────────

	[SkippableFact]
	public async Task DeepKnockIn_IsEscapable_MashToSurface_ThenBreachToTheBank()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — the intended escape loop, end to end: knocked into the deep →
		// mash strokes toward the surface → within SwimBreachDepth the next press
		// BREACHES at full JumpSpeed → arc onto the bank. (Underwater the jump
		// button belongs to SwimAbility; a press that happens to land in a dry
		// bob-frame above the surface fires the banked double jump instead — both
		// read as a full-strength exit and both are legitimate.)
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		await StageDeepSubmersionAsync(arena);
		var player = arena.Player(0);

		// ACT — hold LEFT (hug the basin's bank wall so the exit lands on solid
		// ground with margin) and mash jump. Deep presses are strokes; the press
		// that lands within breach range (depth ≤ SwimBreachDepth) fires a FULL
		// jump out at the character's JumpSpeed. Detect the breach mid-press and
		// KEEP the button held for the whole arc (JumpHeld semantics — releasing
		// would short-hop the escape). Trace each frame so a failure shows exactly
		// what the game saw. [This loop replaced a crest-then-double-jump dance:
		// the frame trace from that version exposed the bobbing-surface luck-gate
		// that motivated the breach mechanic in the first place.]
		await player.HoldLeftAsync();
		GameStateSnapshot? launched = null;
		var trace = new System.Text.StringBuilder();
		for (int stroke = 0; stroke < 30 && launched == null; stroke++)
		{
			await player.PressJumpAsync();
			for (int f = 0; f < 2 && launched == null; f++)
			{
				await arena.StepAsync(1);
				var s = await arena.StateAsync();
				var pl = s.Players[0];
				trace.AppendLine($" s{stroke}f{f}: y={pl.Y} vy={pl.Vy} sub={pl.Submerged} depth={pl.SubmergedDepth}");
				if (pl.Vy < -500)
				{
					launched = s;   // breach (or post-exit double jump) — keep jump HELD
				}
			}
			if (launched == null)
			{
				await player.ReleaseJumpAsync();
				await arena.StepAsync(3);
			}
		}
		launched.ShouldNotBeNull(
			$"mashing from the deep should eventually fire the breach jump out of the water. Frame trace:\n{trace}");

		// Jump stays held through the arc; drift continues toward the bank.
		var landed = await arena.StepUntilAsync(
			s => s.Players[0].Grounded && !s.Players[0].Submerged, maxFrames: 400, batch: 2);
		await player.ReleaseJumpAsync();
		await player.StopAsync();

		// ASSERT — the exit fired at full jump strength (≈ -800: a breach or the
		// banked double jump above the surface — either is a legitimate exit; a
		// 230 px/s stroke can never read below -500)…
		launched.Players[0].Vy.ShouldBeLessThan(-500);

		// …and the player must come down on solid dry ground ABOVE the basin (bank
		// top ≈ y 520 vs basin floor ≈ y 712), alive: knocked deep, swam out, escaped.
		var p = landed.Players[0];
		p.Submerged.ShouldBeFalse();
		p.Y.ShouldBeLessThan(620, $"the player should land on the bank above the basin, not back on the basin floor (landed at y={p.Y})");
		p.Hp.ShouldBeGreaterThan(0, "the full escape must be survivable from full HP");
	}

	// ── Phantom damage (broadphase vs narrow-phase regression) ───────────────────

	[SkippableFact]
	public async Task DryPlayer_InsideTheDamageAabb_TakesNoDamage()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — player 0 never moves off its left-bank spawn (x≈200). Once the
		// pour starts, splashes caught by the LEFT tiers (x 120-330) stretch the
		// damage AABB's left edge past the player while their own bank-top column
		// is still dry — the exact scenario where pre-fix code chip-damaged dry
		// players. (Tiers catch splashes before bank-top puddles form, which is
		// what opens the dry-inside-the-box window this test needs.)
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.StepUntilAsync(s => s.AcidActive, maxFrames: 1200, batch: 5);

		// ACT — sample through the splashy pour. Latch whether P0 was ever inside the
		// AABB while dry, and whether a splash puddle ever ACTUALLY wet them. Exit as
		// soon as we have a decisive window so late-arriving puddles can't muddy it.
		bool everInsideAabbWhileDry = false;
		bool everSubmerged = false;
		int  dryInsideSamples = 0;
		int  maxHp = (await arena.StateAsync()).Players[0].MaxHp;
		int  hp = maxHp;

		for (int i = 0; i < 120; i++)
		{
			await arena.StepAsync(5);
			var s  = await arena.StateAsync();
			var p0 = s.Players[0];

			everSubmerged |= p0.Submerged;
			int feetY = p0.Y + 24;   // FutureAxe BodyHeight 48 — feet below center
			bool inAabb = p0.X >= s.AcidBoundsLeft && p0.X <= s.AcidBoundsRight
			           && feetY >= s.AcidBoundsTop && feetY <= s.AcidBoundsBottom;
			bool dryInside = inAabb && !p0.Submerged;
			everInsideAabbWhileDry |= dryInside;
			if (dryInside)
			{
				dryInsideSamples++;
			}
			hp = p0.Hp;

			if (everSubmerged)
			{
				break;   // contact became real — the dry-player claim is void
			}
			if (dryInsideSamples >= 12)
			{
				break;   // ~60 frames dry inside the box: decisive evidence collected
			}
		}

		// A splash puddle landing on the player is legitimate contact — the phantom
		// claim can't be tested in that run. Skip rather than assert a falsehood.
		Skip.If(everSubmerged, "a splash puddle genuinely wet the bank player — inconclusive for the phantom-damage check");

		// ASSERT — the broadphase box reached them, the narrow-phase said dry, and
		// the damage pipeline respected it.
		everInsideAabbWhileDry.ShouldBeTrue("guard: the splash AABB should engulf the bank player — otherwise this test exercised nothing");
		hp.ShouldBe(maxHp, "a dry player inside the damage AABB must take ZERO damage (phantom-damage regression)");
	}
}
