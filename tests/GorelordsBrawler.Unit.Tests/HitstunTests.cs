using GorelordsBrawler.Components;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Unit tests for Hitstun component — Trigger/Duration logic only.
/// Update() uses Nez Time.DeltaTime and is not tested here.
/// </summary>
public class HitstunTests
{
	[Fact]
	public void IsActive_FalseByDefault()
	{
		var hs = new Hitstun();
		Assert.False(hs.IsActive);
	}

	[Fact]
	public void Trigger_ActivatesStun()
	{
		var hs = new Hitstun();
		hs.Trigger(0.3f);
		Assert.True(hs.IsActive);
	}

	[Fact]
	public void Trigger_SetsDuration()
	{
		var hs = new Hitstun();
		hs.Trigger(0.5f);
		Assert.Equal(0.5f, hs.Duration);
	}

	[Fact]
	public void Trigger_LongerDuration_Overwrites()
	{
		var hs = new Hitstun();
		hs.Trigger(0.2f);
		hs.Trigger(0.8f);
		Assert.Equal(0.8f, hs.Duration);
	}

	[Fact]
	public void Trigger_ShorterDuration_DoesNotOverwrite()
	{
		var hs = new Hitstun();
		hs.Trigger(0.8f);
		hs.Trigger(0.2f);
		Assert.Equal(0.8f, hs.Duration);
	}

	[Fact]
	public void Trigger_ZeroDuration_DoesNotActivate()
	{
		var hs = new Hitstun();
		hs.Trigger(0f);
		Assert.False(hs.IsActive);
	}

	[Fact]
	public void Trigger_EqualDuration_DoesNotChange()
	{
		var hs = new Hitstun();
		hs.Trigger(0.5f);
		hs.Trigger(0.5f);
		Assert.Equal(0.5f, hs.Duration);
	}
}
