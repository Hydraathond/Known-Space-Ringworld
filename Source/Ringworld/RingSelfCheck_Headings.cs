using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Heading checks (ADR 0004): world headings on the ring match the ring view, so local maps put coasts, rivers, roads
// and caravan edges on the side they're on in the ring view. Headings are read through the tile overloads and the
// WorldGrid wrappers, as vanilla's callers read them, so a mirror inlined away behind them shows up here. That the local
// map itself comes out right is checked by eye.
public static partial class RingSelfCheck
{
	private const float HeadingTolerance = 0.01f;

	// Every neighbour of some sample tiles: the middle of the live arc (and, on a full loop, beside the seam), on both
	// rim rows, the midline and the row dexter of it (so both an even and an odd, shifted row). Same-row neighbours are West to spinward and East to anti-spinward, the
	// dexter row's are North and the sinister row's South.
	private static void CheckHeadingsMatchRingView(RingLayer ring, RingCheckResult result)
	{
		RingGeometry geometry = ring.Geometry;
		List<int> columns = new List<int> { geometry.firstColumn + geometry.liveTilesAround / 2 };
		if (geometry.IsFullLoop)
		{
			columns.Add(0);
		}
		int midline = geometry.tilesAcross / 2;
		IEnumerable<int> rows = geometry.RimRows.Concat(new[] { midline, midline + 1 }).Where(row => row < geometry.tilesAcross).Distinct();
		List<PlanetTile> neighbors = new List<PlanetTile>();
		string firstMismatch = null;
		foreach (int row in rows)
		{
			foreach (int column in columns)
			{
				int id = geometry.TileId(row, column);
				PlanetTile tile = new PlanetTile(id, ring);
				float angle = DegreesAround(ring, id);
				ring.GetTileNeighbors(id, neighbors);
				foreach (PlanetTile neighbor in neighbors)
				{
					int neighborRow = geometry.RowOf(neighbor.tileId);
					bool spinward = Mathf.DeltaAngle(angle, DegreesAround(ring, neighbor.tileId)) > 0f;
					Rot4 expected = ExpectedRot(row, neighborRow, spinward);
					Rot4 rot = Find.WorldGrid.GetRotFromTo(tile, neighbor);
					string mismatch = null;
					if (rot != expected)
					{
						mismatch = $"rot {rot.ToStringHuman()}, expected {expected.ToStringHuman()}";
					}
					else if (neighborRow == row)
					{
						Direction8Way expected8 = spinward ? Direction8Way.West : Direction8Way.East;
						Direction8Way direction8 = ring.GetDirection8WayFromTo(id, neighbor.tileId);
						if (direction8 != expected8)
						{
							mismatch = $"8-way direction {direction8}, expected {expected8}";
						}
					}
					if (mismatch != null)
					{
						result.FailTile(id);
						firstMismatch ??= $"first: from tile {id} (row {row}, column {column}) to {neighbor.tileId}, {mismatch}";
					}
				}
			}
		}
		if (firstMismatch != null)
		{
			result.Fail(firstMismatch);
		}
	}

	// ADR 0004's table: rows go from sinister (row 0) to dexter.
	private static Rot4 ExpectedRot(int row, int neighborRow, bool spinward)
	{
		if (neighborRow != row)
		{
			return neighborRow > row ? Rot4.North : Rot4.South;
		}
		return spinward ? Rot4.West : Rot4.East;
	}

	// On the ring every heading is 360 - vanilla's; on every other layer (the orbit, a planet) it's vanilla's own.
	// Measured from tile 0 to each of its neighbours on each layer.
	private static void CheckHeadingsMirroredOnlyOnRing(RingLayer ring, RingCheckResult result)
	{
		List<PlanetTile> neighbors = new List<PlanetTile>();
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values)
		{
			if (layer.TilesCount == 0)
			{
				continue;
			}
			bool mirrored = layer is RingLayer;
			Vector3 from = layer.GetTileCenter(0);
			layer.GetTileNeighbors(0, neighbors);
			foreach (PlanetTile neighbor in neighbors)
			{
				float vanilla = VanillaHeading(layer, from, layer.GetTileCenter(neighbor.tileId));
				float expected = mirrored ? Patch_PlanetLayer_GetHeadingFromTo.Mirror(vanilla) : vanilla;
				float heading = layer.GetHeadingFromTo(0, neighbor.tileId);
				if (Mathf.Abs(Mathf.DeltaAngle(heading, expected)) > HeadingTolerance)
				{
					result.Fail($"layer {layer.LayerID}{(mirrored ? " (ring)" : "")}: tile 0 to {neighbor.tileId} heads {heading:F2} degrees, vanilla {vanilla:F2}, expected {expected:F2}");
					break;
				}
			}
		}
	}

	// Vanilla's PlanetLayer.GetHeadingFromTo(Vector3, Vector3), unpatched, to compare against. The one place that may
	// build its own tangent frame despite ADR 0004: it's what the mirror is measured against, never used for a direction.
	private static float VanillaHeading(PlanetLayer layer, Vector3 from, Vector3 to)
	{
		WorldRendererUtility.GetTangentialVectorFacing(from, layer.NorthPolePos, out Vector3 north, out Vector3 right);
		WorldRendererUtility.GetTangentialVectorFacing(from, to, out Vector3 forward, out _);
		float heading = Vector3.Angle(north, forward);
		return Vector3.Dot(forward, right) < 0f ? 360f - heading : heading;
	}
}
