using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// World camera for ring-shaped layers (the ring view): it sits on the ring's axis and looks outward at the layer's inner surface.
// State is one angle around the ring, an offset across the ring from the midline (positive towards dexter, the top of
// the screen), and the distance to the surface ring (zoom).
// The surface ring and its atmosphere layers share that one zoom, cut into consecutive zoom bands from the ground outward
// (ZoomBands): crossing a band's edge selects that band's layer without moving the camera.
// Middle-drag moves the surface with the mouse: dragging left rotates the ring left, so more of the right comes into view.
public static class RingCamera
{
	private static float angle;

	private static float distance = -1f;

	private static float desiredDistance = -1f;

	private static float pendingAngle;

	// How far the view is from the midline, in world units; positive is towards the top of the screen (dexter).
	private static float across;

	private static float pendingAcross;

	private static float? vanillaFieldOfView;

	// The ring view's near clip as a share of its nearest zoom (the def's cameraMinDistance): vanilla's 2 over the ring
	// view's nearest zoom of 3 from when hexes were 0.8 apart, so the picture is that one scaled to vanilla-sized hexes.
	// Vanilla's 2 would clip the ground at today's nearest zoom. It stays clear of the region labels' Lift there.
	public const float NearClipShareOfNearestZoom = 2f / 3f;

	private static bool nearClipSet;

	// Whether the world camera has the ring view's near clip rather than vanilla's.
	public static bool NearClipSet => nearClipSet;

	// The layer the camera was last put in the band of; a different selected layer (a tab, a jump) moves the camera into its band.
	private static RingLayer bandLayer;

	// Keeps a camera put on a band's edge inside that band, so rounding can't pick the layer next to it.
	private const float BandEpsilon = 0.01f;

	// Current distance from the camera to the viewed layer's inner surface; negative until the ring camera has run.
	public static float Distance => distance < 0f ? distance : ViewDistance(ActiveRing, distance);

	// The ring-shaped layer being viewed, on a ring world: the selected layer if it's ring-shaped, or the surface ring
	// while no layer is selected. Null while another layer (an orbit) is viewed, or on a planet world.
	public static RingLayer ActiveRing
	{
		get
		{
			RingLayer surface = SurfaceRing.Current;
			if (surface == null)
			{
				return null;
			}
			PlanetLayer selected = Find.WorldSelector?.SelectedLayer;
			return selected == null ? surface : selected as RingLayer;
		}
	}

	public static void LookAt(Vector3 layerLocalPoint)
	{
		angle = RingGeometry.AngleOf(layerLocalPoint);
		pendingAngle = 0f;
		// Clamped on the next Update, once the zoom is known.
		across = layerLocalPoint.y;
		pendingAcross = 0f;
	}

	public static void HandleInput(RingLayer ring)
	{
		if (Find.WindowStack.WindowsPreventCameraMotion || !WorldRendererUtility.WorldSelected)
		{
			return;
		}
		EnsureInitialized(ring);
		if (UnityGUIBugsFixer.MouseDrag(2))
		{
			Vector2 delta = UnityGUIBugsFixer.CurrentEventDelta;
			if (Event.current.type == EventType.mouseDrag)
			{
				Event.current.Use();
			}
			if (delta.x != 0f)
			{
				pendingAngle += ScreenDistanceToAngle(delta.x, ring);
			}
			if (delta.y != 0f)
			{
				// Screen y grows downwards: dragging up moves the surface up, so the view moves down.
				pendingAcross += ScreenDistanceToWorld(delta.y, ring);
			}
		}
		float zoom = ZoomInput();
		if (zoom != 0f)
		{
			List<RingLayer> stack = ZoomStack(ring);
			float[] bands = ZoomBands(stack);
			float wanted = desiredDistance - zoom * desiredDistance * 0.1f;
			if (OnStartingSitePage || MaySwitchLayer)
			{
				float furthest = OnStartingSitePage ? StartingSiteFurthest(bands) : bands[bands.Length - 1];
				if (wanted > furthest && TryZoomOut(stack[stack.Count - 1]))
				{
					return;
				}
				desiredDistance = Mathf.Clamp(wanted, bands[0], furthest);
			}
			else
			{
				desiredDistance = ClampToBand(stack, bands, ring, wanted);
			}
		}
	}

