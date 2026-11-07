using Verse;

namespace Ringworld;

// Ringworld's mod settings: the last choice of world shape, ring size and land coverage made on the Create World
// page (WorldShape), and the options set on the mod options page. A fresh install starts with a Medium ring at 100% land
// coverage, spill mountains on, ghost icons shown and clouds at 140% density.
public class RingworldSettings : ModSettings
{
	public bool lastRing = true;

	public int lastTilesAround = RingSize.Default.tilesAround;

	public int lastTilesAcross = RingSize.Default.tilesAcross;

	public float lastLandCoverage = RingSize.FullLandCoverage;

	// Set on the mod options page. Read when a ring world is generated; a world keeps what it was generated with.
	public bool spillMountains = true;

	// Set on the mod options page; applies to the ghost icons (GhostIcons) immediately in every save.
	public bool ghostIcons = true;

	public const float DefaultCloudDensity = 1.4f;

	// Set on the mod options page, as a fraction (1 = 100%); applies to the clouds immediately in every save.
	public float cloudDensity = DefaultCloudDensity;

	public override void ExposeData()
	{
		Scribe_Values.Look(ref lastRing, "lastRing", true);
		Scribe_Values.Look(ref lastTilesAround, "lastTilesAround", RingSize.Default.tilesAround);
		Scribe_Values.Look(ref lastTilesAcross, "lastTilesAcross", RingSize.Default.tilesAcross);
		Scribe_Values.Look(ref lastLandCoverage, "lastLandCoverage", RingSize.FullLandCoverage);
		Scribe_Values.Look(ref spillMountains, "spillMountains", true);
		Scribe_Values.Look(ref ghostIcons, "ghostIcons", true);
		Scribe_Values.Look(ref cloudDensity, "cloudDensity", DefaultCloudDensity);
	}
}
