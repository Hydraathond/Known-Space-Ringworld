using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Geometry checks: they test the layer's built neighbour arrays, using RingGeometry only to address rows and columns.
public static partial class RingSelfCheck
{
	private static void CheckTileCount(RingLayer ring, RingCheckResult result)
	{
		// The world's saved size and land coverage, worked out here rather than with RingSize.LiveTilesAround so the check
		// doesn't trust the code it checks.
		RingSize size = ring.Size;
		int liveTilesAround = Mathf.Clamp(Mathf.RoundToInt(size.tilesAround * ring.LandCoverage), 1, size.tilesAround);
		int expected = liveTilesAround * size.tilesAcross;
		if (ring.TilesCount != expected)
		{
			result.Fail($"{ring.TilesCount} tiles in the layer, ring size {size} at {ring.LandCoverage:P0} land coverage has {liveTilesAround} x {size.tilesAcross} = {expected}");
		}
		RingGeometry geometry = ring.Geometry;
		if (ring.TilesCount != geometry.TileCount)
		{
			result.Fail($"{ring.TilesCount} tiles in the layer, the geometry has {geometry.liveTilesAround} x {geometry.tilesAcross} = {geometry.TileCount}");
		}
		if (ring.Tiles.Count != ring.TilesCount)
		{
			result.Fail($"{ring.Tiles.Count} tile objects for {ring.TilesCount} tile ids");
		}
	}

	// The layer's radius follows from tiles around (so hexes are vanilla-sized), the geometry was built with it,
	// and the whole ring is within the world camera's far clip.
	private static void CheckRadius(RingLayer ring, RingCheckResult result)
	{
		RingSize size = ring.Size;
		if (Mathf.Abs(ring.Radius - size.Radius) > size.Radius * 1E-04f)
		{
			result.Fail($"layer radius {ring.Radius}, expected {size.tilesAround} x {RingSize.VanillaHexSize} / 2pi = {size.Radius}");
		}
		if (ring.Geometry.radius != ring.Radius)
		{
			result.Fail($"geometry built with radius {ring.Geometry.radius}, layer radius {ring.Radius}");
		}
		Camera camera = Find.WorldCamera;
		if (camera != null && ring.Radius >= camera.farClipPlane)
		{
			result.Fail($"radius {ring.Radius} is not below the world camera's far clip {camera.farClipPlane}");
		}
	}

	// Every neighbour is a valid tile id, isn't the tile itself, is listed once, and lists the tile back.
	private static void CheckNeighborsSymmetric(RingLayer ring, RingCheckResult result)
	{
		List<PlanetTile> neighbors = new List<PlanetTile>();
		List<PlanetTile> back = new List<PlanetTile>();
		HashSet<int> listed = new HashSet<int>();
		for (int id = 0; id < ring.TilesCount; id++)
		{
			ring.GetTileNeighbors(id, neighbors);
			listed.Clear();
			foreach (PlanetTile neighbor in neighbors)
			{
				int other = neighbor.tileId;
				if (other < 0 || other >= ring.TilesCount || other == id || !listed.Add(other))
				{
					result.FailTile(id);
					break;
				}
				ring.GetTileNeighbors(other, back);
				if (!ContainsTile(back, id))
				{
					result.FailTile(id);
					break;
				}
			}
		}
	}

	// On a full loop, in every row the last column and column 0 are neighbours. On a live arc, its first and last
	// columns are neither way: the stand-in span lies between them.
	private static void CheckSeamWraps(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		List<PlanetTile> neighbors = new List<PlanetTile>();
		for (int row = 0; row < geometry.tilesAcross; row++)
		{
			int first = geometry.TileId(row, geometry.firstColumn);
			int last = geometry.TileId(row, geometry.LastColumn);
			ring.GetTileNeighbors(last, neighbors);
			bool lastSeesFirst = ContainsTile(neighbors, first);
			ring.GetTileNeighbors(first, neighbors);
			bool firstSeesLast = ContainsTile(neighbors, last);
			if (geometry.IsFullLoop ? !lastSeesFirst || !firstSeesLast : lastSeesFirst || firstSeesLast)
			{
				result.FailTile(last);
			}
		}
	}

	// Tiles in the first and last row have only their in-row and next-row-inward neighbours (4, or 2 on a one-row ring;
	// fewer at the live arc's cuts).
	private static void CheckRimRows(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		int lastRow = geometry.tilesAcross - 1;
		int expectedCount = geometry.tilesAcross > 1 ? 4 : 2;
		List<PlanetTile> neighbors = new List<PlanetTile>();
		foreach (int row in geometry.RimRows)
		{
			int inward = row == 0 ? 1 : lastRow - 1;
			foreach (int column in geometry.LiveColumns())
			{
				int id = geometry.TileId(row, column);
				ring.GetTileNeighbors(id, neighbors);
				bool atCut = !geometry.IsFullLoop && (column == geometry.firstColumn || column == geometry.LastColumn);
				bool ok = atCut ? neighbors.Count < expectedCount : neighbors.Count == expectedCount;
				foreach (PlanetTile neighbor in neighbors)
				{
					int neighborRow = geometry.RowOf(neighbor.tileId);
					if (neighborRow != row && neighborRow != inward)
					{
						ok = false;
					}
				}
				if (!ok)
				{
					result.FailTile(id);
				}
			}
		}
	}

	private static bool ContainsTile(List<PlanetTile> tiles, int tileId)
	{
		foreach (PlanetTile tile in tiles)
		{
			if (tile.tileId == tileId)
			{
				return true;
			}
		}
		return false;
	}
}
