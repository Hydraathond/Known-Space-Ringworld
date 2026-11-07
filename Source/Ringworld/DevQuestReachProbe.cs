using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Dev harness probe for how much room quests have on the colony's landmass (RW_DEV_QUEST_REACH=1), for v0.1.4 ticket 17.
// After the self-check it logs "Quest reach probe" lines for the surface layer (the ring, or a planet's surface):
// - the landmasses: caravan-reachability fields (WorldReachability's local field ids) with a tile vanilla allows a new
//   settlement on, their sizes, and on a ring how many columns around each spans;
// - for sample colony tiles (vanilla's random starting tile, seeded per sample): the colony's landmass, and for each
//   distance band a vanilla quest node asks for, how many site-valid tiles lie in the band on the colony's landmass and
//   how many off it. The band is measured as FastTileFinder measures it (the angle between tile centres, times the
//   layer's radius over its AverageTileSize), and site-valid is FastTileFinder's own test (IsValidTileForNewSettlement);
// - for the default band (7-27, QuestNode_GetSiteTile's), how often TileFinder.TryFindNewSiteTile's result is off the
//   colony's landmass, which happens only through its fallback search (TryFillFindTile) that crosses impassable tiles.
// Read-only apart from the finder's caches. Grep Player.log for "Quest reach probe".
public static class DevQuestReachProbe
{
	private const int Samples = 40;

	private const int FinderTriesPerSample = 5;

	private static readonly (string name, int min, int max)[] Bands =
	{
		("GetSiteTile default", 7, 27),
		("Royalty threats/ProblemCausers", 1, 10),
		("Ideology relic/complex, worshipped terminal", 2, 10),
		("Anomaly distress call", 3, 9),
		("PeaceTalks", 5, 13),
		("Archonexus third cycle", 10, 40),
		("Odyssey Site/GravShip near", 20, 80),
		("Odyssey GravShip far", 40, 160),
		("Royalty Mission_BanditCamp", 80, 85),
	};

