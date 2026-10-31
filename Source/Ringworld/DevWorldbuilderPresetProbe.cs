using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Dev harness probe for WorldbuilderCompatibility's preset guard (RW_DEV_WB_PRESET=1): runs Worldbuilder's own terrain
// and settlement restores on the ring with stand-in presets, and logs for each whether they threw, how many ring tiles
// changed and how many settlements the world has before and after. The stand-ins, each with one settlement:
// - two saved from vanilla 100% planets, at 7 subdivisions (fewer tiles than most rings) and 10 (more), holding the
//   ring's terrain shifted by half the ring and repeated out to their tile count, with the settlement past the ring's
//   last tile;
// - two with the ring's tile count but saved from rings of another shape: one with the ring's terrain shifted by half
//   the ring (rims across the middle), one as a ring twice as long and half as wide would have it (the rows in from
//   each rim impassable too), with the settlement past the last tile;
// - one saved from this ring's own shape, with every tile's elevation raised and the settlement on a free land tile,
//   which should restore.
// Run it with RW_DEV_WB_UNPATCHED=1 for the "before". The stand-ins keep the world's features and landmarks, so only
// the tile data differs. The restores replace the world's settlements and, without the guard, break the ring, so the
// harness runs the probe after any save.
public static class DevWorldbuilderPresetProbe
{
	private enum Shape { Planet, RimsAcrossMiddle, WiderRims, Same }

	// Vanilla planets at 100% coverage: 12 + 10 x (3^subdivisions - 1) tiles, at 7 and 10 subdivisions. The other
	// shapes have the ring's tile count.
	private static readonly (Shape shape, int tiles)[] Cases = { (Shape.Planet, 12 + 10 * (2187 - 1)), (Shape.Planet, 12 + 10 * (59049 - 1)), (Shape.RimsAcrossMiddle, 0), (Shape.WiderRims, 0), (Shape.Same, 0) };

	private const string PresetTypeName = "Worldbuilder.WorldPreset";

	private const string TerrainTypeName = "Worldbuilder.WorldPresetTerrainData";

	private const string SettlementTypeName = "Worldbuilder.SettlementSaveData";

	// The arrays SurfaceLayer serializes one or two bytes a tile; the road, river and mutator arrays are lists of links
	// or tiles and are kept as they are.
	private static readonly string[] PerTileArrays = { "tileBiome", "tileElevation", "tileHilliness", "tileTemperature", "tileRainfall", "tileSwampiness", "tileFeature", "tilePollution", "tileRiverDistances" };

	private static readonly string[] LinkArrays = { "tileRoadOrigins", "tileRoadAdjacency", "tileRoadDef", "tileRiverOrigins", "tileRiverAdjacency", "tileRiverDef", "tileMutatorTiles", "tileMutatorDefs" };

