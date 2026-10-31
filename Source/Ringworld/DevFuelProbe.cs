using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Dev harness probe for the fuel shown between layers (RW_DEV_FUEL_PROBE=1): vanilla's GravshipUtility.TryGetPathFuelCost
// keeps the last layer-to-layer cost in a cache whose layers are stored the wrong way round, so a flight and then its
// way back reads the first flight's cost. That only shows when the two directions differ, as LAO's fuel cost between
// layers makes them (climbing costs fuel, descending none). For each pair of the world's layers it logs the fuel for
// each direction asked cold, and asked straight after the opposite direction.
public static class DevFuelProbe
{
	private static readonly string[] CacheFields = { "cachedOrigin", "cachedDest", "cachedOriginLayer", "cachedDestLayer" };

	public static void Run()
	{
		WorldGrid grid = Find.WorldGrid;
		List<PlanetLayer> layers = new List<PlanetLayer> { grid.Surface };
		layers.AddRange(grid.PlanetLayers.Values.Where(layer => layer != grid.Surface));
		int mismatches = 0;
		for (int i = 0; i < layers.Count; i++)
		{
			for (int j = i + 1; j < layers.Count; j++)
			{
				PlanetTile a = new PlanetTile(0, layers[i]);
				PlanetTile b = new PlanetTile(0, layers[j]);
				float coldAB = Fuel(a, b, null, null);
				float coldBA = Fuel(b, a, null, null);
				float afterBA = Fuel(a, b, b, a);
				float afterAB = Fuel(b, a, a, b);
				string names = $"{layers[i].Def.defName} <-> {layers[j].Def.defName}";
				bool ok = coldAB == afterBA && coldBA == afterAB;
				mismatches += ok ? 0 : 1;
				Log.Message($"[Ringworld] Fuel probe {(ok ? "OK" : "WRONG")} {names}: out {coldAB}, back {coldBA}; out right after back {afterBA}, back right after out {afterAB}");
			}
		}
		LogRangeFactors(grid, layers);
		Log.Message($"[Ringworld] Fuel probe done: {mismatches} of {layers.Count * (layers.Count - 1) / 2} layer pairs show the wrong fuel after the opposite flight.");
	}

	// For each layer but the ground: its def's range factor and the effective one (RingFlightRange), and the fuel from the
	// ground tile at the middle of the live arc to the tile directly above it and, on a ring-shaped layer, to the tiles 1,
	// 10 and 100 columns over from that one; then the way down from the tile above to the ground tile.
	private static void LogRangeFactors(WorldGrid grid, List<PlanetLayer> layers)
	{
		RingLayer ground = SurfaceRing.Of(grid);
		if (ground == null)
		{
			return;
		}
		RingGeometry groundGeometry = ground.Geometry;
		int[] liveColumns = groundGeometry.LiveColumns().ToArray();
		PlanetTile below = new PlanetTile(groundGeometry.TileId(groundGeometry.tilesAcross / 2, liveColumns[liveColumns.Length / 2]), ground);
		foreach (PlanetLayer layer in layers.Where(layer => layer != ground))
		{
			PlanetTile above = layer.GetClosestTile_NewTemp(below);
			string line = $"[Ringworld] Fuel probe factors {layer.Def.defName}: def {layer.Def.rangeDistanceFactor}, effective {RingFlightRange.RangeFactor(layer)}; up {Fuel(below, above, null, null)}, down {Fuel(above, below, null, null)}";
			if (layer is RingLayer ring)
			{
				RingGeometry geometry = ring.Geometry;
				int row = geometry.RowOf(above.tileId);
				int column = geometry.ColumnOf(above.tileId);
				foreach (int over in new[] { 1, 10, 100 })
				{
					int target = geometry.TileId(row, column + over);
					line += target >= 0 && target < layer.TilesCount ? $", {over} columns over {Fuel(below, new PlanetTile(target, layer), null, null)}" : $", {over} columns over: off the layer";
				}
			}
			Log.Message(line);
		}
	}

	// The fuel asked from one tile to another, after asking for the flight first -> second (null: with a cold cache).
	private static float Fuel(PlanetTile from, PlanetTile to, PlanetTile? firstFrom, PlanetTile? firstTo)
	{
		foreach (string field in CacheFields)
		{
			Traverse.Create(typeof(GravshipUtility)).Field(field).SetValue(field.EndsWith("Layer") ? null : (object)PlanetTile.Invalid);
		}
		if (firstFrom.HasValue)
		{
			GravshipUtility.TryGetPathFuelCost(firstFrom.Value, firstTo.Value, out _, out _);
		}
		GravshipUtility.TryGetPathFuelCost(from, to, out float cost, out _);
		return cost;
	}
}
