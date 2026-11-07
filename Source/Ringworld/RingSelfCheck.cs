using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ringworld;

// Checks invariants of the actual ring world and logs one PASS/FAIL line per check, with offending tile IDs.
// Runs in dev mode after world generation and after loading a save (Patch_World_FinalizeInit), from the
// "Ringworld: self-check" debug action, and from the dev harness. Only ever on the main thread: the checks build draw layers.
// Planet worlds (no ring layer) are skipped silently.
//
// To add a check: write a method with the RingCheck signature (in its own partial file if it has a theme)
// and add one line to Checks.
public static partial class RingSelfCheck
{
	// Reports failures into the result; a check that reports nothing passes.
	private delegate void RingCheck(RingLayer ring, RingCheckResult result);

	// needsGeometry: the check reads ring.Geometry, so it is skipped when the layer has none.
	private static readonly (string name, bool needsGeometry, RingCheck check)[] Checks =
	{
		("Geometry: tile count = tiles around x land coverage x tiles across", true, CheckTileCount),
		("Geometry: radius = tiles around the full loop x hex size / 2pi, whatever the land coverage, below the far clip", true, CheckRadius),
		("Geometry: neighbour links are symmetric", false, CheckNeighborsSymmetric),
		("Geometry: columns wrap at the seam on a full loop, and the live arc's end columns aren't linked", true, CheckSeamWraps),
		("Geometry: rim rows have no neighbours beyond the rim", true, CheckRimRows),
		("Land coverage: the live arc is centred on 0 degrees and spans land coverage x 360 degrees", true, CheckLiveArcCentred),
		("Land coverage: every neighbour link joins tiles at most a column apart round the ring, so none crosses a cut", true, CheckNoLinkAcrossCuts),
		("Land coverage: land fades to ocean before each cut, and every tile at the cut itself is ocean, not lake", true, CheckCutsEndInOcean),
		("Land coverage: below 100%, the stand-in covers the inner surface from cut to cut just under the tiles, out to the rim walls, and at 100% it draws nothing", true, CheckStandIn),
		("Wall foot: every land tile on a rim row is impassable, with a climate biome", true, CheckWallFootImpassable),
		("Wall foot: no wall-foot tile is water", true, CheckWallFootNotWater),
		("Wall foot: wall-foot tiles are exactly the land tiles at the rims", true, CheckWallFootOnlyOnRims),
		("Spill mountains: every spill mountain is a wall-foot tile on the live arc", false, CheckSpillMountainsOnWallFoot),
		($"Spill mountains: on each rim, spill mountains not parted by water are {SpillMountainMinGap}-{SpillMountainMaxGap} hexes apart, and on a world placed with that rule no longer stretch of wall foot goes without one", true, CheckSpillMountainSpacing),
		("Spill mountains: each shows one icon, an ordinary mountain icon rather than the wall foot's impassable one, in vanilla's hills layer or, when ReGrowth 2's World Beautification draws the hill icons, in ReGrowth's layer; skipped when another mod draws them", true, CheckSpillMountainIcons),
		("Coordinates: sample tiles format to known ring coordinates, at 0 and 180 degrees, spinward and anti-spinward, on the midline and on both rim rows", false, CheckCoordinateSamples),
		("Coordinate parallels: both rim rows read between 140 and 144 on a wide ring and inside 144 on any, values mirror about the midline, and the dexter rim row is positive and the one the live headings call North", false, CheckCoordinateParallels),
		("Climate: averaged per row, temperature is highest at the midline and never rises toward a rim", true, CheckClimateFallsTowardRims),
		($"Climate: rim rows are as warm as vanilla at {RingGeometry.RimVanillaLatitudeRange.min}-{RingGeometry.RimVanillaLatitudeRange.max} degrees latitude", true, CheckRimClimate),
		("Rivers and roads: every river and road step joins neighbouring tiles, so none goes through a cut", false, CheckRiverAndRoadStepsJoinNeighbours),
		("Roads: no road step touches a wall-foot tile", true, CheckRoadsAvoidWallFoot),
		("Rivers: no river step goes from one wall-foot tile to another", true, CheckRiversNeverWallFootToWallFoot),
		("Rivers: a wall-foot tile has at most one river link, so it can only be a river's source", true, CheckWallFootHasOneRiverLink),
		("Rivers: every river network drains to water", false, CheckRiversReachWater),
		("Headings: the neighbours of sample tiles, beside the seam on a full loop too, head West to spinward, East to anti-spinward, North to dexter and South to sinister", true, CheckHeadingsMatchRingView),
		("Headings: on the ring every heading is 360 degrees minus vanilla's, and on every other layer it's vanilla's", false, CheckHeadingsMirroredOnlyOnRing),
		("Drawn quads: an arrow drawn as vanilla's world arrows at a neighbour of sample tiles sits just above the inner surface over the neighbour, faces the axis and points at the tile, and no tile of any other layer is near enough the ring surface to be drawn there", true, CheckDrawnQuadsOnInnerSurface),
		("Regions: every tile in a region has the region's name to show in the tile info", false, CheckRegionsNamed),
		("Regions: every region's label is anchored on the inner surface between the rims", true, CheckRegionLabelsOnSurface),
		("Thickness: the outer face is about 1 hex below the inner surface, and a rim-wall face at each rim rises about 2 hexes toward the axis", true, CheckThickness),
		("Structure: the thickness and the shadow squares draw with the ring structure shader, each face with a texture from its surface's folder", true, CheckStructureMaterials),
		("Structure: every surface whose folder has edge codes loaded one complete Wang set of 16, and on every face of the thickness and the shadow squares the tiles of neighbouring squares (also across the loop's wrap) have matching codes on their shared edge", true, CheckStructureWang),
		("Structure: on the outer face and rim-wall faces the texture repeats about every 4 hexes round the loop and across, whatever the ring size, showing at least 3 variants of each surface; the shadow squares' faces too", true, CheckStructureScale),
		("Surface additions: every world-gen step and draw layer on vanilla's Surface def, other mods' included, is on the ring's def and its draw layers are built on the ring, except vanilla's sphere-only draw layers, which the ring never has", false, CheckSurfaceAdditions),
		("Surface identity: IsSurface is true for vanilla's Surface def and the ring's, and false for every other layer of the world", false, CheckIsSurface),
		("Surface identity: vanilla's Surface comparisons in Settlement.Abandon, MapParent.Abandon and SitePartWorker.PostDestroy call IsSurface, and a layer looked up by vanilla's Surface def, from the grid or from an orbit layer connected to the ring, is the ring", false, CheckVanillaSurfaceChecks),
		("Surface identity: with Vanilla Expanded Framework, its object-spawn and moving-base Surface checks call IsSurface instead of reading vanilla's Surface def; without it, nothing carries Ringworld's VEF patches", false, CheckVefSurfaceChecks),
		("Other mods: with My Little Planet, its planet size slider is drawn only with a planet's coverage row, never under the ring rows, whatever the load order", false, CheckMyLittlePlanetSlider),
		("Other mods: with Worldbuilder, its preset terrain, settlement and map-marker restores carry Ringworld's preset-fit guard; without it, nothing carries Ringworld's Worldbuilder patch", false, CheckWorldbuilderPresetGuard),
		("Other mods: with Worldbuilder, its surface world-object lookup (preset saves and restores, faction-wide settlement customisation) finds exactly the ring's settlements of other factions and its map markers", false, CheckWorldbuilderSurfaceLookup),
		("Other mods: with Worldbuilder, its page's centre column has the shape rows before Scale, which they skip with Ring, and above Coverage, its Coverage slider and world preview go through Ringworld's ring stand-ins, and its background preview generation stops on a ring", false, CheckWorldbuilderPageRows),
		("Layers: with Layered Atmosphere and Orbit, the ring has only itself, Odyssey's orbit and LAO's optional layers a ring supports (its atmosphere layers and High Orbit), including every one ticked in LAO's layer list while the integration is active, while the integration works, chained ring first and then by LAO's elevation, zooming out and back zooming in", false, CheckLaoLayers),
		("Atmosphere layers: every other ring-shaped layer is one of LAO's atmosphere layers, at its fixed fraction of the rim wall's height above the inner surface (troposphere 0.25, stratosphere 0.5, mesosphere 0.75), with the ring's tiles across, hexes the ring's size, no hex reaching outside the live arc, the local time of the ground under it, gravship flights between layers on vanilla's timing, and never the surface: not the root surface, IsSurface false, and the surface lookups still find the ring", true, CheckAtmosphereLayers),
		("Flight range: vanilla's gravship and launcher fuel and range read a layer's range factor through RingFlightRange, an atmosphere ring's factor is its def's scaled by its tile size over the planet layer's (2^(10 - subdivisions) x radius / 100 ground tiles), and the surface ring and orbit layers use their def's unchanged", true, CheckFlightRange),
		("Orbit stack: while LAO's integration is active, every sun-centred orbit layer is in the stack (Odyssey's orbit at 0.7 x the ring radius with an orbit band of 15 degrees, or none if it was moved into the stack from a world made without it; High Orbit at 0.5 x with one of 45), each with 5 subdivisions and its world objects on its tiles; with a failed integration, no orbit layer has an orbit band", false, CheckOrbitStack),
		("Orbit: Odyssey's orbit is centred on the sun, without LAO there is at most one sun-centred orbit layer, and without LAO's integration every sun-centred orbit layer without an orbit band is at about half the ring radius, and Odyssey's orbit layers keep vanilla's subdivisions", false, CheckOrbitLayer),
		("Orbit: below 100% land coverage, no tile of any orbit layer lies outside the live arc's angular span, and no tile lies outside a layer's orbit band; at 100% each orbit layer without an orbit band is a full sphere", false, CheckOrbitSpansLiveArc),
		("Quest sites: a site that can't be in space, searched for from tiles of every orbit layer, is only ever found on the ring, at least once per layer, without errors", false, CheckSitesFromOrbitOnRing),
		("Layer tab: with LAO, its Layer tab carries the text swap, which on a ring world gives its Surface group and the group's planet the ring's own texts (the planet's followed by LAO's paragraphs after its first, its gravity-well line) and puts LAO's own back after; without LAO, the tab isn't patched", false, CheckLaoLayerTabText),
		("Orbit view: from every orbit layer, fully zoomed out, the whole ring is in view and inside the far clip", true, CheckOrbitViewFitsRing),
		("Ghost icons: from every layer selected in turn, the ghost icons are exactly the iconed world objects of the surface ring and LAO's atmosphere layers from a sun-centred orbit layer, of the surface ring from an atmosphere layer, and none from any other layer; each on its object's own layer at its draw position, and each object hidden from vanilla's hover, click and drag-box picking in that view; with the ghost-icon setting off, none from any layer", false, CheckGhostIcons),
		($"Orbit icons: on a ring world, a world object's icon on every sun-centred orbit layer is {OrbitIcons.SizeFactor:F1} x vanilla's size and on the ring and its atmosphere layers vanilla's, both centred where vanilla centres them", false, CheckOrbitIconSize),
		("Orbit map: on every orbit layer, the world background camera is inside the ring", true, CheckOrbitMapBackgroundInsideRing),
		("Ring view: at every ring size, fully zoomed out, the full width and both rim walls fit on screen out to its left and right edges, and narrow rings keep the def's furthest zoom", false, CheckRingViewFitsWidth),
		("Create World page: at the minimum, default and maximum tiles around, the tiles-across limit is 200 from the default up, and stops short of 200 only where one tile more wouldn't fit the ring's full width fully zoomed out on a 21:9 screen", false, CheckTilesAcrossLimit),
		("Ring view: panning across stops where the area beyond a rim takes at most 40% of the view's height (exactly 40% when zoomed in), is 0 when fully zoomed out, and a jump-to a wall-foot tile leaves it on screen", false, CheckRingViewPanAcross),
		("Ring view: at every ring size, the furthest zoom never backs the camera past the axis", false, CheckRingViewInsideRing),
		("Ring view: the zoom bands of the surface ring and its atmosphere layers are contiguous and in chain order, split the range from the ground's furthest zoom to the stack's evenly, and end on the axis at most", false, CheckRingViewZoomBands),
		("Ring view: its near clip is below the nearest zoom by more than the region labels' lift, and the camera uses it while the ring view shows", false, CheckRingViewNearClip),
		("Ring view: a ring angle turned into the orbit view's facing looks at that spot's inner surface from across the sun, and comes back as the same angle", false, CheckRingOrbitFacingRoundTrip),
		("Sun: the sun glows at the ring's centre, inside every orbit layer's sphere, in place of vanilla's sun", false, CheckSun),
		("Lighting: the world shaders get no sun direction, so vanilla's night side never darkens the ring", false, CheckEvenLighting),
		("Night shadow: every ring copy of a vanilla lit world shader loads, and on a ring world no live material draws with a vanilla lit world shader", false, CheckLitMaterialsSwitched),
		("Night shadow: the world shaders get it wherever vanilla would show its night side (orbit view, orbit-map backgrounds, ring view with the day/night setting on), and none otherwise", true, CheckNightShadowShown),
		($"Shadow squares: the saved count is {RingSize.MinShadowSquares}-{RingSize.MaxShadowSquares}, the ring has as many night zones, and a freshly generated world's count is round(tiles around / {RingSize.TilesAroundPerShadowSquare}) clamped to that range", true, CheckShadowSquareCount),
		("Night zones: at any moment the midline has as many night stretches as shadow squares, of equal length", true, CheckNightZoneCount),
		("Night zones: tiles half a night zone apart are 12 hours apart in local time", true, CheckNightZonesOpposite),
		("Night zones: before play, the night shadow and shadow squares show 6 am local time at the selected starting tile", true, CheckStartTimeBeforePlay),
		("Shadow squares: one shadow square per night zone, each over the middle of its own night zone on the midline", true, CheckShadowSquaresOverNightZones),
		("Shadow squares: behind the ring-view camera, or not drawn while the ring view shows where it can pass them, clear of the sun, inside every orbit layer's sphere, and shadowing the ring from rim to rim", true, CheckShadowSquaresPlacement),
		("Night shadow: at each sampled moment, one night shadow per night zone, each centred on local midnight on the midline", true, CheckNightShadowCentredOnMidnight),
		("Night shadow: at each sampled moment, each night shadow lies under its own shadow square", true, CheckNightShadowUnderSquares),
		("Night shadow: at each sampled moment, each night shadow is as wide as vanilla's night on the midline that day", true, CheckNightShadowWidth),
		("Night shadow: of the ring's structure, only the rim walls' inner faces and tops take it; the outer face, the walls' outer sides and the shadow squares never do", true, CheckNightShadowOnStructure),
		("Clouds: the cloud band runs round the full loop whatever the land coverage, between the inner surface and the rim-wall tops, out to the rim walls", true, CheckCloudBand),
		("Clouds: the cloud band draws with the ring's copy of vanilla's Clouds shader, with vanilla's noise, sunset and cloud textures", true, CheckCloudShader),
		("Clouds: both samples repeat a whole number of times round the loop, their counts differ, and the density scale follows the density setting", true, CheckCloudVariety),
		("Clouds: the blue haze shares the clouds' fading material and its strength stays below the cap", true, CheckCloudHaze),
		("Clouds: none in the ring view, the ring's or an atmosphere layer's, and full in the orbit view and on orbit-map backgrounds", false, CheckCloudOpacity),
		("Labels: every preset's short and full labels resolve, the dropdown option carries the full name and size, and the button's label for each preset and for a custom size fits the ring size button's narrowest width at the page's font", false, CheckRingSizeLabels),
	};

