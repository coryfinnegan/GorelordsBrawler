using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

public class AnimationKeyBuilderTests
{
	[Fact]
	public void Prefix_CombinesWithUnderscore()
	{
		var kb = new AnimationKeyBuilder("FutureAxe");
		Assert.Equal("FutureAxe_Idle", kb.Idle);
	}

	[Fact]
	public void EmptyPrefix_ReturnsSuffixOnly()
	{
		var kb = new AnimationKeyBuilder(string.Empty);
		Assert.Equal("Idle", kb.Idle);
	}

	[Fact]
	public void NullPrefix_TreatedAsEmpty()
	{
		var kb = new AnimationKeyBuilder(null!);
		Assert.Equal("Run", kb.Run);
	}

	[Fact]
	public void FaceLeftVariants_AppendCorrectSuffix()
	{
		var kb = new AnimationKeyBuilder("Hero");
		Assert.Equal("Hero_IdleFaceLeft",  kb.IdleFaceLeft);
		Assert.Equal("Hero_RunFaceLeft",   kb.RunFaceLeft);
		Assert.Equal("Hero_JumpFaceLeft",  kb.JumpFaceLeft);
		Assert.Equal("Hero_HurtFaceLeft",  kb.HurtFaceLeft);
	}

	[Fact]
	public void CrouchVariants_FormedCorrectly()
	{
		var kb = new AnimationKeyBuilder("Axe");
		Assert.Equal("Axe_CrouchIdle",         kb.CrouchIdle);
		Assert.Equal("Axe_CrouchIdleFaceLeft",  kb.CrouchIdleFaceLeft);
		Assert.Equal("Axe_CrouchRun",           kb.CrouchRun);
		Assert.Equal("Axe_CrouchRunFaceLeft",   kb.CrouchRunFaceLeft);
	}

	[Fact]
	public void LedgeVariants_FormedCorrectly()
	{
		var kb = new AnimationKeyBuilder("Hero");
		Assert.Equal("Hero_LedgeIdle",          kb.LedgeIdle);
		Assert.Equal("Hero_LedgeIdleFaceLeft",  kb.LedgeIdleFaceLeft);
		Assert.Equal("Hero_LedgeClimb",         kb.LedgeClimb);
		Assert.Equal("Hero_LedgeClimbFaceLeft", kb.LedgeClimbFaceLeft);
	}

	[Fact]
	public void AttackVariants_FormedCorrectly()
	{
		var kb = new AnimationKeyBuilder("Hero");
		Assert.Equal("Hero_AttackIdleLeftHand",            kb.AttackIdleLeftHand);
		Assert.Equal("Hero_AttackIdleLeftHandFaceLeft",    kb.AttackIdleLeftHandFaceLeft);
		Assert.Equal("Hero_AttackRunRightHand",            kb.AttackRunRightHand);
		Assert.Equal("Hero_AttackRunRightHandFaceLeft",    kb.AttackRunRightHandFaceLeft);
	}

	[Fact]
	public void AllAttackAnims_ContainsEightEntries()
	{
		var kb = new AnimationKeyBuilder("X");
		Assert.Equal(8, kb.AllAttackAnims.Length);
	}

	[Fact]
	public void AllAttackAnims_ContainsAllFourDirectionalVariants()
	{
		var kb = new AnimationKeyBuilder("X");
		var anims = kb.AllAttackAnims;
		Assert.Contains(kb.AttackIdleLeftHand,           anims);
		Assert.Contains(kb.AttackIdleLeftHandFaceLeft,   anims);
		Assert.Contains(kb.AttackRunLeftHand,            anims);
		Assert.Contains(kb.AttackRunLeftHandFaceLeft,    anims);
		Assert.Contains(kb.AttackIdleRightHand,          anims);
		Assert.Contains(kb.AttackIdleRightHandFaceLeft,  anims);
		Assert.Contains(kb.AttackRunRightHand,           anims);
		Assert.Contains(kb.AttackRunRightHandFaceLeft,   anims);
	}

	[Fact]
	public void AllKeys_AreUniqueForGivenPrefix()
	{
		var kb = new AnimationKeyBuilder("Hero");
		var all = new[]
		{
			kb.Idle, kb.IdleFaceLeft, kb.Run, kb.RunFaceLeft,
			kb.Jump, kb.JumpFaceLeft, kb.CrouchIdle, kb.CrouchIdleFaceLeft,
			kb.CrouchRun, kb.CrouchRunFaceLeft, kb.Hurt, kb.HurtFaceLeft,
			kb.LedgeIdle, kb.LedgeIdleFaceLeft, kb.LedgeClimb, kb.LedgeClimbFaceLeft,
		};
		Assert.Equal(all.Length, new System.Collections.Generic.HashSet<string>(all).Count);
	}

	[Fact]
	public void SamePrefix_ProducesConsistentResults_CalledTwice()
	{
		var kb = new AnimationKeyBuilder("FutureAxe");
		Assert.Equal(kb.Idle, kb.Idle);
		Assert.Equal(kb.Run,  kb.Run);
	}
}
