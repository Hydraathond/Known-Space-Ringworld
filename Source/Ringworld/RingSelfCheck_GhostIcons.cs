using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Ghost icons (GhostIcons): which objects each view shows, where, and that none of them can be picked.
public static partial class RingSelfCheck
{
	// LAO's atmosphere layer defs, by name (the atmosphere checks' list), so the expected sets don't go through the
	// module's own layer test.
	private static readonly HashSet<string> LaoAtmosphereDefNames = new HashSet<string>(AtmosphereLayers.Select(layer => layer.def));

	// Written directly so the check selects a layer without the selected-layer setter's camera moves.
	private static readonly AccessTools.FieldRef<WorldSelector, PlanetLayer> SelectedLayerField = AccessTools.FieldRefAccess<WorldSelector, PlanetLayer>("selectedLayer");

	// From every layer of the world selected in turn, the module's answer is exactly the expected objects: from a
	// sun-centred orbit layer the iconed objects on the surface ring and LAO's atmosphere layers, from an atmosphere
	// layer the surface ring's, from any other layer (the surface ring included) none, leaving out any object vanilla
	// already shows in the view's background. Each ghost is within a tile of its object's tile centre on the object's
	// own layer, and the object is hidden from vanilla's hover, click and
	// drag-box picking in that view (off the selected layer and not shown in its background, so
	// WorldObjectSelectionUtility.HiddenBehindTerrainNow), so a ghost never selects it. With the "Show ghost icons"
	// setting then turned off, the answer from the same layer is empty; a logged line names the layers (the orbit and
	// atmosphere layers) whose icons that hid. The setting is put back after.
	private static void CheckGhostIcons(RingLayer ring, RingCheckResult result)
	{
		// While a save loads, world objects' layers aren't resolved yet; the dev harness and the debug action check later.
		if (Scribe.mode != LoadSaveMode.Inactive || Find.WorldObjects == null || Find.WorldSelector == null)
		{
			return;
		}
		WorldSelector selector = Find.WorldSelector;
		PlanetLayer savedSelected = SelectedLayerField(selector);
		bool savedSetting = WorldShape.settings.ghostIcons;
		List<string> hidden = new List<string>();
		try
		{
			foreach (PlanetLayer selected in Find.WorldGrid.PlanetLayers.Values)
			{
				SelectedLayerField(selector) = selected;
				WorldShape.settings.ghostIcons = true;
				int shownWhenOn = CheckGhostIconsFrom(ring, selected, result);
				WorldShape.settings.ghostIcons = false;
				int shownWhenOff = GhostIcons.For(Find.World, selected).Count;
				if (shownWhenOff > 0)
				{
					result.Fail($"with the ghost-icon setting off, from {selected.Def.defName} (layer {selected.LayerID}), {shownWhenOff} ghost icons are still shown");
				}
				else if (shownWhenOn > 0)
				{
					hidden.Add($"{selected.Def.defName} ({shownWhenOn})");
				}
			}
		}
		finally
		{
			WorldShape.settings.ghostIcons = savedSetting;
			SelectedLayerField(selector) = savedSelected;
		}
		Log.Message($"[Ringworld]   Ghost icons with the setting off: none from any layer; hidden from {(hidden.Count > 0 ? string.Join(", ", hidden) : "no layer")}.");
	}

	// Returns how many ghost icons the module's answer has.
	private static int CheckGhostIconsFrom(RingLayer ring, PlanetLayer selected, RingCheckResult result)
	{
		string from = $"from {selected.Def.defName} (layer {selected.LayerID})";
		bool fromOrbit = selected is OrbitLayer && (selected.Origin - ring.Origin).magnitude < 0.01f;
		bool fromAtmosphere = selected != ring && LaoAtmosphereDefNames.Contains(selected.Def.defName);
		HashSet<WorldObject> expected = new HashSet<WorldObject>();
		foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
		{
			if (!worldObject.def.expandingIcon || ExpandableWorldObjectsUtility.HiddenByRules(worldObject) || worldObject.VisibleInBackground)
			{
				continue;
			}
			PlanetLayer layer = worldObject.Tile.Layer;
			bool onAtmosphere = layer != ring && LaoAtmosphereDefNames.Contains(layer.Def.defName);
			if (((fromOrbit || fromAtmosphere) && layer == ring) || (fromOrbit && onAtmosphere))
			{
				expected.Add(worldObject);
			}
		}
		// Every world has settlements on the ring, so an orbit view with nothing to expect means the expected set is wrong.
		if (fromOrbit && expected.Count == 0 && Find.WorldObjects.Settlements.Any(settlement => settlement.Tile.Layer == ring))
		{
			result.Fail($"{from}, no iconed object was expected, though the ring has settlements");
		}
		List<GhostIcon> icons = GhostIcons.For(Find.World, selected);
		HashSet<WorldObject> shown = new HashSet<WorldObject>(icons.Select(icon => icon.Object));
		if (shown.Count != icons.Count)
		{
			result.Fail($"{from}, an object has more than one ghost icon");
		}
		List<WorldObject> missing = expected.Where(worldObject => !shown.Contains(worldObject)).ToList();
		List<WorldObject> extra = shown.Where(worldObject => !expected.Contains(worldObject)).ToList();
		if (missing.Count > 0)
		{
			result.Fail($"{from}, {missing.Count} of {expected.Count} expected objects have no ghost icon, e.g. {DescribeGhostObject(missing[0])}");
		}
		if (extra.Count > 0)
		{
			result.Fail($"{from}, {extra.Count} objects have a ghost icon they shouldn't, e.g. {DescribeGhostObject(extra[0])}");
		}
		if (icons.Count > 0)
		{
			int onRing = icons.Count(icon => icon.Layer == ring);
			Log.Message($"[Ringworld]   Ghost icons {from}: {icons.Count}, {onRing} of the surface ring's and {icons.Count - onRing} of atmosphere layers'.");
		}
		foreach (GhostIcon icon in icons)
		{
			WorldObject worldObject = icon.Object;
			PlanetTile tile = worldObject.Tile;
			// The object's tile centre on its own layer, not the icon's layer or position, which the module fills in: an
			// icon placed on another layer would be far from it.
			Vector3 tileCentre = tile.Layer.GetTileCenter(tile);
			if ((icon.Position - tileCentre).magnitude > tile.Layer.AverageTileSize)
			{
				result.Fail($"{from}, {DescribeGhostObject(worldObject)}'s ghost icon is at {icon.Position}, more than a tile from its tile's centre {tileCentre} on {tile.LayerDef.defName}");
			}
			// Vanilla's picking skips an object hidden behind terrain, which one off the selected layer and not shown in
			// its background always is, wherever the camera is.
			if (worldObject.Tile.Layer == selected || worldObject.VisibleInBackground)
			{
				result.Fail($"{from}, {DescribeGhostObject(worldObject)} has a ghost icon but vanilla shows it to hover, click and drag-box selection");
			}
		}
		return icons.Count;
	}

	private static string DescribeGhostObject(WorldObject worldObject)
	{
		return $"{worldObject.def.defName} {worldObject.ID} on {worldObject.Tile.LayerDef.defName} tile {worldObject.Tile.tileId}";
	}
}
