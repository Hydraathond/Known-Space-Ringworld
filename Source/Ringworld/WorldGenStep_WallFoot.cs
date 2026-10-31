using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Turns the land of the first and last rows of the ring into the wall foot: impassable mountains that keep their climate
// biome. Rim-row water is left as it is, so a sea that reaches a rim (the ocean at a cut included) meets the rim wall
// directly, and the wall foot ends at the coast.
// Runs right after Terrain (see RW_WallFoot), so lakes, rivers, roads and settlements all see the wall foot; vanilla
// already refuses to settle or route caravans through impassable tiles.
// Tile data is saved with the world, so this only runs on generation.
public class WorldGenStep_WallFoot : WorldGenStep
{
	// Vanilla's own biome choice, called rather than copied so that Harmony patches on it (Worldbuilder's biome sliders)
	// choose the wall foot's biome too. It's a private instance method that uses no instance state, so any instance will do.
	private static readonly Func<Tile, PlanetTile, PlanetLayer, BiomeDef> VanillaBiomeFrom = FindVanillaBiomeFrom();

	public override int SeedPart => 518290374;

	public override void GenerateFresh(string seed, PlanetLayer layer)
	{
		if (layer is not RingLayer { IsSurfaceRing: true } ring || ring.Geometry == null)
		{
			return;
		}
		foreach (int id in ring.Geometry.RimTiles())
		{
			SurfaceTile tile = ring[id];
			if (tile != null && !tile.WaterCovered)
			{
				MakeWallFoot(tile, ring);
			}
		}
	}

	// What vanilla terrain would have made of the tile had it rolled impassable mountains: no swampiness (only flat and
	// small hills get any), and the biome scored again, since some biomes depend on hilliness.
	private static void MakeWallFoot(SurfaceTile tile, PlanetLayer layer)
	{
		tile.hilliness = Hilliness.Impassable;
		tile.swampiness = 0f;
		tile.PrimaryBiome = BiomeFrom(tile, layer);
	}

	private static BiomeDef BiomeFrom(Tile tile, PlanetLayer layer)
	{
		return VanillaBiomeFrom != null ? VanillaBiomeFrom(tile, tile.tile, layer) : FallbackBiomeFrom(tile, layer);
	}

	private static Func<Tile, PlanetTile, PlanetLayer, BiomeDef> FindVanillaBiomeFrom()
	{
		MethodInfo method = AccessTools.Method(typeof(WorldGenStep_Terrain), "BiomeFrom", new[] { typeof(Tile), typeof(PlanetTile), typeof(PlanetLayer) });
		if (method == null || method.ReturnType != typeof(BiomeDef))
		{
			Log.Error("[Ringworld] Couldn't find WorldGenStep_Terrain.BiomeFrom; the wall foot chooses its biome with vanilla's scoring, so mods that change biome choice (Worldbuilder's biome sliders) don't apply to it.");
			return null;
		}
		return AccessTools.MethodDelegate<Func<Tile, PlanetTile, PlanetLayer, BiomeDef>>(method, new WorldGenStep_Terrain());
	}

	// Vanilla's BiomeFrom as of 1.7, for when it can't be found.
	private static BiomeDef FallbackBiomeFrom(Tile tile, PlanetLayer layer)
	{
		BiomeDef best = null;
		float bestScore = 0f;
		foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
		{
			if (!biome.implemented || !biome.generatesNaturally || !biome.Worker.CanPlaceOnLayer(biome, layer))
			{
				continue;
			}
			float score = biome.Worker.GetScore(biome, tile, tile.tile);
			if (score > bestScore || best == null)
			{
				best = biome;
				bestScore = score;
			}
		}
		return best;
	}
}
