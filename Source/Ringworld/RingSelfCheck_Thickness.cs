using System.Linq;
using UnityEngine;
using Verse;

namespace Ringworld;

// Thickness checks: where the ring's thickness mesh reaches, for the world's ring size. How it looks, and that clicks
// go through it to the tiles, is checked by eye.
public static partial class RingSelfCheck
{
	// The ring is about 1 hex thick below the inner surface, and at each rim a rim-wall face stands at the rim and rises
	// about 2 hexes toward the axis. "About" is half a hex either way. The hex counts are the spec's, written out here
	// rather than read from the draw layer, so the check can disagree with it.
	private static void CheckThickness(RingLayer ring, RingCheckResult result)
	{
		WorldDrawLayer_RingThickness thickness = ring.WorldDrawLayers.OfType<WorldDrawLayer_RingThickness>().FirstOrDefault();
		if (thickness == null)
		{
			result.Fail("the ring layer has no thickness draw layer");
			return;
		}
		BuildIfNeeded(thickness);
		float hex = ring.Geometry.tileSize;
		float maxRadius = float.MinValue;
		// Per rim (0: +y, 1: -y): the smallest distance from the axis, and the smallest |y| of the vertices standing
		// more than a hex above the inner surface (the rim-wall face's top).
		float[] minRadius = { float.MaxValue, float.MaxValue };
		float[] wallMinY = { float.MaxValue, float.MaxValue };
		int count = 0;
		foreach (Vector3 vert in thickness.Vertices)
		{
			float radius = RingGeometry.DistanceFromAxis(vert);
			int rim = vert.y >= 0f ? 0 : 1;
			maxRadius = Mathf.Max(maxRadius, radius);
			minRadius[rim] = Mathf.Min(minRadius[rim], radius);
			if (radius < ring.Radius - hex)
			{
				wallMinY[rim] = Mathf.Min(wallMinY[rim], Mathf.Abs(vert.y));
			}
			count++;
		}
		if (count == 0)
		{
			result.Fail("the thickness mesh is empty");
			return;
		}
		CheckAbout(result, "outer face depth below the inner surface", maxRadius - ring.Radius, hex, 1f);
		string[] rimNames = { "+y rim", "-y rim" };
		for (int rim = 0; rim < 2; rim++)
		{
			CheckAbout(result, $"rim-wall face height at the {rimNames[rim]}", ring.Radius - minRadius[rim], hex, 2f);
			if (Mathf.Abs(wallMinY[rim] - ring.Geometry.HalfWidth) > 0.5f * hex)
			{
				result.Fail($"rim-wall face at the {rimNames[rim]} stands at |y| {wallMinY[rim]:F2}, rim at {ring.Geometry.HalfWidth:F2}");
			}
		}
	}

	private static void CheckAbout(RingCheckResult result, string what, float actual, float hex, float hexes)
	{
		if (Mathf.Abs(actual - hexes * hex) > 0.5f * hex)
		{
			result.Fail($"{what} is {actual:F2}, expected about {hexes} x hex {hex:F2}");
		}
	}
}
