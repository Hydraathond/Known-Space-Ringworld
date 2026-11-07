using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Dev harness probe for how big hexes are (RW_DEV_HEX_SIZE=1), to compare the ring's hexes with a vanilla planet's.
// Every planet layer logs a "Hex size probe" line as it's built: tiles, radius, subdivisions, view angle, the
// AverageTileSize vanilla measured (or the ring set), and the neighbour spacing an even hex grid of that many tiles over
// the whole sphere would give. After the self-check it logs one hex's on-screen size (GenWorldUI.CurUITileSize, UI
// pixels) at the nearest and furthest zoom: vanilla's world camera on a planet world, the ring view on a ring world.
// RW_DEV_RING=planet makes @quicktest's world a planet, and RW_DEV_COVERAGE=<fraction> sets its coverage (DevHarness).
public static class DevHexSizeProbe
{
	private static readonly AccessTools.FieldRef<PlanetLayer, int> Subdivisions = AccessTools.FieldRefAccess<PlanetLayer, int>("subdivisions");

	// PlanetLayer.InitializeLayer postfix: runs after the ring's own build too (its prefix skips vanilla's).
	public static void LogLayer(PlanetLayer __instance)
	{
		PlanetLayer layer = __instance;
		int tiles = layer.TilesCount;
		// An even hex grid: each tile covers sqrt(3)/2 s^2 of the sphere's 4 pi r^2.
		float sphereSpacing = tiles > 0 ? Mathf.Sqrt(8f * Mathf.PI * layer.Radius * layer.Radius / (Mathf.Sqrt(3f) * tiles)) : 0f;
		WorldInfo info = (Current.CreatingWorld ?? Find.World)?.info;
		string coverage = info != null ? info.planetCoverage.ToString("0.###") : "?";
		string shape = layer is RingLayer ring && ring.Geometry != null
			? $"ring {ring.Size}, land coverage {ring.LandCoverage:0.###}, geometry tile size {ring.Geometry.tileSize:F4} (RingSize.VanillaHexSize {RingSize.VanillaHexSize})"
			: $"sphere, subdivisions {Subdivisions(layer)}; whole-sphere hex spacing for this many tiles {sphereSpacing:F4}";
		Log.Message($"[Ringworld] Hex size probe: layer {layer.LayerID} ({layer.Def?.defName}, {layer.GetType().Name}) built: {tiles} tiles, radius {layer.Radius:F3}, view angle {layer.ViewAngle:F1}, planet coverage {coverage}, AverageTileSize {layer.AverageTileSize:F4}; {shape}");
	}

	// One hex's on-screen size at the nearest and furthest zoom, then the camera put back.
	public static void LogScreenSizes()
	{
		Camera camera = Find.WorldCamera;
		if (camera == null)
		{
			Log.Warning("[Ringworld] Hex size probe: no world camera");
			return;
		}
		string screen = $"screen {Screen.width}x{Screen.height}, UI scale {Prefs.UIScale}, UI height {UI.screenHeight}, AverageTileSize {Find.WorldGrid.AverageTileSize:F4}";
		if (SurfaceRing.Current is { } ring)
		{
			Traverse distance = Traverse.Create(typeof(RingCamera)).Field("distance");
			Traverse desired = Traverse.Create(typeof(RingCamera)).Field("desiredDistance");
			float savedDistance = distance.GetValue<float>();
			float savedDesired = desired.GetValue<float>();
			// Measured from the surface ring: with atmosphere layers the harness's zoom may have selected one, which
			// CurUITileSize would measure from instead (at most 0.75 of the rim wall's height nearer the camera).
			PlanetLayer savedSelected = PlanetLayer.Selected;
			PlanetLayer.Selected = ring;
			RingworldExtension settings = ring.Settings;
			(string name, float at)[] zooms =
			{
				("nearest", settings.cameraMinDistance),
				("start", settings.cameraStartDistance),
				("ground's furthest G", RingCamera.MaxDistance(ring)),
				("furthest F", RingCamera.FurthestZoom(ring)),
			};
			foreach ((string name, float at) in zooms)
			{
				distance.SetValue(at);
				desired.SetValue(at);
				Log.Message($"[Ringworld] Hex size probe: ring view {name} zoom, {at:F2} from the surface ring (viewing {RingCamera.ActiveRing?.Def?.defName} at {RingCamera.Distance:F2}, field of view {settings.cameraFieldOfView}): one hex is {GenWorldUI.CurUITileSize():F2} UI px; {screen}");
			}
			distance.SetValue(savedDistance);
			desired.SetValue(savedDesired);
			PlanetLayer.Selected = savedSelected;
			return;
		}
		Transform transform = camera.transform;
		Vector3 savedPosition = transform.position;
		Quaternion savedRotation = transform.rotation;
		Vector3 origin = Find.WorldGrid.Surface.Origin;
		(string name, float altitude)[] altitudes =
		{
			("nearest", WorldCameraDriver.MinAltitude),
			("furthest", OrbitViewCamera.MaxAltitude),
		};
		foreach ((string name, float altitude) in altitudes)
		{
			transform.position = origin - transform.forward * altitude;
			Log.Message($"[Ringworld] Hex size probe: planet view {name} zoom, altitude {altitude:F0} ({altitude - Find.WorldGrid.Surface.Radius:F0} from the surface, field of view {camera.fieldOfView}, camera {transform.localPosition.magnitude:F0} from the origin): one hex is {GenWorldUI.CurUITileSize():F2} UI px; {screen}");
		}
		transform.position = savedPosition;
		transform.rotation = savedRotation;
	}
}
