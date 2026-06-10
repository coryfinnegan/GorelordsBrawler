using GorelordsBrawler.Combat;
using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

public class CombatMathTests
{
	private const float Scaling = GameConstants.Combat.KnockbackScaling;

	// ── KnockbackScale ────────────────────────────────────────────────────────

	[Fact]
	public void KnockbackScale_AtFullHp_ReturnsOne()
	{
		float scale = CombatMath.KnockbackScale(100, 100, Scaling);
		Assert.Equal(1f, scale, precision: 4);
	}

	[Fact]
	public void KnockbackScale_AtZeroHp_ReturnsOnePlusScaling()
	{
		float scale = CombatMath.KnockbackScale(0, 100, Scaling);
		Assert.Equal(1f + Scaling, scale, precision: 4);
	}

	[Fact]
	public void KnockbackScale_AtHalfHp_ReturnsOnePlusHalfScaling()
	{
		float scale = CombatMath.KnockbackScale(50, 100, Scaling);
		Assert.Equal(1f + 0.5f * Scaling, scale, precision: 4);
	}

	[Fact]
	public void KnockbackScale_IsMonotonicallyDecreasing_WithHp()
	{
		float full     = CombatMath.KnockbackScale(100, 100, Scaling);
		float halfHp   = CombatMath.KnockbackScale(50,  100, Scaling);
		float quarterHp = CombatMath.KnockbackScale(25, 100, Scaling);
		float dead     = CombatMath.KnockbackScale(0,   100, Scaling);

		Assert.True(full < halfHp);
		Assert.True(halfHp < quarterHp);
		Assert.True(quarterHp < dead);
	}

	[Fact]
	public void KnockbackScale_ZeroScaling_AlwaysReturnsOne()
	{
		Assert.Equal(1f, CombatMath.KnockbackScale(0,   100, 0f), precision: 4);
		Assert.Equal(1f, CombatMath.KnockbackScale(50,  100, 0f), precision: 4);
		Assert.Equal(1f, CombatMath.KnockbackScale(100, 100, 0f), precision: 4);
	}

	[Fact]
	public void KnockbackScale_OneLostHp_IsSlightlyAboveOne()
	{
		float scale = CombatMath.KnockbackScale(99, 100, Scaling);
		Assert.True(scale > 1f);
		Assert.True(scale < 1.1f);
	}

	[Theory]
	[InlineData(100, 100)]
	[InlineData(75,  100)]
	[InlineData(50,  100)]
	[InlineData(25,  100)]
	[InlineData(0,   100)]
	public void KnockbackScale_NeverBelowOne(int current, int max)
	{
		float scale = CombatMath.KnockbackScale(current, max, Scaling);
		Assert.True(scale >= 1f, $"Scale was {scale} at {current}/{max} HP");
	}

	[Fact]
	public void KnockbackScale_DefaultScaling_MatchesGameConstants()
	{
		// Full HP = 1×; 0 HP = (1 + KnockbackScaling)× = 3× with default KnockbackScaling=2
		float atDead = CombatMath.KnockbackScale(0, 100, GameConstants.Combat.KnockbackScaling);
		Assert.Equal(1f + GameConstants.Combat.KnockbackScaling, atDead, precision: 3);
	}

	// ── AcidDpsMultiplier (Phase B depth-scaled lethality) ─────────────────────

	private const float DeepMult   = GameConstants.Hazards.AcidDeepDpsMult;
	private const float FullDepth  = GameConstants.Hazards.AcidFullSubmergeDepth;

	[Fact]
	public void AcidDps_AtSurface_IsBaseRate()
	{
		// Depth 0 (and below) = at/above surface → no amplification.
		Assert.Equal(1f, CombatMath.AcidDpsMultiplier(0f, DeepMult, FullDepth), precision: 4);
		Assert.Equal(1f, CombatMath.AcidDpsMultiplier(-20f, DeepMult, FullDepth), precision: 4);
	}

	[Fact]
	public void AcidDps_AtFullSubmerge_IsDeepMultiplier()
	{
		Assert.Equal(DeepMult, CombatMath.AcidDpsMultiplier(FullDepth, DeepMult, FullDepth), precision: 4);
	}

	[Fact]
	public void AcidDps_PastFullSubmerge_Saturates()
	{
		// Deeper than "fully submerged" doesn't keep climbing — clamps at DeepMult.
		Assert.Equal(DeepMult, CombatMath.AcidDpsMultiplier(FullDepth * 3f, DeepMult, FullDepth), precision: 4);
	}

