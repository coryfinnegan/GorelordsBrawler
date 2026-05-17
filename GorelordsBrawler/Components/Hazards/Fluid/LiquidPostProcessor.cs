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
	/// </summary>
	public class LiquidPostProcessor : PostProcessor
	{
		private readonly LiquidFieldRenderer _fieldRenderer;
		private readonly Color _bodyColor;
		private readonly Color _edgeColor;

		public LiquidPostProcessor(int executionOrder, Effect liquidEffect,
			LiquidFieldRenderer fieldRenderer, Color bodyColor, Color edgeColor)
			: base(executionOrder, liquidEffect)
		{
			_fieldRenderer = fieldRenderer;
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

			DrawFullscreenQuad(source, destination, Effect);
		}
	}
}
