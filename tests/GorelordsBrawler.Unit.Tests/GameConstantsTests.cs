using System;
using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Sanity checks that GameConstants values satisfy invariants the engine relies on.
/// These catch accidental edits that would break physics stability, timing contracts, etc.
/// </summary>
public class GameConstantsTests
{
	// ── Physics ordering ──────────────────────────────────────────────────────

	[Fact]
	public void PhysicsBodyUpdateOrder_IsBeforeLocomotionAnimator()
	{
		Assert.True(
			GameConstants.Physics.PhysicsBodyUpdateOrder < GameConstants.Physics.LocomotionAnimatorUpdateOrder,
			"PhysicsBody must run before LocomotionAnimator so movement is resolved before animation reads velocity.");
	}

	[Fact]
	public void MaxDeltaTime_IsPositiveAndReasonable()
	{
		float max = GameConstants.Physics.MaxDeltaTime;
		Assert.True(max > 0f,   "MaxDeltaTime must be positive.");
		Assert.True(max < 0.2f, "MaxDeltaTime > 200ms would cause tunneling at most speeds.");
	}

	[Fact]
	public void LandingWindowDuration_IsShortEnough_NotToBreakAnimation()
	{
		float window = GameConstants.Physics.LandingWindowDuration;
		Assert.True(window >= 0f,   "LandingWindowDuration must be non-negative.");
		Assert.True(window < 0.5f,  "LandingWindowDuration > 500ms would feel unresponsive.");
	}

	// ── Combat timing ─────────────────────────────────────────────────────────

	[Fact]
	public void DefaultStockCount_IsPositive()
	{
		Assert.True(GameConstants.Combat.DefaultStockCount > 0);
	}

	[Fact]
	public void KnockbackScaling_IsPositive()
	{
		Assert.True(GameConstants.Combat.KnockbackScaling > 0f);
	}

	[Fact]
	public void HitstopDuration_IsShorterThan_HitFlashDuration()
	{
		Assert.True(
			GameConstants.Combat.HitstopDuration < GameConstants.Combat.HitFlashDuration,
			"Hitstop should end before the hit flash fades so the flash is visible.");
	}

	[Fact]
	public void RespawnDelay_IsPositive()
	{
		Assert.True(GameConstants.Combat.RespawnDelay > 0f);
	}

	[Fact]
	public void AttackBufferWindow_IsNonNegative()
	{
		Assert.True(GameConstants.Combat.AttackBufferWindow >= 0f);
	}

	// ── Match timing ──────────────────────────────────────────────────────────

	[Fact]
	public void AnnouncementDurations_SumToReasonableTotal()
	{
		float total = GameConstants.Match.AnnouncementFadeInDuration
		            + GameConstants.Match.AnnouncementDisplayDuration
		            + GameConstants.Match.AnnouncementFadeOutDuration;
		Assert.True(total > 0.5f,  "Announcement must be visible long enough to read.");
		Assert.True(total < 5f,    "Announcement should not block gameplay for > 5s.");
	}

	[Fact]
	public void CountdownDuration_IsPositive()
	{
		Assert.True(GameConstants.Match.CountdownDuration > 0f);
	}

	// ── Hazards ───────────────────────────────────────────────────────────────
	// (Pour-rate sanity now lives in AcidConfigTests — flow is per-loop
	// particles/sec in AcidConfig, not a GameConstants area rate.)

	[Fact]
	public void AcidDepthLethality_ConstantsAreSane()
	{
		// Phase B: surface chip must exist, the deep end must amplify it, and the
		// saturation depth must be a real distance.
		Assert.True(GameConstants.Hazards.AcidSurfaceDps > 0f);
		Assert.True(GameConstants.Hazards.AcidDeepDpsMult > 1f);
		Assert.True(GameConstants.Hazards.AcidFullSubmergeDepth > 0f);
	}

	[Fact]
	public void SwimStroke_ClampIsAtLeastTheImpulse()
	{
		// If the rise-speed clamp were below the per-stroke impulse, every stroke
		// would be immediately truncated and mashing would feel dead.
		Assert.True(GameConstants.Hazards.SwimStrokeImpulse > 0f);
		Assert.True(GameConstants.Hazards.SwimMaxRiseSpeed >= GameConstants.Hazards.SwimStrokeImpulse);
	}

	[Fact]
	public void SwimBreachDepth_IsANearSurfaceBand()
	{
		// The breach band must exist but stay NEAR the surface: if it reached
		// full-submerge depth, every deep press would be a free full jump and the
		// stroke/mash regime (and the deep-launch kill window) would vanish.
		Assert.True(GameConstants.Hazards.SwimBreachDepth > 0f);
		Assert.True(GameConstants.Hazards.SwimBreachDepth < GameConstants.Hazards.AcidFullSubmergeDepth / 2f);
	}

	[Fact]
	public void SpringDamping_IsPositive()
	{
		Assert.True(GameConstants.Hazards.Damping > 0f);
	}

	// (PlatformSpawnMinX/MaxX are gone — drop-logs now spawn at the dissolved
	// tiers' former X anchors, side-alternating; see AcidConfigTests for the
	// slot-placement coverage.)

	// (The PlatformTable_* tests are gone with the normalized Platforms array —
	// the pre-Sump geometry it described is retired. Its replacement, AcidConfig,
	// is covered by AcidConfigTests: escalation curves, particle-budget invariant,
	// inlet placement, and flood-safe respawn candidates.)

	[Fact]
	public void PlatformImpactFactor_IsInValidRange()
	{
		float f = GameConstants.Hazards.PlatformImpactFactor;
		Assert.True(f > 0f,  "PlatformImpactFactor must be positive to transfer any energy.");
		Assert.True(f <= 1f, "PlatformImpactFactor > 1 would violate energy conservation.");
	}

	// ── Arena bounds ──────────────────────────────────────────────────────────

	[Fact]
	public void ArenaInnerBounds_AreOrdered()
	{
		Assert.True(GameConstants.Arena.InnerLeft < GameConstants.Arena.InnerRight);
	}

	// ── Ledge ─────────────────────────────────────────────────────────────────

	[Fact]
	public void LedgeGrabRange_IsPositive()
	{
		Assert.True(GameConstants.Ledge.GrabRangeX > 0f);
		Assert.True(GameConstants.Ledge.GrabRangeY > 0f);
	}

	[Fact]
	public void LedgeRegrabCooldown_IsPositive()
	{
		Assert.True(GameConstants.Ledge.RegrabCooldown > 0f);
	}
}
