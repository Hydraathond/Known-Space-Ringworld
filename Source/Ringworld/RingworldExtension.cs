using Verse;

namespace Ringworld;

// Ring rendering and camera settings, attached to the ring's PlanetLayerDef as a mod extension.
// The ring's size is per-world state (RingLayer.Size), not a def setting.
public class RingworldExtension : DefModExtension
{
	public static readonly RingworldExtension Default = new RingworldExtension();

	// The surface ring def's settings, or Default before defs are loaded (the mod's constructor reads the last ring size).
	public static RingworldExtension OfSurfaceRing => SurfaceIdentity.RingDefIfLoaded?.GetModExtension<RingworldExtension>() ?? Default;

	// Reverses the corner order of every tile. Use it if the ring turns out invisible or unclickable from the inside.
	public bool flipWinding;

	public float cameraFieldOfView = 50f;

	// Camera distance from the inner surface, in world units.
	public float cameraMinDistance = 10f;

	// 0 = up to the ring radius (camera on the axis). Large rings need a cap, or zooming out shows a thin strip.
	public float cameraMaxDistance;

	// The ring view's furthest zoom (where the orbit view opens) as a multiple of the ground's, cameraMaxDistance or the
	// width fit. The atmosphere layers' zoom bands split the range between the two evenly.
	public float furthestZoomRatio = 3.4f;

	public float cameraStartDistance = 60f;

	public float dragSensitivity = 1f;

	public float keyPanSpeed = 60f;
}
