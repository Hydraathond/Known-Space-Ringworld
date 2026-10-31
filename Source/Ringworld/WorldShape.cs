using UnityEngine;
using Verse;

namespace Ringworld;

// The world shape, ring size and land coverage picked on the Create World page. Read when the next world is generated
// (Patch_WorldGenerator_GenerateWorld, Patch_WorldGrid_CreateRequiredLayers, RingLayer). Every choice the player makes
// is saved in the mod settings as the last choice, which these start from each time the game starts or the page
// resets, so a world generated without the page (e.g. dev quick test) gets the last choice too.
public static class WorldShape
{
	public static readonly RingSizePreset[] Presets =
	{
		new RingSizePreset("RW_RingSizePreset_Small", "RW_RingSizePresetShort_Small", RingSize.Default),
		new RingSizePreset("RW_RingSizePreset_Medium", "RW_RingSizePresetShort_Medium", new RingSize(4000, 30)),
		new RingSizePreset("RW_RingSizePreset_Large", "RW_RingSizePresetShort_Large", new RingSize(8000, 40)),
		new RingSizePreset("RW_RingSizePreset_LoreAccurate", "RW_RingSizePresetShort_LoreAccurate", new RingSize(18000, 30)),
	};

	// False: Planet, which stays fully vanilla.
	public static bool Ring = true;

	public static RingSize RingSize = RingSize.Default;

	public static float LandCoverage = RingSize.FullLandCoverage;

	// Cleared if the page can't show the toggle, so new worlds fall back to vanilla planets.
	internal static bool ringByDefault = true;

	// Set by RingworldMod before any world can be generated.
	internal static RingworldSettings settings;

	// Set while the Create World page draws the shape rows, cleared when it closes: a world generated while it's
	// false was set up without them, so it gets a message saying it's the last choice.
	public static bool ShapeRowsShown;

	// Called at startup and with the Create World page's own reset, so every new game starts from the last choice.
	public static void Reset()
	{
		Ring = ringByDefault && settings.lastRing;
		RingSize = new RingSize(settings.lastTilesAround, settings.lastTilesAcross).ClampToLimits();
		LandCoverage = Mathf.Clamp(settings.lastLandCoverage, RingSize.MinLandCoverage, RingSize.FullLandCoverage);
	}

	// Called whenever the player sets the shape, size or coverage on the page.
	public static void SaveLastChoice()
	{
		settings.lastRing = Ring;
		settings.lastTilesAround = RingSize.tilesAround;
		settings.lastTilesAcross = RingSize.tilesAcross;
		settings.lastLandCoverage = LandCoverage;
		settings.Write();
	}

	// e.g. "This world is a Ring (Small, 50% coverage), the last choice on the Create World page."
	public static string LastChoiceMessage()
	{
		if (!Ring)
		{
			return "RW_LastChoiceWorld_Planet".Translate();
		}
		string size = CurrentPreset?.Label ?? "RW_LastChoiceWorld_CustomSize".Translate(RingSize.tilesAround, RingSize.tilesAcross).ToString();
		return "RW_LastChoiceWorld_Ring".Translate(size, LandCoverage.ToStringPercent());
	}

	public static int LiveTileCount => RingSize.LiveTileCount(LandCoverage);

	public static bool IsLarge => RingSize.IsLargeAt(LandCoverage);

	// The preset matching the chosen size, or null for any other size.
	public static RingSizePreset CurrentPreset
	{
		get
		{
			foreach (RingSizePreset preset in Presets)
			{
				if (preset.size.Equals(RingSize))
				{
					return preset;
				}
			}
			return null;
		}
	}
}

public sealed class RingSizePreset
{
	public readonly string labelKey;

	public readonly string shortLabelKey;

	public readonly RingSize size;

	public RingSizePreset(string labelKey, string shortLabelKey, RingSize size)
	{
		this.labelKey = labelKey;
		this.shortLabelKey = shortLabelKey;
		this.size = size;
	}

	// e.g. "Lore-accurate ratio"
	public string Label => labelKey.Translate();

	// e.g. "Lore ratio", for the ring size button
	public string ShortLabel => shortLabelKey.Translate();

	// e.g. "Lore-accurate ratio (18000 × 30)"
	public string LabelWithSize => "RW_RingSizePresetWithSize".Translate(Label, size.tilesAround, size.tilesAcross);
}
