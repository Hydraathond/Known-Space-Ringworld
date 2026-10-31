using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The ring drawn solid: an outer face Thickness below the inner surface, and a rim-wall face at each rim rising
// WallHeight toward the axis, WallThickness thick, with every side closed. Visual only: it builds no colliders, so
// clicks go through it to the terrain, and it holds no tiles. Without it the orbit view shows only the far half of the
// ring (the terrain faces the axis), and the ring view shows starfield past the rims.
//
// Cross-section at one angle, distance from the axis r against y (the +y rim; the -y rim mirrors it):
//
//   r = R - WallHeight   +---+  wall top, facing the axis
//                        |   |
//   r = R      terrain --+   |  inner wall face at y = RimLine, facing the midline; it runs on down to the outer face,
//                        |   |  so no gap shows under the hex tips of the rim row (they end inside the wall)
//   r = R + Thickness  --+---+  outer face, facing away from the axis; the wall's outer side faces +y
//
// Drawn with the ring structure shader (RingStructure): hull on the outer face, rock on the rim walls' inner faces, scrith on
// their tops and outer sides, each face in its own shade so the ring reads as solid. The night shadow darkens the rim walls'
// inner faces and tops with the ground below them; the outer face and the walls' outer sides stay unshadowed. Each strip
// is built one panel segment (HexesPerRepeat positions) at a time, each segment with its own variant's material. On a
// surface of Wang tiles (RingStructure) each strip is cut into squares instead, about HexesPerRepeat hexes round the loop
// and across, each with the tile that matches its neighbours' edges.
public class WorldDrawLayer_RingThickness : WorldDrawLayer, IRingNativeDrawLayer
{
	// In hexes (one hex = the ring's tile size).
	public const float Thickness = 1f;

	public const float WallHeight = 2f;

	public const float WallThickness = 1f;

	// Each face's shade, multiplying its texture.
	private static readonly Color32 OuterFaceShade = new Color32(200, 200, 200, 255);

	private static readonly Color32 WallSideShade = new Color32(220, 220, 220, 255);

	private static readonly Color32 WallTopShade = new Color32(255, 255, 255, 255);

	private static readonly Color32 WallInnerShade = new Color32(235, 235, 235, 255);

	// One flat strip around the ring: its two long sides in (distance from the axis, y), and the way it faces.
	private readonly struct Strip
	{
		public readonly Vector2 from;

		public readonly Vector2 to;

		// In (radially outward, y).
		public readonly Vector2 facing;

		public readonly RingStructureSurface surface;

		public readonly Color32 shade;

		public readonly bool nightShadow;

		public Strip(Vector2 from, Vector2 to, Vector2 facing, RingStructureSurface surface, Color32 shade, bool nightShadow)
		{
			this.from = from;
			this.to = to;
			this.facing = facing;
			this.surface = surface;
			this.shade = shade;
			this.nightShadow = nightShadow;
		}
	}

	// For the self-check: each submesh holds one surface's material.
	public IEnumerable<LayerSubMesh> SubMeshes => subMeshes;

	// For the self-check: every square built from Wang tiles.
	public IReadOnlyList<WangSquare> WangSquares => wangSquares;

	private readonly List<WangSquare> wangSquares = new List<WangSquare>();

