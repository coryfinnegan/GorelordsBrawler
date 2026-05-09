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

	[Fact]
	public void AcidRiseSpeed_IsPositive()
	{
		Assert.True(GameConstants.Hazards.AcidRiseSpeed > 0f);
	}

	[Fact]
	public void AcidDamagePerSec_IsPositive()
	{
		Assert.True(GameConstants.Hazards.AcidDamagePerSec > 0f);
	}

	[Fact]
	public void PlatformBurnDuration_IsLongerThan_BurnDelay()
	{
		Assert.True(
			GameConstants.Hazards.PlatformBurnDuration > GameConstants.Hazards.PlatformBurnDelay,
			"PlatformBurnDuration must exceed PlatformBurnDelay — the platform can't finish burning before the burn starts.");
	}

	[Fact]
	public void SpringDamping_IsPositive()
	{
		Assert.True(GameConstants.Hazards.Damping > 0f);
	}

	[Fact]
	public void PlatformSpawnRange_IsValid()
	{
		Assert.True(GameConstants.Hazards.PlatformSpawnMinX >= 0f);
		Assert.True(GameConstants.Hazards.PlatformSpawnMaxX <= 1f);
		Assert.True(GameConstants.Hazards.PlatformSpawnMinX < GameConstants.Hazards.PlatformSpawnMaxX);
	}

	[Fact]
	public void PlatformTable_HasFiveEntries()
	{
		Assert.Equal(5, GameConstants.Hazards.Platforms.Length);
	}

	[Fact]
	public void PlatformTable_AllEntriesNormalized()
	{
		foreach (var (cx, cy, w) in GameConstants.Hazards.Platforms)
		{
			Assert.InRange(cx, 0f, 1f);
			Assert.InRange(cy, 0f, 1f);
			Assert.InRange(w,  0f, 1f);
		}
	}

	[Fact]
	public void PlatformTable_ExactlyOneTopPlatform()
	{
		// Log spawning is triggered by the single highest platform — must be unambiguous.
		float minCy = float.MaxValue;
		foreach (var (_, cy, _) in GameConstants.Hazards.Platforms)
			if (cy < minCy) minCy = cy;

		int topCount = 0;
		foreach (var (_, cy, _) in GameConstants.Hazards.Platforms)
			if (cy == minCy) topCount++;

		Assert.Equal(1, topCount);
	}

	[Fact]
	public void PlatformTable_TwoBottomPlatformsSymmetricAboutCenter()
	{
		// The cascade geometry relies on two symmetric bot platforms whose
		// outer edges align with the top platform's edges.
		float maxCy = 0f;
		foreach (var (_, cy, _) in GameConstants.Hazards.Platforms)
			if (cy > maxCy) maxCy = cy;

		float sumCx = 0f;
		int   count = 0;
		foreach (var (cx, cy, _) in GameConstants.Hazards.Platforms)
			if (cy == maxCy) { sumCx += cx; count++; }

		Assert.Equal(2, count);
		// cx of left + cx of right should equal 1.0 (symmetric about center)
		Assert.True(MathF.Abs(sumCx - 1f) < 0.01f,
			$"Bottom platform cx values should sum to 1.0 for symmetry; got {sumCx:F4}");
	}

	[Fact]
	public void PlatformTable_TopPlatformIsAboveMidpoint()
	{
		// Spawn trigger must fire in the upper half of the map.
		float minCy = float.MaxValue;
		foreach (var (_, cy, _) in GameConstants.Hazards.Platforms)
			if (cy < minCy) minCy = cy;

		Assert.True(minCy < 0.6f,
			$"Top platform cy={minCy:F3} should be in the upper 60% of the map.");
	}

	[Fact]
	public void PlatformTable_CascadeGeometry_TopEdgesAlignWithBotOuterEdges()
	{
		// The top platform's left/right edges should align with the bot platforms'
		// outer edges so the tier-1 waterfall lands on the bot platforms naturally.
		// Check in normalized coords; tolerance 0.02 (= 25px at 1280px wide).
		float minCy = float.MaxValue, maxCy = 0f;
		foreach (var (_, cy, _) in GameConstants.Hazards.Platforms)
		{
			if (cy < minCy) minCy = cy;
			if (cy > maxCy) maxCy = cy;
		}

		(float cx, float cy, float w) topPlat = default;
		foreach (var p in GameConstants.Hazards.Platforms)
			if (p.cy == minCy) topPlat = p;

		float topLeftEdge  = topPlat.cx - topPlat.w / 2f;
		float topRightEdge = topPlat.cx + topPlat.w / 2f;

		float botLeftOuter  = 1f;
		float botRightOuter = 0f;
		foreach (var (cx, cy, w) in GameConstants.Hazards.Platforms)
		{
			if (cy != maxCy) continue;
			float leftX  = cx - w / 2f;
			float rightX = cx + w / 2f;
			if (cx < 0.5f) botLeftOuter  = rightX;  // left-side bot platform outer = right edge
			else            botRightOuter = leftX;   // right-side bot platform outer = left edge
		}

		Assert.True(MathF.Abs(topLeftEdge  - botLeftOuter)  < 0.02f,
			$"Top platform left edge ({topLeftEdge:F3}) should align with BotLeft right edge ({botLeftOuter:F3}).");
		Assert.True(MathF.Abs(topRightEdge - botRightOuter) < 0.02f,
			$"Top platform right edge ({topRightEdge:F3}) should align with BotRight left edge ({botRightOuter:F3}).");
	}

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