	private const int MaxListedTiles = 20;

	// Runs every check on the world's surface ring, if it has one. The checks are about the ground, so they never run on
	// another ring-shaped layer (an atmosphere layer). reason says what triggered it, for the log.
	public static void Run(World world, string reason)
	{
		// Off the main thread the draw layers' materials can't be made, and a failed build would show as FAILs.
		if (!UnityData.IsInMainThread)
		{
			Log.Error($"[Ringworld] Self-check ({reason}) called off the main thread; skipped. Run it from the main thread, e.g. through LongEventHandler.ExecuteWhenFinished.");
			return;
		}
		if (SurfaceRing.Of(world?.grid) is { } ring)
		{
			RunOn(ring, reason);
		}
	}

	public static bool WorldHasRing(World world)
	{
		return SurfaceRing.Of(world?.grid) != null;
	}

	private static void RunOn(RingLayer ring, string reason)
	{
		Log.Message($"[Ringworld] Self-check of ring layer {ring.LayerID} ({reason}):");
		LogCounts(ring);
		LogOrbitLayers(ring);
		int passed = 0;
		int run = 0;
		bool hasGeometry = ring.Geometry != null;
		if (!hasGeometry)
		{
			Log.Error("[Ringworld]   FAIL the ring layer has no geometry; skipping the checks that need it");
		}
		foreach ((string name, bool needsGeometry, RingCheck check) in Checks)
		{
			if (needsGeometry && !hasGeometry)
			{
				continue;
			}
			run++;
			RingCheckResult result = new RingCheckResult();
			try
			{
				check(ring, result);
			}
			catch (Exception e)
			{
				result.Fail("threw " + e);
			}
			if (result.Passed && result.SkippedBecause != null)
			{
				// Counted as skipped, like a check left out for want of geometry.
				run--;
				Log.Warning("[Ringworld]   SKIP " + name + ": " + result.SkippedBecause);
			}
			else if (result.Passed)
			{
				passed++;
				Log.Message("[Ringworld]   PASS " + name);
			}
			else
			{
				Log.Error("[Ringworld]   FAIL " + name + ": " + result.Describe(MaxListedTiles));
			}
		}
		string skipped = run < Checks.Length ? $", {Checks.Length - run} skipped" : "";
		Log.Message($"[Ringworld] Self-check done: {passed}/{run} passed{skipped}.");
	}

