using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Atmosphere layers on a ring world (ADR 0005, RingAtmosphere): each existing one is a ring at its height, with the
// ground's tiles across, vanilla-sized hexes and nothing outside the live arc, the ground's local time, and never the
// surface.
public static partial class RingSelfCheck
{
	// From the spec: LAO's atmosphere layers, lowest first, and their heights above the inner surface as fractions of the
	// rim wall's height.
	private static readonly (string def, float heightFraction)[] AtmosphereLayers =
	{
		("LAO_Troposphere", 0.25f),
		("LAO_Stratosphere", 0.5f),
		("LAO_Mesosphere", 0.75f),
	};

	// How far an atmosphere layer's height may be from its fraction, as a fraction of the rim wall's height.
	private const float AtmosphereHeightTolerance = 0.005f;

	// How far an atmosphere layer's hexes may be from the ground's in size: tiles around are rounded to the radius.
	private const float AtmosphereTileSizeTolerance = 0.01f;

	// Every ring-shaped layer but the ground is one of LAO's atmosphere layers, at its height fraction of the rim wall
	// above the inner surface, with the ground's tiles across and hexes within a rounding of the ground's, and no tile
	// centre outside the ground's live arc (the hexes of its tiles at each end). It isn't the surface: not the grid's
	// root surface, not what the surface lookups find (vanilla's Surface def, from the grid and from its own tiles, and
	// LAO's Surface-group defs), and IsSurface is false for its def, while the world's surface is still the ring.
	private static void CheckAtmosphereLayers(RingLayer ring, RingCheckResult result)
	{
		WorldGrid grid = Find.WorldGrid;
		List<RingLayer> atmosphere = grid.PlanetLayers.Values.OfType<RingLayer>().Where(layer => layer != ring).ToList();
		if (atmosphere.Count == 0)
		{
			return;
		}
		if (grid.Surface != ring || SurfaceRing.Of(grid) != ring || !ring.IsSurfaceRing)
		{
			result.Fail($"with atmosphere layers, the world's surface is layer {grid.Surface?.LayerID}, not the ring (layer {ring.LayerID})");
		}
		if (grid.FirstLayerOfDef(PlanetLayerDefOf.Surface) != ring)
		{
			result.Fail($"a layer looked up by vanilla's Surface def is layer {grid.FirstLayerOfDef(PlanetLayerDefOf.Surface)?.LayerID}, not the ring");
		}
		(float from, float to) arc = AngularSpan(ring);
		CheckAtmosphereFlightsVanilla(result);
		foreach (RingLayer layer in atmosphere)
		{
			string name = $"ring-shaped layer {layer.LayerID} ({layer.Def?.defName})";
			int index = Array.FindIndex(AtmosphereLayers, entry => entry.def == layer.Def?.defName);
			if (index < 0)
			{
				result.Fail($"{name} is neither the ring nor one of LAO's atmosphere layers");
				continue;
			}
			if (layer.IsSurfaceRing || layer.IsRootSurface || SurfaceIdentity.IsSurface(layer.Def))
			{
				result.Fail($"{name} counts as the surface");
			}
			if (layer.Geometry == null)
			{
				result.Fail($"{name} has no ring geometry");
				continue;
			}
			RingGeometry geometry = layer.Geometry;
			float wallHeight = WorldDrawLayer_RingThickness.WallHeight * ring.Geometry.tileSize;
			float fraction = (ring.Radius - layer.Radius) / wallHeight;
			float expected = AtmosphereLayers[index].heightFraction;
			if (Mathf.Abs(fraction - expected) > AtmosphereHeightTolerance)
			{
				result.Fail($"{name} is {fraction:F3} of the rim wall's height above the inner surface (radius {layer.Radius:F2}, the ring's {ring.Radius:F2}); expected {expected:F2}");
			}
			if (fraction <= 0f || fraction >= 1f)
			{
				result.Fail($"{name} is at {fraction:F3} of the rim wall's height, not between the inner surface and the rim-wall tops");
			}
			if (geometry.tilesAcross != ring.Geometry.tilesAcross)
			{
				result.Fail($"{name} has {geometry.tilesAcross} tiles across; the ring has {ring.Geometry.tilesAcross}");
			}
			if (Mathf.Abs(geometry.tileSize / ring.Geometry.tileSize - 1f) > AtmosphereTileSizeTolerance)
			{
				result.Fail($"{name} has hexes {geometry.tileSize:F4} across with {geometry.tilesAround} tiles around; the ring's are {ring.Geometry.tileSize:F4}");
			}
			if (ring.Geometry.IsFullLoop != geometry.IsFullLoop)
			{
				result.Fail($"{name} {(geometry.IsFullLoop ? "is" : "isn't")} a full loop, unlike the ring");
			}
			if (!ring.Geometry.IsFullLoop)
			{
				int outside = 0;
				int firstOutside = -1;
				float halfHex = 0.5f * 360f / geometry.tilesAround;
				for (int id = 0; id < layer.TilesCount; id++)
				{
					float degrees = RingGeometry.AngleOf(layer.GetTileCenter(id)) * Mathf.Rad2Deg;
					if (degrees - halfHex < arc.from || degrees + halfHex > arc.to)
					{
						outside++;
						firstOutside = firstOutside < 0 ? id : firstOutside;
					}
				}
				if (outside > 0)
				{
					result.Fail($"{name} has {outside} tiles whose hexes reach outside the ring's live arc from {arc.from:F2} to {arc.to:F2} degrees (first: {firstOutside})");
				}
			}
			if (layer.TilesCount == 0)
			{
				result.Fail($"{name} has no tiles");
				continue;
			}
			CheckAtmosphereLocalTime(ring, layer, name, result);
			// After loading a save, the check runs before layer connections are resolved (see CheckLaoLayers).
			PlanetTile sample = new PlanetTile(layer.TilesCount / 2, layer);
			if (Scribe.mode == LoadSaveMode.Inactive && grid.TryGetFirstAdjacentLayerOfDef(sample, PlanetLayerDefOf.Surface, out PlanetLayer adjacent) && adjacent != ring)
			{
				result.Fail($"from {name}, the adjacent layer of vanilla's Surface def is layer {adjacent.LayerID}, not the ring");
			}
			foreach (PlanetLayerDef surfaceDef in new[] { PlanetLayerDefOf.Surface, ring.Def })
			{
				if (grid.TryGetFirstLayerOfDef(surfaceDef, out PlanetLayer found) && found != ring)
				{
					result.Fail($"a layer looked up by {surfaceDef.defName} is layer {found.LayerID}, not the ring");
				}
			}
		}
	}

	// Gravship flights between ring-shaped layers take vanilla's time, which follows the angle between the two tiles,
	// not LAO's route, which is timed by the radial distance between layers (about 4x vanilla's on a Small ring, 40x on
	// a Lore ratio ring): LAO's travel prefix carries Ringworld's counter-patch, which steps it aside on a ring world.
	private static void CheckAtmosphereFlightsVanilla(RingCheckResult result)
	{
		MethodInfo laoTravel = AccessTools.Method(AccessTools.TypeByName("LayeredAtmosphereOrbit.HarmonyPatches"), "GU_TravelTo_Prefix");
		if (laoTravel == null)
		{
			result.Fail("LAO's gravship travel prefix (GU_TravelTo_Prefix) wasn't found");
			return;
		}
		if (Harmony.GetPatchInfo(laoTravel)?.Prefixes.Any(patch => patch.owner == "ringworld.lao") != true)
		{
			result.Fail("LAO's gravship travel prefix doesn't carry Ringworld's counter-patch, so flights between layers take LAO's radial route");
		}
	}

	// Sample tiles of an atmosphere layer have the local time (vanilla's longitude) of the ring's midline tile nearest
	// them round the axis, within that tile's own span, so a map there has the time of day the night shadow shows.
	private static void CheckAtmosphereLocalTime(RingLayer ring, RingLayer layer, string name, RingCheckResult result)
	{
		int midline = ring.Geometry.tilesAcross / 2;
		float tolerance = 360f * ring.Geometry.nightZones / ring.Geometry.tilesAround;
		foreach (int id in new[] { 0, layer.TilesCount / 3, layer.TilesCount / 2, layer.TilesCount - 1 })
		{
			float angle = RingGeometry.AngleOf(layer.GetTileCenter(id));
			int nearest = -1;
			float nearestGap = float.MaxValue;
			foreach (int column in ring.Geometry.LiveColumns())
			{
				int groundId = ring.Geometry.TileId(midline, column);
				float gap = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, RingGeometry.AngleOf(ring.GetTileCenter(groundId)) * Mathf.Rad2Deg));
				if (gap < nearestGap)
				{
					nearest = groundId;
					nearestGap = gap;
				}
			}
			float own = layer.LongLatOf(new PlanetTile(id, layer)).x;
			float ground = ring.LongLatOf(new PlanetTile(nearest, ring)).x;
			if (Mathf.Abs(Mathf.DeltaAngle(own, ground)) > tolerance)
			{
				result.Fail($"{name} tile {id} has local-time longitude {own:F1}, but the ring's tile {nearest} nearest it round the axis has {ground:F1}");
			}
		}
	}

	// The angles, in degrees, the ring's live arc covers: from its tiles' centres out to the ends of its end hexes.
	private static (float from, float to) AngularSpan(RingLayer ring)
	{
		float from = float.MaxValue;
		float to = float.MinValue;
		for (int id = 0; id < ring.TilesCount; id++)
		{
			float degrees = RingGeometry.AngleOf(ring.GetTileCenter(id)) * Mathf.Rad2Deg;
			from = Mathf.Min(from, degrees);
			to = Mathf.Max(to, degrees);
		}
		float halfHex = 0.5f * 360f / ring.Geometry.tilesAround;
		return (from - halfHex, to + halfHex);
	}
}
