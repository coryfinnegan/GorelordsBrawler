using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>Mirrors the JSON structure emitted by DebugStateExporter.</summary>
public class GameStateSnapshot
{
	[JsonPropertyName("time")]              public float Time              { get; set; }
	[JsonPropertyName("acidActive")]        public bool  AcidActive        { get; set; }
	[JsonPropertyName("acidLevel")]         public int   AcidLevel         { get; set; }
	// MEASURED standing surface (median basin-center probes) — assert fill
	// ceilings against THIS; acidLevel above is the legacy volumetric estimate,
	// geometry-blind for a basin-shaped pool.
	[JsonPropertyName("acidSurfaceY")]      public int   AcidSurfaceY      { get; set; }
	[JsonPropertyName("acidSpeed")]         public float AcidSpeed         { get; set; }
	[JsonPropertyName("acidParticleCount")] public int   AcidParticleCount { get; set; }
	[JsonPropertyName("acidFinite")]        public bool  AcidFinite        { get; set; } = true;
	[JsonPropertyName("hitstopActive")]     public bool  HitstopActive     { get; set; }

	// Phase C: the phase machine's observable state.
	[JsonPropertyName("acidPhase")]      public string AcidPhase      { get; set; } = "";
	[JsonPropertyName("acidLoop")]       public int    AcidLoop       { get; set; }
	[JsonPropertyName("acidSurgeCount")] public int    AcidSurgeCount { get; set; }
	// Phase D: true while a surge/crest/rise telegraph is running.
	[JsonPropertyName("acidTellActive")] public bool   AcidTellActive { get; set; }
	[JsonPropertyName("acidDraining")]   public bool   AcidDraining   { get; set; }
	[JsonPropertyName("acidFillCap")]    public int    AcidFillCap    { get; set; }
	// Footing-cycle oracles (docs/platform-respawn-proposal.md): population,
	// the active ghost telegraph and its position, and the last materialized
	// spawn — the cycle tests assert the ghost LEADS the spawn and the spawn
	// lands exactly on its ghost.
	[JsonPropertyName("platformsAlive")] public int    PlatformsAlive { get; set; }
	[JsonPropertyName("ghostActive")]    public bool   GhostActive    { get; set; }
	[JsonPropertyName("ghostX")]         public int    GhostX         { get; set; } = -1;
	[JsonPropertyName("ghostY")]         public int    GhostY         { get; set; } = -1;
	[JsonPropertyName("lastSpawnX")]     public int    LastSpawnX     { get; set; } = -1;
	[JsonPropertyName("lastSpawnY")]     public int    LastSpawnY     { get; set; } = -1;

	// Phase B: the live damage-AABB of the acid (the ContactHazard broadphase).
	// Lets tests prove a player was INSIDE the box while dry — the phantom-damage
	// regression scenario — instead of asserting vacuously.
	[JsonPropertyName("acidBoundsLeft")]   public int AcidBoundsLeft   { get; set; }
	[JsonPropertyName("acidBoundsTop")]    public int AcidBoundsTop    { get; set; }
	[JsonPropertyName("acidBoundsRight")]  public int AcidBoundsRight  { get; set; }
	[JsonPropertyName("acidBoundsBottom")] public int AcidBoundsBottom { get; set; }

	[JsonPropertyName("players")]           public List<PlayerSnapshot> Players { get; set; } = new();
}

public class PlayerSnapshot
{
	[JsonPropertyName("id")]       public int  Id       { get; set; }
	[JsonPropertyName("x")]        public int  X        { get; set; }
	[JsonPropertyName("y")]        public int  Y        { get; set; }
	[JsonPropertyName("hp")]       public int  Hp       { get; set; }
	[JsonPropertyName("maxHp")]    public int  MaxHp    { get; set; }
	[JsonPropertyName("grounded")] public bool Grounded { get; set; }
	[JsonPropertyName("vx")]       public int  Vx       { get; set; }
	[JsonPropertyName("vy")]       public int  Vy       { get; set; }

	// Trustworthy combat oracles (acid-independent):
	[JsonPropertyName("hitstun")]        public bool Hitstun        { get; set; }
	[JsonPropertyName("meleeHitsTaken")] public int  MeleeHitsTaken { get; set; }
	[JsonPropertyName("dead")]           public bool Dead           { get; set; }
	[JsonPropertyName("facing")]         public int  Facing         { get; set; } = 1;

	// Phase B oracles: depth-scaled lethality + swim escape.
	[JsonPropertyName("submerged")]      public bool Submerged      { get; set; }
	[JsonPropertyName("submergedDepth")] public int  SubmergedDepth { get; set; }
}
