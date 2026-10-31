using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace Ringworld;

// Land-coverage checks on the built layer's tile centres and neighbour links. The tile count is checked with the
// geometry; that the page's rows and the large-ring warning use live tiles is checked by eye.
public static partial class RingSelfCheck
{
	// A neighbour link is at most one column long round the ring (half a column between rows); this leaves room for
	// float error while staying far shorter than any stand-in span.
	private const float MaxLinkColumns = 1.5f;

	// Below 100%, each row's tiles run from one cut to the other with their middle on 0 degrees, give or take a column,
	// and cover land coverage x 360 degrees, give or take a column. A full loop has no ends to check.
	private static void CheckLiveArcCentred(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		if (geometry.IsFullLoop)
		{
			return;
		}
		float columnAngle = 360f / geometry.tilesAround;
		float expectedSpan = ring.LandCoverage * 360f;
		for (int row = 0; row < geometry.tilesAcross; row++)
		{
			float min = float.MaxValue;
			float max = float.MinValue;
			for (int index = 0; index < geometry.liveTilesAround; index++)
			{
				float angle = DegreesAround(ring, row * geometry.liveTilesAround + index);
				min = Mathf.Min(min, angle);
				max = Mathf.Max(max, angle);
			}
			float middle = 0.5f * (min + max);
			float span = max - min + columnAngle;
			if (Mathf.Abs(middle) > columnAngle || Mathf.Abs(span - expectedSpan) > columnAngle)
			{
				result.Fail($"row {row} runs from {min:F2} to {max:F2} degrees (middle {middle:F2}, span {span:F2}), expected centred on 0 spanning {expectedSpan:F2}");
			}
		}
	}

	// Measured on tile centres, so it holds across the seam of a full loop too.
	private static void CheckNoLinkAcrossCuts(RingLayer ring, RingCheckResult result)
	{
		float maxLinkAngle = MaxLinkColumns * 360f / ring.Geometry.tilesAround;
		List<PlanetTile> neighbors = new List<PlanetTile>();
		for (int id = 0; id < ring.TilesCount; id++)
		{
			float angle = DegreesAround(ring, id);
			ring.GetTileNeighbors(id, neighbors);
			foreach (PlanetTile neighbor in neighbors)
			{
				float neighborAngle = DegreesAround(ring, neighbor.tileId);
				if (Mathf.Abs(Mathf.DeltaAngle(angle, neighborAngle)) > maxLinkAngle)
				{
					result.FailTile(id);
					break;
				}
			}
		}
	}

	// Land fades to ocean before each cut: every tile within CutOceanColumns of a cut is ocean (not a lake: the ocean
	// there is big enough that vanilla's lake step leaves it).
	private static void CheckCutsEndInOcean(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		if (geometry.IsFullLoop)
		{
			return;
		}
		// Tile centres are a whole or half column from a cut; this takes in the ones at CutOceanColumns despite float error.
		float oceanReach = RingGeometry.CutOceanColumns + 0.25f;
		for (int id = 0; id < ring.TilesCount; id++)
		{
			if (geometry.ColumnsFromCut(ring.GetTileCenter(id)) > oceanReach)
			{
				continue;
			}
			SurfaceTile tile = ring[id];
			if (tile == null || !tile.WaterCovered || tile.PrimaryBiome == BiomeDefOf.Lake)
			{
				result.FailTile(id);
			}
		}
	}

	// The built tile centre's angle round the ring, in degrees, -180..180.
	private static float DegreesAround(RingLayer ring, int tileId)
	{
		return RingGeometry.AngleOf(ring.GetTileCenter(tileId)) * Mathf.Rad2Deg;
	}
}
