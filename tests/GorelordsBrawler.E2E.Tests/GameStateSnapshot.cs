using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>Mirrors the JSON structure emitted by DebugStateExporter.</summary>
public class GameStateSnapshot
{
	[JsonPropertyName("time")]              public float Time              { get; set; }
	[JsonPropertyName("acidActive")]        public bool  AcidActive        { get; set; }
	[JsonPropertyName("acidLevel")]         public int   AcidLevel         { get; set; }
	[JsonPropertyName("acidSpeed")]         public float AcidSpeed         { get; set; }
	[JsonPropertyName("acidParticleCount")] public int   AcidParticleCount { get; set; }
	[JsonPropertyName("acidFinite")]        public bool  AcidFinite        { get; set; } = true;
	[JsonPropertyName("hitstopActive")]     public bool  HitstopActive     { get; set; }
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
}