	// Builds a draw layer a check reads, if it's dirty or stale. Runs Regenerate (which clears the dirty flag, as
	// RegenerateNow does) rather than RegenerateNow, because other mods patch that: Worldbuilder skips it while its
	// Create World page is open and its preview hasn't been captured, which is always so on Ring, and world generation
	// runs from that page.
	private static void BuildIfNeeded(WorldDrawLayerBase layer, bool stale = false)
	{
		if (layer.Dirty || stale)
		{
			layer.Regenerate().ExecuteEnumerable();
		}
	}

	// Summary of what world generation put on the ring, to tell "not generated" apart from "generated but not drawn".
	private static void LogCounts(RingLayer ring)
	{
		int water = 0;
		int rivers = 0;
		int roads = 0;
		int hills = 0;
		int landmarks = 0;
		int features = 0;
		int spillMountainHash = 17;
		foreach (int id in ring.SpillMountainTiles.OrderBy(id => id))
		{
			spillMountainHash = spillMountainHash * 31 + id;
		}
		for (int i = 0; i < ring.TilesCount; i++)
		{
			SurfaceTile tile = ring[i];
			if (tile == null)
			{
				continue;
			}
			if (tile.WaterCovered)
			{
				water++;
			}
			if (!tile.potentialRivers.NullOrEmpty())
			{
				rivers++;
			}
			if (!tile.potentialRoads.NullOrEmpty())
			{
				roads++;
			}
			if (tile.hilliness >= Hilliness.LargeHills)
			{
				hills++;
			}
			if (tile.Landmark != null)
			{
				landmarks++;
			}
			if (tile.feature != null)
			{
				features++;
			}
		}
		Log.Message($"[Ringworld]   Counts: {ring.TilesCount} tiles, water {water}, rivers {rivers}, roads {roads}, large hills/mountains {hills}, landmarks {landmarks}, in a named region {features}, spill mountains {ring.SpillMountainTiles.Count} (tile-id hash {spillMountainHash}).");
	}

