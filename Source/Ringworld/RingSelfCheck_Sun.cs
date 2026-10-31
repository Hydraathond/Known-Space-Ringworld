using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Sun and lighting checks. That the sun glows at the centre of the orbit view, isn't hidden by the orbit layer and
// never shows in the ring view, and that nothing but the night shadow darkens the ring on screen, is checked by eye.
public static partial class RingSelfCheck
{
	// Fully zoomed out, the sun's glow is at least this share of the orbit view's half-height, so it can be seen.
	private const float SunMinShareOfView = 0.02f;

	// The sun glows at the ring's centre, and vanilla's own sun, which circles a planet, is gone. Without Odyssey there
	// is no orbit view to show it in; otherwise, for every orbit layer, the glow stays inside the layer's sphere, so the
	// orbit-view camera is never inside it, and fully zoomed out it is still big enough to see.
	private static void CheckSun(RingLayer ring, RingCheckResult result)
	{
		if (Find.WorldGrid.GlobalLayers.Count == 0)
		{
			// On load the world's global draw layers are only made after the save is read (WorldGrid.ExposeData,
			// PostLoadInit), after this check runs; the check after world generation and the debug action cover it.
			return;
		}
		GlobalDrawLayer_RingSun sun = null;
		foreach (WorldDrawLayerBase layer in Find.WorldGrid.GlobalLayers)
		{
			if (layer is GlobalDrawLayer_RingSun ringSun)
			{
				sun = ringSun;
			}
			else if (layer is GlobalDrawLayer_Sun && layer.Visible)
			{
				result.Fail("vanilla's sun is still drawn");
			}
		}
		if (sun == null)
		{
			result.Fail("the world has no ring sun draw layer");
			return;
		}
		float glow = GlobalDrawLayer_RingSun.GlowRadius(ring);
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			string name = RingOrbit.Describe(orbit);
			if (glow >= orbit.Radius)
			{
				result.Fail($"the sun's glow reaches {glow:F0} from the centre, past the sphere of {name} (radius {orbit.Radius:F0})");
			}
			float farthest = OrbitViewCamera.DistanceFromSun(orbit, ring, 1f);
			float halfHeight = farthest * Mathf.Tan(0.5f * OrbitViewCamera.FieldOfView * Mathf.Deg2Rad);
			if (glow < SunMinShareOfView * halfHeight)
			{
				result.Fail($"from {name} fully zoomed out, the sun's glow ({glow:F1}) is under {SunMinShareOfView:P0} of the view's half-height ({halfHeight:F0})");
			}
		}
	}

	// Vanilla's night side never shows on the ring (the night shadow replaces it): once vanilla has set the world's shader
	// globals for this frame, the sun direction the engine hands to the world shaders is zero, which they all draw as
	// full daylight with no highlight. This reads the globals the way the engine does (RimWorldWaylith
	// WorldRenderFeature.ResolveSunDirection).
	private static void CheckEvenLighting(RingLayer ring, RingCheckResult result)
	{
		if (Find.WorldSelector?.SelectedLayer == null)
		{
			// Straight after world generation nothing is selected and vanilla can't set the globals yet; the check on
			// load and the debug action cover it.
			return;
		}
		WorldRendererUtility.UpdateGlobalShadersParams();
		bool enabled = !Shader.TryGetGlobalFloat(ShaderPropertyIDs.PlanetSunLightEnabled, out float value) || value > 0.5f;
		if (enabled && Shader.TryGetGlobalVector(ShaderPropertyIDs.PlanetSunLightDirection, out Vector4 direction) && ((Vector3)direction).sqrMagnitude > 1e-6f)
		{
			result.Fail($"the world shaders are lit from sun direction {(Vector3)direction}, so the ring is drawn darker on the side facing away from it");
		}
	}
}
