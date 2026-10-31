using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Spill-mountain checks: where world generation put them along the wall foot.
public static partial class RingSelfCheck
{
	// The spacing rule from the spec, about one every 150 hexes along each rim, jittered ±30%.
	private const int SpillMountainMinGap = 105;

	private const int SpillMountainMaxGap = 195;

	// Tile ids exist only on the live arc, so a valid id is a live-arc tile.
	private static void CheckSpillMountainsOnWallFoot(RingLayer ring, RingCheckResult result)
	{
		foreach (int id in ring.SpillMountainTiles)
		{
			if (id < 0 || id >= ring.TilesCount || !ring.IsWallFootTile(id))
			{
				result.FailTile(id);
			}
		}
	}

	// A world generated with the option off must have no spill mountains and skips the rest. Otherwise gaps are counted in
	// hexes along the rim. A gap that crosses water is skipped, as placement skips a spot that falls
	// on water rather than moving it. Any world placed with the rule (one that has spill mountains, or was just generated)
	// also has none missing: no stretch of wall foot longer than the largest gap goes without one.
	private static void CheckSpillMountainSpacing(RingLayer ring, RingCheckResult result)
	{
		if (!ring.SpillMountainsOn)
		{
			// Generated with the option off: there are no spill-mountain tiles at all.
			foreach (int id in ring.SpillMountainTiles)
			{
				result.FailTile(id);
			}
			return;
		}
		RingGeometry geometry = ring.Geometry;
		bool placed = ring.SpillMountainTiles.Count > 0 || ring.GeneratedThisSession;
		foreach (int row in geometry.RimRows)
		{
			int length = geometry.liveTilesAround;
			int TileAt(int index) => geometry.TileId(row, geometry.firstColumn + index);
			bool[] wallFoot = new bool[length];
			bool[] spill = new bool[length];
			for (int index = 0; index < length; index++)
			{
				wallFoot[index] = ring.IsWallFootTile(TileAt(index));
				spill[index] = ring.IsSpillMountain(TileAt(index));
			}
			List<int> spills = Enumerable.Range(0, length).Where(index => spill[index]).ToList();
			for (int i = 0; i < spills.Count; i++)
			{
				if (i == spills.Count - 1 && (!geometry.IsFullLoop || spills.Count < 2))
				{
					break;
				}
				int from = spills[i];
				int gap = i < spills.Count - 1 ? spills[i + 1] - from : spills[0] + length - from;
				bool crossesWater = Enumerable.Range(from + 1, gap - 1).Any(index => !wallFoot[index % length]);
				if (!crossesWater && (gap < SpillMountainMinGap || gap > SpillMountainMaxGap))
				{
					result.FailTile(TileAt(from + gap));
				}
			}
			if (!placed)
			{
				continue;
			}
			// Walk the rim from a spill mountain or water (round the loop on a full loop, along the arc otherwise),
			// counting the wall foot since the last spill mountain.
			int start = geometry.IsFullLoop ? Enumerable.Range(0, length).FirstOrDefault(index => spill[index] || !wallFoot[index]) : -1;
			if (geometry.IsFullLoop && wallFoot[start] && !spill[start])
			{
				// All wall foot and no spill mountain.
				result.FailTile(TileAt(0));
				continue;
			}
			int run = 0;
			for (int step = 1; step <= (geometry.IsFullLoop ? length : length - start - 1); step++)
			{
				int index = start + step;
				run = !wallFoot[index % length] || spill[index % length] ? 0 : run + 1;
				if (run > SpillMountainMaxGap)
				{
					result.FailTile(TileAt(index));
					run = 0;
				}
			}
		}
	}

	// Vanilla's hills layer prints one quad per hilly tile, within 0.37 of a tile of its centre (its widest offset, for
	// small hills), into a submesh per icon material, so a quad less than half a tile from a spill mountain's centre is
	// that tile's icon: it must be vanilla's mountain icon, and there must be one.
	private static void CheckSpillMountainIcons(RingLayer ring, RingCheckResult result)
	{
		WorldDrawLayer_Hills hills = ring.WorldDrawLayers.OfType<WorldDrawLayer_Hills>().FirstOrDefault();
		if (hills == null)
		{
			result.Fail("the ring has no hills layer");
			return;
		}
		BuildIfNeeded(hills);
		float tileSize = ring.Geometry.tileSize;
		List<(int id, Vector3 center)> spills = ring.SpillMountainTiles.Select(id => (id, ring.GetTileCenter(id))).ToList();
		HashSet<int> withMountainIcon = new HashSet<int>();
		foreach (LayerSubMesh subMesh in Patch_WorldDrawLayerBase_FinalizeMesh.SubMeshes(hills))
		{
			bool mountainIcon = subMesh.material == WorldMaterials.Mountains;
			foreach (Vector3 quad in QuadCentres(subMesh.verts))
			{
				foreach ((int id, Vector3 center) in spills)
				{
					if ((quad - center).sqrMagnitude >= 0.25f * tileSize * tileSize)
					{
						continue;
					}
					if (!mountainIcon || !withMountainIcon.Add(id))
					{
						result.FailTile(id);
					}
				}
			}
		}
		foreach ((int id, Vector3 _) in spills.Where(spill => !withMountainIcon.Contains(spill.id)))
		{
			result.FailTile(id);
		}
	}

	private static IEnumerable<Vector3> QuadCentres(List<Vector3> verts)
	{
		for (int first = 0; first + 3 < verts.Count; first += 4)
		{
			yield return (verts[first] + verts[first + 1] + verts[first + 2] + verts[first + 3]) / 4f;
		}
	}
}
