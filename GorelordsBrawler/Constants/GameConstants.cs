using Microsoft.Xna.Framework;

namespace GorelordsBrawler.Constants
{
	public static class GameConstants
	{
		public static class EntityNames
		{
			public static readonly string UI = nameof(UI);
			public const string Camera = "camera";
			public const string MeleeHitbox = "melee-hitbox";
			public const string Ground = "ground";
			public const string PlatformMid = "platform-mid";
			public const string PlatformLeft = "platform-left";
			public const string PlatformRight = "platform-right";
			public const string PauseMenu = "pause-menu";
			public const string MatchHUD = "match-hud";
			public const string Announcement = "announcement";
			public const string VictoryScreen = "victory-screen";
			public const string Projectile = "projectile";
		}

		public static class ContentPaths
		{
			public const string CharactersFolder = "Content/Characters/";
			public const string JsonExtension = ".json";
		}

		public static class Characters
		{
			public const string FutureAxe = "FutureAxe";

			public static readonly string[] All = { FutureAxe };
		}

		/// <summary>
		/// Animation name suffixes (no character prefix). Combine with
		/// <see cref="AnimationKeyBuilder"/> to get the full atlas key for a character.
		/// All values use nameof() so renaming the identifier auto-updates the string.
		/// </summary>
		public static class Animations
		{
			// ── Locomotion ────────────────────────────────────────────────
			public const string Idle              = nameof(Idle);
			public const string IdleFaceLeft      = nameof(IdleFaceLeft);
			public const string Run               = nameof(Run);
			public const string RunFaceLeft       = nameof(RunFaceLeft);
			public const string Jump              = nameof(Jump);
			public const string JumpFaceLeft      = nameof(JumpFaceLeft);
			public const string Select            = nameof(Select);
			public const string CrouchIdle        = nameof(CrouchIdle);
			public const string CrouchIdleFaceLeft = nameof(CrouchIdleFaceLeft);
			public const string CrouchRun         = nameof(CrouchRun);
			public const string CrouchRunFaceLeft = nameof(CrouchRunFaceLeft);

			// ── Attack — Legacy LeftHand (original) ───────────────────────
			public const string AttackIdleLeftHand            = nameof(AttackIdleLeftHand);
			public const string AttackIdleLeftHandFaceLeft    = nameof(AttackIdleLeftHandFaceLeft);
			public const string AttackRunLeftHand             = nameof(AttackRunLeftHand);
			public const string AttackRunLeftHandFaceLeft     = nameof(AttackRunLeftHandFaceLeft);

			// ── Attack — Legacy RightHand (mirrored) ──────────────────────
			public const string AttackIdleRightHand           = nameof(AttackIdleRightHand);
			public const string AttackIdleRightHandFaceLeft   = nameof(AttackIdleRightHandFaceLeft);
			public const string AttackRunRightHand            = nameof(AttackRunRightHand);
			public const string AttackRunRightHandFaceLeft    = nameof(AttackRunRightHandFaceLeft);

			// ── Attack — Dynamic suffixes (used by CombatController) ──────
			public const string Jab          = nameof(Jab);
			public const string NeutralAir   = nameof(NeutralAir);
			public const string Heavy        = nameof(Heavy);
			public const string CrouchAttack = nameof(CrouchAttack);

			// ── Hurt ──────────────────────────────────────────────────────
			public const string Hurt         = nameof(Hurt);
			public const string HurtFaceLeft = nameof(HurtFaceLeft);

			// ── Ledge ─────────────────────────────────────────────────────
			public const string LedgeIdle         = nameof(LedgeIdle);
			public const string LedgeIdleFaceLeft = nameof(LedgeIdleFaceLeft);
			public const string LedgeClimb         = nameof(LedgeClimb);
			public const string LedgeClimbFaceLeft = nameof(LedgeClimbFaceLeft);
		}

