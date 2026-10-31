using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// What each face of the ring's structure is made of: the ring's thickness (WorldDrawLayer_RingThickness) and the shadow
// squares (GlobalDrawLayer_RingShadowSquares).
public enum RingStructureSurface
{
	// The outer face: scrith hull with a large panel grid and ribs running round the loop.
	Hull,

	// The rim wall's inner face.
	Rock,

	// The rim wall's top and outer side, and the shadow squares' edges.
	Scrith,

	// A shadow square's sun side.
	Photovoltaic,

	// A shadow square's ring side.
	Matte,
}

// One square of a Wang face, for the self-check: where it is on its face (face id, column, row, and the face's number of
// columns, which wrap round the loop or not) and the material its mesh was built with.
public readonly struct WangSquare
{
	public readonly int face;

	public readonly int column;

	public readonly int row;

	public readonly int columns;

	public readonly bool wraps;

	public readonly Material material;

	public WangSquare(int face, int column, int row, int columns, bool wraps, Material material)
	{
		this.face = face;
		this.column = column;
		this.row = row;
		this.columns = columns;
		this.wraps = wraps;
		this.material = material;
	}
}

// The ring structure shader's textures and materials, one per surface, texture and whether the night shadow darkens it.
// Each surface's textures are every PNG in its own folder under Textures/World/Ringworld/Structure/, so art can be
// swapped or added without code. A folder is one of two kinds:
//   Variants: PNGs that tile, and match along their left and right edges. The meshes pick one per panel segment
//     (VariantAt), and a segment shows it from edge to edge across the strip.
//   A Wang set: exactly 16 PNGs, one per edge code, with the code anywhere in the file name as N[AB]-E[AB]-S[AB]-W[AB]
//     (the prefix and a _v<n> suffix are ignored), e.g. tile_02_NA-EA-SA-WB_v1.png. Tiles with the same edge type on a
//     side have identical pixels along it. The meshes cut each face into squares of about HexesPerRepeat hexes round the
//     loop and across, each drawing one tile once, chosen so every edge shared by two squares has one type on both
//     sides (EdgeIsB, WangTileAt). E is the tile's right side, the direction of u, round the loop; N is its top, the
//     direction of v, across the strip (starboard on the outer face). A Wang surface has no macro blend (MacroBlendID), as
//     its tiles already vary.
// A folder with no edge codes is variants. A folder where only some names carry a code, or the codes aren't one complete
// set, logs a warning (WangProblemOf) and is drawn as variants too.
// The shader (Shaders/World/RW_RingStructure.frag.glsl) reads two UV channels:
//   UV0: the texture, in repeats. Round the loop, a whole number of HexesPerRepeat segments, so there is no seam.
//   UV1: the wear tint's noise, in repeats of WearHexes. Round the loop, a whole number of them too.
[StaticConstructorOnStartup]
public static class RingStructure
{
	public const string ShaderName = "RW_RingStructure";

	public const string TextureFolder = "World/Ringworld/Structure/";

	// About this many hexes per texture repeat on the outer face and rim-wall faces, on every ring size.
	public const float HexesPerRepeat = 4f;

	// The shader's macro texture is this many times the size of the texture per wear repeat (RW_RingStructure.frag.glsl).
	public const float MacroScale = 10f;

	// About this many hexes per repeat of the wear tint's noise: it changes over hundreds of hexes.
	public const float WearHexes = 400f;

	public static readonly int MainTexID = Shader.PropertyToID("_MainTex");

	private static readonly int MacroTexID = Shader.PropertyToID("_MacroTex");

	// 1 on a material whose faces the night shadow darkens, 0 on the rest (RW_RingStructure.frag.glsl).
	public static readonly int NightShadowID = Shader.PropertyToID("_NightShadow");

	// How much of the macro texture the shader mixes in: MacroBlend on variants, none on a Wang set.
	public static readonly int MacroBlendID = Shader.PropertyToID("_MacroBlend");

	public const float MacroBlend = 0.3f;

	private static readonly Regex EdgeCodePattern = new Regex("N([AB])-E([AB])-S([AB])-W([AB])", RegexOptions.Compiled);

	public static readonly RingStructureSurface[] Surfaces = (RingStructureSurface[])System.Enum.GetValues(typeof(RingStructureSurface));

	// Loaded on the main thread at startup: the layers can be built off it (the self-check after loading a save), where
	// ContentFinder finds nothing. Sorted by name, so a ring picks the same variants every time.
	private static readonly Texture2D[][] Textures = Surfaces.Select(LoadTextures).ToArray();

	// Per surface, what is wrong with a folder that carries edge codes but isn't one complete set, or null.
	private static readonly string[] WangProblems = new string[Surfaces.Length];

