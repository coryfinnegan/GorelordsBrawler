using System.Threading.Tasks;
using GorelordsBrawler.E2E.Tests.Pages;
using Shouldly;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E coverage for the acid's depth-scaled lethality and the Smash-style escape
/// (buoyant float-to-surface + jump-press-is-a-full-jump), driven through the
/// scripted-input device in deterministic stepped mode.
///
/// Geometry these tests lean on (arena1.tmx "The Sump" + FutureAxe.json):
///   • Basin channel x∈[448,832], floor top y=736, pre-filled pool surface ≈ y 660s.
///   • Players are staged at x=560 — inside the basin but OFF the inlet column
///     (x≈640) so the pour never lands on the actor mid-test.
///   • FutureAxe: BodyHeight 48 (feet = y+24), JumpSpeed 800, MaxHp 120.
///
/// Velocity is the escape oracle: the buoyant float is capped at
/// AcidBuoyantMaxRiseSpeed (280), while a jump launches at JumpSpeed (800). Any
/// vy below -500 can therefore only be a deliberate jump — the float can't fake
/// it, and a dead jump button can't produce it.
///
/// Depth truths come from the per-player oracles (submerged / submergedDepth), never
/// from assumed pool geometry — the settled surface is wherever the PBF says it is.
/// </summary>
[Trait("Category", "E2E")]
public class AcidPhaseBE2ETests
{
	// Staging spot: basin interior, clear of the inlet column. Teleport places the
	// CENTER (feet = y+24) and zeroes velocity, so a freshly staged body starts
	// its buoyant rise from rest — every test below begins from the same state.
	private const float StageX = 560f;
	private const float DeepY  = 700f;

	/// <summary>Teleport player 0 into the deep basin and step until the depth oracle reads deep.</summary>
	private static async Task<GameStateSnapshot> StageDeepSubmersionAsync(ArenaPage arena)
	{
		await arena.TeleportAsync(player: 0, StageX, DeepY);
		// Let the occupancy grid + oracle catch up (buoyancy only moves the body
		// ~1 px in these first frames — depth is still comfortably deep).
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

		// ACT — 60 fixed frames (1.0 s) of deep soaking. Buoyancy would float the
		// player out mid-measurement, so PIN the depth by re-teleporting every 2
		// frames (teleport zeroes velocity, which also discards the accumulated
		// buoyant rise) — the staging tool holding the scenario still, not gameplay.
		for (int i = 0; i < 30; i++)
		{
			await arena.TeleportAsync(player: 0, StageX, DeepY);
			await arena.StepAsync(2);
		}
		var after = await arena.StateAsync();

		// ASSERT — the depth multiplier must be live: pinned at ~60+ px depth the
		// melt is ~40-58 dps. The flat surface chip (9 dps) would lose ≤ 10 HP in
		// this window, so the lower bound cleanly separates "curve works" from
		// "curve dead".
		int drop = hpBefore - after.Players[0].Hp;
		drop.ShouldBeGreaterThanOrEqualTo(25, "deep acid should melt (depth multiplier), not chip at the flat surface rate");
		drop.ShouldBeLessThanOrEqualTo(80, "deep melt should stay in the tuned fast-melt band, not insta-kill");
		after.Players[0].SubmergedDepth.ShouldBeGreaterThanOrEqualTo(40, "guard: the measurement must have happened at depth");
	}

	// ── Buoyancy: the acid can never trap a body ─────────────────────────────────

	[SkippableFact]
	public async Task PassiveBuoyancy_FloatsThePlayerToTheSurface_NoInputAtAll()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var deep = await StageDeepSubmersionAsync(arena);
		int hpBefore   = deep.Players[0].Hp;
		int startDepth = deep.Players[0].SubmergedDepth;

		// ACT — no input whatsoever: buoyancy alone must carry the body up until
		// the feet clear the local surface. (The StepUntil timeout IS the assert
		// that it happens — a trapped body turns it into a crisp failure.)
		var surfaced = await arena.StepUntilAsync(s => !s.Players[0].Submerged, maxFrames: 180, batch: 2);

