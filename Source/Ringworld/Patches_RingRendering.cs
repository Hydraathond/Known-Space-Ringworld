using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Vanilla lifts every overlay (hills, rivers, roads, tile highlights, world-object quads) "up" along pos.normalized.
// Seen from inside the ring that is behind the terrain. Before a ring overlay mesh is uploaded, mirror it across the
// ring surface so it sits just above the inner surface, then turn every triangle to face the axis.
// Facing is fixed per triangle, not by reversing all of them: overlays built from tile corners (highlights, rivers,
// roads) already face inward, while quads built from sphere tangents (hills, world objects) face outward.
// The terrain mesh itself is the surface and is left alone, and so are layers built for the ring (IRingNativeDrawLayer).
[HarmonyPatch(typeof(WorldDrawLayerBase), "FinalizeMesh")]
public static class Patch_WorldDrawLayerBase_FinalizeMesh
{
	internal static readonly AccessTools.FieldRef<WorldDrawLayerBase, System.Collections.Generic.List<LayerSubMesh>> SubMeshes = AccessTools.FieldRefAccess<WorldDrawLayerBase, System.Collections.Generic.List<LayerSubMesh>>("subMeshes");

	public static void Prefix(WorldDrawLayerBase __instance)
	{
		if (__instance is not WorldDrawLayer drawLayer || drawLayer.planetLayer is not RingLayer ring)
		{
			return;
		}
		if (drawLayer is WorldDrawLayer_Terrain || drawLayer is WorldDrawLayer_RaycastableGrid || drawLayer is IRingNativeDrawLayer)
		{
			return;
		}
		foreach (LayerSubMesh subMesh in SubMeshes(drawLayer))
		{
			Mirror(subMesh, ring.Radius);
		}
	}

