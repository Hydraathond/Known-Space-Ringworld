using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Shadow-square checks: where the squares are against the night zones, and that the ring view can't see them. That
// they show and move in the orbit view is checked by eye.
public static partial class RingSelfCheck
{
	// The saved count is in range and is what the geometry's local time uses. Only a world generated this session must
	// match the formula: a loaded world keeps the count it was made with, and saves from before it was saved load as 8.
	private static void CheckShadowSquareCount(RingLayer ring, RingCheckResult result)
	{
		int count = ring.ShadowSquareCount;
		if (count < RingSize.MinShadowSquares || count > RingSize.MaxShadowSquares)
		{
			result.Fail($"the saved count is {count}, outside {RingSize.MinShadowSquares}-{RingSize.MaxShadowSquares}");
		}
		if (ring.Geometry.nightZones != count)
		{
			result.Fail($"the geometry has {ring.Geometry.nightZones} night zones, but the saved count is {count}");
		}
		if (ring.GeneratedThisSession && count != ring.Size.NewWorldShadowSquareCount)
		{
			result.Fail($"the freshly generated ring ({ring.Size}) has {count} shadow squares, expected {ring.Size.NewWorldShadowSquareCount}");
		}
	}

	// There is one shadow square per night zone, and at each sampled moment each night stretch on the midline has its
	// own shadow square over its middle, as vanilla's sun glow has it; on a full loop that takes every square. On a live
	// arc only whole stretches are lined up: one cut short by a cut has its middle out in the stand-in span. Vanilla's
	// local time steps by whole-hour time zones, 15 / nightZones degrees of the ring wide, so a stretch's middle can be
	// half a time zone off the smooth midnight the squares follow, plus a tile where its ends fall between tile centres.
	private static void CheckShadowSquaresOverNightZones(RingLayer ring, RingCheckResult result)
	{
		if (!TryGetShadowSquares(ring, result, out GlobalDrawLayer_RingShadowSquares squares))
		{
			return;
		}
		RingGeometry geometry = ring.Geometry;
		int row = geometry.tilesAcross / 2;
		float tileAngle = 360f / geometry.tilesAround;
		float firstColumnAngle = geometry.CenterUV(row, 0).x / geometry.radius * Mathf.Rad2Deg;
		float tolerance = 0.5f * GenDate.TimeZoneWidth / geometry.nightZones + tileAngle;
		IReadOnlyList<Vector3> centres = squares.Centres;
		if (centres.Count != geometry.nightZones)
		{
			result.Fail($"there are {centres.Count} shadow squares, expected {geometry.nightZones}");
			return;
		}
		for (int sample = 0; sample < NightZoneSamples; sample++)
		{
			int ticks = SampleTicks(sample);
			List<NightStretch> stretches = MidlineNightStretches(ring, ticks);
			if (stretches == null)
			{
				// A live arc shorter than a night zone may lie wholly in one night, with no whole stretch to line up.
				if (geometry.IsFullLoop)
				{
					result.Fail($"at tick {ticks} the midline is all night, so there is no night stretch to line up with");
				}
				continue;
			}
			if (geometry.IsFullLoop && stretches.Count == 0)
			{
				result.Fail($"at tick {ticks} the midline has no night, so there is no night stretch to line up with");
				continue;
			}
			Quaternion rotation = GlobalDrawLayer_RingShadowSquares.RotationAt(ticks, geometry.nightZones);
			float[] squareAngles = centres.Select(centre => RingGeometry.AngleOf(rotation * centre) * Mathf.Rad2Deg).ToArray();
			bool[] used = new bool[squareAngles.Length];
			int usedCount = 0;
			foreach (NightStretch stretch in stretches.Where(stretch => stretch.whole))
			{
				float middle = firstColumnAngle + (stretch.start + 0.5f * (stretch.length - 1)) * tileAngle;
				int nearest = -1;
				float nearestOff = float.MaxValue;
				for (int i = 0; i < squareAngles.Length; i++)
				{
					float off = Mathf.Abs(Mathf.DeltaAngle(squareAngles[i], middle));
					if (off < nearestOff)
					{
						nearest = i;
						nearestOff = off;
					}
				}
				if (nearestOff > tolerance)
				{
					result.Fail($"at tick {ticks} the night stretch with its middle at {middle:F1} degrees is {nearestOff:F1} degrees from the nearest shadow square (tolerance {tolerance:F1})");
				}
				else if (used[nearest])
				{
					result.Fail($"at tick {ticks} two night stretches are under the same shadow square, at {squareAngles[nearest]:F1} degrees");
				}
				else
				{
					used[nearest] = true;
					usedCount++;
				}
			}
			if (geometry.IsFullLoop && usedCount < squareAngles.Length)
			{
				result.Fail($"at tick {ticks} {squareAngles.Length - usedCount} shadow squares are over no night stretch");
			}
		}
	}

