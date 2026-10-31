using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// While a ring-shaped layer is the viewed layer (RingCamera.ActiveRing), RingCamera drives the world camera instead of
// the vanilla sphere orbit.
// Any other layer (e.g. Odyssey's orbit) keeps the vanilla camera.
[HarmonyPatch(typeof(WorldCameraDriver), nameof(WorldCameraDriver.Update))]
public static class Patch_WorldCameraDriver_Update
{
	public static bool Prefix(WorldCameraDriver __instance)
	{
		if (LongEventHandler.ShouldWaitForEvent || Find.World == null)
		{
			return true;
		}
		RingLayer ring = RingCamera.ActiveRing;
		if (ring == null)
		{
			RingCamera.RestoreVanillaFieldOfView();
			return true;
		}
		if (WorldRendererUtility.WorldBackgroundNow)
		{
			return true;
		}
		if (!Find.WorldInterface.everReset)
		{
			Find.WorldInterface.Reset();
		}
		RingCamera.Update(__instance, ring);
		return false;
	}

	// Runs whether or not vanilla's Update did, so the clip planes always match the view.
	public static void Postfix(WorldCameraDriver __instance)
	{
		if (Find.WorldCamera != null)
		{
			OrbitViewCamera.ApplyClipPlanes(Find.WorldCamera, __instance.AltitudePercent);
		}
	}
}

// Vanilla places the camera around the selected layer; in the orbit view of a ring world it is moved out to the
// scaled distance (see OrbitViewCamera).
[HarmonyPatch(typeof(WorldCameraDriver), "ApplyPositionToGameObject")]
public static class Patch_WorldCameraDriver_ApplyPositionToGameObject
{
	public static void Postfix(WorldCameraDriver __instance)
	{
		if (Find.World != null && Find.WorldCamera != null)
		{
			OrbitViewCamera.ScalePosition(Find.WorldCamera, __instance.AltitudePercent);
		}
	}
}

// Behind a map in orbit, the world background camera is kept inside the ring (see OrbitMapBackgroundCamera).
[HarmonyPatch(typeof(WorldCameraDriver), nameof(WorldCameraDriver.ApplyMapPositionToGameObject))]
public static class Patch_WorldCameraDriver_ApplyMapPositionToGameObject
{
	public static void Postfix()
	{
		if (Find.World != null && Find.WorldCamera != null)
		{
			OrbitMapBackgroundCamera.Apply(Find.WorldCamera);
		}
	}
}

// In the ring view RingCamera takes the input instead of vanilla, including zooming out to the orbit view and between
// ring-shaped layers. Zooming in from the orbit view is vanilla's, except on the Starting-site page, where vanilla's is gated on playing and RingCamera does it: when it switches to a ring-shaped layer, the ring view
// is pointed where the orbit camera was looking.
[HarmonyPatch(typeof(WorldCameraDriver), nameof(WorldCameraDriver.WorldCameraDriverOnGUI))]
public static class Patch_WorldCameraDriver_OnGUI
{
	public static bool Prefix(out PlanetLayer __state)
	{
		__state = null;
		if (Find.World == null || WorldRendererUtility.WorldBackgroundNow)
		{
			return true;
		}
		__state = PlanetLayer.Selected;
		RingLayer ring = RingCamera.ActiveRing;
		if (ring == null)
		{
			// Before play vanilla doesn't zoom in from the orbit view to the layer below.
			return !RingCamera.TryZoomInFromOrbit(Find.WorldCameraDriver);
		}
		RingCamera.HandleInput(ring);
		return false;
	}

	public static void Postfix(WorldCameraDriver __instance, PlanetLayer __state)
	{
		if (__state == null || !SurfaceRing.InRingWorld)
		{
			return;
		}
		// Only from a layer that isn't ring-shaped: between ring-shaped layers RingCamera has already moved the view.
		if (PlanetLayer.Selected is RingLayer ring && __state is not RingLayer && __state.zoomInToLayer == ring)
		{
			RingCamera.ArriveFromOrbit(ring, __instance.CurrentlyLookingAtPointOnSphere);
		}
	}
}

[HarmonyPatch(typeof(WorldCameraDriver), nameof(WorldCameraDriver.JumpTo), typeof(Vector3))]
public static class Patch_WorldCameraDriver_JumpTo
{
	public static bool Prefix(Vector3 newLookAt)
	{
		if (!SurfaceRing.InRingWorld)
		{
			return true;
		}
		if (!Find.WorldInterface.everReset)
		{
			Find.WorldInterface.Reset();
		}
		if (newLookAt != Vector3.zero)
		{
			RingCamera.LookAt(newLookAt);
		}
		return false;
	}
}

// "North up" has no meaning on the ring: its up is always the ring's axis.
[HarmonyPatch(typeof(WorldCameraDriver), nameof(WorldCameraDriver.RotateSoNorthIsUp))]
public static class Patch_WorldCameraDriver_RotateSoNorthIsUp
{
	public static bool Prefix()
	{
		return RingCamera.ActiveRing == null;
	}
}

// The orbit view on the Starting-site page is for getting around the ring: clicks there select nothing, so the tile picked
// in the ring view stays the starting tile.
[HarmonyPatch(typeof(WorldSelector), "SelectUnderMouse")]
public static class Patch_WorldSelector_SelectUnderMouse
{
	public static bool Prefix()
	{
		return !(RingCamera.OnStartingSitePage && RingCamera.ActiveRing == null);
	}
}

// A drag box would clear the selection before selecting anything.
[HarmonyPatch(typeof(WorldSelector), "SelectInsideDragBox")]
public static class Patch_WorldSelector_SelectInsideDragBox
{
	public static bool Prefix()
	{
		return !(RingCamera.OnStartingSitePage && RingCamera.ActiveRing == null);
	}
}

// Vanilla clears the selected tile when the selected layer changes. On the Starting-site page the picked tile has to
// survive the trip to the orbit view and back.
[HarmonyPatch(typeof(WorldSelector), nameof(WorldSelector.SelectedLayer), MethodType.Setter)]
public static class Patch_WorldSelector_SelectedLayer
{
	private static readonly AccessTools.FieldRef<WorldSelector, PlanetTile> SelectedTileField = AccessTools.FieldRefAccess<WorldSelector, PlanetTile>("selectedTile");

	public static void Prefix(WorldSelector __instance, out PlanetTile __state)
	{
		__state = RingCamera.OnStartingSitePage ? __instance.SelectedTile : PlanetTile.Invalid;
	}

	public static void Postfix(WorldSelector __instance, PlanetTile __state)
	{
		if (__state.Valid)
		{
			SelectedTileField(__instance) = __state;
		}
	}
}

// Leaving the page (Next or Back) from the orbit view puts the world view back on the ground.
[HarmonyPatch(typeof(Page_SelectStartingSite), nameof(Page_SelectStartingSite.PostClose))]
public static class Patch_PageSelectStartingSite_PostClose
{
	public static void Postfix()
	{
		if (Find.World != null && SurfaceRing.InRingWorld)
		{
			RingCamera.ReturnToGround();
		}
	}
}