	[Fact]
	public void AcidDps_AtHalfDepth_IsHalfwayToDeep()
	{
		float mid = CombatMath.AcidDpsMultiplier(FullDepth * 0.5f, DeepMult, FullDepth);
		Assert.Equal(1f + (DeepMult - 1f) * 0.5f, mid, precision: 4);
	}

	[Fact]
	public void AcidDps_IsMonotonicWithDepth()
	{
		float prev = CombatMath.AcidDpsMultiplier(0f, DeepMult, FullDepth);
		for (float d = 8f; d <= FullDepth; d += 8f)
		{
			float cur = CombatMath.AcidDpsMultiplier(d, DeepMult, FullDepth);
			Assert.True(cur >= prev, $"multiplier dropped at depth {d}: {prev} -> {cur}");
			prev = cur;
		}
	}

	[Fact]
	public void AcidDps_DegenerateFullDepth_ReturnsBase()
	{
		// Guard against divide-by-zero if misconfigured.
		Assert.Equal(1f, CombatMath.AcidDpsMultiplier(50f, DeepMult, 0f), precision: 4);
	}

	// ── The "deadly but escapable" invariant ───────────────────────────────────
	// These tie the damage curve and the swim-escape tuning together: the whole
	// design promise is that a knock-in is survivable if you react, but lethal if
	// you're launched deep AND already hurt. If someone retunes one constant and
	// breaks the relationship, these fail.

	[Fact]
	public void DeepEnd_IsFastMelt_KillsAFreshFighterInUnderTwoSeconds()
	{
		// At full submersion, DPS = base * deepMult. A 100-HP fighter who does
		// nothing should die in ~1.5-2s (the agreed "fast melt").
		float deepDps = GameConstants.Hazards.AcidSurfaceDps * DeepMult;
		float secondsToKO = 100f / deepDps;
		Assert.InRange(secondsToKO, 1.3f, 2.2f);
	}

	[Fact]
	public void SurfaceChip_IsSurvivable_NotAnInstantThreat()
	{
		// A toe-dip (base rate, 1×) should take many seconds to kill — it's a
		// scare, not an execution. >5s to KO a fresh fighter.
		float surfaceDps = GameConstants.Hazards.AcidSurfaceDps;
		Assert.True(100f / surfaceDps > 5f, $"surface chip too lethal: {100f / surfaceDps:F1}s to KO");
	}

	[Fact]
	public void Swim_StrokeIsMeaningful_AndOutclimbsMidDepthAcid()
	{
		// What's a CLEAN closed-form invariant (and so belongs in a unit test):
		// the swim stroke must be a real fraction of a normal jump, and a mashing
		// player's sustained rise must out-climb the acid's downward pull at
		// mid-depth — otherwise "escapable" is a lie. The stroke clamp is the
		// sustainable ascent speed.
		float riseSpeed   = GameConstants.Hazards.SwimMaxRiseSpeed;
		float strokeImp   = GameConstants.Hazards.SwimStrokeImpulse;

		// A stroke is a meaningful push — at least half a normal jump's launch
		// speed (so it reads as "clawing up", not a twitch).
		Assert.True(strokeImp >= 0.5f * 250f /* baseline JumpSpeed */,
			$"swim stroke too weak to feel like an escape: {strokeImp}");

		// Mid-depth escape: climbing out is comfortably faster than the acid can
		// KO a fresh fighter there. This is the core "escapable" guarantee.
		float timeToEscapeFromMid = (FullDepth * 0.5f) / riseSpeed;
		float midDps = GameConstants.Hazards.AcidSurfaceDps
		             * CombatMath.AcidDpsMultiplier(FullDepth * 0.5f, DeepMult, FullDepth);
		float koTimeMid = 100f / midDps;
		Assert.True(timeToEscapeFromMid < koTimeMid * 0.5f,
			$"mid-depth not comfortably escapable: escape {timeToEscapeFromMid:F2}s vs KO {koTimeMid:F2}s");
	}

	// NOTE on the deep end: whether a frame-perfect mash can escape a deep knock-in
	// at low HP is deliberately NOT asserted here. The real lethality window is the
	// HITSTUN period (you eat deep damage before you can swim at all) plus reaction
	// lag — both runtime dynamics a pure formula can't model. That balance is
	// verified in the functional smoke/playtest, consistent with how this codebase
	// splits pure-math tests from feel. What IS pinned above: fresh-fighter deep
	// melt is ~1.5-2s, surface chip is survivable, and mid-depth is escapable.
}