	// This frame's zoom from the scroll wheel and the zoom keys: positive zooms in.
	private static float ZoomInput()
	{
		float zoom = 0f;
		if (Event.current.type == EventType.scrollWheel)
		{
			zoom -= Event.current.delta.y * 0.1f;
		}
		if (KeyBindingDefOf.MapZoom_In.KeyDownEvent)
		{
			zoom += 2f;
		}
		if (KeyBindingDefOf.MapZoom_Out.KeyDownEvent)
		{
			zoom -= 2f;
		}
		return zoom;
	}

	// Whether the Starting-site page of a ring world is open. Before play zoom skips the atmosphere layers: the ground's
	// band, then the orbit view, whatever "Zoom switches world layer" says.
	public static bool OnStartingSitePage => Current.ProgramState == ProgramState.Entry && SurfaceRing.InRingWorld && Find.WindowStack.IsOpen<Page_SelectStartingSite>();

	// The furthest zoom of the ground's band: past it the Starting-site page opens the orbit view.
	public static float StartingSiteFurthest(float[] bands)
	{
		return bands[1];
	}

	// The orbit view on the Starting-site page: vanilla's zoom-in to the layer below is gated on playing, so zooming in
	// at the orbit view's nearest zoom lands on the ground, at the far end of its band, here.
	// Returns whether it handed over to the ring view.
	public static bool TryZoomInFromOrbit(WorldCameraDriver driver)
	{
		RingLayer ground = SurfaceRing.Current;
		if (!OnStartingSitePage || ground == null || Find.WindowStack.WindowsPreventCameraMotion || !WorldRendererUtility.WorldSelected)
		{
			return false;
		}
		if (ZoomInput() <= 0f || !OrbitViewCamera.AtNearestZoom(driver))
		{
			return false;
		}
		Vector3 lookAt = driver.CurrentlyLookingAtPointOnSphere;
		PlanetLayer.Selected = ground;
		ArriveFromOrbit(ground, lookAt);
		return true;
	}

	// Leaving the Starting-site page: the world view goes back to the ground.
	public static void ReturnToGround()
	{
		RingLayer ground = SurfaceRing.Current;
		if (ground != null && PlanetLayer.Selected != ground)
		{
			PlanetLayer.Selected = ground;
		}
	}

	// Whether zooming may switch layers: the same conditions as vanilla's WorldCameraDriverOnGUI.
	private static bool MaySwitchLayer => Current.ProgramState == ProgramState.Playing && Prefs.ZoomSwitchWorldLayer;

	// Zooming out past the stack's furthest zoom hands over to the layer above its highest ring-shaped layer; the orbit
	// view opens looking at the spot just left (OrbitLookAtFacing).
	private static bool TryZoomOut(RingLayer top)
	{
		PlanetLayer outer = top.zoomOutToLayer;
		if (!(OnStartingSitePage || MaySwitchLayer) || outer == null)
		{
			return false;
		}
		PlanetLayer.Selected = outer;
		OrbitViewCamera.OpenFacing(Find.WorldCameraDriver, outer, OrbitRotationFacing(angle));
		return true;
	}

	// Vanilla has just zoomed in from the orbit view to the ring: face the far side the orbit camera was looking at,
	// at the far end of the arriving layer's band (the stack's furthest zoom, for the highest layer), so scrolling in goes
	// down through each layer to the ground.
	public static void ArriveFromOrbit(RingLayer ring, Vector3 orbitLookAt)
	{
		angle = AngleFacing(orbitLookAt);
		pendingAngle = 0f;
		across = 0f;
		pendingAcross = 0f;
		bandLayer = ring;
		List<RingLayer> stack = ZoomStack(ring);
		distance = desiredDistance = ClampToBand(stack, ZoomBands(stack), ring, float.MaxValue);
	}

	// How far above the ring's plane the orbit view opens, looking down at the sun, so the far side's inner surface
	// shows instead of the ring edge-on.
	public const float OrbitTiltDegrees = 15f;

