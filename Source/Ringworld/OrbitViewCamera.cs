using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The orbit view of a ring world, from any of its orbit layers (RingOrbit), is vanilla's orbit view scaled up about the
// sun. Vanilla's camera driver runs as usual (altitude, zoom steps, rotation, icon sizes all unchanged); afterwards the
// camera's distance from the sun and its clip planes are multiplied by DistanceScale, so the view looks as vanilla's
// would around an orbit sphere of the scaled radius, with the same depth precision.
// Zoomed in, DistanceScale is the layer's radius over its baseline (RingOrbit.BaselineRadius); zooming out it grows
// smoothly until, fully zoomed out, the whole ring fits on screen at any ring size.
public static class OrbitViewCamera
{
	// WorldCameraManager's field of view.
	public const float FieldOfView = 20f;

	// WorldCameraManager's near clip plane.
	private const float VanillaNearClipPlane = 2f;

	// WorldCameraDriver's maximum altitude (private there).
	public const float MaxAltitude = 1100f;

	// Fully zoomed out, the ring's radius is this much smaller than the view's half-height, so the ring is clear of the
	// screen's edges.
	private const float FitMargin = 1.1f;

	private static bool clipPlanesScaled;

	// WorldCameraDriver's private state, set when the ring view hands over to the orbit view.
	private static readonly AccessTools.FieldRef<WorldCameraDriver, float> DesiredAltitude = AccessTools.FieldRefAccess<WorldCameraDriver, float>("desiredAltitude");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, float> LayerAltitudeOffset = AccessTools.FieldRefAccess<WorldCameraDriver, float>("layerAltitudeOffset");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, float> LayerOffsetVelocity = AccessTools.FieldRefAccess<WorldCameraDriver, float>("layerOffsetVel");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, Vector3> LayerOriginOffset = AccessTools.FieldRefAccess<WorldCameraDriver, Vector3>("layerOriginOffset");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, Vector3> LayerOriginOffsetVelocity = AccessTools.FieldRefAccess<WorldCameraDriver, Vector3>("layerOriginOffsetVel");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, float> AltitudeDecay = AccessTools.FieldRefAccess<WorldCameraDriver, float>("altitudeDecay");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, Vector2> RotationVelocity = AccessTools.FieldRefAccess<WorldCameraDriver, Vector2>("rotationVelocity");

	private static readonly AccessTools.FieldRef<WorldCameraDriver, PlanetLayer> PrevSelectedLayer = AccessTools.FieldRefAccess<WorldCameraDriver, PlanetLayer>("prevSelectedLayer");

	// Puts vanilla's camera driver, which hasn't run while the ring view was showing, at an orbit layer's closest zoom
	// with the given rotation, settled at once: vanilla's easing between layers would start from wherever the driver
	// was left, which has nothing to do with the ring view's camera.
	public static void OpenFacing(WorldCameraDriver driver, PlanetLayer orbit, Quaternion sphereRotation)
	{
		if (driver == null)
		{
			return;
		}
		driver.sphereRotation = sphereRotation;
		driver.altitude = WorldCameraDriver.MinAltitude;
		DesiredAltitude(driver) = WorldCameraDriver.MinAltitude;
		LayerAltitudeOffset(driver) = orbit.ExtraCameraAltitude;
		LayerOffsetVelocity(driver) = 0f;
		LayerOriginOffset(driver) = orbit.Origin;
		LayerOriginOffsetVelocity(driver) = Vector3.zero;
		AltitudeDecay(driver) = 0f;
		RotationVelocity(driver) = Vector2.zero;
		PrevSelectedLayer(driver) = orbit;
	}

	// How far above the nearest zoom still counts as the nearest.
	private const float NearestZoomTolerance = 0.01f;

	// Whether the orbit view is zoomed in as far as it goes.
	public static bool AtNearestZoom(WorldCameraDriver driver)
	{
		return DesiredAltitude(driver) <= WorldCameraDriver.MinAltitude + NearestZoomTolerance;
	}

	// The selected sun-centred orbit layer and the ring, while the orbit view of a ring world is showing.
	public static bool TryGetActive(out PlanetLayer orbit, out RingLayer ring)
	{
		orbit = null;
		ring = null;
		if (!SurfaceRing.InRingWorld || WorldRendererUtility.WorldBackgroundNow)
		{
			return false;
		}
		PlanetLayer selected = PlanetLayer.Selected;
		if (selected == null || !RingOrbit.IsSunCentred(selected))
		{
			return false;
		}
		orbit = selected;
		ring = SurfaceRing.Current;
		return true;
	}

	// zoomPercent: 0 fully zoomed in, 1 fully zoomed out (WorldCameraDriver.AltitudePercent).
	public static float DistanceScale(PlanetLayer orbit, RingLayer ring, float zoomPercent)
	{
		float scale = orbit.Radius / RingOrbit.BaselineRadius(orbit.Def);
		float vanillaFarthest = (MaxAltitude + orbit.ExtraCameraAltitude) * scale;
		float fitDistance = FitMargin * ring.Radius / Mathf.Sin(0.5f * FieldOfView * Mathf.Deg2Rad);
		float zoomOutFactor = Mathf.Max(1f, fitDistance / vanillaFarthest);
		return scale * Mathf.Lerp(1f, zoomOutFactor, zoomPercent);
	}

	// Where the camera settles at a zoom, as vanilla's driver puts it before the scaling: altitude plus the orbit
	// layer's extra camera altitude.
	public static float DistanceFromSun(PlanetLayer orbit, RingLayer ring, float zoomPercent)
	{
		float altitude = Mathf.Lerp(WorldCameraDriver.MinAltitude, MaxAltitude, zoomPercent);
		return (altitude + orbit.ExtraCameraAltitude) * DistanceScale(orbit, ring, zoomPercent);
	}

	public static float FarClipPlane(PlanetLayer orbit, RingLayer ring, float zoomPercent)
	{
		return WorldCameraManager.FarClipPlane * DistanceScale(orbit, ring, zoomPercent);
	}

	public static float NearClipPlane(PlanetLayer orbit, RingLayer ring, float zoomPercent)
	{
		return VanillaNearClipPlane * DistanceScale(orbit, ring, zoomPercent);
	}

	// Moves the camera vanilla has just placed to the scaled distance from the sun.
	public static void ScalePosition(Camera camera, float zoomPercent)
	{
		if (!TryGetActive(out PlanetLayer orbit, out RingLayer ring))
		{
			return;
		}
		Transform transform = camera.transform;
		transform.position = orbit.Origin + (transform.position - orbit.Origin) * DistanceScale(orbit, ring, zoomPercent);
	}

	// Scaled clip planes in the orbit view of a ring world, vanilla ones everywhere else.
	public static void ApplyClipPlanes(Camera camera, float zoomPercent)
	{
		if (TryGetActive(out PlanetLayer orbit, out RingLayer ring))
		{
			camera.nearClipPlane = NearClipPlane(orbit, ring, zoomPercent);
			camera.farClipPlane = FarClipPlane(orbit, ring, zoomPercent);
			clipPlanesScaled = true;
		}
		else if (clipPlanesScaled)
		{
			camera.nearClipPlane = VanillaNearClipPlane;
			camera.farClipPlane = WorldCameraManager.FarClipPlane;
			clipPlanesScaled = false;
		}
	}
}
