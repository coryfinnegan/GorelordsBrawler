using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// PASS 3 of the liquid pipeline: samples the scene render target AND
	/// the additive field RT produced by <see cref="LiquidFieldRenderer"/>,
	/// then runs the <c>liquid.fx</c> shader to threshold the field into
	/// solid liquid and composite it over the scene.
	///
	/// Tunables (read from <see cref="FluidConfig"/>) are pushed to the
	/// shader every frame so live-editing the values rebuild-applies
	/// without rebooting the game.
	///
	/// Phase 3 addition: also projects each active player's collider bounds
	/// into UV space and pushes them to the shader, so the shader can
	/// reduce the body mask + tint the scene inside player regions —
	/// makes the player visible THROUGH the acid when submerged.
	/// </summary>
	public class LiquidPostProcessor : PostProcessor
	{
		// MAX_PLAYERS in the shader. Keep in sync with liquid.fx #define.
		private const int MaxPlayers = 4;

		private readonly LiquidFieldRenderer _fieldRenderer;
		private readonly PlayerManager       _playerManager;
		private readonly Color _bodyColor;
		private readonly Color _edgeColor;

		// Reused per frame — no allocations in the render path.
		private readonly Vector4[] _playerRects = new Vector4[MaxPlayers];

		public LiquidPostProcessor(int executionOrder, Effect liquidEffect,
			LiquidFieldRenderer fieldRenderer, PlayerManager playerManager,
			Color bodyColor, Color edgeColor)
			: base(executionOrder, liquidEffect)
		{
			_fieldRenderer = fieldRenderer;
			_playerManager = playerManager;
			_bodyColor     = bodyColor;
			_edgeColor     = edgeColor;
		}

		public override void Process(RenderTarget2D source, RenderTarget2D destination)
		{
			// Forward tunables to the shader. Done every frame so changes to
			// FluidConfig constants (or live-tuned override values) apply
			// immediately on rebuild.
			Effect.Parameters["FieldTexture"]?.SetValue(_fieldRenderer.FieldTexture.RenderTarget);
			Effect.Parameters["ThresholdMin"]?.SetValue(FluidConfig.LiquidThresholdMin);
			Effect.Parameters["ThresholdMax"]?.SetValue(FluidConfig.LiquidThresholdMax);
			Effect.Parameters["EdgeBandWidth"]?.SetValue(FluidConfig.LiquidEdgeBandWidth);
			Effect.Parameters["LiquidColor"]?.SetValue(_bodyColor.ToVector4());
			Effect.Parameters["EdgeColor"]?.SetValue(_edgeColor.ToVector4());

			// Pulse — sells "alive / corrosive / dangerous". Drives an
			// in-shader modulation of the bright surface-highlight band only;
			// the body shape is unaffected so geometry stays stable.
			// Rate of 2.5 Hz reads as a slow breath. 0..1 range.
			float pulse = (System.MathF.Sin(Time.TotalTime * FluidConfig.LiquidPulseSpeed) * 0.5f) + 0.5f;
			Effect.Parameters["Pulse"]?.SetValue(pulse);
			Effect.Parameters["PulseStrength"]?.SetValue(FluidConfig.LiquidPulseStrength);

			// Phase 3: project each active player's collider bounds into UV
			// space (0..1 across source RT) so the shader can mask + tint
			// the player region inside the acid composite.
			int playerCount = CollectPlayerRectsUv(source);
			Effect.Parameters["PlayerRects"]?.SetValue(_playerRects);
			Effect.Parameters["PlayerCount"]?.SetValue(playerCount);
			Effect.Parameters["PlayerMaskStrength"]?.SetValue(FluidConfig.LiquidPlayerMaskStrength);
			Effect.Parameters["PlayerTintStrength"]?.SetValue(FluidConfig.LiquidPlayerTintStrength);

			DrawFullscreenQuad(source, destination, Effect);
		}

		/// <summary>
		/// Fills <see cref="_playerRects"/> with each active player's collider
		/// AABB in UV space (0..1 across the source render target). Returns
		/// the active player count. Unused slots are zeroed so the shader's
		/// AABB tests for those slots fail everywhere except (0,0).
		///
		/// World → screen → UV: the scene's Camera projects world coords to
		/// screen coords via <c>Matrix.Invert(camera.TransformMatrix)</c> — we
		/// just normalise by RT dimensions.
		/// </summary>
		private int CollectPlayerRectsUv(RenderTarget2D source)
		{
			// Zero all slots first so any inactive slot reads (0,0,0,0).
			for (int i = 0; i < MaxPlayers; i++) _playerRects[i] = Vector4.Zero;

			if (_playerManager == null) return 0;
			// _scene is PostProcessor's reference to the owning Scene
			// (set in OnAddedToScene). Not Entity — PostProcessors aren't
			// attached to entities the way Components are.
			var camera = _scene?.Camera;
			if (camera == null) return 0;

			float rtW = source.Width;
			float rtH = source.Height;

			var players = _playerManager.GetActivePlayers();
			int count = System.Math.Min(players.Count, MaxPlayers);
			int written = 0;
			for (int i = 0; i < count; i++)
			{
				var collider = players[i].GetComponent<Collider>();
				if (collider == null) continue;
				var b = collider.Bounds;

				// World-space AABB → screen-space (pixels) → UV (0..1).
				Vector2 minScreen = camera.WorldToScreenPoint(new Vector2(b.X, b.Y));
				Vector2 maxScreen = camera.WorldToScreenPoint(new Vector2(b.X + b.Width, b.Y + b.Height));

				float uMin = MathHelper.Clamp(System.Math.Min(minScreen.X, maxScreen.X) / rtW, 0f, 1f);
				float uMax = MathHelper.Clamp(System.Math.Max(minScreen.X, maxScreen.X) / rtW, 0f, 1f);
				float vMin = MathHelper.Clamp(System.Math.Min(minScreen.Y, maxScreen.Y) / rtH, 0f, 1f);
				float vMax = MathHelper.Clamp(System.Math.Max(minScreen.Y, maxScreen.Y) / rtH, 0f, 1f);

				// Skip degenerate rects (player completely off-screen).
				if (uMax - uMin < 1e-4f || vMax - vMin < 1e-4f) continue;

				_playerRects[written++] = new Vector4(uMin, vMin, uMax, vMax);
			}
			return written;
		}
	}
}
