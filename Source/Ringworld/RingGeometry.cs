using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ringworld;

// A closed band of hexes on the inside of a cylinder whose axis is the Y axis.
//
// Flat coordinates: u runs around the ring (0 .. circumference), v runs across it (centred on 0).
// World position of (u, v): angle a = u / radius, point = (radius * sin a, v, -radius * cos a).
// With this mapping vanilla PlanetLayer.LongLatOf's longitude is exactly the angle around the ring (the mod then
// replaces it with LocalTimeLongitudeOf).
//
// Hexes are pointy-topped in (u, v): rows run around the ring, and odd rows are shifted by half a hex,
// which closes seamlessly for any number of columns.
//
// Below 100% land coverage only the live arc has tiles: liveTilesAround columns from firstColumn to LastColumn, centred
// on column 0 (negative columns are anti-spinward of it). Tile id = row * liveTilesAround + (column - firstColumn), so
// at 100% (firstColumn 0) it is row * tilesAround + column, as in v0.1.0. Columns are always numbered round the full loop,
// which sets the radius and tile size. Land fades to ocean over the last cutFadeColumns before each cut, pushed in by
// up to maxCutWanderColumns so the coast there wanders.
public sealed class RingGeometry
{
	// Corners in the order they are stored, at angles 330, 270, 210, 150, 90 and 30 degrees in the (u, v) plane.
	// Seen from inside the ring, u points to screen-left, so decreasing angles run counter-clockwise on screen,
	// which makes the vanilla tile triangulation front-facing towards the ring's axis.
	// Corners sit on an integer lattice (u in half tile sizes, v in half corner radii), so tiles that share a corner
	// produce bit-identical positions; vanilla compares corners with exact equality (e.g. landmark rotation).
	private static readonly float[] CornerAnglesDeg = { 330f, 270f, 210f, 150f, 90f, 30f };

	private static readonly int[] CornerLatticeU = { 1, 0, -1, -1, 0, 1 };

	private static readonly int[] CornerLatticeV = { -1, -2, -1, 1, 2, 1 };

	public readonly int tilesAround;

	public readonly int tilesAcross;

	public readonly float radius;

	// Distance between the centres of neighbouring hexes in the same row.
	public readonly float tileSize;

	public readonly float cornerRadius;

	public readonly float rowSpacing;

	// The number of night zones, one per shadow square (the ring's saved RingLayer.ShadowSquareCount): local time runs
	// through a whole day this many times around the full loop.
	public readonly int nightZones;

	// Columns on the live arc: tilesAround at 100% land coverage.
	public readonly int liveTilesAround;

	// The live arc's first (most anti-spinward) column: 0 at 100%, otherwise -liveTilesAround / 2.
	public readonly int firstColumn;

	public int LastColumn => firstColumn + liveTilesAround - 1;

	// Below 100% land coverage, land fades to ocean over this many columns before each cut (0 on a full loop), like
	// vanilla's island conversion on a partial-coverage planet. Wider rings get a wider fade, so the coast at a cut is
	// about as irregular as the ring is wide.
	public readonly float cutFadeColumns;

	// Tiles this close to a cut are always ocean: enough of them that vanilla's lake step (ocean bodies of 15 tiles or
	// fewer become lakes) never sees the ocean at the cut as a lake, even on the narrowest ring.
	public const float CutOceanColumns = 2f;

	private const float MinCutFadeColumns = 5f;

	private const float CutFadeColumnsPerTileAcross = 0.3f;

	// How far, in columns, the fade can be pushed further into the arc (CutFade's wander noise), so the coast at a cut
	// wanders like any other coast instead of running straight across the ring: up to half the tiles across, less on
	// a short arc so the fade and the wander together take at most a quarter of it.
	public readonly float maxCutWanderColumns;

	private const float MaxCutWanderColumnsPerTileAcross = 0.5f;

	// The fade and its wander together take at most this share of the arc at each cut, so a very short arc
	// (Worldbuilder's lowest coverages) never fades away entirely.
	private const float MaxCutShareOfArc = 0.25f;

	// The live arc is the whole loop, so columns wrap at the seam; otherwise edge tiles at the cuts have no neighbour
	// across them.
	public bool IsFullLoop => liveTilesAround == tilesAround;

	public int TileCount => liveTilesAround * tilesAcross;

	public float HalfWidth => (tilesAcross - 1) * 0.5f * rowSpacing + cornerRadius;

	// Distance from the midline of the rim rows' outer corners that aren't hex tips: the straight line each rim row's
	// zigzag outline touches. The rim row's hex tips stick out past it by half a corner radius, to HalfWidth.
	public float RimLine => HalfWidth - 0.5f * cornerRadius;

