using System;
using System.IO;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace WaveSimulation.Tests;

/// <summary>
/// Renders wave simulation frames as PNGs and asserts on visual quality metrics.
/// Output images land in tests/WaveSimulation.Tests/wave-output/ (relative to working dir)
/// or in bin/Debug/net8.0/wave-output/ when run via dotnet test.
/// </summary>
[Trait("Category", "Visual")]
public class WaveVisualTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "wave-output");

    private const int MapWidth  = 1280;
    private const int MapHeight = 800;

    private readonly ITestOutputHelper _out;

    public WaveVisualTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(OutputDir);
    }

    // ── Timelapse ────────────────────────────────────────────────────────────

    [Fact]
    public void RenderTimelapse_SavesFramesAtKeyMoments()
    {
        float[] timestamps = { 1f, 4f, 10f, 30f, 60f };

        foreach (float t in timestamps)
        {
            var engine = new WaveEngine(MapWidth, MapHeight);
            engine.Activate();
            engine.SimulateSeconds(t);

            var (min, max, amp) = engine.GetSurfaceStats();
            _out.WriteLine($"t={t,4:F0}s  amp={amp,6:F1}px  rms={engine.GetWaveRms(),5:F2}  src={engine.CurrentLevel,6:F1}px");

            using var img  = RenderFrame(engine, t);
            string    path = Path.Combine(OutputDir, $"wave_t{t:F0}s.png");
            img.SaveImage(path);
            Assert.True(File.Exists(path), $"Frame t={t}s not saved");
        }

        _out.WriteLine($"Images saved to: {OutputDir}");
    }

    // ── Pour-in burst sequence ───────────────────────────────────────────────

    [Fact]
    public void RenderPourInBurst_SequenceShows_DramaticInitialWave()
    {
        // 0.5s snapshots through the first 4s — burst peaks around t=2s then decays.
        float peakAmp   = 0f;

        for (int i = 1; i <= 8; i++)
        {
            float t      = i * 0.5f;
            var   engine = new WaveEngine(MapWidth, MapHeight);
            engine.Activate();
            engine.SimulateSeconds(t);

            var (_, _, amp) = engine.GetSurfaceStats();
            _out.WriteLine($"burst t={t:F1}s  amp={amp:F1}px  rms={engine.GetWaveRms():F2}");
            if (amp > peakAmp) peakAmp = amp;

            using var img  = RenderFrame(engine, t);
            string    path = Path.Combine(OutputDir, $"burst_{i:D2}_t{t:F1}s.png");
            img.SaveImage(path);
        }

        // Settled state (only bubbles)
        var settled = new WaveEngine(MapWidth, MapHeight);
        settled.Activate();
        settled.SimulateSeconds(30f);
        var (_, _, settledAmp) = settled.GetSurfaceStats();
        _out.WriteLine($"settled t=30s  amp={settledAmp:F1}px  rms={settled.GetWaveRms():F2}");

        Assert.True(peakAmp > settledAmp + 4f,
            $"Pour-in burst peak ({peakAmp:F1}px) should clearly exceed settled ripple ({settledAmp:F1}px)");
    }

    // ── Disturb / splash ─────────────────────────────────────────────────────

    [Fact]
    public void RenderDisturb_ShowsVisibleSplash_ThenPropagates()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();
        engine.SimulateSeconds(10f);

        using var before = RenderFrame(engine, 10f);
        before.SaveImage(Path.Combine(OutputDir, "disturb_00_before.png"));

        float centerAmpBefore = MathF.Abs(engine.WaveHeight[engine.Cols / 2]);

        engine.Disturb(MapWidth / 2f, 64f, 400f);

        float maxAmpAfterDisturb = 0f;
        for (int i = 1; i <= 12; i++)
        {
            engine.SimulateSeconds(0.25f);
            var (_, _, amp) = engine.GetSurfaceStats();
            if (amp > maxAmpAfterDisturb) maxAmpAfterDisturb = amp;

            using var frame = RenderFrame(engine, 10f + i * 0.25f, highlight: MapWidth / 2f);
            frame.SaveImage(Path.Combine(OutputDir, $"disturb_{i:D2}.png"));
        }

        Assert.True(maxAmpAfterDisturb > centerAmpBefore + 2f,
            $"Disturb should cause measurable amplitude increase; before={centerAmpBefore:F1}, peak after={maxAmpAfterDisturb:F1}");
    }

    // ── Surface smoothness ───────────────────────────────────────────────────

    [Fact]
    public void SurfaceRender_IsSmooth_NoBandingOrArtifacts()
    {
        var engine = new WaveEngine(MapWidth, MapHeight);
        engine.Activate();
        engine.SimulateSeconds(5f);

        using var img  = RenderFrame(engine, 5f);
        using var gray = new Mat();
        Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);

        // Sobel on X — detects horizontal surface discontinuities in the rendered image
        using var sobelX = new Mat();
        Cv2.Sobel(gray, sobelX, MatType.CV_32F, 1, 0, ksize: 3);

        // Measure gradient on the row at the source level (base surface band)
        int sourceRow = Math.Clamp((int)(engine.CurrentLevel * 0.5f), 0, gray.Rows - 1);
        using var surfaceRow = sobelX.Row(sourceRow);
        Cv2.MinMaxLoc(surfaceRow, out _, out double maxGrad);

        _out.WriteLine($"Surface row Sobel max gradient: {maxGrad:F1}");

        // Save annotated frame
        using var annotated = img.Clone();
        Cv2.PutText(annotated,
            $"amp={engine.GetSurfaceStats().amplitude:F1}px  sobel_max={maxGrad:F0}",
            new Point(8, 24), HersheyFonts.HersheyPlain, 1.4, Scalar.White, 1);
        annotated.SaveImage(Path.Combine(OutputDir, "wave_t5s_analyzed.png"));

        // The surface should be smooth: no single horizontal pixel jump > 128 grey levels
        Assert.True(maxGrad < 200,
            $"Horizontal surface gradient too high ({maxGrad:F0}) — may indicate rendering bands");
    }

    // ── Parameter comparison ─────────────────────────────────────────────────

    [Fact]
    public void RenderParameterComparison_DefaultVsTuned()
    {
        var configs = new[]
        {
            ("default",   WaveConfig.Default),
            ("high_damp", new WaveConfig { WaveDamping = 0.998f, PourSustain = 8f }),
            ("low_damp",  new WaveConfig { WaveDamping = 0.980f, PourSustain = 4f }),
            ("big_burst", new WaveConfig { PourBurst = 60f, PourSustain = 8f }),
        };

        foreach (var (name, cfg) in configs)
        {
            var engine = new WaveEngine(MapWidth, MapHeight, cfg);
            engine.Activate();
            engine.SimulateSeconds(10f);

            var (_, _, amp) = engine.GetSurfaceStats();
            _out.WriteLine($"{name,-12}  amp@10s={amp,6:F1}px  rms={engine.GetWaveRms(),5:F2}");

            using var img  = RenderFrame(engine, 10f);
            img.SaveImage(Path.Combine(OutputDir, $"compare_{name}_t10s.png"));
        }
    }

    // ── Renderer ─────────────────────────────────────────────────────────────

    private static Mat RenderFrame(WaveEngine engine, float time, float highlight = -1f)
    {
        const float Scale = 0.5f;
        int w = (int)(MapWidth  * Scale);
        int h = (int)(MapHeight * Scale);

        // Dark arena background
        var img = new Mat(h, w, MatType.CV_8UC3, new Scalar(20, 20, 20));

        // Acid body — fill each column from surfaceY to bottom
        float[] surface = engine.SurfaceY;
        for (int col = 0; col < engine.Cols; col++)
        {
            int x0 = (int)( col      * WaveEngine.CellSize * Scale);
            int x1 = (int)((col + 1) * WaveEngine.CellSize * Scale);
            int sy = (int)(surface[col] * Scale);

            if (sy < h)
                Cv2.Rectangle(img, new Rect(x0, sy, x1 - x0, h - sy),
                    new Scalar(35, 150, 35), thickness: -1);
        }

        // Source-level line (base acid, no waves — dark green)
        int srcY = (int)(engine.CurrentLevel * Scale);
        if (srcY >= 0 && srcY < h)
            Cv2.Line(img, new Point(0, srcY), new Point(w, srcY),
                new Scalar(0, 70, 0), thickness: 1);

        // Wave surface polyline — bright green
        var pts = new Point[engine.Cols];
        for (int col = 0; col < engine.Cols; col++)
            pts[col] = new Point(
                (int)(col * WaveEngine.CellSize * Scale),
                (int)(surface[col] * Scale));
        Cv2.Polylines(img, new[] { pts }, isClosed: false, new Scalar(80, 255, 80), thickness: 2);

        // Optional highlight column (e.g. disturb point)
        if (highlight >= 0f)
        {
            int hx = (int)(highlight * Scale);
            Cv2.Line(img, new Point(hx, 0), new Point(hx, h),
                new Scalar(0, 200, 255), thickness: 1);
        }

        // Annotation
        var (_, _, amp) = engine.GetSurfaceStats();
        Cv2.PutText(img,
            $"t={time:F1}s  amp={amp:F1}px  rms={engine.GetWaveRms():F2}  src={engine.CurrentLevel:F0}px",
            new Point(6, 18), HersheyFonts.HersheyPlain, 1.1, Scalar.White, 1);

        return img;
    }
}
