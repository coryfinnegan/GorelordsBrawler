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
}