	// radius: the full loop's radius; by default the size's (RingSize.Radius), so hexes are vanilla-sized. An atmosphere
	// layer passes its own (RingAtmosphere), with tiles around scaled to it, so its hexes are within a rounding of that.
	public RingGeometry(RingSize size, float landCoverage, int nightZones, float? radius = null)
	{
		this.nightZones = nightZones;
		tilesAround = size.tilesAround;
		tilesAcross = size.tilesAcross;
		liveTilesAround = size.LiveTilesAround(landCoverage);
		firstColumn = IsFullLoop ? 0 : -(liveTilesAround / 2);
		this.radius = radius ?? size.Radius;
		tileSize = 2f * Mathf.PI * this.radius / this.tilesAround;
		cornerRadius = tileSize / Mathf.Sqrt(3f);
		rowSpacing = 1.5f * cornerRadius;
		// The wander shrinks first, so the fade itself only shrinks on an arc too short to hold it.
		cutFadeColumns = IsFullLoop ? 0f : Mathf.Min(Mathf.Max(MinCutFadeColumns, tilesAcross * CutFadeColumnsPerTileAcross), liveTilesAround * MaxCutShareOfArc);
		maxCutWanderColumns = IsFullLoop ? 0f : Mathf.Clamp(liveTilesAround * MaxCutShareOfArc - cutFadeColumns, 0f, tilesAcross * MaxCutWanderColumnsPerTileAcross);
	}

	// The tile at (row, column), with the column wrapped round the full loop, or -1 if that column isn't on the live arc.
	public int TileId(int row, int column)
	{
		int index = Wrap(column - firstColumn, tilesAround);
		return index < liveTilesAround ? row * liveTilesAround + index : -1;
	}

	public int RowOf(int tileId)
	{
		return tileId / liveTilesAround;
	}

	// The tile's column round the full loop, firstColumn .. LastColumn.
	public int ColumnOf(int tileId)
	{
		return tileId % liveTilesAround + firstColumn;
	}

	// Every column on the live arc, anti-spinward to spinward.
	public IEnumerable<int> LiveColumns()
	{
		for (int column = firstColumn; column <= LastColumn; column++)
		{
			yield return column;
		}
	}

	// The first and last rows, one on each rim (just row 0 on a one-row ring).
	public int[] RimRows => tilesAcross > 1 ? new[] { 0, tilesAcross - 1 } : new[] { 0 };

	public bool IsRimRow(int row)
	{
		return row == 0 || row == tilesAcross - 1;
	}

	// The wall foot is the rim rows' land, which the wall-foot step makes impassable, so it's read from the tile's
	// hilliness (rim-row water is left flat): it ends at every coast, a cut's included. v0.1.0 saves, whose rim rows are
	// impassable all the way round, read the same. RingLayer.IsWallFootTile reads the hilliness from the layer.
	public bool IsWallFootTile(int tileId, Hilliness hilliness)
	{
		return IsRimRow(RowOf(tileId)) && hilliness == Hilliness.Impassable;
	}

	// How far in from the nearer cut a tile's centre is, in columns: 0 for the outermost tile of a row at each end of the
	// live arc, 0.5 for the next row's (odd rows are shifted half a column). Infinite on a full loop, which has no cuts.
	public float ColumnsFromCut(int tileId)
	{
		float shift = (RowOf(tileId) & 1) == 1 ? 0.5f : 0f;
		return ColumnsFromCut(ColumnOf(tileId) + shift);
	}

	// The same for a layer-local point, from its angle round the ring.
	public float ColumnsFromCut(Vector3 point)
	{
		return ColumnsFromCut(AngleOf(point) * tilesAround / (2f * Mathf.PI));
	}

	// columns: position round the ring in columns from column 0's centre, -tilesAround / 2 .. tilesAround / 2. The live
	// arc's tile centres run from firstColumn to LastColumn + 0.5; anything outside it (the stand-in span) is 0.
	private float ColumnsFromCut(float columns)
	{
		if (IsFullLoop)
		{
			return float.PositiveInfinity;
		}
		return Mathf.Max(0f, Mathf.Min(columns - firstColumn, LastColumn + 0.5f - columns));
	}

	// How far land has faded to ocean at this distance from a cut: 0 outside the fade, rising to 1 at CutOceanColumns
	// and closer. CutFade passes the distance less its wander, which only ever moves the fade inward.
	public float CutFadeAt(float columnsFromCut)
	{
		if (columnsFromCut >= cutFadeColumns)
		{
			return 0f;
		}
		return Mathf.InverseLerp(cutFadeColumns, CutOceanColumns, columnsFromCut);
	}

