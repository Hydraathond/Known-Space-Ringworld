using System;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Where quests put their sites. Quests look for a site near a player's tile, and one that can't be in space, searched
// for from an orbit layer, goes on the surface: on a ring world that's the ring.
public static partial class RingSelfCheck
{
	// Searches from each orbit layer, from tiles spread over the layer, each with its own random seed.
	private const int SiteSearchesPerLayer = 8;

	// No search for a site that can't be in space, from a tile of any orbit layer, throws, every site found is on the
	// ring, and from each layer at least one is found. LAO's site finder looks the surface up by a random def of its
	// Surface group, vanilla's Surface or the ring's, and a world has a layer of only one of them. A single search may find nothing, as vanilla's does on a planet: it starts from
	// the closest ring tile, which can be ocean.
	private static void CheckSitesFromOrbitOnRing(RingLayer ring, RingCheckResult result)
	{
		// After loading a save, the check runs before the layers' connections are resolved; the dev harness and the
		// debug action check it later. The site finder reads the game's tick.
		if (Scribe.mode != LoadSaveMode.Inactive || Find.TickManager == null)
		{
			return;
		}
		foreach (PlanetLayer orbit in RingOrbit.Layers(Find.WorldGrid))
		{
			int sitesFound = 0;
			for (int i = 0; i < SiteSearchesPerLayer; i++)
			{
				PlanetTile near = new PlanetTile(i * orbit.TilesCount / SiteSearchesPerLayer, orbit);
				string from = $"from {RingOrbit.Describe(orbit)} tile {near.tileId}";
				bool found;
				PlanetTile site;
				Rand.PushState(i);
				try
				{
					found = TileFinder.TryFindNewSiteTile(out site, near, canBeSpace: false);
				}
				catch (Exception e)
				{
					result.Fail($"{from}, the site search threw {e.GetType().Name}: {e.Message}");
					continue;
				}
				finally
				{
					Rand.PopState();
				}
				if (!found)
				{
					continue;
				}
				sitesFound++;
				if (site.Layer != ring)
				{
					result.Fail($"{from}, the site is on {site.LayerDef.defName}, not the ring");
				}
			}
			if (sitesFound == 0)
			{
				result.Fail($"from {RingOrbit.Describe(orbit)}, none of {SiteSearchesPerLayer} searches found a site");
			}
		}
	}
}
