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

// Dev harness probe for VefCompatibility's object-spawn patch (RW_DEV_VEF_SPAWNS=1): generates a map on a free land
// tile of the world's surface, preferring one with a road, and logs how many of VEF's object-spawn defs pass VEF's own
// map check there and how many things of theirs the map has. Run it with RW_DEV_VEF_UNPATCHED=1 for the "before". A
// spawn def with no layer, road or biome condition and nothing to spawn (Vanilla Vehicles Expanded's every-map wrecks
// without a tier mod) is given a stand-in thing for the probe, so an allowed def shows up on the map. The map, its camp
// and the stand-in are removed afterwards.
public static class DevVefSpawnProbe
{
	private const string SpawnDefTypeName = "VEF.Maps.ObjectSpawnsDef";

	private const string GenerateMapPatchTypeName = "VEF.Maps.VanillaExpandedFramework_MapGenerator_GenerateMap_Patch";

	// Not found on a freshly generated camp map otherwise.
	private static ThingDef StandIn => ThingDefOf.ComponentSpacer;

	public static void Run()
	{
		Type spawnDefType = AccessTools.TypeByName(SpawnDefTypeName);
		if (spawnDefType == null)
		{
			Log.Message("[Ringworld] VEF spawn probe: VEF isn't loaded.");
			return;
		}
		MethodInfo canSpawnAt = AccessTools.Method(AccessTools.TypeByName(GenerateMapPatchTypeName), "CanSpawnAt", new[] { typeof(Map), spawnDefType });
		List<Def> spawnDefs = ((IEnumerable)typeof(DefDatabase<>).MakeGenericType(spawnDefType).GetProperty("AllDefs").GetValue(null)).Cast<Def>().ToList();
		PlanetLayer surface = Find.WorldGrid.Surface;
		PlanetTile tile = FreeTile(surface, withRoad: true);
		if (!tile.Valid)
		{
			tile = FreeTile(surface, withRoad: false);
		}
		if (!tile.Valid)
		{
			Log.Error("[Ringworld] VEF spawn probe: no free land tile on the surface.");
			return;
		}
		Def standInDef = spawnDefs.FirstOrDefault(def => Unconditional(def) && !ThingsOf(def).Any());
		FieldInfo thingDefs = spawnDefType.GetField("thingDefs");
		object originalThings = standInDef == null ? null : thingDefs.GetValue(standInDef);
		if (standInDef != null)
		{
			thingDefs.SetValue(standInDef, new List<ThingOption> { new ThingOption(StandIn, 1f) });
		}
		Map map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(100, 1, 100), WorldObjectDefOf.Camp);
		try
		{
			List<string> allowed = spawnDefs.Where(def => (bool)canSpawnAt.Invoke(null, new object[] { map, def })).Select(def => def.defName).ToList();
			HashSet<ThingDef> spawnThings = new HashSet<ThingDef>(spawnDefs.SelectMany(ThingsOf));
			int things = map.listerThings.AllThings.Count(thing => spawnThings.Contains(thing.def));
			bool road = (tile.Tile as SurfaceTile)?.Roads?.Count > 0;
			Log.Message($"[Ringworld] VEF spawn probe on {surface.Def.defName} tile {tile.tileId} ({(road ? "road" : "no road")}, {map.Biome.defName}): {allowed.Count} of {spawnDefs.Count} spawn defs allowed [{string.Join(", ", allowed)}], {things} of their things on the map ({spawnThings.Count} thing defs listed{(standInDef == null ? "" : $", {StandIn.defName} standing in for {standInDef.defName}'s")}).");
		}
		finally
		{
			if (standInDef != null)
			{
				thingDefs.SetValue(standInDef, originalThings);
			}
			MapParent parent = map.Parent;
			Current.Game.DeinitAndRemoveMap(map, notifyPlayer: false);
			parent?.Destroy();
		}
	}

	private static PlanetTile FreeTile(PlanetLayer layer, bool withRoad)
	{
		for (int i = 0; i < layer.TilesCount; i++)
		{
			PlanetTile tile = new PlanetTile(i, layer);
			if (tile.Tile is SurfaceTile surfaceTile && !surfaceTile.WaterCovered && surfaceTile.hilliness != Hilliness.Impassable && !RiversAndRoads.IsWallFoot(tile) && !Find.WorldObjects.AnyWorldObjectAt(tile) && (!withRoad || surfaceTile.Roads?.Count > 0))
			{
				return tile;
			}
		}
		return PlanetTile.Invalid;
	}

	private static bool Unconditional(Def spawnDef)
	{
		Traverse def = Traverse.Create(spawnDef);
		return def.Field("allowedPlanetLayers").GetValue() == null && def.Field("allowedRoads").GetValue() == null && def.Field("allowedBiomes").GetValue() == null && def.Field("pawnKindDef").GetValue() == null;
	}

	// The things a spawn def can place: its thing, its weighted things and its category's things.
	private static IEnumerable<ThingDef> ThingsOf(Def spawnDef)
	{
		Traverse def = Traverse.Create(spawnDef);
		if (def.Field("thingDef").GetValue() is ThingDef single)
		{
			yield return single;
		}
		if (def.Field("thingDefs").GetValue() is List<ThingOption> options)
		{
			foreach (ThingOption option in options)
			{
				yield return option.thingDef;
			}
		}
		if (def.Field("category").GetValue() is ThingCategoryDef category)
		{
			foreach (ThingDef child in category.childThingDefs)
			{
				yield return child;
			}
		}
	}
}
