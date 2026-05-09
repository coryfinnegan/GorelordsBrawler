using GorelordsBrawler.Data;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Unit tests for CharacterData defaults and field invariants.
/// Pure data class — no Nez or MonoGame runtime required.
/// </summary>
public class CharacterDataTests
{
	[Fact]
	public void DefaultMaxHp_Is100()
	{
		var data = new CharacterData();
		Assert.Equal(100, data.MaxHp);
	}

	[Fact]
	public void DefaultBodyWidth_Is32()
	{
		var data = new CharacterData();
		Assert.Equal(32f, data.BodyWidth);
	}

	[Fact]
	public void DefaultBodyHeight_Is48()
	{
		var data = new CharacterData();
		Assert.Equal(48f, data.BodyHeight);
	}

	[Fact]
	public void DefaultColor_IsMidGray()
	{
		var data = new CharacterData();
		Assert.Equal(128, data.ColorR);
		Assert.Equal(128, data.ColorG);
		Assert.Equal(128, data.ColorB);
	}

	[Fact]
	public void DefaultHurtbox_IsZero_FallsBackToBody()
	{
		var data = new CharacterData();
		Assert.Equal(0f, data.HurtboxWidth);
		Assert.Equal(0f, data.HurtboxHeight);
	}

	[Fact]
	public void DefaultHurtboxOffset_IsZero()
	{
		var data = new CharacterData();
		Assert.Equal(0f, data.HurtboxOffsetX);
		Assert.Equal(0f, data.HurtboxOffsetY);
	}

	[Fact]
	public void FieldAssignment_Roundtrips()
	{
		var data = new CharacterData
		{
			Name        = "TestChar",
			MaxHp       = 150,
			BodyWidth   = 40f,
			BodyHeight  = 56f,
			ColorR      = 200,
			ColorG      = 50,
			ColorB      = 80,
		};

		Assert.Equal("TestChar", data.Name);
		Assert.Equal(150,        data.MaxHp);
		Assert.Equal(40f,        data.BodyWidth);
		Assert.Equal(56f,        data.BodyHeight);
		Assert.Equal(200,        data.ColorR);
		Assert.Equal(50,         data.ColorG);
		Assert.Equal(80,         data.ColorB);
	}

	[Fact]
	public void NullableStats_DefaultToNull()
	{
		var data = new CharacterData();
		Assert.Null(data.Movement);
		Assert.Null(data.Melee);
		Assert.Null(data.Attacks);
		Assert.Null(data.Projectile);
		Assert.Null(data.Sprite);
	}
}
