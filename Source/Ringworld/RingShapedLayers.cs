using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Overlays drawn by position rather than by layer (world lines, tangential quads) go onto the ring-shaped layer they're
// drawn near.
public static class RingShapedLayers
{
	// On a ring world, the ring-shaped layer whose inner surface the layer-local point is near (RingGeometry.NearSurface),
	// the nearest if several are; otherwise null. Called per quad each frame, so it doesn't allocate.
	public static RingLayer Near(Vector3 point)
	{
		WorldGrid grid = Find.World?.grid;
		if (SurfaceRing.Of(grid) == null)
		{
			return null;
		}
		RingLayer nearest = null;
		float nearestGap = float.MaxValue;
		float fromAxis = RingGeometry.DistanceFromAxis(point);
		foreach (PlanetLayer layer in grid.PlanetLayers.Values)
		{
			if (layer is RingLayer ring && RingGeometry.NearSurface(point, ring.Radius))
			{
				float gap = Mathf.Abs(fromAxis - ring.Radius);
				if (gap < nearestGap)
				{
					nearest = ring;
					nearestGap = gap;
				}
			}
		}
		return nearest;
	}
}
