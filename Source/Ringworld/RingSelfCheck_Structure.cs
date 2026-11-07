using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace Ringworld;

// Structure checks: what the ring's thickness and the shadow squares draw with, and how big their texture is. That the
// textures look organic, and that clicks still go through to the tiles, are checked by eye.
public static partial class RingSelfCheck
{
	// The spec's scale on the outer face and rim-wall faces, written out here rather than read from RingStructure:
	// about 4 vanilla hexes per texture repeat, round the loop and across, on every ring size.
	private const float StructureHexesPerRepeat = 4f;

	// The spec's 3-4 variants per texture: at least this many differ round the loop, if the folder has them.
	private const int StructureMinVariantsShown = 3;

	// Every thickness and shadow-square face draws with the ring structure shader, each with a texture from its surface's
	// folder, and every surface has a texture.
	private static void CheckStructureMaterials(RingLayer ring, RingCheckResult result)
	{
		if (!RingMaterials.RingShaderLoaded(RingStructure.ShaderName))
		{
			result.Fail($"the ring shader {RingStructure.ShaderName} didn't load");
		}
		foreach (RingStructureSurface surface in RingStructure.Surfaces)
		{
			if (RingStructure.TexturesOf(surface).Count == 0)
			{
				result.Fail($"there are no {surface} textures in Textures/{RingStructure.FolderPathOf(surface)}");
			}
		}
		foreach ((string layer, _, LayerSubMesh subMesh) in StructureSubMeshes(ring, result))
		{
			Material material = subMesh.material;
			if (RingMaterials.ShaderDefName(material) != RingStructure.ShaderName)
			{
				result.Fail($"the {layer} draws with {RingMaterials.ShaderDefName(material)}, not {RingStructure.ShaderName}");
				return;
			}
			if (RingStructure.SurfaceOf(material) is not RingStructureSurface surface)
			{
				result.Fail($"the {layer} draws with {material.name}, which isn't a ring structure material");
				return;
			}
			// By name: ContentFinder finds nothing off the main thread, so names hold wherever the texture was loaded.
			string texture = material.GetTexture(RingStructure.MainTexID)?.name;
			if (!RingStructure.TexturesOf(surface).Any(known => known.name == texture))
			{
				result.Fail($"the {layer}'s {surface} faces draw with texture {texture ?? "none"}, not one from Textures/{RingStructure.FolderPathOf(surface)}");
				return;
			}
		}
	}

	// A tile's edge code in its file name, read here rather than through RingStructure, so the check doesn't share the
	// loader's parsing.
	private static readonly Regex StructureEdgeCode = new Regex(@"N([AB])-E([AB])-S([AB])-W([AB])");

	// A surface whose folder has edge codes loaded one complete set (RingStructure falls back to variants with a warning
	// otherwise), and on each face of the thickness and the shadow squares, read from the materials the built meshes
	// use, a square's E edge type equals its eastern neighbour's W edge, and its N edge the neighbour on its N side's S edge,
	// with the last column's neighbour being the first on the faces that wrap round the loop.
	private static void CheckStructureWang(RingLayer ring, RingCheckResult result)
	{
		foreach (RingStructureSurface surface in RingStructure.Surfaces)
		{
			if (RingStructure.WangProblemOf(surface) is string problem)
			{
				result.Fail($"the {surface} surface's folder {RingStructure.FolderOf(surface)} has edge codes but isn't one complete Wang set: {problem}");
			}
			else if (RingStructure.IsWang(surface) && RingStructure.TexturesOf(surface).Count != 16)
			{
				result.Fail($"the {surface} Wang set has {RingStructure.TexturesOf(surface).Count} tiles, not 16");
			}
		}
		// Forces the thickness and shadow squares to be built.
		StructureSubMeshes(ring, result).Count();
		List<(string layer, IReadOnlyList<WangSquare> squares)> layers = new List<(string, IReadOnlyList<WangSquare>)>();
		if (ring.WorldDrawLayers.OfType<WorldDrawLayer_RingThickness>().FirstOrDefault() is { } thickness)
		{
			layers.Add(("thickness", thickness.WangSquares));
		}
		if (TryGetShadowSquares(ring, result, out GlobalDrawLayer_RingShadowSquares squares))
		{
			layers.Add(("shadow squares", squares.WangSquares));
		}
		foreach ((string layer, IReadOnlyList<WangSquare> built) in layers)
		{
			if (built.Count == 0 && RingStructure.Surfaces.Any(RingStructure.IsWang))
			{
				result.Fail($"the {layer} built no Wang squares though a surface has a Wang set");
			}
			bool uncoded = false;
			Dictionary<(int face, int column, int row), (int code, WangSquare square)> byPlace = new Dictionary<(int, int, int), (int, WangSquare)>();
			foreach (WangSquare square in built)
			{
				Match match = StructureEdgeCode.Match(square.material.GetTexture(RingStructure.MainTexID)?.name ?? "");
				if (!match.Success)
				{
					result.Fail($"a {layer} Wang square (face {square.face}, column {square.column}, row {square.row}) draws with {square.material.name}, which has no edge code");
					uncoded = true;
					break;
				}
				// Bits N, E, S, W, each 1 for B.
				int code = 0;
				for (int side = 1; side <= 4; side++)
				{
					code = code * 2 + (match.Groups[side].Value == "B" ? 1 : 0);
				}
				byPlace[(square.face, square.column, square.row)] = (code, square);
			}
			if (uncoded)
			{
				continue;
			}
			int mismatches = 0;
			foreach (((int face, int column, int row), (int code, WangSquare square)) in byPlace)
			{
				int eastColumn = square.wraps ? (column + 1) % square.columns : column + 1;
				if (byPlace.TryGetValue((face, eastColumn, row), out var east) && (code >> 2 & 1) != (east.code & 1) && mismatches++ < 3)
				{
					result.Fail($"on the {layer}'s face {face}, the square at column {column}, row {row} and the one east of it (column {eastColumn}) have different edge types on their shared edge");
				}
				if (byPlace.TryGetValue((face, column, row + 1), out var north) && (code >> 3 & 1) != (north.code >> 1 & 1) && mismatches++ < 3)
				{
					result.Fail($"on the {layer}'s face {face}, the square at column {column}, row {row} and the one on its N side have different edge types on their shared edge");
				}
			}
		}
	}

