using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;

namespace GorelordsBrawler.Components.PostProcessors
{
	/// <summary>
	/// Phase 4 of the acid-deadly-polish-plan: composites a chromatic-aberration
	/// pulse and an HP-driven radial vignette over the final scene RT.
	///
	/// Pure presentation — all driving logic lives in
	/// <see cref="GorelordsBrawler.Systems.DamageFeedbackController"/>, which
	/// writes into the public fields below every frame. Same pattern as
	/// <see cref="Hazards.Fluid.LiquidPostProcessor"/>: keep the shader-binding
	/// glue separate from the gameplay logic so each piece stays simple.
	///
	/// Runs at <see cref="Constants.GameConstants.Rendering.DamageFeedbackPostProcessorOrder"/>
	/// (10), AFTER the LiquidPostProcessor (0), so the vignette + CA apply to
	/// the final composited image INCLUDING the acid liquid. If this ran before
	/// liquid, the acid metaball pass would overwrite our tint where there's
	/// acid on screen — defeats the point.
	/// </summary>
	public class DamageFeedbackPostProcessor : PostProcessor
	{
		// Public mutable fields — DamageFeedbackController writes here each
		// frame. Process() forwards them straight to shader uniforms.

		public float CaStrength;
		public float VignetteRadius;
		public float VignetteSoftness;
		public float VignetteIntensity;
		public Color VignetteColor;

		public DamageFeedbackPostProcessor(int executionOrder, Effect effect)
			: base(executionOrder, effect)
		{
		}

		public override void Process(RenderTarget2D source, RenderTarget2D destination)
		{
			// Forward live values to the shader. Null-conditional in case the
			// shader was compiled without one of the parameters (defensive —
			// same pattern as LiquidPostProcessor.Process).
			Effect.Parameters["CaStrength"]?.SetValue(CaStrength);
			Effect.Parameters["VignetteRadius"]?.SetValue(VignetteRadius);
			Effect.Parameters["VignetteSoftness"]?.SetValue(VignetteSoftness);
			Effect.Parameters["VignetteIntensity"]?.SetValue(VignetteIntensity);
			Effect.Parameters["VignetteColor"]?.SetValue(VignetteColor.ToVector4());

			DrawFullscreenQuad(source, destination, Effect);
		}
	}
}