	public static void Run()
	{
		PlanetLayer layer = Find.WorldGrid.Surface;
		RingLayer ring = layer as RingLayer;
		string shape = ring != null ? $"ring {ring.Size}, land coverage {ring.LandCoverage:0.###}" : $"planet, coverage {Find.World.info.planetCoverage:0.###}";
		int count = layer.TilesCount;
		int[] field = new int[count];
		bool[] siteValid = new bool[count];
		Vector3[] dir = new Vector3[count];
		for (int i = 0; i < count; i++)
		{
			PlanetTile tile = new PlanetTile(i, layer);
			field[i] = Find.WorldReachability.GetLocalFieldId(tile);
			siteValid[i] = field[i] > 0 && TileFinder.IsValidTileForNewSettlement(tile);
			dir[i] = layer.GetTileCenter(tile).normalized;
		}
		float tilesPerRadian = layer.Radius / layer.AverageTileSize;

		// Landmasses: fields with at least one site-valid tile.
		Dictionary<int, int> fieldTiles = new Dictionary<int, int>();
		Dictionary<int, int> fieldSiteTiles = new Dictionary<int, int>();
		Dictionary<int, HashSet<int>> fieldColumns = new Dictionary<int, HashSet<int>>();
		for (int i = 0; i < count; i++)
		{
			if (field[i] <= 0)
			{
				continue;
			}
			fieldTiles[field[i]] = fieldTiles.TryGetValue(field[i], out int n) ? n + 1 : 1;
			if (siteValid[i])
			{
				fieldSiteTiles[field[i]] = fieldSiteTiles.TryGetValue(field[i], out int s) ? s + 1 : 1;
			}
			if (ring?.Geometry != null)
			{
				if (!fieldColumns.TryGetValue(field[i], out HashSet<int> columns))
				{
					fieldColumns[field[i]] = columns = new HashSet<int>();
				}
				columns.Add(ring.Geometry.ColumnOf(i));
			}
		}
		List<int> lands = fieldSiteTiles.Keys.OrderByDescending(f => fieldTiles[f]).ToList();
		int totalSite = fieldSiteTiles.Values.Sum();
		string Columns(int f) => fieldColumns.TryGetValue(f, out HashSet<int> c) ? $", {c.Count} columns" : "";
		string top = string.Join("; ", lands.Take(8).Select(f => $"{fieldTiles[f]} tiles ({fieldSiteTiles[f]} site-valid{Columns(f)})"));
		int bigOnes = lands.Count(f => fieldSiteTiles[f] >= 100);
		Log.Message($"[Ringworld] Quest reach probe: {shape}; surface {count} tiles, AverageTileSize {layer.AverageTileSize:F4}, {tilesPerRadian:F1} tiles per radian; {lands.Count} landmasses with a site-valid tile ({bigOnes} with 100 or more), {totalSite} site-valid tiles in all; largest: {top}");

		// Sample colonies.
		List<int> landTiles = new List<int>();
		List<int> landColumns = new List<int>();
		Dictionary<string, List<(int on, int off)>> perBand = Bands.ToDictionary(b => b.name, _ => new List<(int, int)>());
		int finderTries = 0;
		int finderFound = 0;
		int finderOff = 0;
		int startTick = Find.TickManager.TicksGame;
		for (int s = 0; s < Samples; s++)
		{
			Rand.PushState(Gen.HashCombineInt(17, s));
			PlanetTile colony;
			try
			{
				colony = TileFinder.RandomSettlementTileFor(layer, Faction.OfPlayer, mustBeAutoChoosable: true);
			}
			finally
			{
				Rand.PopState();
			}
			int home = colony.tileId;
			int homeField = field[home];
			landTiles.Add(homeField > 0 ? fieldTiles[homeField] : 0);
			landColumns.Add(homeField > 0 && fieldColumns.TryGetValue(homeField, out HashSet<int> cols) ? cols.Count : 0);
			int[] on = new int[Bands.Length];
			int[] off = new int[Bands.Length];
			Vector3 origin = dir[home];
			for (int i = 0; i < count; i++)
			{
				if (!siteValid[i])
				{
					continue;
				}
				float dot = Mathf.Clamp(Vector3.Dot(origin, dir[i]), -1f, 1f);
				float dist = Mathf.Acos(dot) * tilesPerRadian;
				for (int b = 0; b < Bands.Length; b++)
				{
					if (dist >= Bands[b].min && dist <= Bands[b].max)
					{
						if (field[i] == homeField)
						{
							on[b]++;
						}
						else
						{
							off[b]++;
						}
					}
				}
			}
			for (int b = 0; b < Bands.Length; b++)
			{
				perBand[Bands[b].name].Add((on[b], off[b]));
			}
			for (int t = 0; t < FinderTriesPerSample; t++)
			{
				finderTries++;
				Find.TickManager.DebugSetTicksGame(startTick + s * FinderTriesPerSample + t + 1);
				Rand.PushState(Gen.HashCombineInt(s, t + 1000));
				try
				{
					if (TileFinder.TryFindNewSiteTile(out PlanetTile site, colony, 7, 27))
					{
						finderFound++;
						if (!Find.WorldReachability.CanReach(colony, site))
						{
							finderOff++;
						}
					}
				}
				finally
				{
					Rand.PopState();
				}
			}
		}
		Find.TickManager.DebugSetTicksGame(startTick);
		Log.Message($"[Ringworld] Quest reach probe: {Samples} sample colonies; their landmass in tiles: {Spread(landTiles)}" + (ring != null ? $"; in columns around: {Spread(landColumns)} (of {ring.Geometry.liveTilesAround} live columns)" : ""));
		foreach ((string name, int min, int max) in Bands)
		{
			List<(int on, int off)> rows = perBand[name];
			int none = rows.Count(r => r.on == 0);
			int noneAtAll = rows.Count(r => r.on == 0 && r.off == 0);
			List<int> share = rows.Where(r => r.on + r.off > 0).Select(r => Mathf.RoundToInt(100f * r.on / (r.on + r.off))).ToList();
			Log.Message($"[Ringworld] Quest reach probe: band {min}-{max} ({name}): site-valid tiles on the colony's landmass {Spread(rows.Select(r => r.on).ToList())}, off it {Spread(rows.Select(r => r.off).ToList())}; on-landmass share % {Spread(share)}; {none} of {rows.Count} colonies have none on their landmass ({noneAtAll} none anywhere in the band)");
		}
		Log.Message($"[Ringworld] Quest reach probe: TryFindNewSiteTile 7-27 from the sample colonies: {finderFound} of {finderTries} found a tile, {finderOff} of them off the colony's landmass");
	}

	// Min / quartiles / max.
	private static string Spread(List<int> values)
	{
		if (values.Count == 0)
		{
			return "n/a";
		}
		List<int> sorted = values.OrderBy(v => v).ToList();
		int At(float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
		return $"min {sorted[0]} / p25 {At(0.25f)} / median {At(0.5f)} / p75 {At(0.75f)} / max {sorted[sorted.Count - 1]}";
	}
}
