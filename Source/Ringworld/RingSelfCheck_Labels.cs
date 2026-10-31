
using Verse;

namespace Ringworld;

// Checks the ring size button's labels: each preset's short and full names resolve, and what the button shows fits it.
public static partial class RingSelfCheck
{
	private static void CheckRingSizeLabels(RingLayer ring, RingCheckResult result)
	{
		foreach (RingSizePreset preset in WorldShape.Presets)
		{
			CheckKey(preset.labelKey, result);
			CheckKey(preset.shortLabelKey, result);
			string option = preset.LabelWithSize;
			if (!option.Contains(preset.Label) || !option.Contains(preset.size.tilesAround.ToString()) || !option.Contains(preset.size.tilesAcross.ToString()))
			{
				result.Fail($"{preset.labelKey}: the dropdown option \"{option}\" doesn't carry the full name and size");
			}
		}
		CheckKey("RW_RingSizeCustomShort", result);
		// The fit needs IMGUI's text measuring, which is only safe on the main thread; this check also runs from
		// World.FinalizeInit after a load, which may not be on it.
		if (!UnityData.IsInMainThread)
		{
			return;
		}
		GameFont font = Text.Font;
		Text.Font = GameFont.Small;
		try
		{
			float buttonWidth = CreateWorldPageRows.RingSizeButtonWidth(CreateWorldPageRows.RingSizeMinControlWidth);
			foreach (RingSizePreset preset in WorldShape.Presets)
			{
				CheckFits(preset.ShortLabel, buttonWidth, result);
			}
			CheckFits("RW_RingSizeCustomShort".Translate(), buttonWidth, result);
		}
		finally
		{
			Text.Font = font;
		}
	}

	private static void CheckKey(string key, RingCheckResult result)
	{
		if (!key.CanTranslate())
		{
			result.Fail($"{key} doesn't resolve");
		}
	}

	// ButtonText draws its label in the whole button rect.
	private static void CheckFits(string label, float buttonWidth, RingCheckResult result)
	{
		float needed = Text.CalcSize(label).x;
		if (needed > buttonWidth)
		{
			result.Fail($"\"{label}\" needs {needed:0} wide, the ring size button is at least {buttonWidth:0}");
		}
	}
}
