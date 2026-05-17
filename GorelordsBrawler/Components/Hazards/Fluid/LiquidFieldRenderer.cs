using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Textures;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// PASS 1 of the liquid pipeline: custom Nez <see cref="Renderer"/> that
	/// owns its own <see cref="RenderTexture"/> (the "field RT") and renders
	/// only renderables on <see cref="GameConstants.Rendering.LiquidRenderLayer"/>
	/// into it, using <see cref="BlendState.Additive"/>.
	///
	/// Overlapping particle splats accumulate alpha → a potential field that
	/// <see cref="LiquidPostProcessor"/> then thresholds with a shader to
	/// produce the visible liquid body.
	///
	/// Half-resolution: the field RT is half the back-buffer size. The
	/// threshold smoothstep gives smooth edges regardless of source
	/// resolution, so quartering pixel count (4× faster) costs us nothing
	/// visible at game scale.
	///
	/// Order: <see cref="GameConstants.Rendering.LiquidFieldRendererOrder"/>
	/// is negative so we run BEFORE the default scene renderers — by the
	/// time the LiquidPostProcessor runs at end-of-frame the field RT is
	/// fully populated.
	/// </summary>
	public class LiquidFieldRenderer : Renderer
	{
		public RenderTexture FieldTexture { get; private set; }

		public LiquidFieldRenderer(int renderOrder) : base(renderOrder, null)
		{
			// Setting the inherited RenderTexture flips
			// WantsToRenderToSceneRenderTarget to false automatically (it's a
			// computed property on the base class).
		}

		public override void OnAddedToScene(Scene scene)
		{
			base.OnAddedToScene(scene);
			FieldTexture = new RenderTexture();
			RenderTexture = FieldTexture; // inherited; declares the field RT as our target
		}

		public override void OnSceneBackBufferSizeChanged(int newWidth, int newHeight)
		{
			// Match the scene back buffer exactly — the camera's transform
			// matrix is calibrated for it. A half-res RT shifts content into
			// only one quadrant because the camera matrix would render past
			// the RT's clipped bounds.
			FieldTexture.OnSceneBackBufferSizeChanged(newWidth, newHeight);
		}

		public override void Unload()
		{
			FieldTexture?.Dispose();
			FieldTexture = null;
			base.Unload();
		}

		public override void Render(Scene scene)
		{
			var cam = Camera ?? scene.Camera;

			Core.GraphicsDevice.SetRenderTarget(FieldTexture);
			// Transparent clear — NOT Color.Black. If we cleared to black the
			// alpha would still be 0 but R/G/B would compose with subsequent
			// additive draws and tint the field weird colours when sampled.
			Core.GraphicsDevice.Clear(Color.Transparent);

			// Begin overload (BlendState, SamplerState, DepthStencil, Rasterizer, Effect, Matrix)
			Graphics.Instance.Batcher.Begin(
				BlendState.Additive,
				SamplerState.LinearClamp,
				DepthStencilState.None,
				RasterizerState.CullNone,
				null,
				cam.TransformMatrix);

			var renderables = scene.RenderableComponents.ComponentsWithRenderLayer(
				GameConstants.Rendering.LiquidRenderLayer);
			for (int j = 0; j < renderables.Length; j++)
			{
				var r = renderables.Buffer[j];
				if (r.Enabled && r.IsVisibleFromCamera(cam))
				{
					r.Render(Graphics.Instance.Batcher, cam);
				}
			}

			Graphics.Instance.Batcher.End();
			// Detach the RT so the next Renderer's BeginRender → SetRenderTarget
			// targets the scene RT (or backbuffer), not ours.
			Core.GraphicsDevice.SetRenderTarget(null);
		}
	}
}