	// Returned as a node so it can hide itself on planet worlds.
	[DebugAction("Ringworld", "Ringworld: self-check", allowedGameStates = AllowedGameStates.Playing)]
	private static DebugActionNode SelfCheckAction()
	{
		return new DebugActionNode
		{
			action = () => Run(Find.World, "debug action"),
			visibilityGetter = () => WorldHasRing(Find.World),
		};
	}
}

// What one check found: offending tile IDs and/or a reason that isn't about specific tiles.
public sealed class RingCheckResult
{
	private readonly List<int> tiles = new List<int>();

	private readonly HashSet<int> seen = new HashSet<int>();

	private readonly List<string> reasons = new List<string>();

	public bool Passed => tiles.Count == 0 && reasons.Count == 0;

	// Why the check couldn't look at what it checks (another mod drew it, say), or null. A check skipped without a
	// failure is logged as SKIP with the reason, neither passed nor failed.
	public string SkippedBecause { get; private set; }

	public void Skip(string reason)
	{
		SkippedBecause = reason;
	}

	// Each tile is listed once, in the order it was reported.
	public void FailTile(int tileId)
	{
		if (seen.Add(tileId))
		{
			tiles.Add(tileId);
		}
	}

	public void Fail(string reason)
	{
		reasons.Add(reason);
	}

	public string Describe(int maxTiles)
	{
		List<string> parts = new List<string>(reasons);
		if (tiles.Count > 0)
		{
			string listed = string.Join(", ", tiles.Take(maxTiles));
			string more = tiles.Count > maxTiles ? $", ... ({tiles.Count - maxTiles} more)" : "";
			parts.Add($"{tiles.Count} tiles: {listed}{more}");
		}
		return string.Join("; ", parts);
	}
}