		public static class SceneNames
		{
			public const string ArenaScene = "ArenaScene";
			public const string MainMenuScene = "MainMenuScene";
			public const string SettingsScene = "SettingsScene";
			public const string CharacterSelectScene = "CharacterSelectScene";
		}

		public static class Physics
		{
			public const float GroundNormalThreshold = -0.5f;
			public const float CeilingNormalThreshold = 0.5f;
			public const int PhysicsBodyUpdateOrder = 100;
			public const int LocomotionAnimatorUpdateOrder = 101;
			// Cap per-frame delta time so window resize/focus-loss spikes
			// don't cause physics to tunnel through platforms.
			public const float MaxDeltaTime = 0.05f; // 50ms = 20fps floor
			// Brief hold on the last jump frame after landing — gives a squash feel
			// without needing separate landing frames in the atlas.
			public const float LandingWindowDuration = 0.08f; // 80ms
		}

		public static class Rendering
		{
			public const int ScreenSpaceRenderLayer = 999;
			public const int ScreenSpaceRendererOrder = 100;
			public const int DefaultRenderLayer = 0;
			public const int HitboxRenderLayer = -1;
			public const int HealthBarRenderLayer = -2;
			// Liquid particles' soft-disc splats. NOT included in the scene's
			// default RenderLayerRenderer — picked up exclusively by the
			// LiquidFieldRenderer which renders this layer into its own RT
			// with BlendState.Additive (see nez-liquid-rendering skill).
			public const int LiquidRenderLayer = 100;
			public const int LiquidFieldRendererOrder = -10; // runs before the default scene Renderer
			// PlayerMaskRenderer renders each active player's CURRENT sprite frame
			// into its own RT with Color.White tint, so the RT's alpha channel is
			// a pixel-perfect silhouette of the players. Sampled by liquid.fx to
			// reduce the metaball mask exactly over the player art (instead of
			// the bounding-rect approximation we tried first). Order -5 so it
			// runs after LiquidFieldRenderer (-10) but before the default scene
			// renderer (0) — order within renderers doesn't actually matter for
			// the post-process consumer, but -5 keeps the rendering passes
			// chronologically grouped for inspector clarity.
			public const int PlayerMaskRendererOrder = -5;
			public const int LiquidPostProcessorOrder = 0;
			// Phase 4 acid-deadly-polish: runs AFTER LiquidPostProcessor so the
			// vignette + chromatic aberration apply to the FINAL composited
			// image (including the acid). If this ran before liquid, the
			// metaball pass would paint over our tint in acid regions.
			public const int DamageFeedbackPostProcessorOrder = 10;
			public const float HitboxColorAlpha = 0.6f;
		}

		public static class Input
		{
			public const float JumpBufferTime = 0.1f;
			public const int MaxGamePads = 4;
		}

		public static class Screen
		{
			public const int DesignWidth = 800;
			public const int DesignHeight = 600;
		}

		public static class UI
		{
			public const string TitleText = "GORELORDS";
			public const string SubtitleText = "MUTANT DEATH MATCH FIGURES";
			public const string PlayButtonText = "PLAY";
			public const string SettingsButtonText = "SETTINGS";
			public const string SettingsTitleText = "SETTINGS";
			public const string ApplyButtonText = "APPLY";
			public const string BackButtonText = "BACK";
			public const string ResolutionLabel = "Resolution";
			public const string FullscreenLabel = "Fullscreen";
			public const string BorderlessLabel = "Borderless";
			public const string VSyncLabel = "VSync";
			public const string ResolutionFormat = "{0}x{1}";
			public const float TitleScale = 4f;
			public const float SubtitleScale = 0.75f;
			public const float ButtonPadding = 10f;
			public const float SettingsLabelScale = 0.6f;
			public const float SettingsRowPadding = 8f;
			public const float SettingsWidgetMinWidth = 200f;

			// Stock HUD
			public const string StockIndicator = "X";
			public const float StockHUDPadding = 10f;
			public const float StockHUDScale = 0.6f;
			public const float StockSpacing = 8f;
			public const string PlayerLabelFormat = "P{0}";

