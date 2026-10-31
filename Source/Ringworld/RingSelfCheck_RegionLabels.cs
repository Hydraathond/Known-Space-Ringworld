using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Region checks: what the tile info and the region labels read. How a label looks is checked by eye.
public static partial class RingSelfCheck
{
	private static IEnumerable<WorldFeature> RegionsOn(RingLayer ring)
	{
		return Find.WorldFeatures?.features?.Where(feature => feature.layer == ring) ?? Enumerable.Empty<WorldFeature>();
	}

	// The tile info shows the region's name: every tile in a region must have one to show.
	private static void CheckRegionsNamed(RingLayer ring, RingCheckResult result)
	{
		for (int id = 0; id < ring.TilesCount; id++)
		{
			if (ring[id]?.feature is WorldFeature feature && feature.name.NullOrEmpty())
			{
				result.FailTile(id);
			}
		}
	}

	// A label is drawn above its region's draw centre, so the centre must be on the inner surface between the rims
	// (within half a hex: draw centres are tile centres).
	private static void CheckRegionLabelsOnSurface(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		float tolerance = geometry.tileSize * 0.5f;
		foreach (WorldFeature feature in RegionsOn(ring))
		{
			Vector3 center = feature.drawCenter;
			bool onSurface = Mathf.Abs(RingGeometry.DistanceFromAxis(center) - ring.Radius) <= tolerance;
			bool betweenRims = Mathf.Abs(center.y) <= geometry.HalfWidth;
			if (!onSurface || !betweenRims)
			{
				result.Fail($"'{feature.name}' (id {feature.uniqueID}) has its label at {center}");
			}
		}
	}
}
