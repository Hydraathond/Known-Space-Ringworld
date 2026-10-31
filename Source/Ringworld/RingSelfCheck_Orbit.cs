using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Orbit checks, on every sun-centred orbit layer of a ring world (RingOrbit.Layers): where each sits, and that the orbit view from
// each can show the whole ring. How the bigger orbit tiles and travel arcs look is checked by eye. Without Odyssey there
// is no orbit layer, and these checks have nothing to check.
public static partial class RingSelfCheck
{
	// Of a layer's expected radius fraction: "about half the ring radius", from the spec, for Odyssey's orbit.
	private const float OrbitRadiusTolerance = 0.1f;

	// One line per orbit layer, so the log shows which layers the orbit checks covered.
	private static void LogOrbitLayers(RingLayer ring)
	{
		List<PlanetLayer> orbits = RingOrbit.Layers(Find.WorldGrid).ToList();
		if (orbits.Count == 0)
		{
			Log.Message("[Ringworld]   Orbit layers: none.");
			return;
		}
		foreach (PlanetLayer orbit in orbits)
		{
			string span = RingOrbitSpan.IsFullSpan(orbit) ? "full loop" : $"spans {RingOrbitSpan.HalfAngleOf(orbit):F2} degrees either side of 0";
			string band = RingOrbitSpan.IsFullBand(orbit) ? "pole to pole" : $"orbit band {RingOrbitSpan.BandHalfAngleOf(orbit):F2} degrees either side of the ring plane";
			string zoom = $"zooms in to {orbit.zoomInToLayer?.Def.defName ?? "nothing"}, out to {orbit.zoomOutToLayer?.Def.defName ?? "nothing"}";
			string fuel = string.Join(", ", Find.WorldGrid.PlanetLayers.Values.Where(other => other.HasConnectionFromTo(orbit)).Select(other => $"from {other.Def.defName} {other.GetConnectionFromTo(orbit).fuelCost:F0}"));
			// World objects' layers aren't resolved yet while a save loads.
			string worldObjects = Scribe.mode == LoadSaveMode.Inactive && Find.WorldObjects != null ? $"{Find.WorldObjects.AllWorldObjectsOnLayer(orbit).Count} world objects" : "world objects not counted while loading";
			string moved = RingOrbitSpan.MovedIntoStack(orbit) ? ", moved into the orbit stack" : "";
			Log.Message($"[Ringworld]   {RingOrbit.Describe(orbit)}: radius {orbit.Radius:F1} ({orbit.Radius / ring.Radius:F2} x the ring){moved}, {span}, {band}, {orbit.TilesCount} tiles, {worldObjects}, {zoom}, fuel {fuel}.");
		}
	}

	// Odyssey's orbit is centred on the sun. Outside LAO's orbit stack, each sun-centred layer is at about half the ring
	// radius, and Odyssey's orbit layers keep vanilla's subdivisions. While LAO's integration is active on the world,
	// every orbit layer is in the stack, checked by CheckOrbitStack instead; without it, a layer with an orbit band was
	// made with the stack and is left alone too. Without LAO there's at most one sun-centred orbit layer (none without
	// Odyssey); vanilla's dev "add test planet layer" adds a second, so that FAIL is expected after using it. A layer that spans only the live arc is fine here;
	// CheckOrbitSpansLiveArc checks its tiles.
	private static void CheckOrbitLayer(RingLayer ring, RingCheckResult result)
	{
		PlanetLayer odysseyOrbit = Find.WorldGrid?.Orbit;
		if (odysseyOrbit != null && !RingOrbit.IsSunCentred(odysseyOrbit))
		{
			result.Fail($"{RingOrbit.Describe(odysseyOrbit)} is centred at {odysseyOrbit.Origin}, the sun (the ring's centre) at {ring.Origin}");
		}
		if (LaoIntegration.ActiveOn(Find.WorldGrid))
		{
			return;
		}
		int count = RingOrbit.Layers(Find.WorldGrid).Count();
		if (!LaoIntegration.Active && count > 1)
		{
			result.Fail($"without LAO the world has {count} sun-centred orbit layers; it should have at most one");
		}
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			if (!RingOrbitSpan.IsFullBand(orbit))
			{
				continue;
			}
			string name = RingOrbit.Describe(orbit);
			const float expected = 0.5f;
			float ratio = orbit.Radius / ring.Radius;
			if (Mathf.Abs(ratio - expected) > expected * OrbitRadiusTolerance)
			{
				result.Fail($"{name} has radius {orbit.Radius:F1}, {ratio:F2} x the ring radius {ring.Radius:F1}; expected about {expected:F2} x");
			}
			int subdivisions = PlanetLayerSettingsDefOf.Orbit.settings.subdivisions;
			if (orbit.Def == PlanetLayerDefOf.Orbit && Subdivisions(orbit) != subdivisions)
			{
				result.Fail($"{name} has {Subdivisions(orbit)} subdivisions; vanilla's orbit has {subdivisions}");
			}
		}
	}

	// From every orbit layer: fully zoomed out, the ring (outer face and rim walls included) fits in the orbit view's
	// vertical field of view, seen from any direction, and its far side is inside the far clip. Fully zoomed in, the
	// camera is still outside the layer's sphere.
	private static void CheckOrbitViewFitsRing(RingLayer ring, RingCheckResult result)
	{
		float hex = ring.Geometry.tileSize;
		float ringBounds = new Vector2(ring.Radius + hex, ring.Geometry.HalfWidth).magnitude;
		float halfFov = 0.5f * OrbitViewCamera.FieldOfView;
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			string name = RingOrbit.Describe(orbit);
			float farthest = OrbitViewCamera.DistanceFromSun(orbit, ring, 1f);
			float ringAngle = Mathf.Asin(Mathf.Min(1f, ringBounds / farthest)) * Mathf.Rad2Deg;
			if (ringAngle > halfFov)
			{
				result.Fail($"from {name} fully zoomed out, the camera is {farthest:F0} from the sun and the ring spans {ringAngle:F1} degrees from the centre of the view; half the field of view is {halfFov:F1}");
			}
			float farClip = OrbitViewCamera.FarClipPlane(orbit, ring, 1f);
			if (farthest + ringBounds > farClip)
			{
				result.Fail($"from {name} fully zoomed out, the ring's far side is {farthest + ringBounds:F0} from the camera, beyond the far clip {farClip:F0}");
			}
			float nearest = OrbitViewCamera.DistanceFromSun(orbit, ring, 0f);
			if (nearest <= orbit.Radius)
			{
				result.Fail($"from {name} fully zoomed in, the camera is {nearest:F0} from the sun, inside the layer's sphere (radius {orbit.Radius:F0})");
			}
		}
	}

	// On a map on any orbit layer, the world background is seen from inside the ring: the background camera, pushed out
	// from the map's orbit tile, stays at least a hex short of the ring's inner surface, and still outside the layer's
	// sphere.
	private static void CheckOrbitMapBackgroundInsideRing(RingLayer ring, RingCheckResult result)
	{
		float limit = ring.Radius - ring.Geometry.tileSize;
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			string name = RingOrbit.Describe(orbit);
			float offset = OrbitMapBackgroundCamera.Offset(orbit, ring);
			float distance = orbit.Radius + offset;
			if (distance > limit)
			{
				result.Fail($"on a map on {name}, the background camera is {distance:F0} from the sun, outside the ring (inner surface at {ring.Radius:F0})");
			}
			if (offset <= 0f)
			{
				result.Fail($"on a map on {name}, the background camera is {offset:F0} out from the layer's sphere; it should be outside it");
			}
		}
	}
}