			// Pause menu
			public const string PausedTitleText = "PAUSED";
			public const string ResumeButtonText = "RESUME";
			public const string ControlsButtonText = "CONTROLS";
			public const string MainMenuButtonText = "MAIN MENU";
			public const string QuitButtonText = "QUIT";
			public const string ControlsTitleText = "CONTROLS";

			// Controls reference
			public const string Player1Header = "Player 1 (WASD)";
			public const string Player2Header = "Player 2 (Arrows)";
			public const string GamepadHeader = "Gamepad";
			public const string MoveLabel = "Move";
			public const string JumpLabel = "Jump";
			public const string AttackLabel = "Attack";
			public const string SpecialLabel = "Special";
			public const string Player1MoveKeys = "A / D";
			public const string Player1JumpKey = "W";
			public const string Player1AttackKey = "F";
			public const string Player1SpecialKey = "G";
			public const string Player2MoveKeys = "Left / Right";
			public const string Player2JumpKey = "Up";
			public const string Player2AttackKey = "Right Ctrl";
			public const string Player2SpecialKey = "Right Shift";
			public const string GamepadMoveInput = "L-Stick / D-Pad";
			public const string GamepadJumpInput = "A Button";
			public const string GamepadAttackInput = "X Button";
			public const string GamepadSpecialInput = "Y Button";
		}

		public static class CharacterSelect
		{
			public const string TitleText = "CHARACTER SELECT";
			public const string JoinPromptWASD = "Press W to join";
			public const string JoinPromptArrows = "Press Up to join";
			public const string JoinPromptGamepad = "Press A to join";
			public const string ReadyText = "READY";
			public const string NotReadyText = "";
			public const string AllReadyText = "All players ready!";
			public const string NeedPlayersText = "Need at least 2 players";
			public const string ReadyPrompt = "Attack to ready";
			public const string UnreadyPrompt = "Attack to unready";
			public const string LeavePrompt = "Jump to leave";
			public const string LeftArrow = "< ";
			public const string RightArrow = " >";
			public const float PreviewScale = 2f;
			public const float PanelPadding = 15f;
			public const float NameScale = 0.5f;
			public const float ReadyScale = 0.6f;
			public const float StatusScale = 0.5f;
			public const float TitleScale = 2f;
			public const float CountdownDuration = 2f;
			public const int MinPlayers = 2;
			public const float SlotWidth = 160f;
			public const float SlotHeight = 300f;
		}

		public static class Combat
		{
			public const float RespawnDelay = 2f;
			public const float HealthBarWidth = 40f;
			public const float HealthBarHeight = 4f;
			public const float HealthBarOffsetY = 35f;
			public static readonly Color HealthBarBackgroundColor = new Color(40, 40, 40);
			public static readonly Color HealthBarHighColor = Color.Green;
			public static readonly Color HealthBarLowColor = Color.Red;
			public const int DefaultStockCount = 3;

			// Knockback scaling — multiplier range: 1× (fresh) → (1 + KnockbackScaling)× (just killed)
			public const float KnockbackScaling   = 2.0f;

			// Hit freeze — hard TimeScale=0 pause on every hit (unscaled countdown)
			public const float HitstopDuration    = 0.06f;   // 60 ms ≈ 4 frames

			// Hit flash — white tint on the defender (unscaled so visible during hitstop)
			public const float HitFlashDuration   = 0.10f;   // 100 ms

			// Camera shake — trauma-based, decays after hit
			public const float MaxShakeOffset     = 8f;      // pixels of max displacement
			public const float ShakeDecay         = 6f;      // trauma/sec (~200 ms to clear)

			// Hurt vibration — rapid X-axis oscillation on the defender during hitstun
			public const float HurtVibrationFrequency = 40f;   // oscillations/sec
			public const float HurtVibrationAmplitude = 2.0f;  // pixels of displacement

