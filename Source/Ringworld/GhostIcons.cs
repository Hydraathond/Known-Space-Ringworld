using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// One ghost icon: a world object whose icon is drawn, display only, in a view of another layer.
public readonly struct GhostIcon
{
	public readonly WorldObject Object;

	// The object's own layer, which the icon is drawn on.
	public readonly PlanetLayer Layer;

	// Where the icon is drawn: the object's draw position on its own layer.
	public readonly Vector3 Position;

	public GhostIcon(WorldObject worldObject)
	{
		Object = worldObject;
		Layer = worldObject.Tile.Layer;
		Position = worldObject.DrawPos;
	}
}

// Ghost icons: translucent, smaller copies of world objects' icons, drawn at each object's place on its own layer, so
// the player can steer by the ground (and the ring's atmosphere layers) from a view of another layer. This class alone
// decides which the current view shows (For); the drawing (Patch_ExpandableWorldObjectsUtility_GhostIcons) and the
// self-check (RingSelfCheck_GhostIcons) both read its answer. The rules:
// - A sun-centred orbit layer selected (the orbit view, the starting-site page's included): every iconed object on the
//   surface ring and on every atmosphere layer.
// - An atmosphere layer selected: the surface ring's only. LAO already shows the other atmosphere layers' objects there.
// - The surface ring, or any other layer, selected, or a planet world: none.
// - The "Show ghost icons" mod setting (RingworldSettings.ghostIcons) off: none, from any layer.
// An object is iconed when vanilla would draw its expanding icon on its own layer: its def has one, and the world
// view's icon settings (bases, other icons) don't hide it. One vanilla already shows in the view's background
// (VisibleInBackground, which LAO extends, read for the world selector's selected layer) is left out, so nothing is
// shown twice. Display only: nothing here is hoverable, clickable or
// selectable, and the objects and vanilla's own icon drawing are left alone; the ghost icons are an extra draw.
public static class GhostIcons
{
	// Of the icon's own colour: about 40% opacity.
	public const float Opacity = 0.4f;

	// Of the icon's normal on-screen size.
	public const float SizeFactor = 0.8f;

	// Vanilla's tint for the icon of an object with a map and no expanding material (ExpandableWorldObjectsUtility,
	// private).
	private static readonly Color HasMapColor = new Color(0.1f, 0.9f, 0.1f, 1f);

	private static readonly List<GhostIcon> drawBuffer = new List<GhostIcon>();

	// The ghost icons a view of the world with the layer selected shows, in no particular order.
	public static List<GhostIcon> For(World world, PlanetLayer selected)
	{
		List<GhostIcon> icons = new List<GhostIcon>();
		AddFor(world, selected, icons);
		return icons;
	}

	private static void AddFor(World world, PlanetLayer selected, List<GhostIcon> into)
	{
		RingLayer surface = SurfaceRing.Of(world?.grid);
		if (!WorldShape.settings.ghostIcons || surface == null || selected == null || world.worldObjects == null)
		{
			return;
		}
		bool fromOrbit = RingOrbit.IsSunCentred(selected);
		bool fromAtmosphere = selected is RingLayer { IsSurfaceRing: false };
		if (!fromOrbit && !fromAtmosphere)
		{
			return;
		}
		List<WorldObject> objects = world.worldObjects.AllWorldObjects;
		for (int i = 0; i < objects.Count; i++)
		{
			WorldObject worldObject = objects[i];
			PlanetLayer layer = worldObject.Tile.Layer;
			bool shown = layer == surface || (fromOrbit && layer is RingLayer { IsSurfaceRing: false });
			// One vanilla (or LAO, through VisibleInBackground) already shows in this view's background isn't duplicated.
			if (shown && IsIconed(worldObject) && !worldObject.VisibleInBackground)
			{
				into.Add(new GhostIcon(worldObject));
			}
		}
	}

	private static bool IsIconed(WorldObject worldObject)
	{
		return worldObject.def.expandingIcon && !ExpandableWorldObjectsUtility.HiddenByRules(worldObject);
	}

	// Draws the current view's ghost icons, in screen space as vanilla draws its expanding icons, furthest first.
	internal static void DrawOnGUI()
	{
		drawBuffer.Clear();
		AddFor(Find.World, PlanetLayer.Selected, drawBuffer);
		if (drawBuffer.Count == 0)
		{
			return;
		}
		Transform camera = Find.WorldCamera.transform;
		Vector3 cameraPosition = camera.position;
		drawBuffer.SortBy(icon => -(icon.Position - cameraPosition).sqrMagnitude);
		for (int i = 0; i < drawBuffer.Count; i++)
		{
			// The camera is inside the ring, whose inside is convex, so nothing in front of it hides an icon; one
			// behind it would project onto the screen mirrored.
			if (Vector3.Dot(drawBuffer[i].Position - cameraPosition, camera.forward) > 0f)
			{
				Draw(drawBuffer[i].Object);
			}
		}
		drawBuffer.Clear();
		GUI.color = Color.white;
	}

	// Vanilla's icon (ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI) at the ghost's opacity and size,
	// without its search highlight and targeting pulse.
	private static void Draw(WorldObject worldObject)
	{
		Color color = worldObject.ExpandingIconColor;
		color.a = Opacity;
		GUI.color = color;
		Rect rect = ExpandableWorldObjectsUtility.ExpandedIconScreenRect(worldObject, SizeFactor);
		if (worldObject.ExpandingIconFlipHorizontal)
		{
			rect.x = rect.xMax;
			rect.width *= -1f;
		}
		Material material = worldObject.ExpandingMaterial;
		if (material == null && worldObject is MapParent { HasMap: true })
		{
			MaterialRequest request = new MaterialRequest(null, ShaderDatabase.ExpandingIconUI, color) { needsMainTex = false, colorTwo = HasMapColor };
			material = MaterialPool.MatFrom(request);
		}
		Widgets.DrawTextureRotated(rect, worldObject.ExpandingIcon, worldObject.ExpandingIconRotation, material);
	}
}

// Draws the ghost icons just before vanilla's expanding icons, so the real icons of the selected layer are on top.
// Vanilla only calls it while the world is shown; it draws on repaint with world objects shown, as vanilla does.
[HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
public static class Patch_ExpandableWorldObjectsUtility_GhostIcons
{
	public static void Prefix()
	{
		if (!DebugViewSettings.drawWorldObjects || Event.current.type != EventType.Repaint || !SurfaceRing.InRingWorld)
		{
			return;
		}
		try
		{
			GhostIcons.DrawOnGUI();
		}
		catch (System.Exception e)
		{
			GUI.color = Color.white;
			Log.ErrorOnce($"[Ringworld] Drawing ghost icons threw: {e}", "Ringworld.GhostIcons".GetHashCode());
		}
	}
}
