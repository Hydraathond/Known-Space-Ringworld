using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Atmosphere layers on a ring world are rings (ADR 0005). With LAO, its troposphere, stratosphere and mesosphere defs
// carry a RingAtmosphereExtension (Patches/LayeredAtmosphereOrbit.xml), and on a ring world a layer of such a def is
// built as a RingLayer instead of LAO's sphere: LAO's own defs, so its floating islands, whitelists and per-layer
// settings reach them unchanged, and planet worlds keep LAO's spheres. Each is the ring geometry at a smaller radius:
// a fixed fraction of the rim wall's height above the inner surface (the extension's, the same whichever layers exist),
// the surface ring's tiles across, tiles around scaled to its radius so hexes stay vanilla-sized, and below 100% land
// coverage only the live arc's angular span. Its shape is set from the surface ring when the layer is made and saved
// with it (RingLayer.ExposeRingSettings and vanilla's radius), so a later change to these rules never moves it.
// An atmosphere ring is never the surface: it has LAO's def, not the ring's (SurfaceIdentity.IsSurface).
// Its tiles are LAO's plain tiles rather than the surface's (Patch_SurfaceLayer_PlainTiles). Building them as rings is
// only applied with LAO loaded (LaoIntegration), which counts a failure here as a failed integration.
public static class RingAtmosphere
{
	// Whether layers of the def are atmosphere layers on a ring world.
	public static bool IsAtmosphere(PlanetLayerDef def)
	{
		return def != null && def.HasModExtension<RingAtmosphereExtension>();
	}

	// Whether a layer of the def made now on the grid's world is built as a ring: only while LAO's integration works
	// (with it failed or LAO missing, no atmosphere layer is made), and over a built surface ring. The surface ring is
	// registered first (it's the scenario's first layer part), on a new world as on a loaded one.
	public static bool BuildsAsRing(PlanetLayerDef def, WorldGrid grid)
	{
		return LaoIntegration.OrbitStackAvailable && IsAtmosphere(def) && SurfaceUnder(grid) != null;
	}

	// The grid's surface ring, once it's built.
	public static RingLayer SurfaceUnder(WorldGrid grid)
	{
		return SurfaceRing.Of(grid) is { Geometry: not null } ring ? ring : null;
	}

	// The surface ring the layer being made now is built over: set by LayerTypeFor from the grid that registers it,
	// and taken by the RingLayer constructor that vanilla calls right after, so both read the same grid.
	private static RingLayer pendingSurface;

	internal static RingLayer TakePendingSurface()
	{
		RingLayer surface = pendingSurface;
		pendingSurface = null;
		return surface;
	}

	// The def's height above the inner surface, as a fraction of the rim wall's height.
	public static float HeightFraction(PlanetLayerDef def)
	{
		return def.GetModExtension<RingAtmosphereExtension>()?.heightFraction ?? 0f;
	}

	// The radius of a new atmosphere layer of the def over the surface ring.
	public static float RadiusFor(RingLayer surface, PlanetLayerDef def)
	{
		return surface.Radius - HeightFraction(def) * WorldDrawLayer_RingThickness.WallHeight * surface.Geometry.tileSize;
	}

	// The size and land coverage of a new atmosphere layer at this radius over the surface ring: the surface's tiles
	// across, tiles around scaled to the radius, and below the full loop the most columns whose hexes all lie within the
	// surface's live arc.
	public static (RingSize size, float landCoverage) ShapeFor(RingLayer surface, float radius)
	{
		RingGeometry ground = surface.Geometry;
		int tilesAround = Mathf.Max(3, Mathf.RoundToInt(ground.tilesAround * radius / ground.radius));
		RingSize size = new RingSize(tilesAround, ground.tilesAcross);
		if (ground.IsFullLoop)
		{
			return (size, RingSize.FullLandCoverage);
		}
		int live = Mathf.Min(tilesAround - 1, Mathf.FloorToInt((float)ground.liveTilesAround * tilesAround / ground.tilesAround));
		while (live > 1 && !WithinArc(ground, tilesAround, live))
		{
			live--;
		}
		return (size, (float)live / tilesAround);
	}

	// Whether a live arc of this many columns, centred as RingGeometry centres it, has every hex within the ground's live
	// arc. A hex reaches half a column anti-spinward of its column on even rows and a whole column spinward on odd ones.
	private static bool WithinArc(RingGeometry ground, int tilesAround, int live)
	{
		int first = -(live / 2);
		int last = first + live - 1;
		double groundFrom = (ground.firstColumn - 0.5) / ground.tilesAround;
		double groundTo = (ground.LastColumn + 1.0) / ground.tilesAround;
		return (first - 0.5) / tilesAround >= groundFrom && (last + 1.0) / tilesAround <= groundTo;
	}

