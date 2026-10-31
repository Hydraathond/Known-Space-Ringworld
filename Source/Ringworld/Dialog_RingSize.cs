using UnityEngine;
using Verse;

namespace Ringworld;

// The "Advanced…" dialog on the Create World page: sliders for any ring size within RingSize's limits, with a live
// readout. Edits WorldShape.RingSize directly, so the page's ring size row follows along.
public class Dialog_RingSize : Window
{
	// Vanilla's surface layer radius (PlanetLayerSettings.xml), for the readout's comparison.
	private const float VanillaPlanetRadius = 100f;

	private const float SliderHeight = 30f;

	public override Vector2 InitialSize => new Vector2(500f, 360f);

	public Dialog_RingSize()
	{
		doCloseX = true;
		doCloseButton = true;
		closeOnClickedOutside = true;
		absorbInputAroundWindow = true;
	}

	public override void PreOpen()
	{
		base.PreOpen();
		// The presets are all within the limits; this only matters if a size was set some other way.
		WorldShape.RingSize = WorldShape.RingSize.ClampToLimits();
	}

	// The sliders change the size every frame, so it's saved as the last choice once, on closing.
	public override void PostClose()
	{
		base.PostClose();
		WorldShape.SaveLastChoice();
	}

	public override void DoWindowContents(Rect inRect)
	{
		Listing_Standard listing = new Listing_Standard();
		listing.Begin(inRect);
		Text.Font = GameFont.Medium;
		listing.Label("RW_RingSizeDialogTitle".Translate());
		Text.Font = GameFont.Small;
		listing.Gap();

		RingSize size = WorldShape.RingSize;
		listing.Label("RW_RingSizeTilesAround".Translate(size.tilesAround));
		int around = Mathf.RoundToInt(Widgets.HorizontalSlider(listing.GetRect(SliderHeight), size.tilesAround, RingSize.MinTilesAround, RingSize.MaxTilesAround, roundTo: 50f));
		listing.Label("RW_RingSizeTilesAcross".Translate(size.tilesAcross));
		int across = Mathf.RoundToInt(Widgets.HorizontalSlider(listing.GetRect(SliderHeight), size.tilesAcross, RingSize.MinTilesAcross, RingSize.MaxTilesAcross, roundTo: 1f));
		size = new RingSize(around, across).ClampToLimits();
		WorldShape.RingSize = size;

		listing.Gap();
		listing.Label("RW_RingSizeTileCount".Translate(WorldShape.LiveTileCount.ToString("N0"), WorldShape.LandCoverage.ToStringPercent()));
		listing.Label("RW_RingSizeRadius".Translate(Mathf.RoundToInt(size.Radius), (size.Radius / VanillaPlanetRadius).ToString("0.#")));
		if (WorldShape.IsLarge)
		{
			GUI.color = ColorLibrary.RedReadable;
			listing.Label("RW_RingSizeLargeWarning".Translate(RingSize.LargeTileCount.ToString("N0")));
			GUI.color = Color.white;
		}
		listing.End();
	}
}
