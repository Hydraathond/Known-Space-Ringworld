using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Drawn-quad checks (G3, ticket 34): quads vanilla draws tangent to the planet each frame (targeter, goto marker, world
// arrows, other mods' markers) go onto the inner surface on the ring. Checked through the matrix the patch draws with;
// that they show where they should is checked by eye.
public static partial class RingSelfCheck
{
	// World arrows' size and lift, as GenDraw.DrawArrowPointingAt draws them.
	private const float ArrowSize = 1.2f;

	private const float ArrowAltOffset = 0.05f;

	private const float QuadPositionTolerance = 0.001f;

	private const float QuadFacingToleranceDegrees = 0.1f;

	// Hex neighbours aren't exactly along the tangent frame's axes, so arrows may point a little off.
	private const float ArrowPointingToleranceDegrees = 5f;

	// Each sample tile's neighbours (rim rows, the midline and the shifted row starboard of it, mid-arc and beside the
	// seam), as GenDraw draws an arrow at a neighbour pointing at the tile: rotation heading - 90, heading read through
	// the tile overload. The quad must sit ArrowAltOffset nearer the axis than the inner surface, at the neighbour's angle
	// and height, face the axis, and point (its local +Z, the texture's up, as on a planet) from the neighbour towards
	// the tile. Sizes and rotation copy vanilla's call by hand, so a change to it won't show here. No tile of another
	// layer is near enough the ring surface to be drawn this way.
	private static void CheckDrawnQuadsOnInnerSurface(RingLayer ring, RingCheckResult result)
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
				Vector3 target = ring.GetTileCenter(id);
				ring.GetTileNeighbors(id, neighbors);
				foreach (PlanetTile neighbor in neighbors)
				{
					Vector3 from = ring.GetTileCenter(neighbor.tileId);
					float heading = ring.GetHeadingFromTo(neighbor.tileId, id);
					Matrix4x4 matrix = Patch_WorldRendererUtility_DrawQuadTangentialToPlanet.RingMatrix(from, ArrowSize, ArrowAltOffset, heading - 90f, false, ring.Radius);
					string mismatch = DrawnQuadMismatch(ring, matrix, from, target);
					if (mismatch != null)
					{
						result.FailTile(id);
						firstMismatch ??= $"first: arrow at tile {neighbor.tileId} pointing at {id} (heading {heading:F1}), {mismatch}";
					}
				}
			}
		}
		if (firstMismatch != null)
		{
			result.Fail(firstMismatch);
		}
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values)
		{
			// Ring-shaped layers draw quads on their own inner surface, as they should.
			if (layer is RingLayer)
			{
				continue;
			}
			for (int id = 0; id < layer.TilesCount; id++)
			{
				if (RingGeometry.NearSurface(layer.GetTileCenter(id), ring.Radius))
				{
					result.Fail($"layer {layer.LayerID}: tile {id} is near the ring surface, so quads on it would be drawn on the inner surface");
					break;
				}
			}
		}
	}

	private static string DrawnQuadMismatch(RingLayer ring, Matrix4x4 matrix, Vector3 from, Vector3 target)
	{
		Vector3 position = matrix.GetColumn(3);
		Vector3 inward = RingGeometry.InwardNormal(from);
		float distance = RingGeometry.DistanceFromAxis(position);
		if (Mathf.Abs(distance - (ring.Radius - ArrowAltOffset)) > QuadPositionTolerance)
		{
			return $"{distance:F3} from the axis, expected {ring.Radius - ArrowAltOffset:F3}";
		}
		if (Vector3.Angle(position - from, inward) > QuadFacingToleranceDegrees || Mathf.Abs(position.y - from.y) > QuadPositionTolerance)
		{
			return $"at {position}, not over the tile centre {from}";
		}
		if (Vector3.Angle(matrix.MultiplyVector(Vector3.up), inward) > QuadFacingToleranceDegrees)
		{
			return "doesn't face the axis";
		}
		Vector3 toTarget = Vector3.ProjectOnPlane(target - from, inward).normalized;
		float pointingError = Vector3.Angle(matrix.MultiplyVector(Vector3.forward), toTarget);
		return pointingError > ArrowPointingToleranceDegrees ? $"points {pointingError:F1} degrees off the direction to the tile" : null;
	}
}
