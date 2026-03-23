using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Constants
{
	/// <summary>
	/// Builds fully-qualified animation keys for a specific character by combining
	/// a character prefix with the generic animation name suffixes in
	/// <see cref="GameConstants.Animations"/>.
	///
	/// Usage:
	///   var keys = new AnimationKeyBuilder("FutureAxe");
	///   keys.Idle        // "FutureAxe_Idle"
	///   keys.RunFaceLeft // "FutureAxe_RunFaceLeft"
	///
	/// When prefix is empty the suffix is returned as-is, making the builder
	/// safe to use with characters that have no prefix configured.
	///
	/// Adding a new animation type: add one property here and one suffix constant
	/// in <see cref="GameConstants.Animations"/> — no per-character code needed.
	/// </summary>
	public sealed class AnimationKeyBuilder
	{
		private readonly string _prefix;

		public AnimationKeyBuilder(string prefix) => _prefix = prefix ?? string.Empty;

		private string B(string suffix) =>
			string.IsNullOrEmpty(_prefix)
				? suffix
				: string.Concat(_prefix, "_", suffix);

		// ── Locomotion ────────────────────────────────────────────────────────
		public string Idle         => B(GameConstants.Animations.Idle);
		public string IdleFaceLeft => B(GameConstants.Animations.IdleFaceLeft);
		public string Run          => B(GameConstants.Animations.Run);
		public string RunFaceLeft  => B(GameConstants.Animations.RunFaceLeft);
		public string Jump         => B(GameConstants.Animations.Jump);
		public string JumpFaceLeft => B(GameConstants.Animations.JumpFaceLeft);
		public string Select       => B(GameConstants.Animations.Select);

		// ── Attack — LeftHand ─────────────────────────────────────────────────
		public string AttackIdleLeftHand         => B(GameConstants.Animations.AttackIdleLeftHand);
		public string AttackIdleLeftHandFaceLeft  => B(GameConstants.Animations.AttackIdleLeftHandFaceLeft);
		public string AttackRunLeftHand          => B(GameConstants.Animations.AttackRunLeftHand);
		public string AttackRunLeftHandFaceLeft   => B(GameConstants.Animations.AttackRunLeftHandFaceLeft);

		// ── Attack — RightHand ────────────────────────────────────────────────
		public string AttackIdleRightHand        => B(GameConstants.Animations.AttackIdleRightHand);
		public string AttackIdleRightHandFaceLeft => B(GameConstants.Animations.AttackIdleRightHandFaceLeft);
		public string AttackRunRightHand         => B(GameConstants.Animations.AttackRunRightHand);
		public string AttackRunRightHandFaceLeft  => B(GameConstants.Animations.AttackRunRightHandFaceLeft);

		// ── Hurt ──────────────────────────────────────────────────────────────
		public string Hurt         => B(GameConstants.Animations.Hurt);
		public string HurtFaceLeft => B(GameConstants.Animations.HurtFaceLeft);

		// ── Attack — all variants (used for OnAnimationCompleted checks) ──────
		/// <summary>All attack animation keys for this character.</summary>
		public string[] AllAttackAnims => new[]
		{
			AttackIdleLeftHand, AttackIdleLeftHandFaceLeft,
			AttackRunLeftHand,  AttackRunLeftHandFaceLeft,
			AttackIdleRightHand, AttackIdleRightHandFaceLeft,
			AttackRunRightHand,  AttackRunRightHandFaceLeft,
		};
	}
}
