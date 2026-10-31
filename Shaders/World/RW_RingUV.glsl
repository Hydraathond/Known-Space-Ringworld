// Texture coordinates on the ring, shared by the ring's copies of vanilla world shaders.
// Vanilla textures the world by the direction from the planet centre (sphereCoordsToUV: a cube map with a fixed number
// of repeats per face). That suits the radius-100 planet but magnifies textures by radius / 40 on a ring. These map
// onto the unrolled ring instead: arc length around and height across. The ring's axis is world Y and the angle
// matches RingGeometry.AngleOf. _RingRadius is set on each ring material (RingMaterials).

const float RingTwoPi = 6.28318531;

struct RingUV
{
    vec2 uv;
    vec2 ddx;
    vec2 ddy;
};

// repeatsPerUnit: texture repeats per world unit, rounded so a whole number fits around the ring and the wrap has no
// seam. Call from uniform control flow (it takes screen-space derivatives).
RingUV ringUV(vec3 p, float radius, float repeatsPerUnit)
{
    float around = max(1.0, floor(RingTwoPi * radius * repeatsPerUnit + 0.5));
    float perRadian = around / RingTwoPi;
    float perUnit = around / (RingTwoPi * radius);
    vec3 dx = dFdx(p);
    vec3 dy = dFdy(p);
    float r2 = max(p.x * p.x + p.z * p.z, 1e-6);
    RingUV r;
    r.uv = vec2(atan(p.x, -p.z) * perRadian, p.y * perUnit);
    // The angle jumps by 2 pi at the wrap, so its derivatives are taken analytically: the mip level stays right there.
    r.ddx = vec2((p.x * dx.z - p.z * dx.x) / r2 * perRadian, dx.y * perUnit);
    r.ddy = vec2((p.x * dy.z - p.z * dy.x) / r2 * perRadian, dy.y * perUnit);
    return r;
}

vec4 ringTexture(sampler2D tex, RingUV r)
{
    return textureGrad(tex, r.uv, r.ddx, r.ddy);
}

// Vanilla's repeats per cube face, as repeats per world unit at the centre of a face of the radius-100 planet.
float ringRepeatsPerUnit(float vanillaRepeatsPerFace)
{
    return vanillaRepeatsPerFace * 0.5 / 100.0;
}