	public static void Run()
	{
		Type presetType = AccessTools.TypeByName(PresetTypeName);
		MethodInfo restoreTerrain = WorldbuilderCompatibility.Restore("RestoreTerrain");
		MethodInfo restoreBases = WorldbuilderCompatibility.Restore("RestoreBases");
		if (presetType == null || restoreTerrain == null || restoreBases == null)
		{
			Log.Message("[Ringworld] Worldbuilder preset probe: Worldbuilder isn't loaded.");
			return;
		}
		if (SurfaceRing.Current is not { Geometry: RingGeometry geometry } ring)
		{
			Log.Message("[Ringworld] Worldbuilder preset probe: the world isn't a ring.");
			return;
		}
		Faction faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer && !f.Hidden && Find.WorldObjects.Settlements.Any(s => s.Faction == f));
		foreach ((Shape shape, int caseTiles) in Cases)
		{
			int presetTiles = shape == Shape.Planet ? caseTiles : ring.TilesCount;
			object preset = Activator.CreateInstance(presetType);
			Traverse traverse = Traverse.Create(preset);
			traverse.Field("name").SetValue("Ringworld probe");
			traverse.Field("saveTerrain").SetValue(true);
			traverse.Field("saveBases").SetValue(true);
			traverse.Field("_terrainData").SetValue(StandInTerrain(ring, geometry, shape, presetTiles));
			PlanetTile settlementTile = shape == Shape.Same ? FreeTile(ring) : new PlanetTile(ring.TilesCount + 10, ring);
			traverse.Field("savedSettlementsData").SetValue(StandInSettlements(settlementTile, faction));
			List<(BiomeDef biome, float elevation, Hilliness hilliness)> before = Snapshot(ring);
			int settlementsBefore = Find.WorldObjects.Settlements.Count;
			string terrainOutcome = Outcome(restoreTerrain, Find.World, Find.WorldGrid, preset);
			string basesOutcome = Outcome(restoreBases, Find.World, preset);
			List<(BiomeDef biome, float elevation, Hilliness hilliness)> after = Snapshot(ring);
			int changed = after.Count == before.Count ? before.Where((tile, i) => tile != after[i]).Count() : -1;
			Log.Message($"[Ringworld] Worldbuilder preset probe: a {presetTiles:N0}-tile preset ({shape}) onto the ring's {before.Count:N0} tiles: terrain restore {terrainOutcome}, {(changed < 0 ? "tile count changed" : changed + " tiles changed")}; settlement restore {basesOutcome}, settlements {settlementsBefore} -> {Find.WorldObjects.Settlements.Count}.");
		}
	}

	private static string Outcome(MethodInfo restore, params object[] arguments)
	{
		try
		{
			restore.Invoke(null, arguments);
			return "didn't throw";
		}
		catch (TargetInvocationException e)
		{
			return $"threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}";
		}
	}

	private static object StandInTerrain(RingLayer ring, RingGeometry geometry, Shape shape, int presetTiles)
	{
		AccessTools.Method(typeof(SurfaceLayer), "TilesToRawData").Invoke(ring, null);
		object terrain = Activator.CreateInstance(AccessTools.TypeByName(TerrainTypeName));
		Traverse data = Traverse.Create(terrain);
		Traverse layer = Traverse.Create(ring);
		int shiftTiles = shape == Shape.Planet || shape == Shape.RimsAcrossMiddle ? ring.TilesCount / 2 : 0;
		foreach (string name in PerTileArrays)
		{
			byte[] own = layer.Field(name).GetValue<byte[]>();
			int bytesPerTile = own == null ? 0 : own.Length / ring.TilesCount;
			data.Field(name).SetValue(own == null ? null : Shifted(own, bytesPerTile * shiftTiles, bytesPerTile * presetTiles));
		}
		if (shape == Shape.WiderRims)
		{
			// The rows in from each rim are the rest of a twice-as-long ring's rim rows.
			byte[] hilliness = data.Field("tileHilliness").GetValue<byte[]>();
			int live = geometry.liveTilesAround;
			for (int i = 0; i < live; i++)
			{
				hilliness[live + i] = (byte)Hilliness.Impassable;
				hilliness[ring.TilesCount - 2 * live + i] = (byte)Hilliness.Impassable;
			}
		}
		if (shape == Shape.Same)
		{
			// tileElevation is a ushort a tile, low byte first: one metre up for (almost) every tile.
			byte[] elevation = data.Field("tileElevation").GetValue<byte[]>();
			for (int i = 0; i < elevation.Length; i += 2)
			{
				elevation[i]++;
			}
		}
		foreach (string name in LinkArrays)
		{
			data.Field(name).SetValue(layer.Field(name).GetValue<byte[]>());
		}
		data.Field("features").SetValue(Find.WorldFeatures.features.ToList());
		data.Field("landmarks").SetValue(new Dictionary<PlanetTile, Landmark>(Find.World.landmarks.landmarks));
		return terrain;
	}

	// Worldbuilder's List<SettlementSaveData> with one settlement of faction's on tile.
	private static object StandInSettlements(PlanetTile tile, Faction faction)
	{
		Type settlementType = AccessTools.TypeByName(SettlementTypeName);
		object settlement = Activator.CreateInstance(settlementType);
		Traverse.Create(settlement).Field("tileID").SetValue(tile);
		Traverse.Create(settlement).Field("faction").SetValue(faction?.def);
		Traverse.Create(settlement).Field("name").SetValue("Ringworld probe");
		IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(settlementType));
		list.Add(settlement);
		return list;
	}

	private static PlanetTile FreeTile(RingLayer ring)
	{
		for (int i = 0; i < ring.TilesCount; i++)
		{
			PlanetTile tile = new PlanetTile(i, ring);
			if (!ring[i].WaterCovered && ring[i].hilliness != Hilliness.Impassable && !Find.WorldObjects.AnyWorldObjectAt(tile))
			{
				return tile;
			}
		}
		return PlanetTile.Invalid;
	}

	// source from byte offset on, wrapping round, out to length bytes.
	private static byte[] Shifted(byte[] source, int offset, int length)
	{
		byte[] result = new byte[length];
		for (int i = 0; i < length && source.Length > 0; i++)
		{
			result[i] = source[(i + offset) % source.Length];
		}
		return result;
	}

	private static List<(BiomeDef biome, float elevation, Hilliness hilliness)> Snapshot(RingLayer ring)
	{
		List<(BiomeDef, float, Hilliness)> tiles = new List<(BiomeDef, float, Hilliness)>();
		for (int i = 0; i < ring.TilesCount; i++)
		{
			SurfaceTile tile = ring[i];
			tiles.Add((tile.PrimaryBiome, tile.elevation, tile.hilliness));
		}
		return tiles;
	}
}
