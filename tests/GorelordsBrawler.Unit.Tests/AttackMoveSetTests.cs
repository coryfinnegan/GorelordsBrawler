using GorelordsBrawler.Components.Stats;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

public class AttackMoveSetTests
{
	private static MeleeStats MakeMelee(int damage = 20, float force = 300f,
		float angleX = 1f, float angleY = -0.5f,
		float hitboxW = 40f, float hitboxH = 30f,
		float offsetX = 30f, float offsetY = 0f,
		float hitstun = 0.3f, float cooldown = 0.5f,
		float hitboxDur = 0.15f,
		int startFrame = -1, int endFrame = -1) => new MeleeStats
	{
		Damage          = damage,
		KnockbackForce  = force,
		KnockbackAngleX = angleX,
		KnockbackAngleY = angleY,
		HitboxWidth     = hitboxW,
		HitboxHeight    = hitboxH,
		HitboxOffsetX   = offsetX,
		HitboxOffsetY   = offsetY,
		HitstunDuration = hitstun,
		Cooldown        = cooldown,
		HitboxDuration  = hitboxDur,
		ActiveStartFrame = startFrame,
		ActiveEndFrame   = endFrame,
	};

	[Fact]
	public void FromMeleeStats_MapsJabDamage()
	{
		var melee = MakeMelee(damage: 35);
		var moveset = AttackMoveSet.FromMeleeStats(melee);
		Assert.Equal(35, moveset.Jab.Damage);
	}

	[Fact]
	public void FromMeleeStats_MapsKnockbackForce()
	{
		var melee = MakeMelee(force: 450f);
		var moveset = AttackMoveSet.FromMeleeStats(melee);
		Assert.Equal(450f, moveset.Jab.KnockbackForce);
	}

	[Fact]
	public void FromMeleeStats_MapsKnockbackAngle()
	{
		var melee = MakeMelee(angleX: 0.7f, angleY: -0.3f);
		var moveset = AttackMoveSet.FromMeleeStats(melee);
		Assert.Equal(0.7f,  moveset.Jab.KnockbackAngleX);
		Assert.Equal(-0.3f, moveset.Jab.KnockbackAngleY);
	}

	[Fact]
	public void FromMeleeStats_MapsHitboxDimensions()
	{
		var melee = MakeMelee(hitboxW: 50f, hitboxH: 35f, offsetX: 25f, offsetY: -5f);
		var moveset = AttackMoveSet.FromMeleeStats(melee);
		Assert.Equal(50f,  moveset.Jab.HitboxWidth);
		Assert.Equal(35f,  moveset.Jab.HitboxHeight);
		Assert.Equal(25f,  moveset.Jab.HitboxOffsetX);
		Assert.Equal(-5f,  moveset.Jab.HitboxOffsetY);
	}

	[Fact]
	public void FromMeleeStats_MapsTimingValues()
	{
		var melee = MakeMelee(hitstun: 0.4f, cooldown: 0.6f, hitboxDur: 0.2f);
		var moveset = AttackMoveSet.FromMeleeStats(melee);
		Assert.Equal(0.4f, moveset.Jab.HitstunDuration);
		Assert.Equal(0.6f, moveset.Jab.Cooldown);
		Assert.Equal(0.2f, moveset.Jab.HitboxDuration);
	}

	[Fact]
	public void FromMeleeStats_MapsFrameWindows()
	{
		var melee = MakeMelee(startFrame: 5, endFrame: 12);
		var moveset = AttackMoveSet.FromMeleeStats(melee);
		Assert.Equal(5,  moveset.Jab.ActiveStartFrame);
		Assert.Equal(12, moveset.Jab.ActiveEndFrame);
	}

	[Fact]
	public void FromMeleeStats_AnimationSuffix_IsNull()
	{
		var moveset = AttackMoveSet.FromMeleeStats(MakeMelee());
		Assert.Null(moveset.Jab.AnimationSuffix);
	}

	[Fact]
	public void FromMeleeStats_MovementMultiplier_IsZero()
	{
		var moveset = AttackMoveSet.FromMeleeStats(MakeMelee());
		Assert.Equal(0f, moveset.Jab.MovementMultiplier);
	}

	[Fact]
	public void FromMeleeStats_OtherSlots_AreNull()
	{
		var moveset = AttackMoveSet.FromMeleeStats(MakeMelee());
		Assert.Null(moveset.NeutralAir);
		Assert.Null(moveset.CrouchAttack);
		Assert.Null(moveset.Heavy);
	}
}
