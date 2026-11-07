using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// Spill-mountain checks: where world generation put them along the wall foot.
public static partial class RingSelfCheck
{
	// The spacing rule from the spec, about one every 150 hexes along each rim, jittered ±30%.
	private const int SpillMountainMinGap = 105;

	private const int SpillMountainMaxGap = 195;

	// Tile ids exist only on the live arc, so a valid id is a live-arc tile.
	private static void CheckSpillMountainsOnWallFoot(RingLayer ring, RingCheckResult result)
	{
		foreach (int id in ring.SpillMountainTiles)
		{
			if (id < 0 || id >= ring.TilesCount || !ring.IsWallFootTile(id))
			{
				result.FailTile(id);
			}
		}
	}

	// A world generated with the option off must have no spill mountains and skips the rest. Otherwise gaps are counted in
	// hexes along the rim. A gap that crosses water is skipped, as placement skips a spot that falls
	// on water rather than moving it. Any world placed with the rule (one that has spill mountains, or was just generated)
	// also has none missing: no stretch of wall foot longer than the largest gap goes without one.
	private static void CheckSpillMountainSpacing(RingLayer ring, RingCheckResult result)
	{
		if (!ring.SpillMountainsOn)
		{
			// Generated with the option off: there are no spill-mountain tiles at all.
			foreach (int id in ring.SpillMountainTiles)
			{
				result.FailTile(id);
			}
			return;
		}
		RingGeometry geometry = ring.Geometry;
		bool placed = ring.SpillMountainTiles.Count > 0 || ring.GeneratedThisSession;
		foreach (int row in geometry.RimRows)
		{
			int length = geometry.liveTilesAround;
			int TileAt(int index) => geometry.TileId(row, geometry.firstColumn + index);
			bool[] wallFoot = new bool[length];
			bool[] spill = new bool[length];
			for (int index = 0; index < length; index++)
			{
				wallFoot[index] = ring.IsWallFootTile(TileAt(index));
				spill[index] = ring.IsSpillMountain(TileAt(index));
			}
			List<int> spills = Enumerable.Range(0, length).Where(index => spill[index]).ToList();
			for (int i = 0; i < spills.Count; i++)
			{
				if (i == spills.Count - 1 && (!geometry.IsFullLoop || spills.Count < 2))
				{
					break;
				}
				int from = spills[i];
				int gap = i < spills.Count - 1 ? spills[i + 1] - from : spills[0] + length - from;
				bool crossesWater = Enumerable.Range(from + 1, gap - 1).Any(index => !wallFoot[index % length]);
				if (!crossesWater && (gap < SpillMountainMinGap || gap > SpillMountainMaxGap))
				{
					result.FailTile(TileAt(from + gap));
				}
			}
			if (!placed)
			{
				continue;
			}
			// Walk the rim from a spill mountain or water (round the loop on a full loop, along the arc otherwise),
			// counting the wall foot since the last spill mountain.
			int start = geometry.IsFullLoop ? Enumerable.Range(0, length).FirstOrDefault(index => spill[index] || !wallFoot[index]) : -1;
			if (geometry.IsFullLoop && wallFoot[start] && !spill[start])
			{
				// All wall foot and no spill mountain.
				result.FailTile(TileAt(0));
				continue;
			}
			int run = 0;
			for (int step = 1; step <= (geometry.IsFullLoop ? length : length - start - 1); step++)
			{
				int index = start + step;
				run = !wallFoot[index % length] || spill[index % length] ? 0 : run + 1;
				if (run > SpillMountainMaxGap)
				{
					result.FailTile(TileAt(index));
					run = 0;
				}
			}
		}
	}