	// The agreed range of vanilla latitudes whose climate the rim rows should match, in degrees.
	public static readonly FloatRange RimVanillaLatitudeRange = new FloatRange(60f, 70f);

	// The vanilla latitude the rim rows' climate matches. Near the warm end of RimVanillaLatitudeRange, so the wall foot
	// seldom gets cold enough for ice sheet at the default temperature.
	public const float RimVanillaLatitude = 62f;

	// The climate gradient, as the latitude in degrees that vanilla's climate code sees for a tile.
	// 0 at the midline and +-RimVanillaLatitude on the rim rows, linear in the row so rings of any tiles across span
	// the same range. Positive towards the last row, the side vanilla's own latitude (from v) would call north.
	public float VanillaLatitudeOf(int tileId)
	{
		if (tilesAcross < 2)
		{
			return 0f;
		}
		float midline = (tilesAcross - 1) * 0.5f;
		return (RowOf(tileId) - midline) / midline * RimVanillaLatitude;
	}

	// The longitude in degrees that vanilla's local time sees for a tile, which makes the night zones: the angle around the ring
	// times nightZones, wrapped back into -180..180. So each 1/nightZones of the ring runs through all 24 time zones,
	// and local dates still differ by at most a day.
	public float LocalTimeLongitudeOf(int tileId)
	{
		float angle = CenterUV(RowOf(tileId), ColumnOf(tileId)).x / radius * Mathf.Rad2Deg;
		return Mathf.Repeat(angle * nightZones + 180f, 360f) - 180f;
	}

	// Kilometres between the centres of neighbouring hexes, for ring coordinates only. Ringworld's own convention: vanilla
	// has no size for a tile.
	public const double KmPerHex = 25.0;

	// The tile's ring coordinates, from its row and column (never from LongLatOf, which is climate and local time).
	// Around: the tile centre's angle from column 0 (the live arc's centre), positive spinward, in -180..180. Column 0
	// sits at angle 0 round the axis on every ring geometry, so the origin is the surface ring's on any ring-shaped layer. Across:
	// distance of its row from the midline, negative towards port (row 0).
	public RingCoordinates CoordinatesOf(int tileId)
	{
		int row = RowOf(tileId);
		double shift = (row & 1) == 1 ? 0.5 : 0.0;
		double degrees = (ColumnOf(tileId) + shift) * 360.0 / tilesAround;
		if (degrees > 180.0)
		{
			degrees -= 360.0;
		}
		double km = (row - (tilesAcross - 1) * 0.5) * 1.5 / System.Math.Sqrt(3.0) * KmPerHex;
		return new RingCoordinates(degrees, km);
	}

	// Every tile of the rim rows, along the live arc.
	public IEnumerable<int> RimTiles()
	{
		foreach (int row in RimRows)
		{
			foreach (int column in LiveColumns())
			{
				yield return TileId(row, column);
			}
		}
	}

	public Vector2 CenterUV(int row, int column)
	{
		float shift = (row & 1) == 1 ? 0.5f : 0f;
		return new Vector2((column + shift) * tileSize, (row - (tilesAcross - 1) * 0.5f) * rowSpacing);
	}

	public Vector3 ToWorld(float u, float v)
	{
		float a = u / radius;
		return new Vector3(radius * Mathf.Sin(a), v, -radius * Mathf.Cos(a));
	}

	// Corner k of the tile at (row, column), computed only from its lattice point.
	private Vector3 CornerWorld(int row, int column, int k)
	{
		int latticeU = Wrap(2 * column + (row & 1) + CornerLatticeU[k], 2 * tilesAround);
		int latticeV = 3 * row + CornerLatticeV[k];
		float v = (latticeV - 1.5f * (tilesAcross - 1)) * 0.5f * cornerRadius;
		return ToWorld(latticeU * 0.5f * tileSize, v);
	}

	// Distance of a layer-local point from the ring's axis.
	public static float DistanceFromAxis(Vector3 point)
	{
		return Mathf.Sqrt(point.x * point.x + point.z * point.z);
	}

	// Unit vector from a layer-local point towards the ring's axis: the ring's "up" at that point.
	public static Vector3 InwardNormal(Vector3 point)
	{
		Vector3 flat = new Vector3(0f - point.x, 0f, 0f - point.z);
		float length = flat.magnitude;
		return length > 1E-06f ? flat / length : Vector3.up;
	}

	// Whether a layer-local point is within a tenth of the radius of the ring surface: drawn there, not on an orbit layer.
	public static bool NearSurface(Vector3 point, float ringRadius)
	{
		return Mathf.Abs(DistanceFromAxis(point) - ringRadius) < ringRadius * 0.1f;
	}

