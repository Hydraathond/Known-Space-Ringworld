using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Below 100% land coverage, the inner surface of the stand-in span (the part of the loop with no tiles): one strip
// from the spinward cut round to the anti-spinward cut, rim wall to rim wall, in vanilla's ungenerated-planet material
// so it looks like ocean. Visual only: it builds no colliders, so clicking or hovering it finds no tile, as on the
// starfield. At 100% it draws nothing.
//
// The arc's cut is stepped: odd rows are shifted half a column spinward, so each row's tiles end half a column apart.
// The strip sits StandInDepth below the inner surface so the tiles hide it where they overlap, and runs CutOverlapColumns
// under every row's edge tile at each cut: where an edge tile ended right where the strip began, the step down between
// them showed through to space as a dark line seen at an angle.
public class WorldDrawLayer_RingStandIn : WorldDrawLayer, IRingNativeDrawLayer
{
	// In hexes, below the inner surface; well short of the outer face (WorldDrawLayer_RingThickness.Thickness).
	private const float StandInDepth = 0.1f;

	// How far the strip runs under the edge tiles of the rows that end nearest the stand-in span at each cut (the other
	// rows' edge tiles cover it another half column). Far more than the step StandInDepth leaves at any view angle.
	private const float CutOverlapColumns = 0.5f;

	// Ring positions per GetSubMesh call. GetSubMesh starts a new submesh only once one holds 40000 verts, so each
	// call adds just a little, keeping every submesh under 16-bit indices.
	private const int PositionsPerChunk = 4096;

	// Every vertex of the stand-in mesh, layer-local.
	public IEnumerable<Vector3> Vertices
	{
		get
		{
			foreach (LayerSubMesh subMesh in subMeshes)
			{
				foreach (Vector3 vert in subMesh.verts)
				{
					yield return vert;
				}
			}
		}
	}

	public override IEnumerable Regenerate()
	{
		foreach (object item in base.Regenerate())
		{
			yield return item;
		}
		if (planetLayer is RingLayer { Geometry: RingGeometry geometry } && !geometry.IsFullLoop)
		{
			Build(geometry);
		}
		// Even with nothing to build, so a regenerate at 100% leaves no mesh behind.
		FinalizeMesh(MeshParts.All);
	}

	private void Build(RingGeometry geometry)
	{
		// In columns from column 0's centre: the even rows' spinward edge, and the odd rows' anti-spinward edge a whole
		// loop later, each moved CutOverlapColumns into the arc.
		float startColumn = geometry.LastColumn + 0.5f - CutOverlapColumns;
		float endColumn = geometry.firstColumn + geometry.tilesAround + CutOverlapColumns;
		int positions = Mathf.CeilToInt(endColumn - startColumn);
		float radius = geometry.radius + StandInDepth * geometry.tileSize;
		float y = geometry.RimLine;
		for (int start = 0; start < positions; start += PositionsPerChunk)
		{
			int end = Mathf.Min(start + PositionsPerChunk, positions);
			LayerSubMesh subMesh = GetSubMesh(WorldMaterials.UngeneratedPlanetParts);
			int first = subMesh.verts.Count;
			for (int i = start; i <= end; i++)
			{
				float column = Mathf.Lerp(startColumn, endColumn, (float)i / positions);
				Vector3 outward = RingGeometry.Outward(2f * Mathf.PI * column / geometry.tilesAround);
				subMesh.verts.Add(new Vector3(outward.x * radius, -y, outward.z * radius));
				subMesh.verts.Add(new Vector3(outward.x * radius, y, outward.z * radius));
			}
			for (int q = 0; q < end - start; q++)
			{
				int a = first + 2 * q;
				Vector3 facing = RingGeometry.InwardNormal(subMesh.verts[a]);
				WorldDrawLayer_RingThickness.AddFacing(subMesh, a, a + 2, a + 1, facing);
				WorldDrawLayer_RingThickness.AddFacing(subMesh, a + 1, a + 2, a + 3, facing);
			}
		}
	}
}