	// The layer that draws hill icons on the ring is checked: vanilla's hills layer, or, when ReGrowth 2's World
	// Beautification has emptied that, ReGrowth's beautification layer (Patch_ReGrowth_WorldDrawLayer_Beautification). Each
	// prints one quad per hill icon, within 0.37 of a tile of its tile's centre (vanilla's widest offset, for small hills),
	// so a hill-icon quad less than half a tile from a spill mountain's centre is that tile's icon: there must be one, and
	// it must be an ordinary mountain icon. If vanilla's layer drew nothing and ReGrowth's doesn't explain it, another mod
	// draws the hill icons, and the check is skipped rather than failed tile by tile.
	private static void CheckSpillMountainIcons(RingLayer ring, RingCheckResult result)
	{
		WorldDrawLayer_Hills hills = ring.WorldDrawLayers.OfType<WorldDrawLayer_Hills>().FirstOrDefault();
		if (hills == null)
		{
			result.Fail("the ring has no hills layer");
			return;
		}
		if (ring.SpillMountainTiles.Count == 0)
		{
			return;
		}
		BuildIfNeeded(hills);
		WorldDrawLayerBase iconLayer = hills;
		// Every quad vanilla's hills layer draws is a hill icon.
		Func<Material, HillIcon> iconKind = material => material == WorldMaterials.Mountains ? HillIcon.Mountain : HillIcon.OtherHill;
		if (!DrewHillIcons(hills, iconKind))
		{
			WorldDrawLayer beautification = ring.WorldDrawLayers.FirstOrDefault(layer => layer.GetType().FullName == Patch_ReGrowth_WorldDrawLayer_Beautification.LayerTypeName);
			if (beautification != null)
			{
				BuildIfNeeded(beautification);
			}
			Dictionary<Texture, bool> hillTextures = ReGrowthHillTextures();
			iconKind = material => ReGrowthHillIconKind(material, hillTextures);
			if (beautification == null || !DrewHillIcons(beautification, iconKind))
			{
				List<string> others = ring.WorldDrawLayers.Select(layer => layer.GetType()).Where(type => type.Assembly != typeof(WorldDrawLayer).Assembly && type.Assembly != typeof(RingLayer).Assembly).Select(type => type.FullName).ToList();
				string layers = others.Count > 0 ? string.Join(", ", others) : "none from other mods";
				result.Skip($"hill icons on the ring are drawn by another mod (layers: {layers}); spill-mountain icons not checked");
				return;
			}
			iconLayer = beautification;
		}
		float tileSize = ring.Geometry.tileSize;
		List<(int id, Vector3 center)> spills = ring.SpillMountainTiles.Select(id => (id, ring.GetTileCenter(id))).ToList();
		HashSet<int> withMountainIcon = new HashSet<int>();
		foreach (LayerSubMesh subMesh in Patch_WorldDrawLayerBase_FinalizeMesh.SubMeshes(iconLayer))
		{
			HillIcon icon = iconKind(subMesh.material);
			if (icon == HillIcon.NotHill)
			{
				continue;
			}
			foreach (Vector3 quad in QuadCentres(subMesh.verts))
			{
				foreach ((int id, Vector3 center) in spills)
				{
					if ((quad - center).sqrMagnitude >= 0.25f * tileSize * tileSize)
					{
						continue;
					}
					if (icon != HillIcon.Mountain || !withMountainIcon.Add(id))
					{
						result.FailTile(id);
					}
				}
			}
		}
		foreach ((int id, Vector3 _) in spills.Where(spill => !withMountainIcon.Contains(spill.id)))
		{
			result.FailTile(id);
		}
	}

	// What a draw layer's submesh draws, as far as the spill-mountain icon check cares.
	private enum HillIcon
	{
		Mountain,
		OtherHill,
		NotHill,
	}

	// Whether the layer drew any quad with a hill icon.
	private static bool DrewHillIcons(WorldDrawLayerBase layer, Func<Material, HillIcon> iconKind)
	{
		return Patch_WorldDrawLayerBase_FinalizeMesh.SubMeshes(layer).Any(subMesh => subMesh.verts.Count > 0 && iconKind(subMesh.material) != HillIcon.NotHill);
	}

	// ReGrowth's hill-icon kinds, as it names its textures: BiomesKit's per biome, and its Default ones.
	private static readonly string[] ReGrowthHillKinds = { "SmallHills", "LargeHills", "SmallSnowpiles", "LargeSnowpiles", "Mountains", "Impassable" };

	private static readonly string[] ReGrowthSnowSuffixes = { "", "_SemiSnowy", "_Snowy", "_VerySnowy", "_FullySnowy" };

	// Every hill-icon texture ReGrowth's beautification layer can draw (WorldMaterials/BiomesKit/<biome or
	// Default>/Hills/<kind><snow suffix>), each with whether it's a Mountains one. A texture's name is only its file
	// name, so the textures are looked up by path.
	private static Dictionary<Texture, bool> ReGrowthHillTextures()
	{
		Dictionary<Texture, bool> textures = new Dictionary<Texture, bool>();
		foreach (string folder in DefDatabase<BiomeDef>.AllDefsListForReading.Select(biome => biome.defName).Append("Default"))
		{
			foreach (string kind in ReGrowthHillKinds)
			{
				foreach (string suffix in ReGrowthSnowSuffixes)
				{
					Texture2D texture = ContentFinder<Texture2D>.Get($"WorldMaterials/BiomesKit/{folder}/Hills/{kind}{suffix}", reportFailure: false);
					if (texture != null)
					{
						textures[texture] = kind == "Mountains";
					}
				}
			}
		}
		return textures;
	}

	// For a submesh of ReGrowth's beautification layer: a mountain icon is vanilla's or a Mountains texture; anything
	// else it draws that isn't a hill icon is a forest or biome overlay.
	private static HillIcon ReGrowthHillIconKind(Material material, Dictionary<Texture, bool> hillTextures)
	{
		if (material == WorldMaterials.Mountains)
		{
			return HillIcon.Mountain;
		}
		if (material == WorldMaterials.SmallHills || material == WorldMaterials.LargeHills || material == WorldMaterials.ImpassableMountains)
		{
			return HillIcon.OtherHill;
		}
		if (material.mainTexture == null || !hillTextures.TryGetValue(material.mainTexture, out bool mountains))
		{
			return HillIcon.NotHill;
		}
		return mountains ? HillIcon.Mountain : HillIcon.OtherHill;
	}

	private static IEnumerable<Vector3> QuadCentres(List<Vector3> verts)
	{
		for (int first = 0; first + 3 < verts.Count; first += 4)
		{
			yield return (verts[first] + verts[first + 1] + verts[first + 2] + verts[first + 3]) / 4f;
		}
	}
}
