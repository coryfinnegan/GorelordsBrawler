using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	public class HealthBar : RenderableComponent
	{
		public override float Width => GameConstants.Combat.HealthBarWidth;
		public override float Height => GameConstants.Combat.HealthBarHeight;

		private Health _health;

		public override void OnAddedToEntity()
		{
			_health = Entity.GetComponent<Health>();
			RenderLayer = GameConstants.Rendering.HealthBarRenderLayer;
			LocalOffset = new Vector2(0, -GameConstants.Combat.HealthBarOffsetY);
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			var pos = Entity.Transform.Position;
			var barX = pos.X - GameConstants.Combat.HealthBarWidth / 2;
			var barY = pos.Y - GameConstants.Combat.HealthBarOffsetY;

			// Background
			batcher.DrawRect(barX, barY,
				GameConstants.Combat.HealthBarWidth, GameConstants.Combat.HealthBarHeight,
				GameConstants.Combat.HealthBarBackgroundColor);

			// Fill (green to red based on health %)
			var fillPercent = (float)_health.CurrentHp / _health.MaxHp;
			var fillColor = Color.Lerp(GameConstants.Combat.HealthBarLowColor,
				GameConstants.Combat.HealthBarHighColor, fillPercent);
			batcher.DrawRect(barX, barY,
				GameConstants.Combat.HealthBarWidth * fillPercent, GameConstants.Combat.HealthBarHeight,
				fillColor);
		}
	}
}