	// Reflects a layer-local point across the ring surface (distance from the axis r -> 2R - r).
	// Vanilla lifts overlays outward along pos.normalized; on the ring that is below the surface, seen from inside.
	public static Vector3 MirrorAcrossSurface(Vector3 point, float ringRadius)
	{
		float distance = DistanceFromAxis(point);
		if (distance < 1E-06f)
		{
			return point;
		}
		float scale = (2f * ringRadius - distance) / distance;
		return new Vector3(point.x * scale, point.y, point.z * scale);
	}

	// Unit vector from the axis towards the point at this angle around the ring; the inverse of AngleOf.
	public static Vector3 Outward(float angle)
	{
		return new Vector3(Mathf.Sin(angle), 0f, 0f - Mathf.Cos(angle));
	}

	// Angle around the ring, in radians, of a layer-local point.
	public static float AngleOf(Vector3 point)
	{
		return Mathf.Atan2(point.x, -point.z);
	}

	// Writes the four packed arrays PlanetLayer expects: per-tile corners and per-tile neighbours,
	// with neighbour k being the tile across the edge between corner k and corner k + 1 (as in vanilla).
	public void Build(int layerId, bool flipWinding, List<Vector3> verts, List<int> vertOffsets, List<int> neighborOffsets, List<PlanetTile> neighborValues, out float averageTileSize)
	{
		float[] corners = new float[6];
		int[] cornerIndices = new int[6];
		for (int i = 0; i < 6; i++)
		{
			cornerIndices[i] = flipWinding ? 5 - i : i;
			corners[i] = CornerAnglesDeg[cornerIndices[i]] * Mathf.Deg2Rad;
		}
		double distanceSum = 0.0;
		int distanceCount = 0;
		for (int row = 0; row < tilesAcross; row++)
		{
			foreach (int column in LiveColumns())
			{
				Vector2 center = CenterUV(row, column);
				Vector3 centerWorld = ToWorld(center.x, center.y);
				vertOffsets.Add(verts.Count);
				for (int k = 0; k < 6; k++)
				{
					verts.Add(CornerWorld(row, column, cornerIndices[k]));
				}
				neighborOffsets.Add(neighborValues.Count);
				for (int k = 0; k < 6; k++)
				{
					float a = corners[k];
					float b = corners[(k + 1) % 6];
					float edgeDeg = Mathf.Atan2(Mathf.Sin(a) + Mathf.Sin(b), Mathf.Cos(a) + Mathf.Cos(b)) * Mathf.Rad2Deg;
					int direction = Wrap(Mathf.RoundToInt(edgeDeg / 60f), 6);
					if (TryGetNeighbor(row, column, direction, out int neighborRow, out int neighborColumn))
					{
						neighborValues.Add(new PlanetTile(TileId(neighborRow, neighborColumn), layerId));
						Vector2 neighborCenter = CenterUV(neighborRow, neighborColumn);
						distanceSum += Vector3.Distance(centerWorld, ToWorld(neighborCenter.x, neighborCenter.y));
						distanceCount++;
					}
				}
			}
		}
		averageTileSize = distanceCount > 0 ? (float)(distanceSum / distanceCount) : 1f;
	}

	// direction: 0 = +u, 1 = up-right, 2 = up-left, 3 = -u, 4 = down-left, 5 = down-right (multiples of 60 degrees).
	// Columns wrap around the ring; rows stop at the two rims, and columns at the live arc's cuts.
	private bool TryGetNeighbor(int row, int column, int direction, out int neighborRow, out int neighborColumn)
	{
		bool odd = (row & 1) == 1;
		neighborRow = row;
		neighborColumn = column;
		switch (direction)
		{
		case 0:
			neighborColumn = column + 1;
			break;
		case 1:
			neighborRow = row + 1;
			neighborColumn = odd ? column + 1 : column;
			break;
		case 2:
			neighborRow = row + 1;
			neighborColumn = odd ? column : column - 1;
			break;
		case 3:
			neighborColumn = column - 1;
			break;
		case 4:
			neighborRow = row - 1;
			neighborColumn = odd ? column : column - 1;
			break;
		case 5:
			neighborRow = row - 1;
			neighborColumn = odd ? column + 1 : column;
			break;
		}
		neighborColumn = Wrap(neighborColumn - firstColumn, tilesAround) + firstColumn;
		return neighborRow >= 0 && neighborRow < tilesAcross && neighborColumn <= LastColumn;
	}

	private static int Wrap(int value, int count)
	{
		int result = value % count;
		return result < 0 ? result + count : result;
	}
}
