using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// Renders a three-tier liquid cascade and drives the floor pool level.
	///
	/// Cascade path (matches the normalized platform table):
	///   Inlet        : above-screen-top → top platform center (vertical)
	///   Top spread   : left and right across the top platform surface
	///   Tier-1 falls : top platform edges → BotLeft/BotRight inner edges (vertical)
	///   Bot spreads  : across BotLeft (rightward) and BotRight (leftward)
	///   Tier-2 falls : BotLeft/BotRight outer edges → floor pool surface (vertical)
	///
	/// Pool physics: AddVolume() is called every frame so the pool rises BECAUSE
	/// the stream is pouring into it — no independent timer in AcidSurface.
	///
	/// Visual: solid opaque columns with turbulent edge wobble (shape deformation,
	/// not scrolling color bands). The stream color matches the pool so they look
	/// like a continuous body of the same liquid.
	/// </summary>
	public class CascadeRenderer : RenderableComponent, IUpdatable
	{
		private readonly AcidSurface _acid;
		private readonly int         _mapWidth;
		private readonly int         _mapHeight;

		// ── Tier-1 geometry (top platform) ────────────────────────────────────
		private readonly float _inletX;
		private readonly float _inletTopY;
		private readonly float _platTopY;
		private readonly float _platLeftX;
		private readonly float _platRightX;

		// ── Tier-2 geometry (bot platforms) ───────────────────────────────────
		private readonly float _botLeftTopY;
		private readonly float _botLeftInnerX;   // right edge of BotLeft
		private readonly float _botLeftOuterX;   // left edge  of BotLeft
		private readonly float _botRightTopY;
		private readonly float _botRightInnerX;  // left  edge of BotRight
		private readonly float _botRightOuterX;  // right edge of BotRight

		// ── Stream dimensions ──────────────────────────────────────────────────
		private const float InletHalfW = 14f;   // focused inlet beam
		private const float FallHalfW  = 10f;   // side falls slightly narrower
		private const float SpreadH    =  7f;   // height of the platform spreading layer

		// ── Colors (match the pool for visual continuity) ──────────────────────
		private readonly Color _streamColor;
		private readonly Color _coreColor;

		// ── Timing & volume ────────────────────────────────────────────────────
		private const float FadeInDuration = 0.6f;
		private const float PourRate       = 5f;   // wave-injection rate at floor impact

		private readonly float _flowRatePerSec;  // pixel-area / sec added to pool

		// ── State ──────────────────────────────────────────────────────────────
		private float _timeActive;
		private float _fadeAlpha;
		private bool  _burstFired;

		private PrimitiveBatch _primBatch;

		public override RectangleF Bounds => new RectangleF(0, 0, 9999, 9999);

		public CascadeRenderer(AcidSurface acid, int mapWidth, int mapHeight)
		{
			_acid      = acid;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			float platH = GameConstants.Hazards.PlatformHeight;
			var   plats = GameConstants.Hazards.Platforms;

			// Identify top platform (min cy) and the two bot platforms (max cy)
			float minCy = float.MaxValue, maxCy = 0f;
			foreach (var p in plats)
			{
				if (p.cy < minCy) minCy = p.cy;
				if (p.cy > maxCy) maxCy = p.cy;
			}

			(float cx, float cy, float w) top  = default;
			(float cx, float cy, float w) botL  = default;
			(float cx, float cy, float w) botR  = default;
			foreach (var p in plats)
			{
				if (p.cy == minCy) top = p;
				if (p.cy == maxCy) { if (p.cx < 0.5f) botL = p; else botR = p; }
			}

			// Top platform
			_platTopY   = top.cy * mapHeight - platH * 0.5f;
			_platLeftX  = (top.cx - top.w * 0.5f) * mapWidth;
			_platRightX = (top.cx + top.w * 0.5f) * mapWidth;
			_inletX     = top.cx * mapWidth;
			_inletTopY  = -40f;

			// Bot platforms
			_botLeftTopY   = botL.cy * mapHeight - platH * 0.5f;
			_botLeftInnerX = (botL.cx + botL.w * 0.5f) * mapWidth;  // right edge
			_botLeftOuterX = (botL.cx - botL.w * 0.5f) * mapWidth;  // left  edge

			_botRightTopY   = botR.cy * mapHeight - platH * 0.5f;
			_botRightInnerX = (botR.cx - botR.w * 0.5f) * mapWidth; // left  edge
			_botRightOuterX = (botR.cx + botR.w * 0.5f) * mapWidth; // right edge

			// Stream color matches the pool
			_streamColor = new Color(55, 175, 38, 215);
			_coreColor   = new Color(110, 235, 75, 240);

			// Flow rate: volume per second that fills the pool at AcidRiseSpeed
			float speedMult  = AppSettings.DebugFastAcid ? GameConstants.Hazards.AcidDebugRiseMultiplier : 1f;
			float risePixSec = GameConstants.Hazards.AcidRiseSpeed * mapHeight * speedMult;
			_flowRatePerSec  = risePixSec * mapWidth;

			RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			LayerDepth  = 0f;
		}

		public override void OnAddedToEntity()
		{
			_primBatch = new PrimitiveBatch(8192);
		}

		// ── IUpdatable ────────────────────────────────────────────────────────

		public void Update()
		{
			if (!_acid.IsRising) return;

			float dt = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);
			_timeActive += dt;
			_fadeAlpha   = Math.Min(1f, _timeActive / FadeInDuration);

			// The stream IS the source of the pool — add volume every frame
			_acid.AddVolume(_flowRatePerSec * dt);

			// Initial splash burst once the stream has had time to reach the floor
			if (!_burstFired && _timeActive > 0.5f)
			{
				_burstFired = true;
				_acid.Disturb(_botLeftOuterX,  52f, 500f);
				_acid.Disturb(_botRightOuterX, 52f, 500f);
			}

			// Sustained wave injection at the floor impact points
			_acid.PourAt(_botLeftOuterX,  40f, PourRate, dt);
			_acid.PourAt(_botRightOuterX, 40f, PourRate, dt);
		}

		// ── Render ────────────────────────────────────────────────────────────

		public override void Render(Batcher batcher, Camera camera)
		{
			if (!_acid.IsRising || _fadeAlpha <= 0f) return;

			// Nothing left to show once the top platform is fully submerged
			if (_acid.CurrentLevel <= _platTopY) return;

			batcher.FlushBatch();

			var gd = Core.GraphicsDevice;
			gd.BlendState        = BlendState.AlphaBlend;
			gd.DepthStencilState = DepthStencilState.None;
			gd.RasterizerState   = RasterizerState.CullNone;

			Matrix proj = camera.ProjectionMatrix;
			Matrix view = camera.TransformMatrix;
			_primBatch.Begin(ref proj, ref view);

			float t     = _timeActive;
			float level = _acid.CurrentLevel;

			// 1. Inlet: above-screen-top → top platform surface
			DrawStream(_inletX, _inletTopY, _inletX, _platTopY, InletHalfW, t, 0f);

			// 2. Top platform spread
			DrawSpread(_inletX, _platLeftX,  _platTopY, SpreadH, t, 0.6f);
			DrawSpread(_inletX, _platRightX, _platTopY, SpreadH, t, 1.4f);

			if (level > _botLeftTopY)
			{
				// 3. Tier-1 falls: top platform edges → bot platform inner edges
				DrawStream(_platLeftX,  _platTopY, _botLeftInnerX,  _botLeftTopY,  FallHalfW, t, 1.9f);
				DrawStream(_platRightX, _platTopY, _botRightInnerX, _botRightTopY, FallHalfW, t, 3.3f);

				// 4. Bot platform spreads
				DrawSpread(_botLeftInnerX,  _botLeftOuterX,  _botLeftTopY,  SpreadH * 0.8f, t, 2.6f);
				DrawSpread(_botRightInnerX, _botRightOuterX, _botRightTopY, SpreadH * 0.8f, t, 4.1f);

				// 5. Tier-2 falls: bot platform outer edges → floor pool surface
				float tier2Bot = Math.Min(level, (float)_mapHeight);
				DrawStream(_botLeftOuterX,  _botLeftTopY,  _botLeftOuterX,  tier2Bot, FallHalfW, t, 5.2f);
				DrawStream(_botRightOuterX, _botRightTopY, _botRightOuterX, tier2Bot, FallHalfW, t, 6.5f);
			}
			else
			{
				// Bot platforms submerged — tier-1 falls extend directly to pool surface
				DrawStream(_platLeftX,  _platTopY, _platLeftX,  level, FallHalfW, t, 1.9f);
				DrawStream(_platRightX, _platTopY, _platRightX, level, FallHalfW, t, 3.3f);
			}

			_primBatch.End();
		}

		// ── Vertical stream ───────────────────────────────────────────────────

		/// <summary>
		/// Draws a solid column of liquid from (x1, topY) to (x2, botY).
		/// An outer body quad covers the full width; a narrower inner core adds a
		/// highlight.  The edge wobble is a shape deformation (two summed sine waves
		/// at different spatial frequencies), not a scrolling color pattern.
		/// </summary>
		private void DrawStream(float x1, float topY, float x2, float botY,
			float halfW, float time, float phase)
		{
			if (botY <= topY) return;

			const float SegH = 5f;
			int segs = Math.Max(1, (int)MathF.Ceiling((botY - topY) / SegH));
			float sh = (botY - topY) / segs;

			for (int i = 0; i < segs; i++)
			{
				float y0 = topY + i       * sh;
				float y1 = topY + (i + 1) * sh;
				float t0 = (float)i       / segs;
				float t1 = (float)(i + 1) / segs;

				// Center X interpolated along the diagonal (usually straight vertical)
				float xc0 = x1 + (x2 - x1) * t0;
				float xc1 = x1 + (x2 - x1) * t1;

				// Turbulent edge wobble: two sine waves at different frequencies
				// creates shape deformation that looks like fluid turbulence.
				float w0 = MathF.Sin(time * 4.3f + y0 * 0.020f + phase) * 1.8f
				         + MathF.Sin(time * 7.9f + y0 * 0.047f + phase * 1.6f) * 0.9f;
				float w1 = MathF.Sin(time * 4.3f + y1 * 0.020f + phase) * 1.8f
				         + MathF.Sin(time * 7.9f + y1 * 0.047f + phase * 1.6f) * 0.9f;

				// Subtle taper: stream loses a little width as it falls
				float hw0 = halfW * (1f - t0 * 0.04f);
				float hw1 = halfW * (1f - t1 * 0.04f);

				Color cBody = _streamColor * _fadeAlpha;
				AddQuad(
					new Vector2(xc0 - hw0 + w0, y0), new Vector2(xc0 + hw0 + w0, y0),
					new Vector2(xc1 - hw1 + w1, y1), new Vector2(xc1 + hw1 + w1, y1),
					cBody, cBody);

				// Bright inner core (narrower)
				float chw0 = hw0 * 0.45f;
				float chw1 = hw1 * 0.45f;
				Color cCore = _coreColor * _fadeAlpha;
				AddQuad(
					new Vector2(xc0 - chw0 + w0, y0), new Vector2(xc0 + chw0 + w0, y0),
					new Vector2(xc1 - chw1 + w1, y1), new Vector2(xc1 + chw1 + w1, y1),
					cCore, cCore);
			}
		}

		// ── Horizontal spread on platform top ─────────────────────────────────

		/// <summary>
		/// Thin horizontal layer spreading from fromX to toX along a platform top.
		/// Alpha fades to zero at the outer edge with a quadratic falloff.
		/// </summary>
		private void DrawSpread(float fromX, float toX, float platTopY,
			float spreadH, float time, float phase)
		{
			float len = toX - fromX;
			if (MathF.Abs(len) < 1f) return;

			float y0 = platTopY;
			float y1 = platTopY + spreadH;

			const float SegW = 5f;
			int segs = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(len) / SegW));
			float sw = len / segs;

			for (int i = 0; i < segs; i++)
			{
				float x0 = fromX + i       * sw;
				float x1 = fromX + (i + 1) * sw;
				float t0 = (float)i / segs;  // 0 = near inlet, 1 = near edge

				float edgeFade = 1f - t0 * t0;  // quadratic: stays solid near center, fades at edge
				Color c = _streamColor * (edgeFade * _fadeAlpha);

				float xl = Math.Min(x0, x1);
				float xr = Math.Max(x0, x1);
				AddQuad(new Vector2(xl, y0), new Vector2(xr, y0),
				        new Vector2(xl, y1), new Vector2(xr, y1), c, c);
			}
		}

		private void AddQuad(Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br, Color ct, Color cb)
		{
			_primBatch.AddVertex(tl, ct, PrimitiveType.TriangleList);
			_primBatch.AddVertex(tr, ct, PrimitiveType.TriangleList);
			_primBatch.AddVertex(bl, cb, PrimitiveType.TriangleList);
			_primBatch.AddVertex(tr, ct, PrimitiveType.TriangleList);
			_primBatch.AddVertex(br, cb, PrimitiveType.TriangleList);
			_primBatch.AddVertex(bl, cb, PrimitiveType.TriangleList);
		}

		public override void OnRemovedFromEntity()
		{
			_primBatch?.Dispose();
			_primBatch = null;
		}
	}
}
