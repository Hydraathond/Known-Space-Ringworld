using RimWorld.Planet;
using Verse;

namespace Ringworld;

// "The ring" means one of two things, and code must say which (ADR 0005):
// - A ring-shaped layer: any RingLayer, the ring geometry at some radius. Ask with `layer is RingLayer`. Code that only
//   needs the shape takes any of them: the ring view camera, tile drawing and the night-shadow material switch, ring
//   coordinates' formatting, local time (night zones), headings, region labels.
// - The surface ring: the world's surface, the ground, when that is a ring. Get it here, or ask a layer at hand with
//   RingLayer.IsSurfaceRing. Code that means the ground picks it: surface identity, world generation, the climate
//   gradient, the live arc and land coverage, ring coordinates' origin, orbit radii, the sun and shadow
//   squares, other mods' compatibility and every self-check about the ground.
// With LAO, a ring world's atmosphere layers are ring-shaped too (ADR 0005, RingAtmosphere), so never find the surface
// ring by looking for "the" RingLayer among the world's layers.
public static class SurfaceRing
{
	// The current world's surface ring, or null on a planet world or with no world. During world generation this is the
	// world being created.
	public static RingLayer Current => Find.World == null ? null : Of(Find.WorldGrid);

	// Whether the current world is a ring world.
	public static bool InRingWorld => Current != null;

	// The grid's surface ring, or null if the grid's surface isn't a ring (or there's no grid). A grid's surface is its
	// root surface, of which there's exactly one.
	public static RingLayer Of(WorldGrid grid)
	{
		return grid?.Surface as RingLayer;
	}
}
