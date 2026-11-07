using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Orbit icons (OrbitIcons): an orbit-layer object's icon at OrbitIcons.SizeFactor of vanilla's, a surface or
// atmosphere-layer object's at vanilla's.
public static partial class RingSelfCheck
{
	// Of an icon's width, in pixels.
	private const float IconSizeTolerance = 0.01f;

	// The object's tile, written directly so a probe object never touches a layer's tile finder.
	private static readonly AccessTools.FieldRef<WorldObject, PlanetTile> WorldObjectTileField = AccessTools.FieldRefAccess<WorldObject, PlanetTile>("tile");

	// On every sun-centred orbit layer, the ring and LAO's atmosphere layers, a probe object on the layer's first tile,
	// plus every real world object on those layers, gets an icon rect of the expected size (vanilla's times SizeFactor
	// on an orbit layer, vanilla's on the ring-shaped layers) centred where vanilla centres it. Without Odyssey there's no
	// orbit layer, and only the ring-shaped layers' icons are checked.
	private static void CheckOrbitIconSize(RingLayer ring, RingCheckResult result)
	{
		// While a save loads, world objects' layers aren't resolved yet; the dev harness and the debug action check later.
		if (Scribe.mode != LoadSaveMode.Inactive || Find.WorldObjects == null)
		{
			return;
		}
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			CheckIconSizesOn(orbit, OrbitIcons.SizeFactor, result);
		}
		foreach (PlanetLayer layer in Find.WorldGrid.PlanetLayers.Values)
		{
			if (layer is RingLayer)
			{
				CheckIconSizesOn(layer, 1f, result);
			}
		}
	}

	private static void CheckIconSizesOn(PlanetLayer layer, float factor, RingCheckResult result)
	{
		List<WorldObject> objects = new List<WorldObject> { IconProbe(layer) };
		objects.AddRange(Find.WorldObjects.AllWorldObjectsOnLayer(layer).Where(worldObject => worldObject.def.expandingIcon));
		foreach (WorldObject worldObject in objects)
		{
			string fault = IconSizeFault(worldObject, factor);
			if (fault != null)
			{
				result.Fail($"{worldObject.def.defName} on {layer.Def.defName} (layer {layer.LayerID}): {fault}");
			}
		}
	}

	// A world object with vanilla's settlement icon on the layer's first tile, made without an ID and never spawned.
	internal static WorldObject IconProbe(PlanetLayer layer)
	{
		WorldObject probe = new WorldObject { def = WorldObjectDefOf.Settlement };
		WorldObjectTileField(probe) = new PlanetTile(0, layer);
		return probe;
	}

	// Null when the object's icon rect is factor x vanilla's size (ExpandableWorldObjectsUtility.ExpandedIconScreenRect's
	// own formula) and centred on its screen position, otherwise what's wrong.
	internal static string IconSizeFault(WorldObject worldObject, float factor)
	{
		float vanillaSize = 30f * worldObject.def.expandingIconDrawSize;
		if (worldObject.ExpandMore)
		{
			vanillaSize = Mathf.Lerp(vanillaSize, vanillaSize * 1.35f, ExpandableWorldObjectsUtility.ExpandMoreTransitionPct);
		}
		Rect rect = ExpandableWorldObjectsUtility.ExpandedIconScreenRect(worldObject);
		Vector2 screenPos = worldObject.ScreenPos();
		if (Mathf.Abs(rect.width - vanillaSize * factor) > IconSizeTolerance || Mathf.Abs(rect.height - vanillaSize * factor) > IconSizeTolerance)
		{
			return $"icon is {rect.width:F2} x {rect.height:F2}, expected {vanillaSize * factor:F2} ({factor:F2} x vanilla's {vanillaSize:F2})";
		}
		if ((rect.center - screenPos).magnitude > IconSizeTolerance)
		{
			return $"icon is centred at {rect.center}, its screen position is {screenPos}";
		}
		return null;
	}
}
