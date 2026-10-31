using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// A ring-shaped planet layer. Its geometry is built by Patch_PlanetLayer_InitializeLayer instead of the icosphere.
// One RingLayer is the world's surface ring (IsSurfaceRing, SurfaceRing): the radius vanilla passes in (from the layer
// settings def) is replaced by the one derived from Size when it's built. With LAO the others are atmosphere layers
// (ADR 0005, RingAtmosphere), with LAO's defs and plain tiles, whose radius is their own (HasOwnRadius); so code that
// means the ground checks IsSurfaceRing rather than the type (see SurfaceRing).
public class RingLayer : SurfaceLayer
{
	public RingGeometry Geometry { get; internal set; }

	// Whether this is the world's surface ring (the grid's root surface). False until vanilla marks it, just after a new
	// layer is built: inside InitializeLayer go by the def (SurfaceIdentity.IsSurface).
	public bool IsSurfaceRing => IsRootSurface;

	// Saved with the world by Patch_PlanetLayer_ExposeData; the geometry is rebuilt from it on every load.
	public RingSize Size { get; private set; } = RingSize.Default;

	// The share of the loop with tiles (RingSize.LandCoverages), separate from vanilla's planet coverage, which a ring
	// world is always generated at 100% of. Saves from before it was saved load as 100%.
	public float LandCoverage { get; private set; } = RingSize.FullLandCoverage;

	// One per night zone. Set from Size when the world is generated and saved with it, never recomputed, so an update
	// can't move a colony's local time. Saves from before it was saved load with v0.1.0's 8.
	public int ShadowSquareCount { get; private set; } = RingSize.V1ShadowSquares;

	// The wall-foot tiles that are spill mountains. Placed once by world generation (WorldGenStep_SpillMountains) and saved
	// with the ring, never recomputed, so a later change to the spacing can't move them. Saves from before they were
	// saved load with none.
	public IReadOnlyCollection<int> SpillMountainTiles => spillMountainTiles;

	private HashSet<int> spillMountainTiles = new HashSet<int>();

	// Whether spill mountains were on in the mod options when the world was generated. Saved with the ring, so a later
	// change to the option never touches it; saves from before it was saved load as on.
	public bool SpillMountainsOn { get; private set; } = true;

	// Made by world generation in this session rather than loaded from a save; not saved.
	public bool GeneratedThisSession { get; }

	// An atmosphere layer's def is LAO's, so it shares the surface ring def's camera settings.
	public RingworldExtension Settings => Def?.GetModExtension<RingworldExtension>() ?? SurfaceIdentity.RingDef.GetModExtension<RingworldExtension>() ?? RingworldExtension.Default;

	// Whether the layer keeps the radius it was made with instead of the one its size gives: an atmosphere layer, which
	// isn't the surface ring. Goes by the def, as IsSurfaceRing isn't set yet while a new world's layers are built.
	public bool HasOwnRadius => !SurfaceIdentity.IsSurface(Def);

	// Whether the tile is wall foot: impassable land on a rim row (see RingGeometry.IsWallFootTile). False before the
	// terrain step has made the tile.
	public bool IsWallFootTile(int tileId)
	{
		return Geometry != null && (uint)tileId < (uint)Tiles.Count && Tiles[tileId] is SurfaceTile tile && Geometry.IsWallFootTile(tileId, tile.hilliness);
	}

	// Every wall-foot tile on the live arc.
	public IEnumerable<int> WallFootTiles()
	{
		return Geometry == null ? Enumerable.Empty<int>() : Geometry.RimTiles().Where(IsWallFootTile);
	}

	public bool IsSpillMountain(int tileId)
	{
		return spillMountainTiles.Contains(tileId);
	}

	internal void SetSpillMountains(IEnumerable<int> tileIds)
	{
		spillMountainTiles = new HashSet<int>(tileIds);
	}

	public RingLayer()
	{
	}

	// Called through Activator.CreateInstance by WorldGrid.RegisterPlanetLayer; the signature must match exactly.
	// Only new layers are made this way (a save's use the parameterless one). The surface ring, made only with a new
	// world, takes the size picked on the Create World page and the spill mountains option from the mod options page.
	// An atmosphere layer, made with a new world or added to a loaded one, takes its shape from the surface ring of the
	// grid registering it (RingAtmosphere.TakePendingSurface) and the radius it's given, and has no spill mountains.
	public RingLayer(int layerId, PlanetLayerDef def, float radius, Vector3 origin, Vector3 viewCenter, float viewAngle, int subdivisions, float extraCameraAltitude, float backgroundWorldCameraOffset, float backgroundWorldCameraParallaxDistance)
		: base(layerId, def, radius, origin, viewCenter, viewAngle, subdivisions, extraCameraAltitude, backgroundWorldCameraOffset, backgroundWorldCameraParallaxDistance)
	{
		GeneratedThisSession = true;
		if (HasOwnRadius)
		{
			RingLayer surface = RingAtmosphere.TakePendingSurface();
			if (surface == null)
			{
				Log.Error($"[Ringworld] Ring layer {layerId} ({def?.defName}) was made with no surface ring to build it over.");
				return;
			}
			(Size, LandCoverage) = RingAtmosphere.ShapeFor(surface, radius);
			ShadowSquareCount = surface.ShadowSquareCount;
			SpillMountainsOn = false;
			return;
		}
		Size = WorldShape.RingSize;
		LandCoverage = WorldShape.LandCoverage;
		ShadowSquareCount = Size.NewWorldShadowSquareCount;
		SpillMountainsOn = WorldShape.settings.spillMountains;
	}

	// Always written, so a save never picks up a changed default.
	internal void ExposeRingSettings()
	{
		int tilesAround = Size.tilesAround;
		int tilesAcross = Size.tilesAcross;
		float landCoverage = LandCoverage;
		int shadowSquareCount = ShadowSquareCount;
		Scribe_Values.Look(ref tilesAround, "ringTilesAround", RingSize.Default.tilesAround, forceSave: true);
		Scribe_Values.Look(ref tilesAcross, "ringTilesAcross", RingSize.Default.tilesAcross, forceSave: true);
		Scribe_Values.Look(ref landCoverage, "ringLandCoverage", RingSize.FullLandCoverage, forceSave: true);
		Scribe_Values.Look(ref shadowSquareCount, "ringShadowSquareCount", RingSize.V1ShadowSquares, forceSave: true);
		List<int> spillMountains = spillMountainTiles.OrderBy(id => id).ToList();
		Scribe_Collections.Look(ref spillMountains, "ringSpillMountains", LookMode.Value);
		bool spillMountainsOn = SpillMountainsOn;
		Scribe_Values.Look(ref spillMountainsOn, "ringSpillMountainsOn", true, forceSave: true);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Size = new RingSize(tilesAround, tilesAcross);
			// Only guards the tile build against no tiles or more than the loop.
			LandCoverage = Mathf.Clamp(landCoverage, 0.01f, RingSize.FullLandCoverage);
			// Only guards local time against a zero; the self-check reports a count outside the range.
			ShadowSquareCount = Mathf.Max(1, shadowSquareCount);
			SetSpillMountains(spillMountains ?? Enumerable.Empty<int>());
			SpillMountainsOn = spillMountainsOn;
		}
	}
}