			// Blood splatter particles
			public const int BloodBaseCount       = 6;       // min particles per hit
			public const int BloodMaxExtra        = 4;       // extra particles at max intensity
			public const float BloodSpeed         = 100f;    // px/sec
			public const float BloodSpeedVariance = 30f;
			public const float BloodAngleVariance = 35f;     // cone spread degrees
			public const float BloodLifespan      = 0.4f;    // seconds
			public const float BloodGravity       = 300f;    // px/sec²
			public const float BloodStartSize     = 3f;      // pixels

			// Impact flash particles
			public const int FlashCount           = 5;
			public const float FlashSpeed         = 50f;
			public const float FlashLifespan      = 0.08f;   // very brief
			public const float FlashStartSize     = 6f;

			// Soft push-back force between overlapping players (px/sec²)
			public const float PlayerPushbackForce = 800f;

			// Input buffer — forgiveness window for attack input before cooldown expires
			public const float AttackBufferWindow = 0.10f;   // 100 ms
		}

		public static class Match
		{
			public const float CountdownDuration = 1.5f;
			public const float AnnouncementFadeInDuration = 0.2f;
			public const float AnnouncementDisplayDuration = 1.2f;
			public const float AnnouncementFadeOutDuration = 0.3f;
			public const float VictoryDelay = 1.5f;
			public const string FightText = "FIGHT!";
			public const string KOText = "K.O.!";
			public const string GameText = "GAME!";
			public const string VictorText = "VICTOR";
			public const string DefeatText = "DEFEAT";
			public const string RematchText = "REMATCH";
			public const float AnnouncementScale = 3f;
			public const float ResultScale = 2f;
			public static readonly Color AnnouncementColor = Color.Yellow;
			public static readonly Color VictorColor = Color.Gold;
			public static readonly Color DefeatColor = Color.Red;
		}

		public static class PauseMenu
		{
			public static readonly Color OverlayColor = new Color(0, 0, 0, 150);
			public const float ControlsLabelScale = 0.5f;
			public const float ControlsRowPadding = 4f;
			public const float ControlsColumnPadding = 15f;
			public const float ControlsSectionPadding = 12f;
		}

		public static class Maps
		{
			public const string Arena1 = "Content/maps/arena1.tmx";
			public const string CollisionLayerName = "collision";
			public const string PlatformsLayerName = "platforms";
		}

		public static class Hazards
		{
			// Timing. (Pour rates live in AcidConfig.InletFlowFor — direct
			// particles/sec per loop; the old AcidRiseSpeed px²-area model was a
			// third density assumption disagreeing with the measured pool.)
			public const float AcidStartDelay        = 30f;
			public const float AcidDebugRiseMultiplier = 4f;     // DebugFastAcid multiplies the pour by this
			// ── Depth-scaled lethality + swim escape (Phase B) ────────────────
			// Acid damage scales with how far a body is submerged below the LOCAL
			// surface. A toe-dip is a survivable scare; a deep launch melts fast
			// (~1.5-2s KO vs a 100-HP fighter) — but is always escapable by mashing
			// jump to stroke upward. The two halves are tuned TOGETHER: the stroke
			// must out-climb shallow/mid damage but lose to the deep end.
			//
			// Damage curve (see CombatMath.AcidDpsMultiplier):
			//   SurfaceDps      = base chip at depth 0 (the ContactHazard base rate)
			//   DeepDpsMult     = multiplier at >= AcidFullSubmergeDepth
			//   AcidFullSubmergeDepth = depth (px, feet-below-surface) at which the
			//     multiplier saturates — ~one body height, i.e. "fully under".
			public const float AcidSurfaceDps         = 9f;    // depth 0 — survivable chip
			public const float AcidDeepDpsMult        = 6.5f;  // 9 * 6.5 ≈ 58 dps → ~1.7s KO @ 100 HP
			public const float AcidFullSubmergeDepth  = 96f;   // px below surface = "fully submerged"

