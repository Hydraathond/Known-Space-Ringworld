using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Worldbuilder is a soft dependency: its methods are looked up by name and patched only when it's active. A world made
// from one of its presets gets the preset's saved terrain written over it tile by tile, and its settlements and map
// markers placed at their saved tile ids, with no check that the preset was saved from a world of this shape. A preset
// that doesn't fit the ring (another tile count, or a ring of another shape) breaks it: a smaller one throws partway
// through the terrain, a larger one or another shape scrambles it, and a settlement past the ring's last tile throws.
// So on a ring, a preset that doesn't fit has its terrain (with the world info restored alongside it), settlements and
// map markers skipped, with one warning, and the ring keeps its own. Planet worlds, and presets saved from a ring of
// this shape, restore as Worldbuilder does. Its own Create World page gets the shape rows too (WorldbuilderPageRows).
// Without Worldbuilder none of this runs or logs.
//   RW_DEV_WB_UNPATCHED=1  applies none of it, to see Worldbuilder's own behaviour on a ring world
[StaticConstructorOnStartup]
public static class WorldbuilderCompatibility
{
	private const string PackageId = "ferny.Worldbuilder";

	public const string HarmonyId = "ringworld.worldbuilder";

	public static readonly bool Active = ModsConfig.IsActive(PackageId);

	// Worldbuilder's restores of a preset's tile data, each taking the preset as "preset" and run from its
	// World.FinalizeInit postfix on a newly generated world with a preset chosen.
	public static readonly string[] Restores = { "RestoreTerrain", "RestoreBases", "RestoreMapMarkets" };

	// The last preset and ring decided on, so the restores of one preset share one decision and one warning.
	private static object decidedPreset;

	private static RingLayer decidedRing;

	private static bool decidedFits;

	public static MethodInfo Restore(string name)
	{
		return AccessTools.Method(AccessTools.TypeByName("Worldbuilder.World_FinalizeInit_Patch"), name);
	}

	static WorldbuilderCompatibility()
	{
		if (!Active || Environment.GetEnvironmentVariable("RW_DEV_WB_UNPATCHED") == "1")
		{
			return;
		}
		Harmony harmony = new Harmony(HarmonyId);
		List<string> problems = new List<string>();
		foreach (string name in Restores)
		{
			try
			{
				MethodInfo target = Restore(name);
				if (target == null)
				{
					problems.Add($"World_FinalizeInit_Patch.{name} not found");
					continue;
				}
				harmony.Patch(target, prefix: new HarmonyMethod(typeof(WorldbuilderCompatibility), nameof(Prefix)));
			}
			catch (Exception e)
			{
				problems.Add($"{name}: {e.Message}");
			}
		}
		try
		{
			WorldbuilderPageRows.Apply(harmony, problems);
		}
		catch (Exception e)
		{
			problems.Add($"Create World page: {e.Message}");
		}
		if (problems.Count > 0)
		{
			Log.Warning($"[Ringworld] Worldbuilder patch incomplete ({string.Join("; ", problems)}); a preset saved from another world may break a ring world, or its Create World page may not offer one.");
		}
	}

	// The preset is Worldbuilder's WorldPreset, taken as object since its type is only known at runtime.
	private static bool Prefix(object preset)
	{
		if (SurfaceRing.Current is not { } ring)
		{
			return true;
		}
		if (preset != decidedPreset || ring != decidedRing)
		{
			decidedPreset = preset;
			decidedRing = ring;
			string misfit = Misfit(preset, ring);
			decidedFits = misfit == null;
			if (misfit != null)
			{
				string label = Traverse.Create(preset).Property("Label").GetValue<string>();
				Log.Warning($"[Ringworld] Worldbuilder preset '{label}' {misfit}; its terrain, world info, settlements and map markers aren't restored, so the ring keeps its own.");
			}
		}
		return decidedFits;
	}

	// Why the preset doesn't fit the ring, or null if it does. Its terrain shows which world it was saved from: its
	// hilliness has one byte per tile, as vanilla serializes it, and a ring of this shape has only wall foot and water on
	// its rim rows (see WallFootMatches). Without saved terrain, there's no knowing.
	private static string Misfit(object preset, RingLayer ring)
	{
		Traverse traverse = Traverse.Create(preset);
		// TerrainData creates an empty terrain file when there's none, so it's only read for a preset that saved terrain.
		byte[] hilliness = traverse.Field("saveTerrain").GetValue<bool>() ? Traverse.Create(traverse.Property("TerrainData").GetValue()).Field("tileHilliness").GetValue<byte[]>() : null;
		if (hilliness == null)
		{
			return "has no saved terrain, so which world it was saved from is unknown";
		}
		if (hilliness.Length != ring.TilesCount)
		{
			return $"was saved from a world of {hilliness.Length:N0} tiles, and this ring has {ring.TilesCount:N0}";
		}
		if (ring.Geometry != null && !WallFootMatches(hilliness, ring.Geometry))
		{
			return $"has this ring's {ring.TilesCount:N0} tiles but was saved from a ring of another shape (its wall foot is elsewhere)";
		}
		return null;
	}

	// On a ring of this shape every rim-row tile is wall foot (Impassable) or water (which vanilla terrain makes Flat).
	// Tile ids run row by row, so a ring of another shape with the same tile count has its rims at other ids: shorter
	// rows put them inside this ring's rim rows, where natural terrain has hills and mountains too, and longer rows also
	// take in the row next to each rim, which then is Impassable wherever the rim row is, as natural terrain never is.
	// A ring under 3 rows has no such row.
	private static bool WallFootMatches(byte[] hilliness, RingGeometry geometry)
	{
		int wallFoot = 0;
		int impassableInside = 0;
		foreach (int id in geometry.RimTiles())
		{
			if (hilliness[id] == (byte)Hilliness.Flat)
			{
				continue;
			}
			if (hilliness[id] != (byte)Hilliness.Impassable)
			{
				return false;
			}
			wallFoot++;
			if (geometry.tilesAcross > 2 && hilliness[geometry.RowOf(id) == 0 ? id + geometry.liveTilesAround : id - geometry.liveTilesAround] == (byte)Hilliness.Impassable)
			{
				impassableInside++;
			}
		}
		return geometry.tilesAcross <= 2 || impassableInside < wallFoot;
	}
}