	// The ring-view camera never sees a shadow square: it looks straight outward from at least
	// ring radius - its maximum zoom distance from the axis, and every point closer to the axis than it is behind it.
	// The squares are also clear of the sun's glow, inside every orbit layer's sphere, so the orbit-view camera never
	// comes near them, and each one's shadow reaches across the ring from rim to rim.
	private static void CheckShadowSquaresPlacement(RingLayer ring, RingCheckResult result)
	{
		if (!TryGetShadowSquares(ring, result, out GlobalDrawLayer_RingShadowSquares squares))
		{
			return;
		}
		float cameraNearest = ring.Radius - RingCamera.FurthestZoom(ring);
		float glow = GlobalDrawLayer_RingSun.GlowRadius(ring);
		float farthest = 0f;
		float nearest = float.MaxValue;
		float shadowReach = float.MaxValue;
		foreach (IReadOnlyList<Vector3> square in squares.Corners)
		{
			float highest = float.MinValue;
			float lowest = float.MaxValue;
			foreach (Vector3 corner in square)
			{
				float distance = RingGeometry.DistanceFromAxis(corner);
				farthest = Mathf.Max(farthest, distance);
				nearest = Mathf.Min(nearest, distance);
				// Seen from the sun, a corner's shadow lands on the inner surface scaled by ring radius / its distance.
				float shadowY = corner.y * ring.Radius / distance;
				highest = Mathf.Max(highest, shadowY);
				lowest = Mathf.Min(lowest, shadowY);
			}
			shadowReach = Mathf.Min(shadowReach, Mathf.Min(highest, -lowest));
		}
		if (farthest >= cameraNearest)
		{
			result.Fail($"a shadow square reaches {farthest:F1} from the axis; the ring-view camera can come to {cameraNearest:F1}");
		}
		if (nearest <= glow)
		{
			result.Fail($"a shadow square comes to {nearest:F1} from the axis, inside the sun's glow ({glow:F1})");
		}
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			if (farthest >= orbit.Radius)
			{
				result.Fail($"a shadow square reaches {farthest:F1} from the axis, outside the sphere of {RingOrbit.Describe(orbit)} (radius {orbit.Radius:F1})");
			}
		}
		if (shadowReach < ring.Geometry.HalfWidth)
		{
			result.Fail($"a shadow square's shadow reaches {shadowReach:F1} from the midline; the rims are at {ring.Geometry.HalfWidth:F1}");
		}
	}

	// The world's shadow-square draw layer, built for this ring.
	private static bool TryGetShadowSquares(RingLayer ring, RingCheckResult result, out GlobalDrawLayer_RingShadowSquares squares)
	{
		squares = null;
		if (Find.WorldGrid.GlobalLayers.Count == 0)
		{
			// On load the world's global draw layers are only made after the save is read, after this check runs (as
			// for the sun); the check after world generation and the debug action cover it.
			return false;
		}
		foreach (WorldDrawLayerBase layer in Find.WorldGrid.GlobalLayers)
		{
			if (layer is GlobalDrawLayer_RingShadowSquares found)
			{
				squares = found;
				break;
			}
		}
		if (squares == null)
		{
			result.Fail("the world has no shadow-square draw layer");
			return false;
		}
		BuildIfNeeded(squares, stale: !squares.BuiltFor(ring));
		return true;
	}
}
