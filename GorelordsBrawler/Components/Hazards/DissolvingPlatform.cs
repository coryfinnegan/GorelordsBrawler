using System;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// A static refuge tier that the acid EATS hole-by-hole. Spawned by
	/// ArenaScene from the TMX "tiers" object layer (tiers can't be static
	/// collision tiles — tiles can't erode). This is a thin host: it owns the
	/// tier's identity (rank) and forwards the "fully eaten" event; the actual
	/// swiss-cheese erosion + per-cell collision lives in
	/// <see cref="ErodibleSurface"/>.
	///
	/// The "low"-rank pair gates the log spawner (functional-test decision:
	/// drop-logs only start once the acid has eaten the first set of platforms)
	/// — ArenaScene subscribes to <see cref="OnDissolved"/> for that.
	/// </summary>
	public class DissolvingPlatform : Component, IUpdatable
	{
		/// <summary>Tier rank from the TMX ("low" / "mid" / "top").</summary>
		public readonly string Rank;

		/// <summary>Fired once when the tier is fully eaten (entity about to be destroyed).</summary>
		public Action OnDissolved;

		/// <summary>
		/// Fired once when the tier is MOSTLY eaten (solid fraction below
		/// <see cref="AcidConfig.TierMostlyEatenFraction"/>). The log spawner
		/// gates on the low pair's mostly-eaten signal, not full erosion —
		/// the last crumbs can outlive the visible destruction by many
		/// seconds, and the debris should fall while the chewing is on screen.
		/// </summary>
		public Action OnMostlyEroded;

		private readonly AcidSurface _acid;
		private readonly float _width;
		private readonly float _height;
		private ErodibleSurface _erodible;
		private bool _mostlyFired;

		// Greybox tier color (matches the old static-tile look until Phase E).
		private static readonly Color _tierColor = new Color(110, 110, 115);

		public DissolvingPlatform(AcidSurface acid, float width, float height, string rank)
		{
			_acid   = acid;
			_width  = width;
			_height = height;
			Rank    = rank ?? "";
		}

		public override void OnAddedToEntity()
		{
			_erodible = Entity.AddComponent(new ErodibleSurface(
				_acid, _width, _height, _tierColor,
				AcidConfig.TierErosionPassesPerSec));
			_erodible.OnFullyEroded = () => OnDissolved?.Invoke();
		}

		public void Update()
		{
			if (!_mostlyFired && _erodible != null
				&& _erodible.SolidFraction <= AcidConfig.TierMostlyEatenFraction)
			{
				_mostlyFired = true;
				OnMostlyEroded?.Invoke();
			}
		}
	}
}
