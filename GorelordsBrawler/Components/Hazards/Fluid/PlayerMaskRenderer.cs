using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Sprites;
using Nez.Textures;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Pixel-perfect player silhouette renderer for the Phase 3 see-through-acid
	/// effect. Renders each active player's CURRENT sprite frame into its own
	/// RenderTexture with a forced <see cref="Color.White"/> tint, so the RT's
	/// alpha channel is a pixel-perfect silhouette of every player that's
	/// currently on screen.
	///
	/// <see cref="LiquidPostProcessor"/> binds this RT as a texture sampler in
	/// <c>liquid.fx</c> (parameter <c>PlayerMaskTexture</c>) and samples its
	/// alpha as the player mask. That's strictly better than the bounding-rect
	/// approach we tried first — the see-through region follows the actual
	/// sprite outline, including weapon poses / animation-frame shape changes,
	/// instead of leaving a rectangular "window" extending past the visible art.
	///
	/// Renders the sprite directly via <c>Batcher.Draw(Sprite, ..., Color.White,
	/// ...)</c> — bypasses each player's <c>SpriteAnimator.Color</c> (which is
	/// being modulated by HitFlash) so the mask is the raw silhouette, not the
	/// tinted appearance. Origin / scale / rotation / flip are read from the
	/// player's Transform so the mask aligns exactly with the scene render.
	///
	/// Order: <see cref="GameConstants.Rendering.PlayerMaskRendererOrder"/>
	/// (-5) — after <see cref="LiquidFieldRenderer"/> (-10), before the default
	/// scene Renderer (0). Order within renderers doesn't matter for the
	/// post-process consumer, but keeps the pre-scene RT passes grouped.
	/// </summary>
	public class PlayerMaskRenderer : Renderer
	{
		private readonly PlayerManager _playerManager;

		public RenderTexture MaskTexture { get; private set; }

		public PlayerMaskRenderer(int renderOrder, PlayerManager playerManager)
			: base(renderOrder, null)
		{
			_playerManager = playerManager;
		}

		public override void OnAddedToScene(Scene scene)
		{
			base.OnAddedToScene(scene);
			MaskTexture   = new RenderTexture();
			RenderTexture = MaskTexture;
		}

		public override void OnSceneBackBufferSizeChanged(int newWidth, int newHeight)
		{
			// Full-resolution: must match the scene RT exactly so the shader's
			// uv (which is scene-RT-relative) samples the mask at the matching
			// pixel. A smaller mask would alias hard on the body edges.
			MaskTexture.OnSceneBackBufferSizeChanged(newWidth, newHeight);
		}

		public override void Unload()
		{
			MaskTexture?.Dispose();
			MaskTexture = null;
			base.Unload();
		}

		public override void Render(Scene scene)
		{
			if (_playerManager == null) return;
			var cam = Camera ?? scene.Camera;

			Core.GraphicsDevice.SetRenderTarget(MaskTexture);
			// Transparent clear — the RT's default alpha is 0 (no player here),
			// drawn sprite alpha is the mask.
			Core.GraphicsDevice.Clear(Color.Transparent);

			// AlphaBlend so the sprite's own per-pixel alpha is the mask value.
			// PointClamp for pixel-art: no interpolation softens the silhouette
			// edge — we want the mask to be as crisp as the source art.
			Graphics.Instance.Batcher.Begin(
				BlendState.AlphaBlend,
				SamplerState.PointClamp,
				DepthStencilState.None,
				RasterizerState.CullNone,
				null,
				cam.TransformMatrix);

			var players = _playerManager.GetActivePlayers();
			for (int i = 0; i < players.Count; i++)
			{
				var player    = players[i];
				var spriteRen = player.GetComponent<SpriteRenderer>(); // SpriteAnimator inherits SpriteRenderer
				if (spriteRen == null || spriteRen.Sprite == null) continue;
				// Skip players whose sprite isn't actually being drawn in the
				// scene (dead / mid-respawn → RespawnHandler disables the
				// SpriteRenderer). Otherwise the mask still carves a see-through
				// window where the body would be, and since the scene there shows
				// only the dark background, the acid renders a player-shaped HOLE
				// of background — the "dead player leaves a silhouette" bug.
				if (!spriteRen.Enabled) continue;

				// Mirror the SpriteRenderer.Render() signature exactly so the
				// mask aligns pixel-for-pixel with the in-scene draw. The ONLY
				// difference is Color.White instead of spriteRen.Color — that
				// guarantees the alpha channel is the sprite's own alpha,
				// untinted by HitFlash or any other gameplay state.
				Graphics.Instance.Batcher.Draw(
					spriteRen.Sprite,
					player.Transform.Position + spriteRen.LocalOffset,
					Color.White,
					player.Transform.Rotation,
					spriteRen.Origin,
					player.Transform.Scale,
					spriteRen.SpriteEffects,
					layerDepth: 0f);
			}

			Graphics.Instance.Batcher.End();
			// Detach so subsequent renderers/post-processors target their own RTs.
			Core.GraphicsDevice.SetRenderTarget(null);
		}
	}
}
