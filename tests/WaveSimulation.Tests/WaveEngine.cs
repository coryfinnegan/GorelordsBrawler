using System;

namespace WaveSimulation.Tests;

/// <summary>
/// Pure C# port of the 1D shallow-water height-field wave simulation from AcidSurface.cs.
/// No MonoGame/Nez dependencies — all float array math.
/// Keep constants in sync with AcidSurface.cs.
/// </summary>
public sealed class WaveConfig
{
    public float WaveK             = 0.35f;
    public float WaveDamping       = 0.994f;   // higher = slower energy decay
    public float WaveMax           = 24f;
    public float PourBurst         = 50f;      // initial burst multiplier
    public float PourDecay         = 0.35f;    // burst e-folding rate (1/s)
    public float PourSustain       = 0f;       // sustained multiplier — 0 prevents long-term saturation
    public int   PourInset         = 4;
    public float RiseSpeedFraction = 0.01f;    // fraction of mapHeight/sec (~8px/sec at 800px)
    public float MaxDeltaTime      = 1f / 20f;

    // Ambient "bubbling": random micro-perturbations that keep the surface alive after the burst decays.
    // Each frame a single random column receives ±BubbleStrength velocity with BubbleProbability chance.
    // Steady-state RMS per column ≈ sqrt(P_in / (2*(1-WaveDamping)*Cols))
    //   = sqrt(0.1 * 9 / (0.012 * 160)) ≈ 0.7px/col → peak-to-trough ~4-6px across arena.
    public float BubbleProbability = 0.10f;    // chance per-frame of a bubble event
    public float BubbleStrength    = 3f;       // peak velocity per event (px/frame)
    public int   Seed              = 42;       // fixed seed for deterministic tests

    // Tiny restoring force toward h=0 per frame: prevents heights from freezing at a non-zero
    // equilibrium after injection stops.  At 0.0001 with WaveDamping=0.994 the effective height
    // halving time ≈ ln(2)/(0.994*0.0001/0.006) ≈ 42 frames ≈ 0.7s — fast enough to clear the
    // burst residual within a few seconds, slow enough not to visibly damp the waves mid-burst.
    public float RestoreK          = 0.0001f;

    public static WaveConfig Default => new WaveConfig();
}

public sealed class WaveEngine
{
    public const int CellSize = 8;

    public readonly int   Cols;
    public readonly int   MapWidth;
    public readonly int   MapHeight;

    private readonly WaveConfig _cfg;
    private readonly float[]    _surfaceY;
    private readonly float[]    _waveHeight;
    private readonly float[]    _waveVelocity;
    private readonly Random     _rng;

    private float _sourceY;
    private float _secondsActive;
    private readonly float _riseSpeedPxSec;

    public bool  IsRising     { get; private set; }
    public float CurrentLevel => _sourceY;

    // Exposed for test inspection — treat as read-only
    public float[] SurfaceY      => _surfaceY;
    public float[] WaveHeight    => _waveHeight;
    public float[] WaveVelocity  => _waveVelocity;

    public WaveEngine(int mapWidth, int mapHeight, WaveConfig? config = null)
    {
        _cfg      = config ?? WaveConfig.Default;
        MapWidth  = mapWidth;
        MapHeight = mapHeight;
        Cols      = mapWidth / CellSize;
        _rng      = new Random(_cfg.Seed);

        _riseSpeedPxSec = _cfg.RiseSpeedFraction * mapHeight;

        _surfaceY     = new float[Cols];
        _waveHeight   = new float[Cols];
        _waveVelocity = new float[Cols];

        _sourceY = mapHeight + CellSize;
        for (int c = 0; c < Cols; c++) _surfaceY[c] = mapHeight;
    }

    public void Activate() => IsRising = true;

