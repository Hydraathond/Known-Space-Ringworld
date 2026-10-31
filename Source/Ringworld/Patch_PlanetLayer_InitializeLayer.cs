using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld.Planet;
using Unity.Collections;
using UnityEngine;
using Verse;

namespace Ringworld;

// Replaces the icosphere build for RingLayer only. Mirrors vanilla PlanetLayer.InitializeLayer:
// dispose old data, fill the four packed arrays, then create draw layers, tile finder, pather and flood filler.
// Vanilla calls InitializeLayer both on world creation and when loading a save, so the ring is rebuilt the same way each time,
// from the layer's ring size, land coverage and shadow-square count; the surface ring's radius is set from that size here
// too, while an atmosphere layer keeps its own (RingAtmosphere).
// Before the draw layers are made, other mods' additions to the Surface def are copied onto the ring's (SurfaceAdditions).
[HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.InitializeLayer))]
public static class Patch_PlanetLayer_InitializeLayer
{
	private static readonly AccessTools.FieldRef<PlanetLayer, NativeArray<Vector3>> Verts = AccessTools.FieldRefAccess<PlanetLayer, NativeArray<Vector3>>("verts");

	private static readonly AccessTools.FieldRef<PlanetLayer, NativeArray<int>> TileIDToVertsOffsets = AccessTools.FieldRefAccess<PlanetLayer, NativeArray<int>>("tileIDToVerts_offsets");

	private static readonly AccessTools.FieldRef<PlanetLayer, NativeArray<int>> TileIDToNeighborsOffsets = AccessTools.FieldRefAccess<PlanetLayer, NativeArray<int>>("tileIDToNeighbors_offsets");

	private static readonly AccessTools.FieldRef<PlanetLayer, NativeArray<PlanetTile>> TileIDToNeighborsValues = AccessTools.FieldRefAccess<PlanetLayer, NativeArray<PlanetTile>>("tileIDToNeighbors_values");

	private static readonly AccessTools.FieldRef<PlanetLayer, float> LayerRadius = AccessTools.FieldRefAccess<PlanetLayer, float>("radius");

	private static readonly AccessTools.FieldRef<PlanetLayer, float> AverageTileSize = AccessTools.FieldRefAccess<PlanetLayer, float>("averageTileSize");

	private static readonly AccessTools.FieldRef<PlanetLayer, FastTileFinder> Finder = AccessTools.FieldRefAccess<PlanetLayer, FastTileFinder>("finder");

	private static readonly AccessTools.FieldRef<PlanetLayer, WorldPathing> Pather = AccessTools.FieldRefAccess<PlanetLayer, WorldPathing>("pather");

	private static readonly AccessTools.FieldRef<PlanetLayer, WorldFloodFiller> Filler = AccessTools.FieldRefAccess<PlanetLayer, WorldFloodFiller>("filler");

	public static bool Prefix(PlanetLayer __instance)
	{
		if (__instance is not RingLayer ring)
		{
			return true;
		}
		BuildRing(ring);
		return false;
	}

	private static void BuildRing(RingLayer ring)
	{
		ring.Dispose();
		RingworldExtension settings = ring.Settings;
		// An atmosphere layer's radius is set when it's made and saved by vanilla; the surface ring's follows from its size.
		RingGeometry geometry = new RingGeometry(ring.Size, ring.LandCoverage, ring.ShadowSquareCount, ring.HasOwnRadius ? ring.Radius : null);
		LayerRadius(ring) = geometry.radius;
		ring.Geometry = geometry;
		List<Vector3> verts = new List<Vector3>(geometry.TileCount * 6);
		List<int> vertOffsets = new List<int>(geometry.TileCount);
		List<int> neighborOffsets = new List<int>(geometry.TileCount);
		List<PlanetTile> neighborValues = new List<PlanetTile>(geometry.TileCount * 6);
		// A layer whose def is gone (an atmosphere layer with LAO removed) gets no tiles: vanilla drops it once the world
		// has loaded, and its tile cache must not look for tiles it never loads.
		float averageTileSize = 1f;
		if (ring.Def != null)
		{
			geometry.Build(ring.LayerID, settings.flipWinding, verts, vertOffsets, neighborOffsets, neighborValues, out averageTileSize);
		}
		Verts(ring) = NativeArrayUtility.GetNativeArrayCopy(verts, Allocator.Persistent);
		TileIDToVertsOffsets(ring) = NativeArrayUtility.GetNativeArrayCopy(vertOffsets, Allocator.Persistent);
		TileIDToNeighborsOffsets(ring) = NativeArrayUtility.GetNativeArrayCopy(neighborOffsets, Allocator.Persistent);
		TileIDToNeighborsValues(ring) = NativeArrayUtility.GetNativeArrayCopy(neighborValues, Allocator.Persistent);
		AverageTileSize(ring) = averageTileSize;
		// Other mods' Surface additions belong to the ground. The layer isn't marked as the root surface yet on a new
		// world, so go by its def, which only the surface ring has.
		if (SurfaceIdentity.IsSurface(ring.Def))
		{
			SurfaceAdditions.CopyOnto(ring.Def);
		}
		ring.WorldDrawLayers.Clear();
		foreach (Type drawLayerType in ring.Def?.worldDrawLayers ?? new List<Type>())
		{
			WorldDrawLayer drawLayer = (WorldDrawLayer)Activator.CreateInstance(drawLayerType);
			drawLayer.Initialize(ring);
			ring.WorldDrawLayers.Add(drawLayer);
		}
		Finder(ring) = new FastTileFinder(ring);
		Pather(ring) = new WorldPathing(ring);
		Filler(ring) = new WorldFloodFiller(ring);
		Log.Message($"[Ringworld] Built ring layer {ring.LayerID} ({ring.Def?.defName}): {geometry.tilesAround} x {geometry.tilesAcross}, land coverage {ring.LandCoverage:P0}: {geometry.liveTilesAround} x {geometry.tilesAcross} = {geometry.TileCount} tiles, radius {geometry.radius}, tile size {averageTileSize:F2}, flipWinding {settings.flipWinding}.");
	}
}
