using HarmonyLib;
using RimWorld.Planet;
using TMPro;
using UnityEngine;
using Verse;

namespace Ringworld;

// Region labels (forest, ocean, mountain range names) on the ring.
// Vanilla bends the label text onto a sphere at radius + 0.4, facing outward: seen from inside the ring that is behind
// the terrain. On the ring the label lies Lift above the inner surface instead, curving with it, facing the axis and
// turned like the ring-view camera (up = across the ring, right = decreasing angle), so it reads left to right.
// Planet labels go through vanilla untouched.
public static class RegionLabels
{
	// How far the label floats above the inner surface, towards the axis. Vanilla lifts its labels by the same amount.
	public const float Lift = 0.4f;

	// Accepted on-screen label size: draw size (the label's font size, in world units) over the visible height at the
	// label's distance. Plays the part of vanilla's altitude rule, which assumes a camera outside a radius-100 sphere.
	// Vanilla's rule works out to roughly 0.04-1.0; the top is raised so the labels of regions as wide as the ring
	// still appear when zoomed out. Tune in game if labels show too small or too large.
	public static readonly FloatRange ScreenSizeRange = new FloatRange(0.04f, 1.5f);

	// Puts the label's transform Lift above the inner surface at its anchor, turned to read like the ring-view camera.
	// The anchor is the transform's current position (the region's draw centre, or the lifted point from an earlier
	// call), so calling it again doesn't move the label.
	public static void Place(Transform transform, RingLayer ring)
	{
		float angle = RingGeometry.AngleOf(transform.position);
		Vector3 outward = RingGeometry.Outward(angle);
		float distance = DistanceFromAxis(ring);
		transform.position = new Vector3(outward.x * distance, transform.position.y, outward.z * distance);
		transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
	}

	// Places the label, then bends its flat text around the ring at the label's height above the surface. Every vertex is mapped on its own, so
	// a long label follows the curve exactly and stays the same distance above the surface along its whole length.
	// The text's x runs along the arc (towards decreasing angle), its y stays along the axis.
	public static void WrapOntoRing(TextMeshPro textMesh, RingLayer ring)
	{
		Transform transform = textMesh.transform;
		Place(transform, ring);
		textMesh.ForceMeshUpdate();
		TMP_MeshInfo[] meshInfo = textMesh.textInfo?.meshInfo;
		if (meshInfo == null)
		{
			return;
		}
		Vector3 anchor = transform.position;
		float anchorAngle = RingGeometry.AngleOf(anchor);
		float distance = DistanceFromAxis(ring);
		Vector3 right = transform.right;
		Vector3 forward = transform.forward;
		Matrix4x4 localToWorld = transform.localToWorldMatrix;
		Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
		for (int m = 0; m < meshInfo.Length; m++)
		{
			Vector3[] vertices = meshInfo[m].vertices;
			if (vertices == null)
			{
				continue;
			}
			int count = Mathf.Min(meshInfo[m].vertexCount, vertices.Length);
			for (int i = 0; i < count; i++)
			{
				Vector3 offset = localToWorld.MultiplyPoint(vertices[i]) - anchor;
				float angle = anchorAngle - Vector3.Dot(offset, right) / distance;
				// Depth (along the camera's forward) becomes distance from the axis; flat text has none.
				float radial = distance + Vector3.Dot(offset, forward);
				Vector3 outward = RingGeometry.Outward(angle);
				vertices[i] = worldToLocal.MultiplyPoint(new Vector3(outward.x * radial, anchor.y + offset.y, outward.z * radial));
			}
		}
		textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
	}

	private static float DistanceFromAxis(RingLayer ring)
	{
		return ring.Radius - Lift;
	}

	// Whether a ring region's label is a readable size on screen right now, from the ring-view camera.
	public static bool GoodScreenSize(WorldFeature feature)
	{
		if (!feature.layer.IsSelected)
		{
			return false;
		}
		Camera camera = Find.WorldCamera;
		Transform cameraTransform = camera.transform;
		float depth = Vector3.Dot(feature.drawCenter - cameraTransform.position, cameraTransform.forward) - Lift;
		if (depth <= camera.nearClipPlane)
		{
			return false;
		}
		float visibleHeight = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
		return ScreenSizeRange.Includes(feature.EffectiveDrawSize * Find.WorldGrid.AverageTileSize / visibleHeight);
	}

}

[HarmonyPatch(typeof(WorldFeatureTextMesh_TextMeshPro), nameof(WorldFeatureTextMesh_TextMeshPro.WrapAroundPlanetSurface))]
public static class Patch_WorldFeatureTextMesh_TextMeshPro_WrapAroundPlanetSurface
{
	private static readonly AccessTools.FieldRef<WorldFeatureTextMesh_TextMeshPro, TextMeshPro> TextMesh = AccessTools.FieldRefAccess<WorldFeatureTextMesh_TextMeshPro, TextMeshPro>("textMesh");

	public static bool Prefix(WorldFeatureTextMesh_TextMeshPro __instance, PlanetLayer layer)
	{
		if (layer is not RingLayer ring)
		{
			return true;
		}
		RegionLabels.WrapOntoRing(TextMesh(__instance), ring);
		return false;
	}
}

// The legacy text (used for characters the TextMeshPro font lacks) is never bent, on the planet either; on the ring
// it is only placed and turned, so it lies flat just above the surface.
[HarmonyPatch(typeof(WorldFeatureTextMesh_Legacy), nameof(WorldFeatureTextMesh_Legacy.WrapAroundPlanetSurface))]
public static class Patch_WorldFeatureTextMesh_Legacy_WrapAroundPlanetSurface
{
	private static readonly AccessTools.FieldRef<WorldFeatureTextMesh_Legacy, TextMesh> TextMesh = AccessTools.FieldRefAccess<WorldFeatureTextMesh_Legacy, TextMesh>("textMesh");

	public static bool Prefix(WorldFeatureTextMesh_Legacy __instance, PlanetLayer layer)
	{
		if (layer is not RingLayer ring)
		{
			return true;
		}
		RegionLabels.Place(TextMesh(__instance).transform, ring);
		return false;
	}
}

// Vanilla fades labels in and out by camera altitude above a radius-100 sphere. The ring camera's altitude is only a
// stand-in (see RingCamera.Apply) and its zoom range is small, so most labels would never show; judge them by their
// size on screen instead.
[HarmonyPatch(typeof(WorldFeatures), "GoodCameraAltitudeFor")]
public static class Patch_WorldFeatures_GoodCameraAltitudeFor
{
	public static bool Prefix(WorldFeature feature, ref bool __result)
	{
		if (feature.layer is not RingLayer)
		{
			return true;
		}
		__result = RegionLabels.GoodScreenSize(feature);
		return false;
	}
}
