using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// The ring is lit by the sun on its axis, and night falls under the shadow squares. Vanilla lights the world from a sun
// direction that circles the planet, which on the ring would draw half of it darker. With the planet sun light off, the
// engine hands every world shader a zero sun direction (RimWorldWaylith WorldRenderFeature.ResolveSunDirection), and
// they all draw that as full daylight with no highlight: terrain, ocean, rivers, roads, pollution and overlays alike, on
// every layer of a ring world. The night shadow takes its place, wherever vanilla would show its night side: always in
// the orbit view and on orbit-map backgrounds, and in the ring view only with the planet day/night setting on. Day and
// night on colony maps are separate and stay vanilla (ADR 0001).
//
// Vanilla calls this once a frame before drawing the world, so the materials are also switched here, before the
// first frame of any world.
[HarmonyPatch(typeof(WorldRendererUtility), nameof(WorldRendererUtility.UpdateGlobalShadersParams))]
public static class Patch_WorldRendererUtility_UpdateGlobalShadersParams
{
	public static void Postfix()
	{
		RingMaterials.Sync(Find.World);
		if (SurfaceRing.Current?.Geometry is not RingGeometry geometry)
		{
			Shader.SetGlobalVector(RingNightShadow.PropertyID, Vector4.zero);
			return;
		}
		// Vanilla's own rule for showing its night side, read before it's switched off.
		bool nightShown = !Shader.TryGetGlobalFloat(ShaderPropertyIDs.PlanetSunLightEnabled, out float enabled) || enabled > 0.5f;
		Shader.SetGlobalFloat(ShaderPropertyIDs.PlanetSunLightEnabled, 0f);
		Shader.SetGlobalVector(RingNightShadow.PropertyID, nightShown ? RingNightShadow.UniformAt(RingLocalTime.NowTicksAbs(), geometry.nightZones) : Vector4.zero);
	}
}

// Vanilla's sun is a glow in the sky that circles a planet. A ring world's sun is the one at its centre, so vanilla's
// is never drawn there, in the ring view or the orbit view.
[HarmonyPatch(typeof(WorldDrawLayerBase), nameof(WorldDrawLayerBase.Visible), MethodType.Getter)]
public static class Patch_WorldDrawLayerBase_Visible
{
	public static void Postfix(WorldDrawLayerBase __instance, ref bool __result)
	{
		if (__result && __instance is GlobalDrawLayer_Sun && SurfaceRing.InRingWorld)
		{
			__result = false;
		}
	}
}
