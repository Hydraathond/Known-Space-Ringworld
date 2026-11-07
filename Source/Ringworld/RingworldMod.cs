using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Ringworld;

public class RingworldMod : Mod
{
	public RingworldMod(ModContentPack content) : base(content)
	{
		// Before patching: the Create World page's transpiler may still turn new worlds into planets.
		WorldShape.settings = GetSettings<RingworldSettings>();
		WorldShape.Reset();
		Harmony harmony = new Harmony("ringworld.core");
		try
		{
			harmony.PatchAll(typeof(RingworldMod).Assembly);
			int patched = harmony.GetPatchedMethods().Count();
			Log.Message($"[Ringworld] Harmony {typeof(Harmony).Assembly.GetName().Version} patched {patched} methods.");
		}
		catch (Exception e)
		{
			Log.Error("[Ringworld] Harmony patching failed: " + e);
		}
	}

	public override string SettingsCategory()
	{
		return "RW_SettingsCategory".Translate();
	}

	// The mod options page: one row per option.
	public override void DoSettingsWindowContents(Rect inRect)
	{
		Listing_Standard listing = new Listing_Standard();
		listing.Begin(inRect);
		listing.CheckboxLabeled("RW_SettingSpillMountains".Translate(), ref WorldShape.settings.spillMountains, "RW_SettingSpillMountainsTip".Translate());
		listing.CheckboxLabeled("RW_SettingGhostIcons".Translate(), ref WorldShape.settings.ghostIcons, "RW_SettingGhostIconsTip".Translate());
		listing.Gap();
		listing.Label("RW_SettingCloudDensity".Translate((WorldShape.settings.cloudDensity * 100f).ToString("F0")), tooltip: "RW_SettingCloudDensityTip".Translate());
		WorldShape.settings.cloudDensity = Mathf.Round(listing.Slider(WorldShape.settings.cloudDensity, 0f, 2f) * 100f) / 100f;
		listing.End();
	}
}
