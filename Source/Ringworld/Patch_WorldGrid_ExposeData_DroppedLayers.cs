using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// A layer whose def is gone (its mod was removed, as LAO's atmosphere layers and High Orbit are with LAO) is dropped by
// vanilla while the world loads, but the layers left keep their zooms and connections to it, so zooming would select a
// layer that isn't there. On a ring world they're cut before vanilla rebuilds the scenario's links. The game's saved
// scenario may link the ring and Odyssey's orbit only through the dropped layers (LAO rewired it), so if nothing joins
// them afterwards they're joined directly, both ways at no fuel, as vanilla's scenarios join a surface and its orbit.
[HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.ExposeData))]
public static class Patch_WorldGrid_ExposeData_DroppedLayers
{
	public static void Prefix(WorldGrid __instance)
	{
		if (Scribe.mode != LoadSaveMode.PostLoadInit || SurfaceRing.Of(__instance) == null)
		{
			return;
		}
		HashSet<PlanetLayer> kept = new HashSet<PlanetLayer>(__instance.PlanetLayers.Values);
		foreach (PlanetLayer layer in kept)
		{
			foreach (PlanetLayer dropped in layer.Connections.Keys.Where(other => !kept.Contains(other)).ToList())
			{
				layer.RemoveConnection(dropped);
			}
			if (layer.zoomInToLayer != null && !kept.Contains(layer.zoomInToLayer))
			{
				layer.zoomInToLayer = null;
			}
			if (layer.zoomOutToLayer != null && !kept.Contains(layer.zoomOutToLayer))
			{
				layer.zoomOutToLayer = null;
			}
		}
	}

	public static void Postfix(WorldGrid __instance)
	{
		RingLayer ring = SurfaceRing.Of(__instance);
		PlanetLayer orbit = __instance.Orbit;
		if (Scribe.mode != LoadSaveMode.PostLoadInit || ring == null || orbit == null || ring.HasConnectionPathTo(orbit))
		{
			return;
		}
		ring.AddConnection(orbit, 0f);
		orbit.AddConnection(ring, 0f);
		ring.zoomOutToLayer ??= orbit;
		orbit.zoomInToLayer ??= ring;
		Log.Message($"[Ringworld] Joined the ring and {RingOrbit.Describe(orbit)} directly: the layers between them are gone.");
	}
}