	// Per surface, the index into Textures of the tile for each edge code (EdgeCodeOf), or null for a surface of variants.
	private static readonly int[][] WangTiles = Surfaces.Select(surface => FindWangTiles(surface, Textures[(int)surface])).ToArray();

	// Vanilla's noise texture, as the clouds use it.
	public static readonly Texture2D NoiseTexture = ContentFinder<Texture2D>.Get("Other/Perlin");

	private static readonly Dictionary<(RingStructureSurface, int, bool), Material> Materials = new Dictionary<(RingStructureSurface, int, bool), Material>();

	public static IReadOnlyList<Texture2D> TexturesOf(RingStructureSurface surface) => Textures[(int)surface];

	// Whether the surface's folder is a complete Wang set; otherwise its textures are variants.
	public static bool IsWang(RingStructureSurface surface) => WangTiles[(int)surface] != null;

	// What is wrong with the surface's folder if its names carry edge codes but aren't one complete set, else null.
	public static string WangProblemOf(RingStructureSurface surface) => WangProblems[(int)surface];

	// The edge code of a tile name (N, E, S, W each A = 0 or B = 1, as bits 8, 4, 2, 1), or -1 if it has none.
	public static int EdgeCodeOf(string name)
	{
		Match match = EdgeCodePattern.Match(name ?? "");
		if (!match.Success)
		{
			return -1;
		}
		int code = 0;
		for (int side = 1; side <= 4; side++)
		{
			code = code * 2 + (match.Groups[side].Value == "B" ? 1 : 0);
		}
		return code;
	}

	// The index of the shader's macro texture: the all-A tile of a Wang set, which tiles with itself, else the first
	// variant.
	public static int AllATile(RingStructureSurface surface) => WangTiles[(int)surface]?[0] ?? 0;

	// Whether this edge of a Wang face is type B (else A), 50/50 and seeded by its position so a ring always builds the
	// same way. On a face cut into columns and rows of squares, the edge at (column, row) with alongLoop is the one
	// between column - 1 and column in that row (an E/W edge); otherwise it's the one between row - 1 and row in that
	// column (an N/S edge). `face` tells the faces of one surface apart.
	public static bool EdgeIsB(RingStructureSurface surface, int face, bool alongLoop, int column, int row)
	{
		int seed = Gen.HashCombineInt(Gen.HashCombineInt(Gen.HashCombineInt(Gen.HashCombineInt(0x57616e67 + (int)surface, face), alongLoop ? 1 : 0), column), row);
		uint h = (uint)seed;
		h ^= h >> 16;
		h *= 0x45d9f3b;
		h ^= h >> 16;
		h *= 0x45d9f3b;
		h ^= h >> 16;
		return (h & 1u) != 0u;
	}

	// The index (into TexturesOf) of the tile for the square in this column and row of a Wang face of `columns` columns:
	// the one whose code matches its four edges. Round the loop (wraps), the last column's E edge is the first column's
	// W edge. Edges on the face's border are free.
	public static int WangTileAt(RingStructureSurface surface, int face, int column, int row, int columns, bool wraps)
	{
		int east = wraps ? (column + 1) % columns : column + 1;
		int code = 0;
		foreach (bool isB in new[]
		{
			EdgeIsB(surface, face, alongLoop: false, column, row + 1),
			EdgeIsB(surface, face, alongLoop: true, east, row),
			EdgeIsB(surface, face, alongLoop: false, column, row),
			EdgeIsB(surface, face, alongLoop: true, column, row),
		})
		{
			code = code * 2 + (isB ? 1 : 0);
		}
		return WangTiles[(int)surface][code];
	}

	public static int VariantCount(RingStructureSurface surface) => Mathf.Max(1, Textures[(int)surface].Length);

	// The variant for the panel segment that starts at this ring position, seeded by its angle (positions round the loop
	// from 0), so neighbouring surfaces at one angle pick independently.
	public static int VariantAt(RingStructureSurface surface, int position, int positions)
	{
		int milliDegrees = Mathf.RoundToInt(360000f * position / positions);
		return Rand.RangeSeeded(0, VariantCount(surface), Gen.HashCombineInt(milliDegrees, 0x52696e67 + (int)surface));
	}

	// Whole texture repeats round a loop of this many ring positions: about one per HexesPerRepeat positions (a position
	// is a hex wide), at least one.
	public static int SegmentsAround(int positions)
	{
		return Mathf.Max(1, Mathf.RoundToInt(positions / HexesPerRepeat));
	}

	// Whole repeats of the wear noise round the loop, about one per WearHexes, at least one.
	public static int WearRepeatsAround(int positions)
	{
		return Mathf.Max(1, Mathf.RoundToInt(positions / WearHexes));
	}

