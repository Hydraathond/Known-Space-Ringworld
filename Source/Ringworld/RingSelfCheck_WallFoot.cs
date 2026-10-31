using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Wall-foot checks: the wall foot is impassable land, and it's exactly the land at the rims.
public static partial class RingSelfCheck
{
	// Read from the rim rows' tiles rather than the wall-foot predicate, which is itself "impassable on a rim row".
	// Settling and caravan routing both refuse impassable hilliness, so this covers them too.
	private static void CheckWallFootImpassable(RingLayer ring, RingCheckResult result)
	{
		foreach (int id in ring.Geometry.RimTiles())
		{
			SurfaceTile tile = ring[id];
			if (tile == null || tile.WaterCovered)
			{
				continue;
			}
			if (tile.hilliness != Hilliness.Impassable || tile.PrimaryBiome == null || !tile.PrimaryBiome.generatesNaturally)
			{
				result.FailTile(id);
			}
		}
	}

	// The wall foot ends at the shore: rim-row water is never part of it, nor raised out of the water to join it.
	private static void CheckWallFootNotWater(RingLayer ring, RingCheckResult result)
	{
		foreach (int id in ring.WallFootTiles())
		{
			if (ring[id] is not SurfaceTile tile || tile.WaterCovered)
			{
				result.FailTile(id);
			}
		}
	}

	// The wall-foot step writes only rim-row land, and vanilla terrain makes impassable tiles anywhere, so this tests the
	// selection against the built layer instead: a tile is a wall-foot tile exactly when it's land and the rim cuts off its
	// neighbours on one side (none of them is further across the ring that way).
	private static void CheckWallFootOnlyOnRims(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		if (geometry.tilesAcross < 2)
		{
			return;
		}
		HashSet<int> wallFootTiles = new HashSet<int>(ring.WallFootTiles());
		List<PlanetTile> neighbors = new List<PlanetTile>();
		// Neighbours in the same row are level with the tile; ones in the next row are a row spacing away.
		float otherRowOffset = 0.5f * geometry.rowSpacing;
		for (int id = 0; id < ring.TilesCount; id++)
		{
			Vector3 center = ring.GetTileCenter(id);
			ring.GetTileNeighbors(id, neighbors);
			bool towardsStarboard = neighbors.Any(neighbor => ring.GetTileCenter(neighbor).y > center.y + otherRowOffset);
			bool towardsPort = neighbors.Any(neighbor => ring.GetTileCenter(neighbor).y < center.y - otherRowOffset);
			bool atRim = !towardsStarboard || !towardsPort;
			bool land = ring[id] is SurfaceTile tile && !tile.WaterCovered;
			if (wallFootTiles.Contains(id) != (atRim && land))
			{
				result.FailTile(id);
			}
		}
	}
}
