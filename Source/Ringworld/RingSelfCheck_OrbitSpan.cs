using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Orbit spans and orbit bands (RingOrbitSpan), checked on every orbit layer's built tile centres. That tile ids stay the same across a
// save and load, and that targeting in the orbit view finds nothing outside the arc, are checked by eye.
public static partial class RingSelfCheck
{
	// Tiles this close to the axis, in tile sizes, have no meaningful angle round the ring.
	private const float OnAxisTiles = 0.1f;

	private static readonly AccessTools.FieldRef<PlanetLayer, int> Subdivisions = AccessTools.FieldRefAccess<PlanetLayer, int>("subdivisions");

	// Below 100% land coverage, every orbit layer spans land coverage x 360 degrees centred on 0, and no tile lies
	// outside it; at 100% a layer without an orbit band is a full sphere with vanilla's tile count. No tile of a layer
	// with an orbit band lies outside it, by the angle from the ring plane measured here. For the span, tiles at the
	// orbit's poles, on the axis, are kept or dropped by whatever angle float error gives them, so they aren't checked.
	private static void CheckOrbitSpansLiveArc(RingLayer ring, RingCheckResult result)
	{
		if (Find.WorldGrid == null)
		{
			return;
		}
		float expected = RingOrbitSpan.HalfAngleForCoverage(ring.LandCoverage);
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			string name = RingOrbit.Describe(orbit);
			float halfAngle = RingOrbitSpan.HalfAngleOf(orbit);
			if (Mathf.Abs(halfAngle - expected) > 0.01f)
			{
				result.Fail($"{name} spans {halfAngle:F2} degrees either side of 0; land coverage {ring.LandCoverage:P0} needs {expected:F2}");
			}
			if (RingOrbitSpan.IsFullSphere(orbit))
			{
				int full = FullSphereTileCount(Subdivisions(orbit));
				if (orbit.TilesCount != full)
				{
					result.Fail($"{name} is a full sphere but has {orbit.TilesCount} tiles; a full sphere of its subdivisions has {full}");
				}
				continue;
			}
			int outside = 0;
			int firstOutside = -1;
			int outsideBand = 0;
			int firstOutsideBand = -1;
			float band = RingOrbitSpan.BandHalfAngleOf(orbit);
			float onAxis = OnAxisTiles * orbit.AverageTileSize;
			for (int id = 0; id < orbit.TilesCount; id++)
			{
				Vector3 center = orbit.GetTileCenter(id);
				if (Mathf.Abs(Mathf.Asin(center.y / center.magnitude) * Mathf.Rad2Deg) > band + 0.01f)
				{
					outsideBand++;
					firstOutsideBand = firstOutsideBand < 0 ? id : firstOutsideBand;
				}
				if (RingGeometry.DistanceFromAxis(center) < onAxis)
				{
					continue;
				}
				if (Mathf.Abs(RingGeometry.AngleOf(center) * Mathf.Rad2Deg) > halfAngle + 0.01f)
				{
					outside++;
					firstOutside = firstOutside < 0 ? id : firstOutside;
				}
			}
			if (outside > 0)
			{
				result.Fail($"{name} has {outside} tiles outside its span of {halfAngle:F2} degrees either side of 0 (first: {firstOutside})");
			}
			if (outsideBand > 0)
			{
				result.Fail($"{name} has {outsideBand} tiles outside its orbit band of {band:F2} degrees either side of the ring plane (first: {firstOutsideBand})");
			}
			if (orbit.TilesCount == 0)
			{
				result.Fail($"{name} has no tiles");
			}
		}
	}

	// A full icosphere: vanilla's subdivision adds a vertex per triangle and triples the triangles on each pass,
	// and makes one tile per vertex, so it has 12 + 10 x (3^s - 1) tiles (2432 for Odyssey's 5, as in a save).
	private static int FullSphereTileCount(int subdivisions)
	{
		return 12 + 10 * ((int)Mathf.Pow(3f, subdivisions) - 1);
	}
}
