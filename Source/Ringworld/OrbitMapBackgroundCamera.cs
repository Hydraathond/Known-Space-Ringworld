using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The world background behind a map on any sun-centred orbit layer (RingOrbit). Vanilla puts the world camera at the map's orbit tile, pushed
// out from the sun by the layer's BackgroundWorldCameraOffset (160 for Odyssey's orbit), looking back at the sun. The
// layer sits at a fraction of the ring radius (RingOrbit), so on smaller rings that push would put the camera outside
// the ring; it is capped at halfway from the layer's sphere to the ring, which leaves bigger rings at vanilla's offset.
public static class OrbitMapBackgroundCamera
{
	// The furthest the camera goes from the orbit sphere towards the ring, as a fraction of the gap between them.
	private const float MaxGapFraction = 0.5f;

	public static float Offset(PlanetLayer orbit, RingLayer ring)
	{
		return Mathf.Min(orbit.BackgroundWorldCameraOffset, MaxGapFraction * (ring.Radius - orbit.Radius));
	}

	// Pulls the camera vanilla has just placed in, towards the sun, to the capped offset. Vanilla's parallax shift is
	// sideways to the view, so it is kept.
	public static void Apply(Camera camera)
	{
		if (!SurfaceRing.InRingWorld)
		{
			return;
		}
		Map map = Find.CurrentMap;
		if (map == null || !RingOrbit.IsSunCentred(map.Tile.Layer))
		{
			return;
		}
		// Vanilla leaves the camera where it was when the map has no world position.
		Vector3 mapPosition = map.ParentHolder is MapParent mapParent ? mapParent.WorldCameraPosition : Find.WorldGrid.GetTileCenter(map.Tile);
		if (mapPosition == Vector3.zero)
		{
			return;
		}
		PlanetLayer orbit = map.Tile.Layer;
		RingLayer ring = SurfaceRing.Current;
		Transform transform = camera.transform;
		transform.position += transform.forward * (orbit.BackgroundWorldCameraOffset - Offset(orbit, ring));
	}
}
