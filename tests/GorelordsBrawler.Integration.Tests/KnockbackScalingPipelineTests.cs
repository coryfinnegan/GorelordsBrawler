using GorelordsBrawler.Combat;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Integration.Tests;

/// <summary>
/// Integration tests for the knockback scaling pipeline:
/// Health.TakeDamage → CombatMath.KnockbackScale → applied velocity.
/// Verifies the contract that near-death players go flying further.
/// </summary>
public class KnockbackScalingPipelineTests
{
	private const float BaseForce   = 300f;
	private const float Scaling     = GameConstants.Combat.KnockbackScaling;

	/// <summary>Simulate a hit: take damage, return the resulting knockback force.</summary>
	private static float SimulateHit(Health health, int damage)
	{
		health.TakeDamage(damage);
		float scale = CombatMath.KnockbackScale(health.CurrentHp, health.MaxHp, Scaling);
		return BaseForce * scale;
	}

	[Fact]
	public void FreshPlayer_ReceivesBaseForce()
	{
		var h = new Health { MaxHp = 100 };
		h.CurrentHp = 100;

		// No damage yet — knockback at full HP is 1× base
		float scale = CombatMath.KnockbackScale(h.CurrentHp, h.MaxHp, Scaling);
		Assert.Equal(1f, scale, precision: 4);
		Assert.Equal(BaseForce, BaseForce * scale, precision: 1);
	}

	[Fact]
	public void DamagedPlayer_ReceivesMoreKnockback_ThanFreshPlayer()
	{
		var h = new Health { MaxHp = 100 };
		h.CurrentHp = 100;

		float forceFresh   = SimulateHit(h, 10);  // 90 HP remaining
		float forceDamaged = SimulateHit(h, 40);  // 50 HP remaining

		Assert.True(forceDamaged > forceFresh,
			$"More damaged player should be launched further: {forceDamaged:F1} vs {forceFresh:F1}");
	}

	[Fact]
	public void NearDeathPlayer_ReceivesMaxKnockback()
	{
		var h = new Health { MaxHp = 100 };
		h.CurrentHp = 1;

		float scale = CombatMath.KnockbackScale(h.CurrentHp, h.MaxHp, Scaling);
		float force = BaseForce * scale;

		// At 1 HP: scale ≈ 1 + 0.99 * 2 = 2.98 → force ≈ 894
		Assert.True(force > BaseForce * 2.5f,
			$"Near-death player should be launched very far: force={force:F1}");
	}

	[Fact]
	public void MaxScaleIsOnePlusScaling_AtZeroHp()
	{
		var h = new Health { MaxHp = 100 };
		h.CurrentHp = 0;

		float scale = CombatMath.KnockbackScale(h.CurrentHp, h.MaxHp, Scaling);
		Assert.Equal(1f + Scaling, scale, precision: 3);
	}

	[Fact]
	public void KnockbackForce_IsStrictlyIncreasing_AsHpDrops()
	{
		var h       = new Health { MaxHp = 100 };
		var forces  = new float[5];
		int[] hps   = { 100, 75, 50, 25, 0 };

		for (int i = 0; i < hps.Length; i++)
		{
			h.CurrentHp = hps[i];
			forces[i]   = BaseForce * CombatMath.KnockbackScale(h.CurrentHp, h.MaxHp, Scaling);
		}

		for (int i = 1; i < forces.Length; i++)
			Assert.True(forces[i] > forces[i - 1],
				$"Force not increasing: hp={hps[i]}, force={forces[i]:F1} <= {forces[i-1]:F1}");
	}

	[Fact]
	public void TwoHitsOnSamePlayer_SecondHit_IsAmplified()
	{
		var h = new Health { MaxHp = 100 };
		h.CurrentHp = 100;

		float force1 = SimulateHit(h, 30);  // hit at 100 HP → takes to 70
		float force2 = SimulateHit(h, 30);  // hit at 70 HP → takes to 40

		Assert.True(force2 > force1,
			"Second hit on already-damaged player should produce more knockback.");
	}
}
