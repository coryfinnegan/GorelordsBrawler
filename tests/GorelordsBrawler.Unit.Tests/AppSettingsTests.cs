using System;
using GorelordsBrawler;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Pins the env-var overlay contract that fixes the "keyboard dead after an E2E
/// run" bug: the test/smoke harnesses inject debug flags as GLB_* environment
/// variables on the game's child process, and <see cref="AppSettings"/> overlays
/// them on top of its config file. The load-bearing guarantee is that a NORMAL
/// run — which injects none of these vars — keeps the file/default value, so
/// DebugAutomation stays false and both players keep the keyboard. An explicit
/// injection (and only that) flips it on.
/// </summary>
public class AppSettingsTests
{
	// ── Absent / blank → keep the default (the "defaults to regular controls" rule) ──

	[Fact]
	public void UnsetEnvVar_ResolvesToNull_AndKeepsCurrentValue()
	{
		// The exact input a manual run feeds the overlay: a GLB_* var nothing
		// sets, so Environment.GetEnvironmentVariable returns null. The default
		// must survive untouched in BOTH directions — this is what keeps
		// DebugAutomation false (keyboard) on a normal launch. Sourcing the null
		// from the real environment (not a null literal) also dodges xUnit1012.
		string? raw = Environment.GetEnvironmentVariable(
			"GLB_DEFINITELY_NOT_SET_" + nameof(UnsetEnvVar_ResolvesToNull_AndKeepsCurrentValue));
		Assert.Null(raw);
		Assert.False(AppSettings.ParseBoolOverride(raw, current: false));
		Assert.True(AppSettings.ParseBoolOverride(raw, current: true));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Blank_KeepsCurrentValue(string raw)
	{
		Assert.False(AppSettings.ParseBoolOverride(raw, current: false));
		Assert.True(AppSettings.ParseBoolOverride(raw, current: true));
	}

	// ── Explicit truthy → on (the harness injection path) ────────────────────

	[Theory]
	[InlineData("1")]
	[InlineData("true")]
	[InlineData("True")]
	[InlineData("TRUE")]
	[InlineData("  true  ")]   // tolerant of stray whitespace from a shell
	public void RecognizedTruthy_TurnsOn_RegardlessOfDefault(string raw)
	{
		Assert.True(AppSettings.ParseBoolOverride(raw, current: false));
		Assert.True(AppSettings.ParseBoolOverride(raw, current: true));
	}

	// ── Explicit falsy → off (a harness can also force a flag OFF) ────────────

	[Theory]
	[InlineData("0")]
	[InlineData("false")]
	[InlineData("False")]
	[InlineData("  0 ")]
	public void RecognizedFalsy_TurnsOff_RegardlessOfDefault(string raw)
	{
		Assert.False(AppSettings.ParseBoolOverride(raw, current: false));
		Assert.False(AppSettings.ParseBoolOverride(raw, current: true));
	}

	// ── Unrecognized → keep the default (fail toward the file, never garbage) ──

	[Theory]
	[InlineData("yes")]
	[InlineData("on")]
	[InlineData("2")]
	[InlineData("nonsense")]
	public void Unrecognized_KeepsCurrentValue(string raw)
	{
		// A typo in the channel must NOT silently disable a flag the file enabled
		// (or vice-versa); it leaves the current value alone.
		Assert.False(AppSettings.ParseBoolOverride(raw, current: false));
		Assert.True(AppSettings.ParseBoolOverride(raw, current: true));
	}
}
