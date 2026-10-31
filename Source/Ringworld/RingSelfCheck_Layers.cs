using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Which planet layers a ring world has, where Ringworld decides it (LaoIntegration), and where LAO's orbit stack puts
// them. Without LAO there's nothing to check.
public static partial class RingSelfCheck
{
	// The orbit stack, from the spec: layer def, fraction of the ring radius, orbit band either side of the ring plane.
	private static readonly (string def, float radiusFactor, float band)[] OrbitStackLayers =
	{
		("Orbit", 0.7f, 15f),
		(LaoIntegration.HighOrbitName, 0.5f, 45f),
	};

	// LAO's subdivisions for its layers, kept on the ring.
	private const int OrbitStackSubdivisions = 5;

	// How far, in degrees, an orbit band may be from the stack's and still match it.
	private const float BandAngleTolerance = 0.01f;

	// While Layered Atmosphere and Orbit is active: no layer but the ring, Odyssey's orbit and LAO's optional layers a
	// ring supports, chained ring first and then by LAO's elevation, each zooming out to the next and in to the one
	// before, and connected both ways; the last zooms out to nothing. While the integration is active on the world,
	// every optional layer ticked in LAO's layer list is there (ADR 0006); an unticked one may be there too.
	private static void CheckLaoLayers(RingLayer ring, RingCheckResult result)
	{
		if (!LaoIntegration.Active)
		{
			return;
		}
		PlanetLayer orbit = Find.WorldGrid.Orbit;
		List<PlanetLayer> optional = new List<PlanetLayer>();
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values)
		{
			if (LaoIntegration.IsRingOptionalLayer(layer.Def))
			{
				optional.Add(layer);
			}
			else if (layer != ring && layer != orbit)
			{
				result.Fail($"layer {layer.LayerID} ({layer.Def.defName}) is neither the ring, Odyssey's orbit nor one of LAO's optional layers a ring supports");
			}
		}
		if (orbit == null)
		{
			foreach (PlanetLayer layer in optional)
			{
				result.Fail($"the world has LAO's {layer.Def.defName} but no Odyssey's orbit");
			}
			return;
		}
		// After loading a save, the check runs from World.FinalizeInit before the loader resolves the layers'
		// references, zooms and connections among them, and before vanilla adds the layers the world lacks; the dev
		// harness and the debug action check them later.
		if (Scribe.mode != LoadSaveMode.Inactive)
		{
			return;
		}
		if (LaoIntegration.ActiveOn(Find.WorldGrid))
		{
			foreach (PlanetLayerDef ticked in LaoIntegration.TickedRingOptionalLayers)
			{
				if (!optional.Any(layer => layer.Def == ticked))
				{
					result.Fail($"{ticked.defName} is ticked in LAO's layer list, but the world has no layer of it");
				}
			}
		}
		// The chain is the integration's: with a failed one, a world keeps the links it was saved with, and LAO's
		// elevations, which order it, may not have been found.
		if (!LaoIntegration.OrbitStackAvailable)
		{
			return;
		}
		List<PlanetLayer> chain = optional.Prepend(orbit).OrderBy(layer => LaoIntegration.Elevation(layer.Def)).Prepend(ring).ToList();
		for (int i = 1; i < chain.Count; i++)
		{
			CheckChained(chain[i - 1], chain[i], result);
		}
		PlanetLayer outermost = chain[chain.Count - 1];
		if (outermost.zoomOutToLayer != null)
		{
			result.Fail($"{outermost.Def.defName} zooms out to {outermost.zoomOutToLayer.Def.defName}; it's the outermost layer");
		}
	}

	private static void CheckChained(PlanetLayer inner, PlanetLayer outer, RingCheckResult result)
	{
		if (inner.zoomOutToLayer != outer)
		{
			result.Fail($"{inner.Def.defName} zooms out to {inner.zoomOutToLayer?.Def.defName ?? "nothing"}, not {outer.Def.defName}");
		}
		if (outer.zoomInToLayer != inner)
		{
			result.Fail($"{outer.Def.defName} zooms in to {outer.zoomInToLayer?.Def.defName ?? "nothing"}, not {inner.Def.defName}");
		}
		if (!inner.HasConnectionFromTo(outer) || !outer.HasConnectionFromTo(inner))
		{
			result.Fail($"{inner.Def.defName} and {outer.Def.defName} aren't connected both ways");
		}
	}

	// While LAO's integration is active on the world, every sun-centred orbit layer is one of the orbit stack's, at its
	// radius, with LAO's subdivisions, and every world object on it is on one of its tiles. Its orbit band is the
	// stack's, or none for an orbit moved into the stack from a world made without it (RingOrbit.UpgradeLoadedLayer,
	// RingOrbitSpan.MovedIntoStack), which stays a full sphere. With LAO but a failed integration, no orbit layer has an orbit band. Tiles outside an
	// orbit band are checked by CheckOrbitSpansLiveArc.
	private static void CheckOrbitStack(RingLayer ring, RingCheckResult result)
	{
		if (!LaoIntegration.Active)
		{
			return;
		}
		if (!LaoIntegration.ActiveOn(Find.WorldGrid))
		{
			foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
			{
				if (!RingOrbitSpan.IsFullBand(orbit))
				{
					result.Fail($"{RingOrbit.Describe(orbit)} has an orbit band of {RingOrbitSpan.BandHalfAngleOf(orbit):F2} degrees, but LAO's integration isn't active");
				}
			}
			return;
		}
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			string name = RingOrbit.Describe(orbit);
			int index = Array.FindIndex(OrbitStackLayers, layer => layer.def == orbit.Def.defName);
			if (index < 0)
			{
				result.Fail($"{name} ({orbit.Def.defName}) isn't one of the orbit stack's layers");
				continue;
			}
			(string _, float radiusFactor, float band) = OrbitStackLayers[index];
			float ratio = orbit.Radius / ring.Radius;
			if (Mathf.Abs(ratio - radiusFactor) > radiusFactor * OrbitRadiusTolerance)
			{
				result.Fail($"{name} has radius {orbit.Radius:F1}, {ratio:F2} x the ring radius {ring.Radius:F1}; expected about {radiusFactor:F2} x");
			}
			float actualBand = RingOrbitSpan.BandHalfAngleOf(orbit);
			bool moved = RingOrbitSpan.MovedIntoStack(orbit);
			if (moved && !RingOrbitSpan.IsFullBand(orbit))
			{
				result.Fail($"{name} was moved into the stack but has an orbit band of {actualBand:F2} degrees; it should stay a full sphere");
			}
			else if (!moved && Mathf.Abs(actualBand - band) > BandAngleTolerance)
			{
				result.Fail($"{name} has an orbit band of {actualBand:F2} degrees either side of the ring plane; expected {band:F0}");
			}
			if (Subdivisions(orbit) != OrbitStackSubdivisions)
			{
				result.Fail($"{name} has {Subdivisions(orbit)} subdivisions; LAO's have {OrbitStackSubdivisions}");
			}
			// After loading a save, the check runs before world objects' tiles are resolved (see CheckLaoLayers).
			if (Scribe.mode != LoadSaveMode.Inactive)
			{
				continue;
			}
			List<WorldObject> offTiles = Find.WorldObjects.AllWorldObjectsOnLayer(orbit).Where(worldObject => !worldObject.Tile.Valid || worldObject.Tile.tileId >= orbit.TilesCount).ToList();
			if (offTiles.Count > 0)
			{
				result.Fail($"{name} has {offTiles.Count} world objects off its {orbit.TilesCount} tiles (first: {offTiles[0].Label} at tile {offTiles[0].Tile.tileId})");
			}
		}
	}
}