    public void Step(float dt)
    {
        dt = MathF.Min(dt, _cfg.MaxDeltaTime);

        if (IsRising)
        {
            _secondsActive += dt;
            _sourceY -= _riseSpeedPxSec * dt;
            _sourceY  = MathF.Max(0f, _sourceY);

            if (_sourceY < MapHeight)
            {
                float envelope = _cfg.PourBurst * MathF.Exp(-_secondsActive * _cfg.PourDecay) + _cfg.PourSustain;
                float pourV    = _riseSpeedPxSec * dt * envelope;

                // Scale injection by remaining headroom so the edge column never freezes at ±WaveMax.
                float lScale = MathF.Max(0f, 1f - MathF.Abs(_waveHeight[_cfg.PourInset])             / _cfg.WaveMax);
                float rScale = MathF.Max(0f, 1f - MathF.Abs(_waveHeight[Cols - 1 - _cfg.PourInset]) / _cfg.WaveMax);
                _waveVelocity[_cfg.PourInset]             -= pourV * lScale;
                _waveVelocity[Cols - 1 - _cfg.PourInset] -= pourV * rScale;
            }
        }

        StepWaves();
        ComputeSurfaces();
    }

    public void SimulateSeconds(float seconds, float fps = 60f)
    {
        int   frames = (int)(seconds * fps);
        float dt     = 1f / fps;
        for (int f = 0; f < frames; f++) Step(dt);
    }

    private void StepWaves()
    {
        // Ambient bubbling — random micro-perturbation that keeps the surface alive
        if (_cfg.BubbleProbability > 0f && _rng.NextSingle() < _cfg.BubbleProbability)
        {
            int   col = _rng.Next(1, Cols - 1);
            float vel = (_rng.NextSingle() * 2f - 1f) * _cfg.BubbleStrength;
            _waveVelocity[col] += vel;
        }

        // Laplacian spring: each column pulled toward average of its two neighbours
        for (int x = 1; x < Cols - 1; x++)
            _waveVelocity[x] += _cfg.WaveK * (_waveHeight[x - 1] + _waveHeight[x + 1] - 2f * _waveHeight[x]);

        // Tiny restoring force toward h=0 — prevents heights freezing at non-zero equilibrium
        if (_cfg.RestoreK > 0f)
            for (int x = 0; x < Cols; x++)
                _waveVelocity[x] -= _cfg.RestoreK * _waveHeight[x];

        // Integrate + damp + clamp
        for (int x = 0; x < Cols; x++)
        {
            _waveVelocity[x] *= _cfg.WaveDamping;
            _waveHeight[x]   += _waveVelocity[x];
            _waveHeight[x]    = Math.Clamp(_waveHeight[x], -_cfg.WaveMax, _cfg.WaveMax);
        }

        // Reflecting boundaries
        _waveHeight[0]          = _waveHeight[1];
        _waveHeight[Cols - 1]   = _waveHeight[Cols - 2];
        _waveVelocity[0]        = _waveVelocity[1];
        _waveVelocity[Cols - 1] = _waveVelocity[Cols - 2];
    }

    private void ComputeSurfaces()
    {
        if (_sourceY >= MapHeight)
        {
            for (int x = 0; x < Cols; x++) _surfaceY[x] = MapHeight;
            return;
        }

        for (int x = 0; x < Cols; x++)
            _surfaceY[x] = Math.Clamp(_sourceY + _waveHeight[x], 0f, MapHeight);
    }

    public void Disturb(float worldX, float width, float speedPxPerSec)
    {
        int   colCenter = Math.Clamp((int)(worldX / CellSize), 1, Cols - 2);
        int   colRadius = Math.Max(1, (int)(width / CellSize / 2));
        float str       = Math.Min(Math.Abs(speedPxPerSec) * 0.12f, 60f);

        for (int cx = colCenter - colRadius; cx <= colCenter + colRadius; cx++)
        {
            if (cx < 1 || cx >= Cols - 1) continue;
            float falloff = 1f - (float)Math.Abs(cx - colCenter) / (colRadius + 1f);
            _waveVelocity[cx] += str * falloff;
        }
    }

    public (float min, float max, float amplitude) GetSurfaceStats()
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float y in _surfaceY)
        {
            if (y < min) min = y;
            if (y > max) max = y;
        }
        return (min, max, max - min);
    }

    /// <summary>RMS of wave heights — average energy in the wave field.</summary>
    public float GetWaveRms()
    {
        float sum = 0f;
        foreach (float h in _waveHeight) sum += h * h;
        return MathF.Sqrt(sum / Cols);
    }
}
