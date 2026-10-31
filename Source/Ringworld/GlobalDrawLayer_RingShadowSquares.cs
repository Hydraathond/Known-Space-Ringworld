using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The shadow squares: one panel per night zone, orbiting between the sun and the ring, drawn only in the orbit view of
// a ring world, from any orbit layer. Cosmetic: local time (RingGeometry.LocalTimeLongitudeOf) makes the night zones,
// and the squares follow it. There are as many as the ring's saved shadow-square count (RingLayer.ShadowSquareCount),
// which is also its number of night zones. The mesh holds them at angles k x 360 / nightZones; turning it about the
// axis by RotationAt puts each one over the middle of a night zone at the current time.
//
// Each square is a flat slab facing the sun, drawn with the ring structure shader (RingStructure): a photovoltaic panel on
// the sun's side and matte near-black on the ring's, and scrith edges, all at the structure's scale: on a Wang set
// (RingStructure) each face is cut into squares of about HexesPerRepeat hexes, else its texture repeats along it.
// It is wide enough to cover most of its night zone seen from the sun, and as tall as the ring is wide, so it scales with the ring's width and its shadow reaches well past both rims.
// The ring view never shows them, with no extra code: at RadiusFactor they are closer to the axis than the ring-view
// camera can ever be, and that camera looks straight outward, so they are always behind it.
public class GlobalDrawLayer_RingShadowSquares : WorldDrawLayerBase
{
	// Of the ring's radius: outside the sun's glow (GlobalDrawLayer_RingSun.GlowRadiusFactor), inside every orbit layer's
	// sphere (RingOrbit), so the orbit-view camera never comes near them.
	public const float RadiusFactor = 0.35f;

	// Of the angle between neighbouring squares, the share one square covers seen from the sun: under vanilla's
	// shortest night at the midline (0.437 of a day), by enough that the half-hour vanilla's whole-hour time zones can
	// shift a night by still leaves its shadow inside the night zone.
	public const float WidthShare = 0.35f;

	// In hexes (one hex = the ring's tile size).
	public const float Thickness = 0.5f;

	// Each face's shade, multiplying its texture.
	private static readonly Color32 FaceShade = new Color32(255, 255, 255, 255);

	private static readonly Color32 EdgeShade = new Color32(200, 200, 200, 255);

	private float builtRadius = -1f;

	private int builtNightZones = -1;

	private readonly List<Vector3> centres = new List<Vector3>();

	private readonly List<Vector3[]> corners = new List<Vector3[]>();

	// Each square's centre, layer-local and before RotationAt.
	public IReadOnlyList<Vector3> Centres => centres;

	// Each square's corners (both faces), layer-local and before RotationAt.
	public IEnumerable<IReadOnlyList<Vector3>> Corners => corners;

	// For the self-check: each submesh holds one surface's material.
	public IEnumerable<LayerSubMesh> SubMeshes => subMeshes;

	// For the self-check: every square of a face built from Wang tiles.
	public IReadOnlyList<WangSquare> WangSquares => wangSquares;

	private readonly List<WangSquare> wangSquares = new List<WangSquare>();

	// How far the squares' centres are from the axis.
	public static float Radius(RingLayer ring)
	{
		return ring.Radius * RadiusFactor;
	}

	public override bool Visible => base.Visible && OrbitViewCamera.TryGetActive(out _, out _);

	public override bool ShouldRegenerate => base.ShouldRegenerate || (Visible && SurfaceRing.Current is { } ring && !BuiltFor(ring));

	public override Vector3 Position => Find.WorldGrid.Surface.Origin;

	protected override Quaternion Rotation => SurfaceRing.Current is { Geometry: RingGeometry geometry } ? RotationAt(RingLocalTime.NowTicksAbs(), geometry.nightZones) : Quaternion.identity;

	// Turning about the axis by TurnAt moves a point from angle a around the ring to a minus that angle.
	public static Quaternion RotationAt(int ticksAbs, int nightZones)
	{
		return Quaternion.AngleAxis(TurnAt(ticksAbs, nightZones), Vector3.up);
	}

	// In degrees. Local time at a tile is the hour at longitude 0 plus its longitude / 15, and the night zones'
	// longitude is the angle around the ring x nightZones, so local midnight (the middle of the night) is at angles
	// -TurnAt + k x 360 / nightZones. The night shadow (RingNightShadow) is centred there too.
	public static float TurnAt(int ticksAbs, int nightZones)
	{
		float hour = GenDate.HourFloat(ticksAbs, 0f);
		return hour * GenDate.TimeZoneWidth / nightZones;
	}

	// Whether the squares were last built for this ring; the self-check builds them outside the orbit view if not.
	public bool BuiltFor(RingLayer ring)
	{
		return builtRadius == Radius(ring) && builtNightZones == ring.Geometry?.nightZones;
	}