	// The orbit camera always looks at the sun from outside the ring. The near side of the ring shows its outer face;
	// the inner surface it sees is the far side, across the sun. So the orbit camera that shows the ring view's spot
	// is on the opposite side of the ring, raised by OrbitTiltDegrees; the spot is then at the screen's horizontal
	// centre, a little above the sun. Returns the camera's direction from the sun
	// (WorldCameraDriver.CurrentlyLookingAtPointOnSphere).
	public static Vector3 OrbitLookAtFacing(float ringAngle)
	{
		float tilt = OrbitTiltDegrees * Mathf.Deg2Rad;
		return RingGeometry.Outward(ringAngle + Mathf.PI) * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt);
	}

	// The orbit camera's rotation (WorldCameraDriver.sphereRotation) for OrbitLookAtFacing. As vanilla's JumpTo.
	public static Quaternion OrbitRotationFacing(float ringAngle)
	{
		return Quaternion.Inverse(Quaternion.LookRotation(-OrbitLookAtFacing(ringAngle)));
	}

	// The angle around the ring of the far side the orbit camera sees, from its direction from the sun
	// (WorldCameraDriver.CurrentlyLookingAtPointOnSphere), however far above or below the ring's plane it is tilted.
	public static float AngleFacing(Vector3 orbitLookAt)
	{
		return RingGeometry.AngleOf(orbitLookAt) + Mathf.PI;
	}

	public static void Update(WorldCameraDriver driver, RingLayer ring)
	{
		EnsureInitialized(ring);
		ring = FollowZoom(ring);
		RingworldExtension settings = ring.Settings;
		float deltaTime = Time.deltaTime;
		if (!Find.WindowStack.WindowsPreventCameraMotion && WorldRendererUtility.WorldSelected)
		{
			float panAround = 0f;
			float panAcross = 0f;
			if (KeyBindingDefOf.MapDolly_Up.IsDown)
			{
				panAcross += 1f;
			}
			if (KeyBindingDefOf.MapDolly_Down.IsDown)
			{
				panAcross -= 1f;
			}
			if (KeyBindingDefOf.MapDolly_Left.IsDown)
			{
				panAround += 1f;
			}
			if (KeyBindingDefOf.MapDolly_Right.IsDown)
			{
				panAround -= 1f;
			}
			if (Input.GetKey(KeyCode.LeftShift))
			{
				panAround *= 2.4f;
				panAcross *= 2.4f;
			}
			float keyStep = settings.keyPanSpeed * (ViewDistance(ring, distance) / 60f) * deltaTime;
			angle += panAround * keyStep / ring.Radius;
			across += panAcross * keyStep;
		}
		angle += pendingAngle;
		pendingAngle = 0f;
		across += pendingAcross;
		pendingAcross = 0f;
		distance = Mathf.Lerp(distance, desiredDistance, 1f - Mathf.Exp(-12f * deltaTime));
		if (ring.Geometry != null)
		{
			across = ClampAcross(settings, ring.Geometry, ViewDistance(ring, distance), across);
		}
		Apply(driver, ring);
	}

	public static void RestoreVanillaFieldOfView()
	{
		if (vanillaFieldOfView.HasValue && Find.WorldCamera != null)
		{
			Find.WorldCamera.fieldOfView = vanillaFieldOfView.Value;
			vanillaFieldOfView = null;
		}
	}

	public static float NearClipPlane(RingworldExtension settings)
	{
		return settings.cameraMinDistance * NearClipShareOfNearestZoom;
	}

	// Puts vanilla's near clip back once the ring view has stopped driving the camera.
	public static void RestoreVanillaNearClip()
	{
		if (nearClipSet && Find.WorldCamera != null)
		{
			Find.WorldCamera.nearClipPlane = OrbitViewCamera.VanillaNearClipPlane;
			nearClipSet = false;
		}
	}

	private static void Apply(WorldCameraDriver driver, RingLayer ring)
	{
		Camera camera = Find.WorldCamera;
		RingworldExtension settings = ring.Settings;
		if (!vanillaFieldOfView.HasValue)
		{
			vanillaFieldOfView = camera.fieldOfView;
		}
		camera.fieldOfView = settings.cameraFieldOfView;
		camera.nearClipPlane = NearClipPlane(settings);
		nearClipSet = true;
		Vector3 outward = RingGeometry.Outward(angle);
		camera.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
		camera.transform.position = ring.Origin + outward * (ring.Radius - ViewDistance(ring, distance)) + Vector3.up * across;
		// Keep vanilla's altitude in a plausible range: icon expansion and zoom-dependent drawing read AltitudePercent.
		float zoomPercent = Mathf.InverseLerp(settings.cameraMinDistance, FurthestZoom(ring), distance) * 0.2f;
		driver.altitude = Mathf.Lerp(WorldCameraDriver.MinAltitude, OrbitViewCamera.MaxAltitude, zoomPercent);
	}

	// The share of the view's height that the area beyond a rim (rim wall and empty space) may take at most.
	public const float MaxBeyondRimShare = 0.4f;

	// The view's height, as a share of the ring's full width (rim walls included), over which the pan limit eases to 0:
	// full below the first, 0 at the second. The furthest zoom fits the width with a margin, so it is past the second.
	public const float PanEaseFromWidthShare = 0.9f;

	public const float PanEaseToWidthShare = 1f;

	// The furthest the view may be from the midline at this distance: the top (or bottom) edge of the view is
	// MaxBeyondRimShare of the view's height past the rim line. Where the view is nearly tall enough to show the ring's
	// whole width, the limit eases to 0 (a ring that fits on screen has nothing to pan to, and the plain rule would still
	// allow a pan there). The view's height at the surface is 2 * distance * tan(half the field of view).
	public static float AcrossLimit(RingworldExtension settings, RingGeometry geometry, float distance)
	{
		float viewHeight = ViewHeight(settings, distance);
		float limit = Mathf.Max(0f, geometry.RimLine - (0.5f - MaxBeyondRimShare) * viewHeight);
		float fullWidth = 2f * WorldDrawLayer_RingThickness.WallSideY(geometry);
		float ease = Mathf.InverseLerp(PanEaseToWidthShare * fullWidth, PanEaseFromWidthShare * fullWidth, viewHeight);
		return limit * ease;
	}

	// The height of the view at the surface, distance in front of the camera.
	public static float ViewHeight(RingworldExtension settings, float distance)
	{
		return 2f * distance * Mathf.Tan(settings.cameraFieldOfView * 0.5f * Mathf.Deg2Rad);
	}

	public static float ClampAcross(RingworldExtension settings, RingGeometry geometry, float distance, float value)
	{
		float limit = AcrossLimit(settings, geometry, distance);
		return Mathf.Clamp(value, -limit, limit);
	}

	// Fully zoomed out, the ring's width, rim walls included, is this much smaller than the view's height.
	// The self-check asks for a smaller margin (RingViewMinFitMargin), so it fails only if the fit goes wrong.
	public const float WidthFitMargin = 1.1f;

	// The ground's furthest zoom (G): the def's, or further if that is needed to fit the ring's full width on screen.
	// The camera can back off at most to the ring's axis.
	public static float MaxDistance(RingLayer ring)
	{
		return MaxDistance(ring.Settings, ring.Geometry, ring.Radius, ScreenAspect);
	}

	// aspect: the screen's width over its height.
	public static float MaxDistance(RingworldExtension settings, RingGeometry geometry, float radius, float aspect)
	{
		float max = settings.cameraMaxDistance;
		if (max <= 0f)
		{
			return radius;
		}
		if (geometry != null)
		{
			max = Mathf.Max(max, WidthFitDistance(settings, geometry, aspect));
		}
		return Mathf.Min(max, radius);
	}

	// The stack's furthest zoom (F), past which the orbit view opens: the ground's furthest zoom times the def's
	// furthestZoomRatio, squeezed onto the axis if that would back the camera past it.
	public static float FurthestZoom(RingLayer ring)
	{
		RingLayer ground = ZoomStack(ring)[0];
		float[] bands = ZoomBands(ground.Settings, ground.Geometry, ground.Radius, ScreenAspect, 1);
		return bands[bands.Length - 1];
	}

	// The zoom bands of a stack of layerCount ring-shaped layers, ground first, as layerCount + 1 edges of distance from the
	// surface ring: layer i runs from edge i to edge i + 1. The ground runs from cameraMinDistance to G (to the stack's
	// furthest zoom F with no atmosphere layer); the atmosphere layers split G to F evenly in scroll ticks, i.e.
	// geometrically. An F past the axis squeezes every edge above the nearest zoom together so F lands on the axis.
	public static float[] ZoomBands(RingworldExtension settings, RingGeometry geometry, float radius, float aspect, int layerCount)
	{
		float ground = MaxDistance(settings, geometry, radius, aspect);
		float furthest = ground * Mathf.Max(settings.furthestZoomRatio, 1f);
		float[] edges = new float[layerCount + 1];
		edges[0] = settings.cameraMinDistance;
		int atmosphere = layerCount - 1;
		if (atmosphere <= 0)
		{
			edges[1] = furthest;
		}
		else
		{
			edges[1] = ground;
			for (int i = 1; i <= atmosphere; i++)
			{
				edges[i + 1] = ground * Mathf.Pow(furthest / ground, (float)i / atmosphere);
			}
		}
		float squeeze = Mathf.Min(1f, radius / furthest);
		for (int i = 1; i < edges.Length; i++)
		{
			edges[i] *= squeeze;
		}
		return edges;
	}

	// The ring-shaped layers that share the ring view's zoom, ground first: the surface ring and the ring-shaped layers
	// it zooms out to. Just the ring itself if it isn't among them.
	internal static List<RingLayer> ZoomStack(RingLayer ring)
	{
		List<RingLayer> stack = new List<RingLayer>();
		RingLayer layer = SurfaceRing.Current ?? ring;
		while (layer != null && !stack.Contains(layer))
		{
			stack.Add(layer);
			layer = layer.zoomOutToLayer as RingLayer;
		}
		if (!stack.Contains(ring))
		{
			stack.Clear();
			stack.Add(ring);
		}
		return stack;
	}

	private static float[] ZoomBands(List<RingLayer> stack)
	{
		RingLayer ground = stack[0];
		return ZoomBands(ground.Settings, ground.Geometry, ground.Radius, ScreenAspect, stack.Count);
	}

	// How far a layer's inner surface is from the camera, for a distance to the surface ring: nearer by how far the layer
	// sits above the ground.
	private static float ViewDistance(RingLayer ring, float stackDistance)
	{
		RingLayer ground = SurfaceRing.Current;
		return ring == null || ground == null ? stackDistance : stackDistance - (ground.Radius - ring.Radius);
	}

	// The distance nearest to wanted that is inside the layer's band.
	private static float ClampToBand(List<RingLayer> stack, float[] bands, RingLayer ring, float wanted)
	{
		int index = stack.IndexOf(ring);
		return Mathf.Clamp(wanted, bands[index] + BandEpsilon, bands[index + 1] - BandEpsilon);
	}

	// Keeps the camera and the selected layer in step. A layer picked some other way than by zooming moves the camera into
	// its band; zooming that may switch layers selects the layer whose band the camera is in; otherwise zoom stays in the
	// selected layer's band. Returns the layer to view.
	private static RingLayer FollowZoom(RingLayer ring)
	{
		List<RingLayer> stack = ZoomStack(ring);
		float[] bands = ZoomBands(stack);
		if (bandLayer != ring)
		{
			bandLayer = ring;
			distance = ClampToBand(stack, bands, ring, distance);
			desiredDistance = ClampToBand(stack, bands, ring, desiredDistance);
		}
		if (!MaySwitchLayer)
		{
			desiredDistance = ClampToBand(stack, bands, ring, desiredDistance);
			return ring;
		}
		desiredDistance = Mathf.Clamp(desiredDistance, bands[0], bands[bands.Length - 1]);
		int index = 0;
		while (index < stack.Count - 1 && distance >= bands[index + 1])
		{
			index++;
		}
		if (stack[index] != ring)
		{
			ring = stack[index];
			PlanetLayer.Selected = ring;
			bandLayer = ring;
		}
		return ring;
	}

	// The world camera's width over height; 16:9 until there is one.
	public static float ScreenAspect => Find.WorldCamera != null ? Find.WorldCamera.aspect : 16f / 9f;

	// The distance from the inner surface at which both rim walls' outer top corners are inside the view, with
	// WidthFitMargin. The wall tops curve around the camera, so they come nearest it, and look tallest, at the screen's
	// left and right edges: the view must fit them where the edge rays meet the wall tops' cylinder.
	// Along an edge ray at angle e from the view direction, a point k away is k cos e deep; it is on the cylinder of
	// radius r when the camera is c = sqrt(r^2 - k^2 sin^2 e) - k cos e from the axis.
	public static float WidthFitDistance(RingworldExtension settings, RingGeometry geometry, float aspect)
	{
		float tanHalfFov = Mathf.Tan(settings.cameraFieldOfView * 0.5f * Mathf.Deg2Rad);
		float edge = Mathf.Atan(aspect * tanHalfFov);
		float wallTopRadius = geometry.radius - WorldDrawLayer_RingThickness.WallHeight * geometry.tileSize;
		float neededDepth = WidthFitMargin * WorldDrawLayer_RingThickness.WallSideY(geometry) / tanHalfFov;
		float k = neededDepth / Mathf.Cos(edge);
		float underRoot = wallTopRadius * wallTopRadius - k * k * Mathf.Sin(edge) * Mathf.Sin(edge);
		if (underRoot <= 0f)
		{
			return geometry.radius;
		}
		float axisToCamera = Mathf.Sqrt(underRoot) - k * Mathf.Cos(edge);
		return geometry.radius - axisToCamera;
	}

	// The widest screen shape (width over height) every ring size the Create World page offers fits on, fully zoomed out.
	public const float WidestFittedAspect = 21f / 9f;

	// The most tiles across, up to RingSize.MaxTilesAcross, at which a ring this many tiles around still fits its full
	// width on a WidestFittedAspect screen: the camera can back off only as far as the ring's axis, so a ring that is
	// wide for its radius can't fit at all.
	public static int MaxTilesAcrossFitting(RingworldExtension settings, int tilesAround)
	{
		// The Advanced dialog asks every frame, so the last answer is kept.
		if (settings == fittingSettings && tilesAround == fittingTilesAround)
		{
			return fittingTilesAcross;
		}
		fittingSettings = settings;
		fittingTilesAround = tilesAround;
		fittingTilesAcross = SearchMaxTilesAcrossFitting(settings, tilesAround);
		return fittingTilesAcross;
	}

	private static RingworldExtension fittingSettings;

	private static int fittingTilesAround;

	private static int fittingTilesAcross;

	private static int SearchMaxTilesAcrossFitting(RingworldExtension settings, int tilesAround)
	{
		for (int tilesAcross = RingSize.MaxTilesAcross; tilesAcross > RingSize.MinTilesAcross; tilesAcross--)
		{
			RingSize size = new RingSize(tilesAround, tilesAcross);
			RingGeometry geometry = new RingGeometry(size, RingSize.FullLandCoverage, size.NewWorldShadowSquareCount);
			if (WidthFitDistance(settings, geometry, WidestFittedAspect) < geometry.radius)
			{
				return tilesAcross;
			}
		}
		return RingSize.MinTilesAcross;
	}

	private static void EnsureInitialized(RingLayer ring)
	{
		if (distance < 0f)
		{
			RingworldExtension settings = ring.Settings;
			distance = desiredDistance = Mathf.Clamp(settings.cameraStartDistance, settings.cameraMinDistance, FurthestZoom(ring));
		}
	}

	// Converts a mouse movement (UI pixels) into world units at the surface.
	private static float ScreenDistanceToWorld(float pixels, RingLayer ring)
	{
		float worldPerPixel = ViewHeight(ring.Settings, Distance) / UI.screenHeight;
		return pixels * worldPerPixel * ring.Settings.dragSensitivity;
	}

	// Converts a horizontal mouse movement (UI pixels) into an angle, so the surface under the cursor follows the mouse.
	private static float ScreenDistanceToAngle(float pixels, RingLayer ring)
	{
		return ScreenDistanceToWorld(pixels, ring) / ring.Radius;
	}
}
