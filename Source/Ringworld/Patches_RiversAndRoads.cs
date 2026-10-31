using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Rivers and roads respect the wall foot on ring layers. Planet layers are untouched.

// Roads never enter the wall foot. Road pathing already avoids impassable tiles; this also catches road endpoints and any
// other road source, so a road step touching the wall foot is never written.
[HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.OverlayRoad))]
public static class Patch_WorldGrid_OverlayRoad
{
	public static bool Prefix(PlanetTile __0, PlanetTile __1)
	{
		return !RiversAndRoads.IsWallFoot(__0) && !RiversAndRoads.IsWallFoot(__1);
	}
}

// Vanilla grows rivers upstream from the coast: ExtendRiver(tile) links the tile to the next tile upstream and recurses.
// A river that reaches the wall foot stops there instead, so the wall-foot tile is its source and it flows inward from it.
// Stopping here (rather than only refusing the link) also keeps the river from carrying on, detached, past the wall foot.
[HarmonyPatch(typeof(WorldGenStep_Rivers), "ExtendRiver")]
public static class Patch_WorldGenStep_Rivers_ExtendRiver
{
	public static bool Prefix(PlanetTile __2)
	{
		return !RiversAndRoads.IsWallFoot(__2);
	}
}

// A wall-foot tile gets at most one river link, never to another wall-foot tile, so it can only be a river's source.
// Besides ExtendRiver, vanilla's CreateRivers links a coastal tile to every neighbour with enough flow, which could
// otherwise give a wall-foot tile a second river.
[HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.OverlayRiver))]
public static class Patch_WorldGrid_OverlayRiver
{
	public static bool Prefix(PlanetTile __0, PlanetTile __1)
	{
		bool fromWallFoot = RiversAndRoads.IsWallFoot(__0);
		bool toWallFoot = RiversAndRoads.IsWallFoot(__1);
		if (fromWallFoot && toWallFoot)
		{
			return false;
		}
		return (!fromWallFoot || RiversAndRoads.HasNoRiverExcept(__0, __1)) && (!toWallFoot || RiversAndRoads.HasNoRiverExcept(__1, __0));
	}
}

// Vanilla draws a river or road edge by the neighbour's index in the tile's neighbour list, taking neighbour k to be
// across the edge between corners k and k + 1. Rim tiles have fewer than 6 neighbours, so their list is shorter and
// the indices shift: segments leave through the wrong edge (the broken stubs along a rim, and rivers running off it).
// On ring layers, the neighbour list read here is padded so each neighbour sits at its edge's index.
[HarmonyPatch(typeof(WorldDrawLayer_Paths), nameof(WorldDrawLayer_Paths.GeneratePaths))]
public static class Patch_WorldDrawLayer_Paths_GeneratePaths
{
	private static readonly MethodInfo GetTileNeighbors = AccessTools.Method(typeof(PlanetLayer), nameof(PlanetLayer.GetTileNeighbors), new[] { typeof(PlanetTile), typeof(List<PlanetTile>) });

	private static readonly MethodInfo GetEdgeNeighbors = AccessTools.Method(typeof(RiversAndRoads), nameof(RiversAndRoads.GetEdgeNeighbors));

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(GetTileNeighbors))
			{
				replaced++;
				yield return new CodeInstruction(OpCodes.Call, GetEdgeNeighbors).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
				continue;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			Log.Error($"[Ringworld] Expected one neighbour lookup in WorldDrawLayer_Paths.GeneratePaths, patched {replaced}; river and road edges along the rims may draw wrongly.");
		}
	}
}

public static class RiversAndRoads
{
	public static bool IsWallFoot(PlanetTile tile)
	{
		return tile.Valid && tile.Layer is RingLayer { IsSurfaceRing: true } ring && ring.IsWallFootTile(tile.tileId);
	}

	// The tile has no river link, or only the one to other (which may still be upgraded).
	public static bool HasNoRiverExcept(PlanetTile tile, PlanetTile other)
	{
		List<SurfaceTile.RiverLink> rivers = (tile.Tile as SurfaceTile)?.potentialRivers;
		return rivers == null || rivers.TrueForAll(link => link.neighbor == other);
	}

	// Like PlanetLayer.GetTileNeighbors, but on a ring tile with fewer than 6 neighbours the list has one entry per edge,
	// with PlanetTile.Invalid where the rim cuts a neighbour off. The neighbour across an edge is the one that shares
	// both of its corners; ring corners are bit-identical between tiles (see RingGeometry).
	public static void GetEdgeNeighbors(PlanetLayer layer, PlanetTile tile, List<PlanetTile> neighbors)
	{
		layer.GetTileNeighbors(tile, neighbors);
		if (layer is not RingLayer || neighbors.Count >= 6)
		{
			return;
		}
		List<PlanetTile> present = new List<PlanetTile>(neighbors);
		List<Vector3> corners = new List<Vector3>();
		List<Vector3> neighborCorners = new List<Vector3>();
		layer.GetTileVertices(tile, corners);
		neighbors.Clear();
		for (int k = 0; k < corners.Count; k++)
		{
			Vector3 a = corners[k];
			Vector3 b = corners[(k + 1) % corners.Count];
			PlanetTile across = PlanetTile.Invalid;
			foreach (PlanetTile neighbor in present)
			{
				layer.GetTileVertices(neighbor, neighborCorners);
				if (neighborCorners.Contains(a) && neighborCorners.Contains(b))
				{
					across = neighbor;
					break;
				}
			}
			neighbors.Add(across);
		}
	}
}
