using GorelordsBrawler.Components;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Unit tests for Health component pure logic.
/// Health extends Nez.Component but its methods only access own fields —
/// no Core.Instance or Entity context required.
/// </summary>
public class HealthTests
{
	private static Health MakeHealth(int maxHp, int? current = null)
	{
		var h = new Health { MaxHp = maxHp };
		h.CurrentHp = current ?? maxHp;
		return h;
	}

	// ── TakeDamage ────────────────────────────────────────────────────────────

	[Fact]
	public void TakeDamage_ReducesCurrentHp()
	{
		var h = MakeHealth(100);
		h.TakeDamage(30);
		Assert.Equal(70, h.CurrentHp);
	}

	[Fact]
	public void TakeDamage_ClampsAtZero()
	{
		var h = MakeHealth(100);
		h.TakeDamage(999);
		Assert.Equal(0, h.CurrentHp);
	}

	[Fact]
	public void TakeDamage_FiresOnDamagedEvent_WithAmount()
	{
		var h = MakeHealth(100);
		int received = -1;
		h.OnDamaged += dmg => received = dmg;
		h.TakeDamage(25);
		Assert.Equal(25, received);
	}

	[Fact]
	public void TakeDamage_ToZero_FiresOnDeathEvent()
	{
		var h = MakeHealth(100);
		bool died = false;
		h.OnDeath += () => died = true;
		h.TakeDamage(100);
		Assert.True(died);
	}

	[Fact]
	public void TakeDamage_WhenAlreadyDead_DoesNothing()
	{
		var h = MakeHealth(100, current: 0);
		int events = 0;
		h.OnDamaged += _ => events++;
		h.TakeDamage(10);
		Assert.Equal(0, events);
		Assert.Equal(0, h.CurrentHp);
	}

	[Fact]
	public void TakeDamage_PartialDamage_DoesNotFireOnDeath()
	{
		var h = MakeHealth(100);
		bool died = false;
		h.OnDeath += () => died = true;
		h.TakeDamage(50);
		Assert.False(died);
	}

	[Fact]
	public void TakeDamage_ExactLethal_FiresOnDeath()
	{
		var h = MakeHealth(50);
		bool died = false;
		h.OnDeath += () => died = true;
		h.TakeDamage(50);
		Assert.True(died);
		Assert.Equal(0, h.CurrentHp);
	}

	// ── Heal ──────────────────────────────────────────────────────────────────

	[Fact]
	public void Heal_IncreasesCurrentHp()
	{
		var h = MakeHealth(100, current: 50);
		h.Heal(20);
		Assert.Equal(70, h.CurrentHp);
	}

	[Fact]
	public void Heal_ClampsAtMaxHp()
	{
		var h = MakeHealth(100, current: 90);
		h.Heal(999);
		Assert.Equal(100, h.CurrentHp);
	}

	[Fact]
	public void Heal_FromZero_RestoresHp()
	{
		var h = MakeHealth(100, current: 0);
		h.Heal(40);
		Assert.Equal(40, h.CurrentHp);
	}

	// ── Reset ─────────────────────────────────────────────────────────────────

	[Fact]
	public void Reset_RestoresCurrentHpToMax()
	{
		var h = MakeHealth(120, current: 1);
		h.Reset();
		Assert.Equal(120, h.CurrentHp);
	}

	[Fact]
	public void Reset_AfterDeath_AllowsDamageAgain()
	{
		var h = MakeHealth(100);
		h.TakeDamage(100);
		Assert.True(h.IsDead);
		h.Reset();
		Assert.False(h.IsDead);
		h.TakeDamage(10);
		Assert.Equal(90, h.CurrentHp);
	}

	// ── IsDead ────────────────────────────────────────────────────────────────

	[Fact]
	public void IsDead_FalseWhenHpAboveZero()
	{
		var h = MakeHealth(100);
		Assert.False(h.IsDead);
	}

	[Fact]
	public void IsDead_TrueWhenHpIsZero()
	{
		var h = MakeHealth(100, current: 0);
		Assert.True(h.IsDead);
	}

	// ── Edge cases ────────────────────────────────────────────────────────────

	[Fact]
	public void MultipleHits_AccumulateCorrectly()
	{
		var h = MakeHealth(100);
		h.TakeDamage(20);
		h.TakeDamage(30);
		h.TakeDamage(10);
		Assert.Equal(40, h.CurrentHp);
	}

	[Fact]
	public void OnDamaged_FiredOnce_PerTakeDamageCall()
	{
		var h = MakeHealth(100);
		int count = 0;
		h.OnDamaged += _ => count++;
		h.TakeDamage(10);
		h.TakeDamage(10);
		Assert.Equal(2, count);
	}
}
