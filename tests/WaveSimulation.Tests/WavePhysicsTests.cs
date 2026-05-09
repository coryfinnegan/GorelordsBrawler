using System;
using Xunit;

namespace WaveSimulation.Tests;

public class WavePhysicsTests
{
    private const int MapWidth  = 1280;
    private const int MapHeight = 800;

    [Fact]
    public void WaveDoesNotDiverge_Over60Seconds()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();

        for (int frame = 0; frame < 3600; frame++)
        {
            engine.Step(1f / 60f);

            foreach (float h in engine.WaveHeight)
            {
                Assert.False(float.IsNaN(h),      $"NaN in WaveHeight at frame {frame}");
                Assert.False(float.IsInfinity(h), $"Infinity in WaveHeight at frame {frame}");
            }

            foreach (float s in engine.SurfaceY)
                Assert.InRange(s, 0f, MapHeight);
        }
    }

    [Fact]
    public void CurrentLevel_IsMonotonicallyDecreasing_WhileRising()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();

        float prev = engine.CurrentLevel;
        for (int frame = 0; frame < 600; frame++)
        {
            engine.Step(1f / 60f);
            Assert.True(engine.CurrentLevel <= prev + 0.001f,
                $"CurrentLevel rose at frame {frame}: {prev:F3} → {engine.CurrentLevel:F3}");
            prev = engine.CurrentLevel;
        }
    }

    [Fact]
    public void CurrentLevel_NotPulledByWaveOscillation()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();

        // Inject a huge disturb to create violent wave oscillation
        engine.SimulateSeconds(5f);
        engine.Disturb(MapWidth / 2f, 200f, 2000f);
        engine.Disturb(MapWidth / 4f, 200f, 2000f);

        float levelBefore = engine.CurrentLevel;

        // One single step — CurrentLevel should only move by riseSpeed * dt, not by wave
        engine.Step(1f / 60f);

        float expected = levelBefore - (WaveConfig.Default.RiseSpeedFraction * MapHeight / 60f);
        Assert.Equal(expected, engine.CurrentLevel, precision: 2);
    }

    [Fact]
    public void WavePropagates_FromEdgeToCenter()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();

        // Pour-in naturally starts at edge columns; after 3s the centre should have energy
        engine.SimulateSeconds(3f);

        int   centerCol = engine.Cols / 2;
        float centerAmp = MathF.Abs(engine.WaveHeight[centerCol]);

        Assert.True(centerAmp > 0.5f,
            $"Wave should propagate from edge to centre by t=3s; centre amplitude={centerAmp:F2}px");
    }

    [Fact]
    public void PourIn_BurstIsLargerThanSteadyState()
    {
        // Burst peaks around t=2-3s (acid enters at t=1s, injection builds quickly).
        var burst = new WaveEngine(MapWidth, MapHeight);
        burst.Activate();
        burst.SimulateSeconds(3f);
        var (_, _, ampBurst) = burst.GetSurfaceStats();

        var settled = new WaveEngine(MapWidth, MapHeight);
        settled.Activate();
        settled.SimulateSeconds(40f);
        var (_, _, ampSettled) = settled.GetSurfaceStats();

        Assert.True(ampBurst > ampSettled + 4f,
            $"Burst amplitude ({ampBurst:F1}px @ t=3s) should clearly exceed settled amplitude ({ampSettled:F1}px @ t=40s)");
    }

    [Fact]
    public void Disturb_InjectsVelocityAtTargetColumn()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();
        engine.SimulateSeconds(5f);

        int   targetCol = engine.Cols / 2;
        float worldX    = targetCol * WaveEngine.CellSize;
        float velBefore = MathF.Abs(engine.WaveVelocity[targetCol]);

        engine.Disturb(worldX, 32f, 300f);

        float velAfter = MathF.Abs(engine.WaveVelocity[targetCol]);
        Assert.True(velAfter > velBefore,
            $"Disturb should increase velocity at col {targetCol}; before={velBefore:F2}, after={velAfter:F2}");
    }

    [Fact]
    public void BoundaryCondition_WaveReflectsOffWall()
    {
        // Use a no-bubble config so ambient noise doesn't mask the reflection signal.
        var cfg    = new WaveConfig { BubbleProbability = 0f };
        var engine = new WaveEngine(MapWidth, MapHeight, cfg);
        engine.Activate();

        // Simulate long enough that the burst fully decays to near-zero.
        engine.SimulateSeconds(40f);

        // Field is now flat — inject near the right wall and measure centre RMS before/after.
        float nearWallX = (engine.Cols - 8) * WaveEngine.CellSize;
        engine.Disturb(nearWallX, 32f, 800f);

        float rmsBefore = CentreRms(engine);

        // Wave speed ≈ sqrt(WaveK) ≈ 0.59 cols/frame → ~35 cols/s.
        // From col (Cols-8) to wall (8 cols, 0.23s), reflect, travel to centre (80 cols, 2.3s) ≈ 2.5s total.
        engine.SimulateSeconds(3f);

        float rmsAfter = CentreRms(engine);

        Assert.True(rmsAfter > rmsBefore + 1f,
            $"Reflected wave should raise centre RMS; before={rmsBefore:F2}, after={rmsAfter:F2}");
    }

    private static float CentreRms(WaveEngine engine)
    {
        int   colL = engine.Cols / 4;
        int   colR = 3 * engine.Cols / 4;
        float sum  = 0f;
        for (int x = colL; x <= colR; x++) sum += engine.WaveHeight[x] * engine.WaveHeight[x];
        return MathF.Sqrt(sum / (colR - colL + 1));
    }

    [Fact]
    public void SurfaceAmplitude_IsVisiblyLarge_At5Seconds()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();
        engine.SimulateSeconds(5f);

        var (_, _, amplitude) = engine.GetSurfaceStats();
        Assert.True(amplitude >= 4f,
            $"Surface amplitude at t=5s should be >= 4px for visual impact; got {amplitude:F1}px");
    }

    [Fact]
    public void SettledState_HasGentleRipple_NotFlatNotWild()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();
        engine.SimulateSeconds(60f);

        // Bubbles maintain a gentle ~3-6px ripple — not dead flat, not huge.
        var (_, _, amplitude) = engine.GetSurfaceStats();
        Assert.InRange(amplitude, 0.5f, 14f);
    }

    [Fact]
    public void SurfaceIsSmooth_NoAbruptJumps()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();
        engine.SimulateSeconds(5f);

        float maxJump = 0f;
        for (int x = 1; x < engine.Cols; x++)
        {
            float jump = MathF.Abs(engine.SurfaceY[x] - engine.SurfaceY[x - 1]);
            if (jump > maxJump) maxJump = jump;
        }

        Assert.True(maxJump < WaveConfig.Default.WaveMax,
            $"Adjacent column jump ({maxJump:F1}px) exceeds WaveMax ({WaveConfig.Default.WaveMax}px) — surface is discontinuous");
    }

    [Fact]
    public void WaveSymmetry_PourIn_IsExactlySymmetric_WithoutBubbles()
    {
        // Disable bubbles so only the pour-in affects the field — it's injected identically
        // at left and right inset columns every frame, so the result must be left-right symmetric.
        var cfg    = new WaveConfig { BubbleProbability = 0f };
        var engine = new WaveEngine(MapWidth, MapHeight, cfg);
        engine.Activate();
        engine.SimulateSeconds(2f);

        int   half = engine.Cols / 2;
        float rms  = 0f;
        for (int i = 0; i < half; i++)
        {
            float diff = engine.WaveHeight[i] - engine.WaveHeight[engine.Cols - 1 - i];
            rms += diff * diff;
        }
        rms = MathF.Sqrt(rms / half);

        Assert.True(rms < 0.01f,
            $"Pure pour-in (no bubbles) must be exactly symmetric; asymmetry RMS={rms:F4}px");
    }
}