	// Every vertex of the thickness mesh, layer-local.
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
		if (planetLayer is RingLayer { Geometry: RingGeometry geometry })
		{
			Build(geometry);
			FinalizeMesh(MeshParts.All);
		}
	}

	// One GetSubMesh call per segment of each strip: GetSubMesh starts a new submesh only once one holds 40000 verts, so
	// each call adds just a little, keeping every submesh under 16-bit indices.
	private void Build(RingGeometry geometry)
	{
		int positions = geometry.tilesAround;
		int segments = RingStructure.SegmentsAround(positions);
		wangSquares.Clear();
		List<Strip> strips = StripsOf(geometry);
		for (int face = 0; face < strips.Count; face++)
		{
			Strip strip = strips[face];
			// Wang surfaces: rows of squares across the strip, the last one cut off.
			int rows = RingStructure.IsWang(strip.surface) ? Mathf.Max(1, Mathf.CeilToInt(StripLength(strip) / (RingStructure.HexesPerRepeat * geometry.tileSize) - 0.001f)) : 0;
			for (int segment = 0; segment < segments; segment++)
			{
				int start = SegmentStart(segment, segments, positions);
				int end = SegmentStart(segment + 1, segments, positions);
				if (rows == 0)
				{
					int variant = RingStructure.VariantAt(strip.surface, start, positions);
					LayerSubMesh subMesh = GetSubMesh(RingStructure.MaterialFor(strip.surface, variant, strip.nightShadow));
					AddPanel(subMesh, strip, geometry, start, end, segment, 0f, 1f);
					continue;
				}
				float rowShare = RingStructure.HexesPerRepeat * geometry.tileSize / StripLength(strip);
				for (int row = 0; row < rows; row++)
				{
					int tile = RingStructure.WangTileAt(strip.surface, face, segment, row, segments, wraps: true);
					LayerSubMesh subMesh = GetSubMesh(RingStructure.MaterialFor(strip.surface, tile, strip.nightShadow));
					AddPanel(subMesh, strip, geometry, start, end, 0, row * rowShare, Mathf.Min(1f, (row + 1) * rowShare));
					wangSquares.Add(new WangSquare(face, segment, row, segments, wraps: true, subMesh.material));
				}
			}
		}
	}

	private static float StripLength(Strip strip)
	{
		return (strip.to - strip.from).magnitude;
	}

	// The first ring position of a panel segment; segment `segments` starts at `positions`, closing the loop.
	private static int SegmentStart(int segment, int segments, int positions)
	{
		return (int)((long)segment * positions / segments);
	}

	// Distance from the midline of each rim wall's outer side: the widest the ring is drawn.
	public static float WallSideY(RingGeometry geometry)
	{
		return geometry.RimLine + WallThickness * geometry.tileSize;
	}

	private static List<Strip> StripsOf(RingGeometry geometry)
	{
		float hex = geometry.tileSize;
		float radius = geometry.radius;
		float wallTopRadius = radius - WallHeight * hex;
		float outerFaceRadius = radius + Thickness * hex;
		float wallFaceY = geometry.RimLine;
		float wallSideY = WallSideY(geometry);
		List<Strip> strips = new List<Strip>
		{
			new Strip(new Vector2(outerFaceRadius, -wallSideY), new Vector2(outerFaceRadius, wallSideY), new Vector2(1f, 0f), RingStructureSurface.Hull, OuterFaceShade, nightShadow: false),
		};
		foreach (float sign in new[] { 1f, -1f })
		{
			strips.Add(new Strip(new Vector2(outerFaceRadius, sign * wallFaceY), new Vector2(wallTopRadius, sign * wallFaceY), new Vector2(0f, -sign), RingStructureSurface.Rock, WallInnerShade, nightShadow: true));
			strips.Add(new Strip(new Vector2(wallTopRadius, sign * wallFaceY), new Vector2(wallTopRadius, sign * wallSideY), new Vector2(-1f, 0f), RingStructureSurface.Scrith, WallTopShade, nightShadow: true));
			strips.Add(new Strip(new Vector2(wallTopRadius, sign * wallSideY), new Vector2(outerFaceRadius, sign * wallSideY), new Vector2(0f, sign), RingStructureSurface.Scrith, WallSideShade, nightShadow: false));
		}
		return strips;
	}

	// One panel of a strip, from ring position `start` to `end` and from the share t0 to t1 of the way across it, one quad
	// per ring position in it. Position i is at angle 2pi i / positions; the last position wraps to 0, so the mesh closes
	// exactly at the seam. The texture runs from u = uBase to uBase + 1 round the loop, and HexesPerRepeat hexes per repeat
	// across, from v = 0; the wear noise runs WearRepeatsAround times round the loop, and across by
	// y - (distance from the axis - radius), which agrees where faces meet.
	private static void AddPanel(LayerSubMesh subMesh, Strip strip, RingGeometry geometry, int start, int end, int uBase, float t0, float t1)
	{
		int positions = geometry.tilesAround;
		float repeatLength = RingStructure.HexesPerRepeat * geometry.tileSize;
		float wearLength = RingStructure.WearHexes * geometry.tileSize;
		float wearAround = RingStructure.WearRepeatsAround(positions);
		float stripLength = (strip.to - strip.from).magnitude;
		int first = subMesh.verts.Count;
		for (int i = start; i <= end; i++)
		{
			Vector3 outward = RingGeometry.Outward(2f * Mathf.PI * (i % positions) / positions);
			float u = uBase + (float)(i - start) / (end - start);
			float wearU = wearAround * i / positions;
			foreach (float t in new[] { t0, t1 })
			{
				Vector2 edge = Vector2.Lerp(strip.from, strip.to, t);
				float v = (t - t0) * stripLength / repeatLength;
				subMesh.verts.Add(new Vector3(outward.x * edge.x, edge.y, outward.z * edge.x));
				subMesh.colors.Add(strip.shade);
				subMesh.uvs.Add(new Vector3(u, v, 0f));
				subMesh.uvsChannelTwo.Add(new Vector3(wearU, (edge.y - (edge.x - geometry.radius)) / wearLength, 0f));
			}
		}
		for (int q = 0; q < end - start; q++)
		{
			int a = first + 2 * q;
			Vector3 midOutward = RingGeometry.Outward(2f * Mathf.PI * (start + q + 0.5f) / positions);
			Vector3 facing = midOutward * strip.facing.x + Vector3.up * strip.facing.y;
			AddFacing(subMesh, a, a + 2, a + 1, facing);
			AddFacing(subMesh, a + 1, a + 2, a + 3, facing);
		}
	}

	// Adds triangle (a, b, c) wound so its front faces the given way. Unity front faces are clockwise as seen by the
	// viewer, so the front side of (a, b, c) is Cross(b - a, c - a), the same convention as the overlay mirroring.
	public static void AddFacing(LayerSubMesh subMesh, int a, int b, int c, Vector3 facing)
	{
		List<Vector3> verts = subMesh.verts;
		bool front = Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]), facing) >= 0f;
		subMesh.tris.Add(a);
		subMesh.tris.Add(front ? b : c);
		subMesh.tris.Add(front ? c : b);
	}
}
