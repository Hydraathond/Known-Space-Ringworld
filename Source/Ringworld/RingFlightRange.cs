using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Vanilla prices and limits a flight within a layer per tile, by the layer def's rangeDistanceFactor. A planet's LAO
// atmosphere layers have 5 subdivisions against the surface's 10, so their tiles are far wider than the ground's and
// the factor is per wide tile. A ring world's atmosphere layers keep the ground's hex size (ticket 11), so the same
// factor per tile would charge a ring's atmosphere about 35 times a planet's for the same distance. On an atmosphere
// ring the factor is therefore scaled by the layer's tile size over the tile size its def gives on a planet, both in
// ground tiles. Every other layer (the surface ring, orbit layers, any layer of a planet world) uses its def's factor.
// Vanilla's readers (gravship fuel and range, launcher fuel and range) go through RangeFactor by transpiler.
[HarmonyPatch]
public static class RingFlightRange
{
	// The factor vanilla's flight cost and range use for tiles of the layer.
	public static float RangeFactor(PlanetLayer layer)
	{
		PlanetLayerDef def = layer.Def;
		return layer is RingLayer ring && !ring.IsSurfaceRing && ring.Geometry != null ? EffectiveFactor(ring) : def.rangeDistanceFactor;
	}

	// The def's factor scaled for an atmosphere ring; the def's own if the ground or the planet reference isn't known.
	private static float EffectiveFactor(RingLayer atmosphere)
	{
		float defFactor = atmosphere.Def.rangeDistanceFactor;
		float planetTile = PlanetTileSize(atmosphere.Def);
		RingLayer ground = SurfaceRing.Of(Find.WorldGrid);
		if (ground?.Geometry == null || planetTile <= 0f)
		{
			return defFactor;
		}
		return defFactor * atmosphere.Geometry.tileSize / ground.Geometry.tileSize / planetTile;
	}

	// The width, in ground tiles, of a tile of the def's layer on a planet: 2^(ground subdivisions - its subdivisions)
	// times its radius over the ground's. LAO names a layer's settings def after the layer def. 0 if it has none.
	private static float PlanetTileSize(PlanetLayerDef def)
	{
		PlanetLayerSettings settings = DefDatabase<PlanetLayerSettingsDef>.GetNamedSilentFail(def.defName)?.settings;
		PlanetLayerSettings ground = PlanetLayerSettingsDefOf.Surface?.settings;
		if (settings == null || ground == null)
		{
			return 0f;
		}
		return UnityEngine.Mathf.Pow(2f, ground.subdivisions - settings.subdivisions) * settings.radius / ground.radius;
	}

	internal static readonly FieldInfo FactorField = AccessTools.Field(typeof(PlanetLayerDef), nameof(PlanetLayerDef.rangeDistanceFactor));
	private static readonly MethodInfo LayerDefGetter = AccessTools.PropertyGetter(typeof(PlanetLayer), nameof(PlanetLayer.Def));
	private static readonly MethodInfo TileLayerDefGetter = AccessTools.PropertyGetter(typeof(PlanetTile), nameof(PlanetTile.LayerDef));
	private static readonly MethodInfo TileLayerGetter = AccessTools.PropertyGetter(typeof(PlanetTile), nameof(PlanetTile.Layer));
	internal static readonly MethodInfo RangeFactorMethod = AccessTools.Method(typeof(RingFlightRange), nameof(RangeFactor));

	// The methods that read a layer def's rangeDistanceFactor to price or limit a flight, and how many reads each has.
	public static readonly (MethodInfo method, int reads)[] Targets =
	{
		(AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.TryGetPathFuelCost)), 1),
		(AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.MaxDistForFuel)), 1),
		(AccessTools.Method(typeof(CompPilotConsole), "GetMaxLaunchDistance"), 1),
		(AccessTools.Method(typeof(CompLaunchable), nameof(CompLaunchable.MaxLaunchDistanceEver)), 1),
		(AccessTools.Method(typeof(CompLaunchable), nameof(CompLaunchable.MaxLaunchDistanceAtFuelLevel), new[] { typeof(float), typeof(PlanetLayer) }), 2),
		(AccessTools.Method(typeof(CompLaunchable), nameof(CompLaunchable.FuelNeededToLaunchAtDist)), 1),
	};

	public static IEnumerable<MethodBase> TargetMethods()
	{
		return Targets.Select(target => target.method);
	}

	// Each "layer.Def.rangeDistanceFactor" and "tile.LayerDef.rangeDistanceFactor" becomes RangeFactor(layer).
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		List<CodeInstruction> code = new List<CodeInstruction>();
		int swapped = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (code.Count > 0 && instruction.LoadsField(FactorField))
			{
				CodeInstruction previous = code[code.Count - 1];
				if (previous.Calls(LayerDefGetter))
				{
					code[code.Count - 1] = new CodeInstruction(OpCodes.Call, RangeFactorMethod).MoveLabelsFrom(previous);
					swapped++;
					continue;
				}
				if (previous.Calls(TileLayerDefGetter))
				{
					code[code.Count - 1] = new CodeInstruction(OpCodes.Call, TileLayerGetter).MoveLabelsFrom(previous);
					code.Add(new CodeInstruction(OpCodes.Call, RangeFactorMethod));
					swapped++;
					continue;
				}
			}
			code.Add(instruction);
		}
		int expected = Targets.First(target => target.method == original).reads;
		if (swapped != expected)
		{
			Log.Warning($"[Ringworld] {original.DeclaringType?.Name}.{original.Name} has {swapped} range factor reads, not {expected}; atmosphere rings may be priced as planet layers.");
		}
		return code;
	}
}
