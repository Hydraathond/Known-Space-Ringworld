using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// With Ring picked, a new world's root surface layer is the ring, whatever the scenario. Vanilla builds the layers
// from the scenario's parts and makes the root surface from Scenario.surfaceLayer. While that runs, the part is
// swapped for a ring copy with the same tag and connections (so Odyssey's orbit layer still connects to it), and the
// original is put back afterwards: the scenario is often the def's own object, shared by every game.
// A save's layers are loaded as they were saved, but vanilla also adds any the scenario has gained since, such as the
// ones Layered Atmosphere and Orbit adds. On a ring world with it active, new or loaded, the scenario's parts are
// swapped too: for the ring, Odyssey's orbit and the optional layers ticked in LAO's layer list that a ring supports
// (LaoIntegration.RingParts), so a loaded ring world gains the ticked layers it lacks and keeps the rest.
[HarmonyPatch(typeof(WorldGrid), "CreateRequiredLayers")]
public static class Patch_WorldGrid_CreateRequiredLayers
{
	private static readonly AccessTools.FieldRef<Scenario, ScenPart_PlanetLayer> SurfaceLayer = AccessTools.FieldRefAccess<Scenario, ScenPart_PlanetLayer>("surfaceLayer");

	private static readonly AccessTools.FieldRef<Scenario, List<ScenPart>> Parts = AccessTools.FieldRefAccess<Scenario, List<ScenPart>>("parts");

	public static void Prefix(WorldGrid __instance, out (ScenPart_PlanetLayer surface, List<ScenPart> parts) __state)
	{
		__state = default;
		Scenario scenario = Find.Scenario;
		bool creatingRing = Current.CreatingWorld != null && WorldShape.Ring;
		bool loadedRingWithLao = LaoIntegration.Active && SurfaceRing.Of(__instance) != null;
		if (scenario == null || !(creatingRing || loadedRingWithLao))
		{
			return;
		}
		ScenPart_PlanetLayer surface = SurfaceLayer(scenario);
		if (surface == null)
		{
			return;
		}
		__state = (surface, Parts(scenario));
		if (creatingRing)
		{
			SurfaceLayer(scenario) = new ScenPart_PlanetLayer
			{
				def = surface.def,
				layer = DefDatabase<PlanetLayerDef>.GetNamed("RW_Ring"),
				settingsDef = DefDatabase<PlanetLayerSettingsDef>.GetNamed("RW_Ring"),
				hide = surface.hide,
				tag = surface.tag,
				connections = surface.connections,
			};
		}
		if (LaoIntegration.Active)
		{
			Parts(scenario) = LaoIntegration.RingParts(Parts(scenario), SurfaceLayer(scenario), out ScenPart_PlanetLayer ringSurface);
			SurfaceLayer(scenario) = ringSurface;
		}
	}

	public static void Finalizer((ScenPart_PlanetLayer surface, List<ScenPart> parts) __state)
	{
		if (__state.surface != null)
		{
			SurfaceLayer(Find.Scenario) = __state.surface;
			Parts(Find.Scenario) = __state.parts;
		}
	}
}