	// The layer type vanilla makes a layer of the def as: a ring for an atmosphere layer on a ring world.
	public static Type LayerTypeFor(PlanetLayerDef def, WorldGrid grid)
	{
		pendingSurface = null;
		if (!BuildsAsRing(def, grid))
		{
			return def.layerType;
		}
		pendingSurface = SurfaceUnder(grid);
		return typeof(RingLayer);
	}

	private static readonly FieldInfo LayerTypeField = AccessTools.Field(typeof(PlanetLayerDef), nameof(PlanetLayerDef.layerType));

	private static int layerTypeReadsSwapped;

	// Null if every patch applied, otherwise what's wrong.
	public static string Apply(Harmony harmony)
	{
		MethodInfo register = AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.RegisterPlanetLayer), new[] { typeof(PlanetLayerDef), typeof(Vector3?), typeof(float), typeof(float), typeof(float), typeof(int), typeof(float), typeof(float), typeof(Vector3?) });
		if (register == null)
		{
			return "WorldGrid.RegisterPlanetLayer not found";
		}
		layerTypeReadsSwapped = 0;
		MethodInfo transpiler = AccessTools.Method(typeof(RingAtmosphere), nameof(MakeAtmosphereAsRing));
		harmony.Patch(register, transpiler: new HarmonyMethod(transpiler));
		if (layerTypeReadsSwapped != 1)
		{
			harmony.Unpatch(register, transpiler);
			return $"WorldGrid.RegisterPlanetLayer reads the layer type {layerTypeReadsSwapped} times, not once";
		}
		return null;
	}

	// The layer type read in RegisterPlanetLayer goes through LayerTypeFor, with the grid.
	private static IEnumerable<CodeInstruction> MakeAtmosphereAsRing(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo layerTypeFor = AccessTools.Method(typeof(RingAtmosphere), nameof(LayerTypeFor));
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(LayerTypeField))
			{
				layerTypeReadsSwapped++;
				yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
				yield return new CodeInstruction(OpCodes.Call, layerTypeFor);
				continue;
			}
			yield return instruction;
		}
	}
}

// On a ring world with LAO, an atmosphere layer of the def is a ring this fraction of the rim wall's height above the
// inner surface (RingAtmosphere). The fractions are set in Patches/LayeredAtmosphereOrbit.xml.
public class RingAtmosphereExtension : DefModExtension
{
	public float heightFraction;
}

// An atmosphere layer's tiles are LAO's plain tiles, and the surface layer's per-tile data reads every tile as a surface
// tile, so for a ring layer with plain tiles only what every planet layer keeps is converted: vanilla's PlanetLayer
// methods, which SurfaceLayer overrides, run instead. A ring layer whose def is gone (LAO removed) has no tiles to
// convert; vanilla drops the layer once the world has loaded. Applied whether or not LAO is loaded, for that last case.
[HarmonyPatch]
public static class Patch_SurfaceLayer_PlainTiles
{
	private static bool HasPlainTiles(PlanetLayer layer)
	{
		return layer is RingLayer && (layer.Def == null || !layer.Def.SurfaceTiles);
	}

	[HarmonyPatch(typeof(SurfaceLayer), "TilesToRawData")]
	[HarmonyPrefix]
	public static bool TilesToRawData(SurfaceLayer __instance)
	{
		return RunInstead(__instance, PlanetLayerTilesToRawData);
	}

	[HarmonyPatch(typeof(SurfaceLayer), "RawDataToTiles")]
	[HarmonyPrefix]
	public static bool RawDataToTiles(SurfaceLayer __instance)
	{
		return RunInstead(__instance, PlanetLayerRawDataToTiles);
	}

	// For a ring layer with plain tiles, runs vanilla's PlanetLayer method (unless its def is gone) and skips
	// SurfaceLayer's; for any other layer, lets SurfaceLayer's run.
	private static bool RunInstead(SurfaceLayer layer, Action<PlanetLayer> planetLayerMethod)
	{
		if (!HasPlainTiles(layer))
		{
			return true;
		}
		if (layer.Def != null)
		{
			planetLayerMethod(layer);
		}
		return false;
	}

	[HarmonyReversePatch]
	[HarmonyPatch(typeof(PlanetLayer), "TilesToRawData")]
	public static void PlanetLayerTilesToRawData(PlanetLayer instance)
	{
		throw new NotImplementedException("reverse patch");
	}

	[HarmonyReversePatch]
	[HarmonyPatch(typeof(PlanetLayer), "RawDataToTiles")]
	public static void PlanetLayerRawDataToTiles(PlanetLayer instance)
	{
		throw new NotImplementedException("reverse patch");
	}
}