			// Swim escape: each jump PRESS while submerged sets upward velocity to
			// this (px/s, like a weaker jump). Mashing climbs you out. Capped so a
			// held buffer can't accumulate — one stroke per press.
			public const float SwimStrokeImpulse      = 230f;  // px/s up per stroke
			public const float SwimMaxRiseSpeed       = 260f;  // clamp on upward velocity while submerged

			// Breach: a jump press at depth <= this performs a FULL jump out of the
			// water (character JumpSpeed, JumpHeld semantics) instead of a stroke.
			// Without it the exit is luck-gated: a body cresting the surface bobs in
			// a ~2-frame dry window (short-hop gravity slams it straight back in),
			// so presses land on wet frames and become feeble strokes — found by the
			// DeepKnockIn E2E frame trace. Standard water-exit pattern (Mario/
			// Terraria: surface jump = real jump). ~half a body height.
			public const float SwimBreachDepth        = 24f;

			// ── Basin geometry ("The Sump", Phase A) ──────────────────────────
			// World-space bounds of the central acid basin carved into arena1.tmx.
			// These mirror the TMX collision layer (cols 14-25 open, floored at
			// row 23) — the tiles are the source of truth for COLLISION; these
			// constants exist so AcidSurface.PreFill knows where to drop the
			// resting pool without re-parsing the map. Keep in sync with
			// tools/gen_sump_map.py if the basin moves. (Phase C will fold these
			// into a dedicated AcidConfig alongside inlet/drain/surge data.)
			public const float BasinLeftX   = 448f;   // col 14 * 32 — inner left lip
			public const float BasinRightX  = 832f;   // col 26 * 32 — inner right lip
			public const float BasinFloorY  = 736f;   // row 23 * 32 — top of the floor tiles
			public const float BasinRestTopY = 640f;  // resting acid surface (~3 tiles deep), leaves ~96px of lip above
			// Inlet fill ceiling: the rise tops out here and STOPS, so the basin
			// fills to ~the lip (544) without overflowing the banks. Drives the
			// geometry-derived particle cap in AcidSurface — see the note there on
			// why the old volumetric inlet-stop was geometry-blind. 560 = ~half a
			// tile below the lip, a safe margin against slosh overshoot. Phase C
			// makes this dynamic (the flood deliberately raises it past the lip).
			public const float BasinFillCeilingY = 560f;

			// Acid start — normalized Y just below map bottom
			public const float AcidStartNormalizedY = 1.02f;

			// (The old normalized Platforms array is gone — it described the
			// pre-Sump arena and was only consumed to derive acid inlet/trigger
			// geometry. Phase C replaced every consumer with explicit world-space
			// values in AcidConfig, which mirror the real TMX.)
		}

		public static class Ledge
		{
			public const float GrabRangeX = 24f;      // how close horizontally to edge
			public const float GrabRangeY = 20f;      // how close vertically to platform top
			public const float HangOffsetX = 6f;      // offset away from edge when hanging
			public const float HangTimeout = 3f;      // seconds before auto-drop
			public const float RegrabCooldown = 0.3f;  // seconds before can re-grab same edge
			public const float HangGracePeriod = 0.15f; // seconds before directional climb input accepted
		}

		public static class Arena
		{
			// Inner liquid bounds: 1 wall tile (32 px) on each side of the 1280-wide map
			public const float InnerLeft  = 32f;
			public const float InnerRight = 1248f;

			// Fallback spawn positions if map has no spawns object layer.
			// Match the Sump bank-top spawns in arena1.tmx — far-outer corners,
			// clear of the low refuge tiers (x 128-320 / 960-1152).
			public static readonly Vector2[] FallbackSpawnPositions = new[]
			{
				new Vector2(96, 520),    // P1 — far left bank corner
				new Vector2(1184, 520),  // P2 — far right bank corner
				new Vector2(384, 520),   // inner left
				new Vector2(896, 520),   // inner right
			};
		}
	}
}
