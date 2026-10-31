using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// River and road checks: roads stay out of the wall foot, and rivers only ever start on it.
// Links are stored on both tiles with no flow direction, so a river's direction is read from its network instead:
// vanilla grows every river network out of one coastal water tile, so each network drains to water, and every other
// dead end is a source.
public static partial class RingSelfCheck
{
	private static void CheckRoadsAvoidWallFoot(RingLayer ring, RingCheckResult result)
	{
		for (int id = 0; id < ring.TilesCount; id++)
		{
			if (ring[id] is not SurfaceTile tile || tile.potentialRoads.NullOrEmpty())
			{
				continue;
			}
			if (ring.IsWallFootTile(id))
			{
				result.FailTile(id);
				continue;
			}
			foreach (SurfaceTile.RoadLink link in tile.potentialRoads)
			{
				if (ring.IsWallFootTile(link.neighbor.tileId))
				{
					result.FailTile(id);
				}
			}
		}
	}

	// Every river and road step joins a tile to one of its own neighbours, so with CheckNoLinkAcrossCuts none goes
	// through a cut of the live arc (or skips across the ring anywhere else).
	private static void CheckRiverAndRoadStepsJoinNeighbours(RingLayer ring, RingCheckResult result)
	{
		List<PlanetTile> neighbors = new List<PlanetTile>();
		for (int id = 0; id < ring.TilesCount; id++)
		{
			if (ring[id] is not SurfaceTile tile || (tile.potentialRivers.NullOrEmpty() && tile.potentialRoads.NullOrEmpty()))
			{
				continue;
			}
			ring.GetTileNeighbors(id, neighbors);
			IEnumerable<PlanetTile> riverSteps = tile.potentialRivers?.Select(link => link.neighbor) ?? Enumerable.Empty<PlanetTile>();
			IEnumerable<PlanetTile> roadSteps = tile.potentialRoads?.Select(link => link.neighbor) ?? Enumerable.Empty<PlanetTile>();
			if (riverSteps.Concat(roadSteps).Any(other => other.Layer != ring || !ContainsTile(neighbors, other.tileId)))
			{
				result.FailTile(id);
			}
		}
	}

	private static void CheckRiversNeverWallFootToWallFoot(RingLayer ring, RingCheckResult result)
	{
		foreach (int id in ring.WallFootTiles())
		{
			if (ring[id] is not SurfaceTile tile || tile.potentialRivers.NullOrEmpty())
			{
				continue;
			}
			foreach (SurfaceTile.RiverLink link in tile.potentialRivers)
			{
				if (ring.IsWallFootTile(link.neighbor.tileId))
				{
					result.FailTile(id);
				}
			}
		}
	}

	// With CheckRiversReachWater: a wall-foot tile with one river link and a network that drains to water is a dead end
	// other than the mouth, so it is the river's source. No river flows into the wall foot, ends on it or runs off the ring.
	private static void CheckWallFootHasOneRiverLink(RingLayer ring, RingCheckResult result)
	{
		foreach (int id in ring.WallFootTiles())
		{
			if (ring[id] is SurfaceTile tile && tile.potentialRivers != null && tile.potentialRivers.Count > 1)
			{
				result.FailTile(id);
			}
		}
	}

	private static void CheckRiversReachWater(RingLayer ring, RingCheckResult result)
	{
		HashSet<int> visited = new HashSet<int>();
		Stack<int> open = new Stack<int>();
		int dry = 0;
		for (int start = 0; start < ring.TilesCount; start++)
		{
			if (visited.Contains(start) || ring[start] is not SurfaceTile startTile || startTile.potentialRivers.NullOrEmpty())
			{
				continue;
			}
			bool reachesWater = false;
			visited.Add(start);
			open.Push(start);
			while (open.Count > 0)
			{
				int id = open.Pop();
				SurfaceTile tile = (SurfaceTile)ring[id];
				reachesWater |= tile.WaterCovered;
				foreach (SurfaceTile.RiverLink link in tile.potentialRivers)
				{
					int next = link.neighbor.tileId;
					if (ring[next] is SurfaceTile nextTile && !nextTile.potentialRivers.NullOrEmpty() && visited.Add(next))
					{
						open.Push(next);
					}
				}
			}
			if (!reachesWater)
			{
				dry++;
				result.FailTile(start);
			}
		}
		if (dry > 0)
		{
			result.Fail($"{dry} river networks never reach water (one tile listed per network)");
		}
	}
}
