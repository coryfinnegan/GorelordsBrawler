using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Particles;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Inspectable blood spray tuning. Attach to an entity so the Nez debug
	/// inspector (tilde key) can tweak values at runtime. Changes are applied
	/// to all pooled blood emitter configs before each Emit().
	/// </summary>
	public class BloodSpraySettings : Component
	{
		[Inspectable] public int BaseCount = GameConstants.Combat.BloodBaseCount;
		[Inspectable] public int MaxExtra = GameConstants.Combat.BloodMaxExtra;
		[Inspectable] public float Speed = GameConstants.Combat.BloodSpeed;
		[Inspectable] public float SpeedVariance = GameConstants.Combat.BloodSpeedVariance;
		[Inspectable] public float AngleVariance = GameConstants.Combat.BloodAngleVariance;
		[Inspectable] public float Lifespan = GameConstants.Combat.BloodLifespan;
		[Inspectable] public float LifespanVariance = 0.15f;
		[Inspectable] public float Gravity = GameConstants.Combat.BloodGravity;
		[Inspectable] public float StartSize = GameConstants.Combat.BloodStartSize;
		[Inspectable] public float FinishSize = 1f;
		[Inspectable] public int FlashCount = GameConstants.Combat.FlashCount;
	}

	/// <summary>
	/// Manages pooled particle emitter entities for hit effects (blood splatter + impact flash).
	/// Pre-creates a small pool of reusable emitter entities on enable. On each hit,
	/// positions one at the hit point and calls Emit() for a burst of particles.
	/// </summary>
	public class HitParticleManager : SceneComponent
	{
		private const int PoolSize = 6;

		private Entity[] _poolEntities;
		private ParticleEmitter[] _bloodEmitters;
		private ParticleEmitter[] _flashEmitters;
		private ParticleEmitterConfig[] _bloodConfigs;
		private BloodSpraySettings _settings;
		private int _nextSlot;

		public override void OnEnabled()
		{
			_poolEntities = new Entity[PoolSize];
			_bloodEmitters = new ParticleEmitter[PoolSize];
			_flashEmitters = new ParticleEmitter[PoolSize];
			_bloodConfigs = new ParticleEmitterConfig[PoolSize];

			for (int i = 0; i < PoolSize; i++)
			{
				var entity = Scene.CreateEntity($"hit-particles-{i}");

				var bloodConfig = CreateBloodConfig();
				_bloodConfigs[i] = bloodConfig;
				var bloodEmitter = new ParticleEmitter(bloodConfig, playOnAwake: false);
				bloodEmitter.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
				entity.AddComponent(bloodEmitter);

				var flashEmitter = new ParticleEmitter(CreateFlashConfig(), playOnAwake: false);
				flashEmitter.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
				entity.AddComponent(flashEmitter);

				_poolEntities[i] = entity;
				_bloodEmitters[i] = bloodEmitter;
				_flashEmitters[i] = flashEmitter;
			}

			// Attach inspectable settings to the first pool entity
			_settings = _poolEntities[0].AddComponent(new BloodSpraySettings());
		}

		/// <summary>
		/// Spawn blood splatter and impact flash at the given world position.
		/// </summary>
		public void SpawnHitEffect(Vector2 position, Vector2 direction, float intensity)
		{
			int slot = _nextSlot;
			_nextSlot = (_nextSlot + 1) % PoolSize;

			_poolEntities[slot].Transform.Position = position;

			// Apply inspectable settings to this slot's config before emitting
			ApplySettings(_bloodConfigs[slot]);

			// Set blood spray angle from knockback direction
			float angleDeg = MathF.Atan2(direction.Y, direction.X) * (180f / MathF.PI);
			_bloodConfigs[slot].Angle = angleDeg;

			int bloodCount = _settings.BaseCount
				+ (int)(intensity * _settings.MaxExtra);
			_bloodEmitters[slot].Emit(bloodCount);

			_flashEmitters[slot].Emit(_settings.FlashCount);
		}

		private void ApplySettings(ParticleEmitterConfig config)
		{
			config.Speed = _settings.Speed;
			config.SpeedVariance = _settings.SpeedVariance;
			config.AngleVariance = _settings.AngleVariance;
			config.Gravity = new Vector2(0, _settings.Gravity);
			config.ParticleLifespan = _settings.Lifespan;
			config.ParticleLifespanVariance = _settings.LifespanVariance;
			config.StartParticleSize = _settings.StartSize;
			config.FinishParticleSize = _settings.FinishSize;
		}

		private static ParticleEmitterConfig CreateBloodConfig()
		{
			return new ParticleEmitterConfig
			{
				EmitterType = ParticleEmitterType.Gravity,
				MaxParticles = 32,
				Speed = GameConstants.Combat.BloodSpeed,
				SpeedVariance = GameConstants.Combat.BloodSpeedVariance,
				Angle = 0f,
				AngleVariance = GameConstants.Combat.BloodAngleVariance,
				Gravity = new Vector2(0, GameConstants.Combat.BloodGravity),
				ParticleLifespan = GameConstants.Combat.BloodLifespan,
				ParticleLifespanVariance = 0.15f,
				StartColor = new Color(180, 0, 0, 255),
				FinishColor = new Color(180, 0, 0, 0),
				StartParticleSize = GameConstants.Combat.BloodStartSize,
				FinishParticleSize = 1f,
				SimulateInWorldSpace = true,
				BlendFuncSource = Blend.SourceAlpha,
				BlendFuncDestination = Blend.InverseSourceAlpha,
			};
		}

		private static ParticleEmitterConfig CreateFlashConfig()
		{
			return new ParticleEmitterConfig
			{
				EmitterType = ParticleEmitterType.Gravity,
				MaxParticles = 16,
				Speed = GameConstants.Combat.FlashSpeed,
				SpeedVariance = 20f,
				Angle = 0f,
				AngleVariance = 180f,
				Gravity = Vector2.Zero,
				ParticleLifespan = GameConstants.Combat.FlashLifespan,
				ParticleLifespanVariance = 0.02f,
				StartColor = new Color(255, 255, 200, 255),
				FinishColor = new Color(255, 200, 50, 0),
				StartParticleSize = GameConstants.Combat.FlashStartSize,
				FinishParticleSize = 2f,
				SimulateInWorldSpace = true,
				BlendFuncSource = Blend.SourceAlpha,
				BlendFuncDestination = Blend.One,
			};
		}
	}
}
