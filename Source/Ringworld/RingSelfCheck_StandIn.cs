using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Ringworld;

// Stand-in checks: where the stand-in mesh reaches, against the built layer's tile centres. That it looks like ocean,
// and that clicking or hovering it selects nothing, is checked by eye.
public static partial class RingSelfCheck
{
	// Neighbouring stand-in vertices round the ring are at most this many columns apart, so it leaves no gap.
	private const float MaxStandInStepColumns = 1.5f;

	// Float error in positions measured from vertex and tile-centre angles.
	private const float EdgeToleranceColumns = 0.01f;

	// The stand-in runs under every row's edge tile at each cut by at least this much, so no gap between them shows
	// through to space at an angle, and under the arc by at most the larger amount.
	private const float MinStandInOverlapColumns = 0.25f;

	private const float MaxStandInOverlapColumns = 2f;

	// The stand-in reaches the rim walls' inner faces to within this many hexes.
	private const float RimWallReachTolerance = 0.01f;

	// Below 100% the stand-in runs round the inner surface from the spinward cut to the anti-spinward cut, under every
	// row's edge tile at each cut and not far into the arc, between the inner surface and the outer face, out to the
	// rim walls. At 100% it draws nothing.
	private static void CheckStandIn(RingLayer ring, RingCheckResult result)
	{
		WorldDrawLayer_RingStandIn standIn = ring.WorldDrawLayers.OfType<WorldDrawLayer_RingStandIn>().FirstOrDefault();
		if (standIn == null)
		{
			result.Fail("the ring layer has no stand-in draw layer");
			return;
		}
		BuildIfNeeded(standIn);
		RingGeometry geometry = ring.Geometry;
		List<Vector3> verts = standIn.Vertices.ToList();
		if (geometry.IsFullLoop)
		{
			if (verts.Count > 0)
			{
				result.Fail($"a full loop has no stand-in span, but the stand-in has {verts.Count} vertices");
			}
			return;
		}
		if (verts.Count == 0)
		{
			result.Fail("the stand-in mesh is empty");
			return;
		}
		// Positions round the ring in columns from column 0, 0 .. tilesAround: the live arc's centre is at 0 and the
		// stand-in span sits in the middle of this range. Each row's last tile's spinward edge and first tile's
		// anti-spinward edge are half a column past their centres.
		float columnsPerRadian = geometry.tilesAround / (2f * Mathf.PI);
		float Columns(Vector3 point) => Mathf.Repeat(RingGeometry.AngleOf(point) * columnsPerRadian, geometry.tilesAround);
		float half = 0.5f * geometry.tilesAround;
		float[] spinwardEdge = Enumerable.Repeat(float.MinValue, geometry.tilesAcross).ToArray();
		float[] antiSpinwardEdge = Enumerable.Repeat(float.MaxValue, geometry.tilesAcross).ToArray();
		for (int id = 0; id < ring.TilesCount; id++)
		{
			int row = geometry.RowOf(id);
			float column = Columns(ring.GetTileCenter(id));
			if (column < half)
			{
				spinwardEdge[row] = Mathf.Max(spinwardEdge[row], column + 0.5f);
			}
			else
			{
				antiSpinwardEdge[row] = Mathf.Min(antiSpinwardEdge[row], column - 0.5f);
			}
		}
		List<float> around = verts.Select(Columns).OrderBy(c => c).ToList();
		float start = around[0];
		float end = around[around.Count - 1];
		if (start > spinwardEdge.Min() - MinStandInOverlapColumns + EdgeToleranceColumns || start < spinwardEdge.Max() - MaxStandInOverlapColumns - EdgeToleranceColumns)
		{
			result.Fail($"the stand-in starts at column {start:F2}, the arc's rows end spinward at {spinwardEdge.Min():F2} to {spinwardEdge.Max():F2}");
		}
		if (end < antiSpinwardEdge.Max() + MinStandInOverlapColumns - EdgeToleranceColumns || end > antiSpinwardEdge.Min() + MaxStandInOverlapColumns + EdgeToleranceColumns)
		{
			result.Fail($"the stand-in ends at column {end:F2}, the arc's rows start anti-spinward at {antiSpinwardEdge.Min():F2} to {antiSpinwardEdge.Max():F2}");
		}
		for (int i = 1; i < around.Count; i++)
		{
			if (around[i] - around[i - 1] > MaxStandInStepColumns)
			{
				result.Fail($"the stand-in has a gap from column {around[i - 1]:F2} to {around[i]:F2}");
				break;
			}
		}
		float outerFaceRadius = ring.Radius + WorldDrawLayer_RingThickness.Thickness * geometry.tileSize;
		float minY = float.MaxValue;
		float maxY = float.MinValue;
		foreach (Vector3 vert in verts)
		{
			float radius = RingGeometry.DistanceFromAxis(vert);
			if (radius <= ring.Radius || radius >= outerFaceRadius)
			{
				result.Fail($"a stand-in vertex is {radius:F2} from the axis, outside the inner surface {ring.Radius:F2} to the outer face {outerFaceRadius:F2}");
				break;
			}
			minY = Mathf.Min(minY, vert.y);
			maxY = Mathf.Max(maxY, vert.y);
		}
		float reach = geometry.RimLine - RimWallReachTolerance * geometry.tileSize;
		if (minY > -reach || maxY < reach || minY < -geometry.HalfWidth || maxY > geometry.HalfWidth)
		{
			result.Fail($"the stand-in runs across from y {minY:F2} to {maxY:F2}, the rim walls stand at +-{geometry.RimLine:F2}");
		}
	}
}
