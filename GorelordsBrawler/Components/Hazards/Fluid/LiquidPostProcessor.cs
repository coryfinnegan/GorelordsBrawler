using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;

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
	/// Phase 3 addition: also binds the <see cref="PlayerMaskRenderer"/>'s
	/// pixel-perfect player silhouette RT as a texture sampler in the
	/// shader (parameter <c>PlayerMaskTexture</c>), so the shader can
	/// reduce the body mask + tint the scene exactly under each player's
	/// sprite outline. Makes the player visible THROUGH the acid when
	/// submerged, following the actual sprite shape (not a rectangle).
	/// </summary>
	public class LiquidPostProcessor : PostProcessor
	{
		private readonly LiquidFieldRenderer _fieldRenderer;
		private readonly PlayerMaskRenderer  _playerMaskRenderer;
		private readonly Color _bodyColor;
		private readonly Color _edgeColor;

		public LiquidPostProcessor(int executionOrder, Effect liquidEffect,
			LiquidFieldRenderer fieldRenderer, PlayerMaskRenderer playerMaskRenderer,
			Color bodyColor, Color edgeColor)
			: base(executionOrder, liquidEffect)
		{
			_fieldRenderer      = fieldRenderer;
			_playerMaskRenderer = playerMaskRenderer;
			_bodyColor          = bodyColor;
			_edgeColor          = edgeColor;
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

			// Phase 3: bind the pixel-perfect player silhouette mask RT.
			// PlayerMaskRenderer has already rendered all active players'
			// current sprite frames into this RT by the time post-processors
			// run (renderers complete before post-processors). The shader
			// samples its alpha channel for the per-pixel "is this a player?"
			// signal.
			Effect.Parameters["PlayerMaskTexture"]?.SetValue(_playerMaskRenderer.MaskTexture.RenderTarget);
			Effect.Parameters["PlayerMaskStrength"]?.SetValue(FluidConfig.LiquidPlayerMaskStrength);
			Effect.Parameters["PlayerDesaturation"]?.SetValue(FluidConfig.LiquidPlayerDesaturation);
			Effect.Parameters["PlayerCast"]?.SetValue(FluidConfig.LiquidPlayerCast);
			Effect.Parameters["PlayerDarken"]?.SetValue(FluidConfig.LiquidPlayerDarken);

			DrawFullscreenQuad(source, destination, Effect);
		}
	}
}