	// Mirrors one overlay submesh onto the inner surface and faces it to the axis; does nothing if it already was.
	// Doesn't upload the mesh: call it before LayerSubMesh.FinalizeMesh.
	public static void Mirror(LayerSubMesh subMesh, float radius)
	{
		if (subMesh.verts.Count == 0 || AlreadyMirrored(subMesh))
		{
			return;
		}
		bool hasNormals = subMesh.normals.Count == subMesh.verts.Count;
		for (int i = 0; i < subMesh.verts.Count; i++)
		{
			Vector3 vert = subMesh.verts[i];
			if (hasNormals)
			{
				Vector3 radial = -RingGeometry.InwardNormal(vert);
				Vector3 normal = subMesh.normals[i];
				subMesh.normals[i] = normal - 2f * Vector3.Dot(normal, radial) * radial;
			}
			subMesh.verts[i] = RingGeometry.MirrorAcrossSurface(vert, radius);
		}
		List<Vector3> verts = subMesh.verts;
		List<int> tris = subMesh.tris;
		for (int t = 0; t + 2 < tris.Count; t += 3)
		{
			Vector3 a = verts[tris[t]];
			Vector3 b = verts[tris[t + 1]];
			Vector3 c = verts[tris[t + 2]];
			// Unity front faces are clockwise as seen by the viewer; for triangle (a, b, c) the front side is
			// Cross(b - a, c - a). Same convention the ring terrain uses (checked offline and in game).
			if (Vector3.Dot(Vector3.Cross(b - a, c - a), RingGeometry.InwardNormal(a)) < 0f)
			{
				(tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
			}
		}
		Mirrored.AddOrUpdate(subMesh, new MirrorMark(subMesh.verts.Count, subMesh.verts[0], subMesh.verts[subMesh.verts.Count - 1]));
	}

	// Clear() can reset only some parts (e.g. colours) and keep the vertices, so "finalized" alone can't tell whether
	// the vertices were mirrored already. Remember what each mirrored submesh looked like instead.
	private sealed class MirrorMark
	{
		public readonly int count;

		public readonly Vector3 first;

		public readonly Vector3 last;

		public MirrorMark(int count, Vector3 first, Vector3 last)
		{
			this.count = count;
			this.first = first;
			this.last = last;
		}
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LayerSubMesh, MirrorMark> Mirrored = new System.Runtime.CompilerServices.ConditionalWeakTable<LayerSubMesh, MirrorMark>();

	private static bool AlreadyMirrored(LayerSubMesh subMesh)
	{
		if (!Mirrored.TryGetValue(subMesh, out MirrorMark mark))
		{
			return false;
		}
		int count = subMesh.verts.Count;
		return count == mark.count && subMesh.verts[0].Equals(mark.first) && subMesh.verts[count - 1].Equals(mark.last);
	}
}

// World lines (caravan routes, route planner) are one quad per segment with "up" = pos.normalized.
// On the ring: mirror the endpoints like the overlay meshes and face the quad towards the axis. Gated like the tangential
// quads below, so routes and their arrows agree in every view.
[HarmonyPatch(typeof(GenDraw), nameof(GenDraw.DrawWorldLineBetween), typeof(Vector3), typeof(Vector3), typeof(Material), typeof(float))]
public static class Patch_GenDraw_DrawWorldLineBetween
{
	public static bool Prefix(Vector3 A, Vector3 B, Material material, float widthFactor)
	{
		RingLayer ring = RingShapedLayers.Near(A);
		if (ring == null || !RingGeometry.NearSurface(B, ring.Radius))
		{
			return true;
		}
		A = RingGeometry.MirrorAcrossSurface(A, ring.Radius);
		B = RingGeometry.MirrorAcrossSurface(B, ring.Radius);
		if (Mathf.Abs(A.x - B.x) < 0.005f && Mathf.Abs(A.y - B.y) < 0.005f && Mathf.Abs(A.z - B.z) < 0.005f)
		{
			return false;
		}
		Vector3 pos = (A + B) / 2f;
		Quaternion rotation = Quaternion.LookRotation(A - B, RingGeometry.InwardNormal(pos));
		Vector3 scale = new Vector3(0.2f * Find.WorldGrid.AverageTileSize * widthFactor, 1f, (A - B).magnitude);
		Matrix4x4 matrix = default(Matrix4x4);
		matrix.SetTRS(pos, rotation, scale);
		Graphics.DrawMesh(MeshPool.plane10, matrix, material, WorldCameraManager.WorldLayer);
		return false;
	}
}

// Quads drawn each frame tangent to the planet: the world targeter, the caravan goto marker, world arrows, world objects
// drawn outside their layer mesh, and other mods' markers (Vehicle Framework's). Vanilla lifts them outward along
// pos.normalized, behind the terrain as seen from inside (the ring and orbit views are both inside the ring). Near a
// ring-shaped layer, whichever layer is selected, draw them just above its inner surface
// instead, facing the axis. Materials are switched to their ring shaders first, for any quad: world objects' are often
// made only when first drawn.
[HarmonyPatch(typeof(WorldRendererUtility), nameof(WorldRendererUtility.DrawQuadTangentialToPlanet))]
public static class Patch_WorldRendererUtility_DrawQuadTangentialToPlanet
{
	public static bool Prefix(Vector3 pos, float size, float altOffset, ref Material material, float rotationAngle, bool counterClockwise, bool useSkyboxLayer, MaterialPropertyBlock propertyBlock)
	{
		RingLayer surface = SurfaceRing.Current;
		if (surface == null)
		{
			return true;
		}
		// The switch goes by the surface ring: a material is shared by every layer that draws it, and keeps the one
		// _RingRadius RingMaterials.Sync gives it.
		material = RingMaterials.For(material, surface);
		RingLayer nearRing = RingShapedLayers.Near(pos);
		if (material == null || nearRing == null)
		{
			return true;
		}
		Matrix4x4 matrix = RingMatrix(pos, size, altOffset, rotationAngle, counterClockwise, nearRing.Radius);
		int layer = useSkyboxLayer ? WorldCameraManager.WorldSkyboxLayer : WorldCameraManager.WorldLayer;
		Graphics.DrawMesh(MeshPool.plane10, matrix, material, layer, null, 0, propertyBlock);
		return false;
	}

	// Vanilla's matrix with the inward normal in place of the outward one, at the point mirrored onto the inner surface.
	// Seen from the axis the quad looks as vanilla's does seen from outside: rotation 0 points screen-right and positive
	// angles turn clockwise on screen. Headings on the ring are already mirrored to match the ring view (ADR 0004), so
	// world arrows (rotation heading - 90) point where they should without another flip.
	public static Matrix4x4 RingMatrix(Vector3 pos, float size, float altOffset, float rotationAngle, bool counterClockwise, float ringRadius)
	{
		Vector3 inward = RingGeometry.InwardNormal(pos);
		Vector3 up = counterClockwise ? -inward : inward;
		Quaternion facing = Quaternion.LookRotation(Vector3.Cross(up, Vector3.up), up);
		Quaternion rotation = Quaternion.AngleAxis(rotationAngle, inward) * facing;
		Vector3 position = RingGeometry.MirrorAcrossSurface(pos, ringRadius) + inward * altOffset;
		return Matrix4x4.TRS(position, rotation, new Vector3(size, 1f, size));
	}
}

// Vanilla hides icons whose line of sight crosses a planet sphere. Inside the ring nothing in front of the camera
// can be hidden (the ring's inside is convex), but icons behind the camera project onto the screen mirrored.
[HarmonyPatch(typeof(WorldRendererUtility), nameof(WorldRendererUtility.HiddenBehindTerrainNow))]
public static class Patch_WorldRendererUtility_HiddenBehindTerrainNow
{
	public static bool Prefix(Vector3 pos, ref bool __result)
	{
		if (RingCamera.ActiveRing == null)
		{
			return true;
		}
		Transform camera = Find.WorldCamera.transform;
		__result = Vector3.Dot(pos - camera.position, camera.forward) <= 0f;
		return false;
	}
}

// On-screen size of one tile, used for icon sizes and drag speeds. Vanilla measures it for a camera outside a
// radius-100 sphere; on the ring the surface is RingCamera.Distance in front of the camera.
[HarmonyPatch(typeof(GenWorldUI), nameof(GenWorldUI.CurUITileSize))]
public static class Patch_GenWorldUI_CurUITileSize
{
	public static void Postfix(ref float __result)
	{
		RingLayer ring = RingCamera.ActiveRing;
		if (ring == null || RingCamera.Distance <= 0f)
		{
			return;
		}
		float visibleHeight = 2f * RingCamera.Distance * Mathf.Tan(ring.Settings.cameraFieldOfView * 0.5f * Mathf.Deg2Rad);
		__result = Find.WorldGrid.AverageTileSize / visibleHeight * UI.screenHeight;
	}
}