	public override IEnumerable Regenerate()
	{
		foreach (object item in base.Regenerate())
		{
			yield return item;
		}
		centres.Clear();
		corners.Clear();
		wangSquares.Clear();
		if (SurfaceRing.Current is not { Geometry: RingGeometry geometry } ring)
		{
			yield break;
		}
		builtRadius = Radius(ring);
		builtNightZones = geometry.nightZones;
		float halfAngle = 0.5f * WidthShare * 2f * Mathf.PI / geometry.nightZones;
		float halfWidth = builtRadius * Mathf.Tan(halfAngle);
		Vector3 up = Vector3.up * geometry.HalfWidth;
		float halfThickness = 0.5f * Thickness * geometry.tileSize;
		float repeatLength = RingStructure.HexesPerRepeat * geometry.tileSize;
		for (int k = 0; k < geometry.nightZones; k++)
		{
			float angle = k * 2f * Mathf.PI / geometry.nightZones;
			Vector3 outward = RingGeometry.Outward(angle);
			Vector3 along = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * halfWidth;
			Vector3 centre = outward * builtRadius;
			centres.Add(centre);
			Vector3 sunSide = centre - outward * halfThickness;
			Vector3 ringSide = centre + outward * halfThickness;
			Vector3[] c =
			{
				sunSide - along - up, sunSide - along + up, sunSide + along + up, sunSide + along - up,
				ringSide - along - up, ringSide - along + up, ringSide + along + up, ringSide + along - up,
			};
			// Every face at the structure's scale: a Wang set is cut into squares, else one quad repeats its texture.
			int faceIndex = 0;
			foreach ((RingStructureSurface surface, int[] quad, Vector3 facing) in new[]
			{
				(RingStructureSurface.Photovoltaic, new[] { 0, 1, 2, 3 }, -outward),
				(RingStructureSurface.Matte, new[] { 4, 5, 6, 7 }, outward),
				(RingStructureSurface.Scrith, new[] { 1, 2, 6, 5 }, Vector3.up),
				(RingStructureSurface.Scrith, new[] { 0, 3, 7, 4 }, Vector3.down),
				(RingStructureSurface.Scrith, new[] { 0, 1, 5, 4 }, -along),
				(RingStructureSurface.Scrith, new[] { 3, 2, 6, 7 }, along),
			})
			{
				Color32 shade = surface == RingStructureSurface.Scrith ? EdgeShade : FaceShade;
				Vector3[] quadCorners = quad.Select(i => c[i]).ToArray();
				if (RingStructure.IsWang(surface))
				{
					// Face ids are unique within a surface: 8 is more than the 6 faces of a square.
					AddWangFace(surface, k * 8 + faceIndex, quadCorners, facing, shade, repeatLength);
				}
				else
				{
					int tile = RingStructure.VariantAt(surface, k, geometry.nightZones);
					LayerSubMesh subMesh = GetSubMesh(RingStructure.MaterialFor(surface, tile, nightShadow: false));
					AddQuad(subMesh, quadCorners, facing, shade, repeatLength);
				}
				faceIndex++;
			}
			corners.Add(c);
		}
		FinalizeMesh(MeshParts.All);
	}

	// A face of a square on a Wang set: a row of squares from the first corner towards the second,
	// as many rows across (from the second towards the third) as fit, each square one repeat, with the tile that matches
	// its neighbours' edges. The last in each direction is cut off. Nothing wraps. One GetSubMesh call per square.
	private void AddWangFace(RingStructureSurface surface, int face, Vector3[] quad, Vector3 facing, Color32 shade, float repeatLength)
	{
		Vector3 along = quad[1] - quad[0];
		Vector3 across = quad[3] - quad[0];
		int columns = Mathf.Max(1, Mathf.CeilToInt(along.magnitude / repeatLength - 0.001f));
		int rows = Mathf.Max(1, Mathf.CeilToInt(across.magnitude / repeatLength - 0.001f));
		float columnShare = repeatLength / along.magnitude;
		float rowShare = repeatLength / across.magnitude;
		for (int column = 0; column < columns; column++)
		{
			for (int row = 0; row < rows; row++)
			{
				float s0 = column * columnShare;
				float s1 = Mathf.Min(1f, (column + 1) * columnShare);
				float t0 = row * rowShare;
				float t1 = Mathf.Min(1f, (row + 1) * rowShare);
				Vector3[] square =
				{
					quad[0] + along * s0 + across * t0, quad[0] + along * s1 + across * t0,
					quad[0] + along * s1 + across * t1, quad[0] + along * s0 + across * t1,
				};
				int tile = RingStructure.WangTileAt(surface, face, column, row, columns, wraps: false);
				LayerSubMesh subMesh = GetSubMesh(RingStructure.MaterialFor(surface, tile, nightShadow: false));
				AddQuad(subMesh, square, facing, shade, repeatLength);
				wangSquares.Add(new WangSquare(face, column, row, columns, wraps: false, subMesh.material));
			}
		}
	}

	// One face of a square: a quad through four corners in order round it, facing the given way, in one shade. Its
	// texture runs one repeat per repeatLength from the first corner. The wear and macro
	// channel is the texture's, MacroScale times larger, and both stay put on the square as the layer turns.
	private static void AddQuad(LayerSubMesh subMesh, Vector3[] quad, Vector3 facing, Color32 shade, float repeatLength)
	{
		int first = subMesh.verts.Count;
		float u = (quad[1] - quad[0]).magnitude / repeatLength;
		float v = (quad[2] - quad[1]).magnitude / repeatLength;
		Vector3[] uvs = { new Vector3(0f, 0f), new Vector3(u, 0f), new Vector3(u, v), new Vector3(0f, v) };
		for (int i = 0; i < 4; i++)
		{
			Vector3 vert = quad[i];
			subMesh.verts.Add(vert);
			subMesh.colors.Add(shade);
			subMesh.uvs.Add(uvs[i]);
			subMesh.uvsChannelTwo.Add(uvs[i] / RingStructure.MacroScale);
		}
		WorldDrawLayer_RingThickness.AddFacing(subMesh, first, first + 1, first + 2, facing);
		WorldDrawLayer_RingThickness.AddFacing(subMesh, first, first + 2, first + 3, facing);
	}
}