		// ASSERT — the float-out is survivable from full HP, and the soak on the
		// way up actually bit (guards that the melt was live during the rise, and
		// that the surfacing wasn't a bogus dry reading from frame one).
		int drop = hpBefore - surfaced.Players[0].Hp;
		surfaced.Players[0].Hp.ShouldBeGreaterThan(0, $"the passive float-out must be survivable (started at depth {startDepth})");
		drop.ShouldBeGreaterThanOrEqualTo(5, "the rise should still cost meaningful HP — buoyancy rescues, it doesn't make acid free");
	}

	// ── The jump button: one press = a full-strength exit, from any depth ────────

	[SkippableFact]
	public async Task JumpPress_Underwater_IsAFullStrengthExit()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		await StageDeepSubmersionAsync(arena);
		var player = arena.Player(0);

		// ACT — a single press, held (the escape should reward commitment with the
		// full hold-to-rise arc). Input has ~1 frame of latency: press → poll.
		await player.PressJumpAsync();
		var launched = await arena.StepUntilAsync(s => s.Players[0].Vy < -500, maxFrames: 5, batch: 1);

		// …and that one press must carry the body OUT of the acid — no mashing.
		var exited = await arena.StepUntilAsync(s => !s.Players[0].Submerged, maxFrames: 60, batch: 1);
		await player.ReleaseJumpAsync();

		// ASSERT — the launch is jump-strength (≈ -800), far beyond the 280 px/s
		// buoyant rise cap: the button did a REAL jump underwater, at depth.
		launched.Players[0].Vy.ShouldBeLessThan(-500, "a submerged jump press must launch at full JumpSpeed, not a feeble stroke");
		exited.Players[0].Submerged.ShouldBeFalse();
	}

	// ── Water banks the air jump (the surface-bob dry window can't eat a press) ──

	[SkippableFact]
	public async Task Submersion_RestoresTheAirJump_ForAFollowUpPressAfterExit()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — burn the aerial action FIRST (ground jump, then double jump),
		// so the only way the final air press below can fire is if submersion
		// re-banked it. Without that discriminator this test would pass off the
		// spawn's grounded refresh and prove nothing.
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var player = arena.Player(0);

		await player.PressJumpAsync();
		await arena.StepUntilAsync(s => s.Players[0].Vy < -500, maxFrames: 5, batch: 1);
		await player.ReleaseJumpAsync();
		// Let the released rise decay past the -500 oracle band first, so the next
		// StepUntil can only be satisfied by the double jump itself (short-hop
		// gravity burns -800 → -400 in a few frames).
		await arena.StepUntilAsync(s => s.Players[0].Vy > -400, maxFrames: 20, batch: 1);
		await player.PressJumpAsync();   // double jump — consumes HasAerialAction
		await arena.StepUntilAsync(s => s.Players[0].Vy < -500, maxFrames: 5, batch: 1);
		await player.ReleaseJumpAsync();

		// Dunk the (aerially spent) body into the basin.
		await StageDeepSubmersionAsync(arena);

		// ACT — jump out of the acid…
		await player.PressJumpAsync();
		await arena.StepUntilAsync(s => s.Players[0].Vy < -500, maxFrames: 5, batch: 1);
		await arena.StepUntilAsync(s => !s.Players[0].Submerged, maxFrames: 60, batch: 1);
		await player.ReleaseJumpAsync();

		// …coast past the apex (still airborne, still dry), then press again.
		await arena.StepUntilAsync(
			s => !s.Players[0].Submerged && !s.Players[0].Grounded && s.Players[0].Vy > -100,
			maxFrames: 120, batch: 1);
		await player.PressJumpAsync();
		var second = await arena.StepUntilAsync(s => s.Players[0].Vy < -500, maxFrames: 5, batch: 1);
		await player.ReleaseJumpAsync();

		// ASSERT — the follow-up air press fired a full-strength double jump. The
		// aerial action was spent before the dunk, so only the water's re-banking
		// (SubmersionFeel.HasAerialAction while wet) can explain it — this is the
		// mechanic that turns the old "2-frame dry bob window eats your press"
		// luck-gate into a guaranteed full-strength exit.
		second.Players[0].Vy.ShouldBeLessThan(-500, "water must re-bank the air jump — a press in the post-exit air (or a dry bob frame) can never be a dead input");
	}

	// ── The full design promise: deadly but escapable ────────────────────────────

	[SkippableFact]
	public async Task DeepKnockIn_IsEscapable_OneHeldJumpOntoTheBank()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — the intended escape loop end to end: dunked deep → one held
		// jump press → arc over the basin lip → land on solid dry ground, alive.
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		await StageDeepSubmersionAsync(arena);
		var player = arena.Player(0);

		// ACT — hold LEFT (drift toward the bank wall so the arc comes down on
		// solid ground) and one held jump press for the full-rise arc.
		await player.HoldLeftAsync();
		await player.PressJumpAsync();
		await arena.StepUntilAsync(s => s.Players[0].Vy < -500, maxFrames: 5, batch: 1);

		var landed = await arena.StepUntilAsync(
			s => s.Players[0].Grounded && !s.Players[0].Submerged, maxFrames: 400, batch: 2);
		await player.ReleaseJumpAsync();
		await player.StopAsync();

		// ASSERT — down on dry ground ABOVE the basin (bank top ≈ y 520 vs basin
		// floor ≈ y 712), alive: knocked deep, one press, out.
		var p = landed.Players[0];
		p.Submerged.ShouldBeFalse();
		p.Y.ShouldBeLessThan(620, $"the player should land on the bank above the basin, not back on the basin floor (landed at y={p.Y})");
		p.Hp.ShouldBeGreaterThan(0, "the escape must be survivable from full HP");
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