	// On the outer face and rim-wall faces the texture repeats about every 4 vanilla hexes round the loop and across,
	// whatever the ring's size, and each surface shows several of its variants. The shadow squares' faces count too.
	private static void CheckStructureScale(RingLayer ring, RingCheckResult result)
	{
		float expected = StructureHexesPerRepeat * RingSize.VanillaHexSize;
		// Per direction (0: round the loop, along u; 1: across, along v): world length and repeats, summed over edges.
		float[] length = new float[2];
		float[] repeats = new float[2];
		Dictionary<RingStructureSurface, HashSet<string>> variants = new Dictionary<RingStructureSurface, HashSet<string>>();
		foreach ((string layer, _, LayerSubMesh subMesh) in StructureSubMeshes(ring, result))
		{
			if (subMesh.uvs.Count != subMesh.verts.Count || subMesh.uvsChannelTwo.Count != subMesh.verts.Count)
			{
				result.Fail($"a {layer} submesh has {subMesh.verts.Count} vertices but {subMesh.uvs.Count} and {subMesh.uvsChannelTwo.Count} UVs");
				return;
			}
			if (RingStructure.SurfaceOf(subMesh.material) is not RingStructureSurface surface)
			{
				continue;
			}
			if (!variants.TryGetValue(surface, out HashSet<string> shown))
			{
				variants[surface] = shown = new HashSet<string>();
			}
			shown.Add(subMesh.material.GetTexture(RingStructure.MainTexID)?.name);
			for (int t = 0; t < subMesh.tris.Count; t += 3)
			{
				for (int e = 0; e < 3; e++)
				{
					int a = subMesh.tris[t + e];
					int b = subMesh.tris[t + (e + 1) % 3];
					Vector3 uv = subMesh.uvs[b] - subMesh.uvs[a];
					// Only edges running one way in the texture: the quads' diagonals mix both.
					int direction = Mathf.Approximately(uv.y, 0f) ? 0 : Mathf.Approximately(uv.x, 0f) ? 1 : -1;
					if (direction >= 0)
					{
						length[direction] += (subMesh.verts[b] - subMesh.verts[a]).magnitude;
						repeats[direction] += Mathf.Abs(uv[direction]);
					}
				}
			}
		}
		string[] directions = { "round the loop", "across" };
		for (int direction = 0; direction < 2; direction++)
		{
			if (repeats[direction] <= 0f)
			{
				result.Fail($"the structure texture never changes {directions[direction]}");
				continue;
			}
			float actual = length[direction] / repeats[direction];
			if (Mathf.Abs(actual - expected) > 0.5f * RingSize.VanillaHexSize)
			{
				result.Fail($"the structure texture repeats every {actual:F2} world units {directions[direction]}, expected about {expected:F2} ({StructureHexesPerRepeat} hexes)");
			}
		}
		foreach (RingStructureSurface surface in new[] { RingStructureSurface.Hull, RingStructureSurface.Rock, RingStructureSurface.Scrith })
		{
			int wanted = Mathf.Min(StructureMinVariantsShown, RingStructure.TexturesOf(surface).Count);
			int shown = variants.TryGetValue(surface, out HashSet<string> names) ? names.Count : 0;
			if (shown < wanted)
			{
				result.Fail($"the thickness shows {shown} of the {RingStructure.TexturesOf(surface).Count} {surface} variants, expected at least {wanted}");
			}
		}
	}

	// The submeshes of the thickness and the shadow squares, built for this ring, each with the layer's name and whether
	// it's the shadow squares'.
	private static IEnumerable<(string layer, bool squares, LayerSubMesh subMesh)> StructureSubMeshes(RingLayer ring, RingCheckResult result)
	{
		WorldDrawLayer_RingThickness thickness = ring.WorldDrawLayers.OfType<WorldDrawLayer_RingThickness>().FirstOrDefault();
		if (thickness == null)
		{
			result.Fail("the ring layer has no thickness draw layer");
			yield break;
		}
		BuildIfNeeded(thickness);
		foreach (LayerSubMesh subMesh in thickness.SubMeshes)
		{
			yield return ("thickness", false, subMesh);
		}
		if (TryGetShadowSquares(ring, result, out GlobalDrawLayer_RingShadowSquares squares))
		{
			foreach (LayerSubMesh subMesh in squares.SubMeshes)
			{
				yield return ("shadow squares", true, subMesh);
			}
		}
	}
}