	// The material for one variant of a surface, darkened by the night shadow or never, made on first use, with the ring
	// structure shader, or with vanilla's vertex-colour material if that shader didn't load.
	public static Material MaterialFor(RingStructureSurface surface, int variant, bool nightShadow)
	{
		if (!Materials.TryGetValue((surface, variant, nightShadow), out Material material))
		{
			material = NewMaterial(surface, variant, nightShadow);
			Materials[(surface, variant, nightShadow)] = material;
		}
		return material;
	}

	// The surface a material made here draws, or null for any other material.
	public static RingStructureSurface? SurfaceOf(Material material)
	{
		foreach (KeyValuePair<(RingStructureSurface surface, int, bool), Material> pair in Materials)
		{
			if (pair.Value == material)
			{
				return pair.Key.surface;
			}
		}
		return null;
	}

	private static Material NewMaterial(RingStructureSurface surface, int variant, bool nightShadow)
	{
		Shader shader = RingMaterials.RingShader(ShaderName);
		if (shader == null)
		{
			return WorldMaterials.VertexColor;
		}
		Texture2D[] textures = Textures[(int)surface];
		Material created = new Material(WorldMaterials.VertexColor)
		{
			shader = shader,
			name = $"RingStructure_{surface}_{variant}{(nightShadow ? "_NightShadow" : "")}",
			renderQueue = WorldMaterials.VertexColor.renderQueue,
		};
		created.SetTexture(MainTexID, textures.Length > 0 ? textures[variant % textures.Length] : BaseContent.WhiteTex);
		created.SetTexture(MacroTexID, textures.Length > 0 ? textures[AllATile(surface)] : BaseContent.WhiteTex);
		created.SetTexture(ShaderPropertyIDs.NoiseTex, NoiseTexture);
		created.SetFloat(NightShadowID, nightShadow ? 1f : 0f);
		created.SetFloat(MacroBlendID, IsWang(surface) ? 0f : MacroBlend);
		return created;
	}

	private static Texture2D[] LoadTextures(RingStructureSurface surface)
	{
		Texture2D[] textures = ContentFinder<Texture2D>.GetAllInFolder(TextureFolder + surface).OrderBy(texture => texture.name).ToArray();
		if (textures.Length == 0)
		{
			Log.Warning($"[Ringworld] No textures in Textures/{TextureFolder}{surface}; the ring's {surface} faces are drawn plain white.");
		}
		return textures;
	}

	// The tile index for each edge code if the textures are one complete Wang set, else null. A folder with no edge codes
	// is variants without comment; a half-coded or incomplete one is variants with one warning.
	private static int[] FindWangTiles(RingStructureSurface surface, Texture2D[] textures)
	{
		int[] codes = textures.Select(texture => EdgeCodeOf(texture.name)).ToArray();
		if (codes.All(code => code < 0))
		{
			return null;
		}
		List<string> problems = new List<string>();
		string[] uncoded = textures.Where((texture, i) => codes[i] < 0).Select(texture => texture.name).ToArray();
		if (uncoded.Length > 0)
		{
			problems.Add($"files without an edge code: {string.Join(", ", uncoded)}");
		}
		int[] tiles = Enumerable.Repeat(-1, 16).ToArray();
		List<int> duplicated = new List<int>();
		for (int i = 0; i < codes.Length; i++)
		{
			if (codes[i] < 0)
			{
				continue;
			}
			if (tiles[codes[i]] >= 0)
			{
				duplicated.Add(codes[i]);
			}
			tiles[codes[i]] = i;
		}
		if (duplicated.Count > 0)
		{
			problems.Add($"codes on more than one file: {string.Join(", ", duplicated.Distinct().Select(CodeName))}");
		}
		string[] missing = Enumerable.Range(0, 16).Where(code => tiles[code] < 0).Select(CodeName).ToArray();
		if (missing.Length > 0)
		{
			problems.Add($"codes with no file: {string.Join(", ", missing)}");
		}
		if (problems.Count == 0)
		{
			return tiles;
		}
		WangProblems[(int)surface] = string.Join("; ", problems);
		Log.Warning($"[Ringworld] Textures/{TextureFolder}{surface} has edge codes but isn't one complete Wang set of 16 ({WangProblems[(int)surface]}); its {surface} faces are drawn with its textures as variants.");
		return null;
	}

	private static string CodeName(int code)
	{
		return $"N{((code & 8) != 0 ? 'B' : 'A')}-E{((code & 4) != 0 ? 'B' : 'A')}-S{((code & 2) != 0 ? 'B' : 'A')}-W{((code & 1) != 0 ? 'B' : 'A')}";
	}
}
