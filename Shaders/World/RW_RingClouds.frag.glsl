#version 450

// The ring's copy of vanilla Clouds.frag.glsl, drawn on the cloud band (WorldDrawLayer_RingClouds). Vanilla's clouds,
// self-shadow, sunset and night colours, with five changes:
// - The cloud map is read on the unrolled ring, at vanilla's texels per world unit: vanilla wraps it once round its
//   radius-100 planet and stretches it pole to pole, so here it repeats a whole number of times round the loop (no seam)
//   and the band, rim to rim, takes the strip round vanilla's equator. A second sample, at another whole repeat count
//   and offset, is blended in so the pattern doesn't visibly repeat on a big ring. The repeat counts come from the
//   material (_Repeats1, _Repeats2); the blend is a weighted mean, so it adds no clouds by itself.
// - _DensityScale scales the raw cloud map before vanilla's contrast curve: more or fewer clouds (the density setting).
// - Day and night come from the night shadow (RW_RingShadow.glsl) instead of vanilla's sun direction and global sun
//   switch, which Ringworld turns off: full daylight outside the night shadows, vanilla's night side inside them, and
//   vanilla's sunset colours across their soft edges.
// - Vanilla's view-angle haze is replaced by a blue haze that never depends on the viewing angle (it turned the whole
//   band opaque grey, as the orbit view sees the inner surface nearly edge-on): vanilla's daytime atmosphere colour as a
//   faint even tint (_HazeBase) plus a share that thickens towards each rim wall (_HazeEdge, the square of the distance
//   across over the half-width _RimLine). Both come from the material, capped well below opaque (Haze constants in
//   WorldDrawLayer_RingClouds.cs), darkened to the night colour inside night shadows, and independent of _DensityScale.
//   The clouds are composited over it, and _FinalOpacityMult fades both.
// - With the sun overhead, the clouds' fake self-shadow is cast a fixed distance spinward.

#include "RW_RingUV.glsl"
#include "RW_RingShadow.glsl"

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec3 vWorldPos;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform FragmentUniforms
{
    vec4 SunDir;
    mat4 WorldToCamera;
    mat4 ViewProj;
} uFragment;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec2 _Seed;              // material SetVector(_Seed)
    float _FinalOpacityMult; // material SetFloat(_FinalOpacityMult) - 0 in the ring view
    float _GameTime;
    float _RingRadius;       // @default = 100.0
    float _Repeats1;         // @default = 1.0
    float _Repeats2;         // @default = 2.0
    float _DensityScale;         // @default = 1.0
    float _RimLine;          // @default = 10.0
    float _HazeBase;         // @default = 0.0
    float _HazeEdge;         // @default = 0.0
    vec4 _RingShadow;        // global, no default: see RW_RingShadow.glsl
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _NoiseTex;
layout(set = 2, binding = 2) uniform sampler2D _SunsetColorRamp;
layout(set = 2, binding = 3) uniform sampler2D _CloudTex;

const vec3 CloudColor      = vec3(0.9, 0.9, 0.95);
const vec3 NightCloudColor = vec3(0.25, 0.25, 0.35);
const vec3 NightColor      = vec3(0.0, 0.12, 0.2);
const vec3 ShadowTint      = vec3(0.4, 0.4, 0.4);

const float CloudMoveSpeed           = 0.0002;
const float CloudDanceSpeed          = 0.015;
const float CloudOpacity             = 1.0;
const float CloudDomainWarpFrequency = 10.0;
const float CloudDomainWarpIntensity = 0.0015;

// Vanilla's planet radius, which its cloud map is sized for, and its self-shadow offset (0.01 of that radius).
const float VanillaRadius = 100.0;
const float CloudShadowWorldDistance = 1.0;

// The second sample's offset (in cloud-map widths) and weight against the first.
const float SecondOffset = 0.37;
const float SecondWeight = 0.9;

// Returns (uv.x, uv.y, cover) where cover in [0,1] is the cloud density.
vec3 CalculateCloudCover(float lod, vec2 uv)
{
    uv.x += uMaterial._GameTime * CloudMoveSpeed + uMaterial._Seed.x;
    vec2 distortionUV = uv * CloudDomainWarpFrequency;
    distortionUV.y += uMaterial._GameTime * CloudDanceSpeed + uMaterial._Seed.x;
    distortionUV.x *= 2.0;
    vec2 distortion = 1.0 - 2.0 * texture(_NoiseTex, vec2(distortionUV.x, -distortionUV.y)).xy;
    uv += distortion * CloudDomainWarpIntensity;
    float u = mix(uv.x, 1.0 - uv.x, step(0.5, fract(uMaterial._Seed.y)));
    float cover = textureLod(_CloudTex, vec2(u, 1.0 - uv.y), lod).r;
    return vec3(uv.x, uv.y, cover);
}

// Blends the two samples' raw cover, scales it by the coverage and applies vanilla's contrast curve.
float CloudCover(float lod, vec3 p, float shadowUnits)
{
    float ringU = atan(p.x, -p.z) / RingTwoPi;
    float perUnit1 = uMaterial._Repeats1 / (RingTwoPi * uMaterial._RingRadius);
    float perUnit2 = uMaterial._Repeats2 / (RingTwoPi * uMaterial._RingRadius);
    float v1 = 0.5 + 0.5 * p.y / VanillaRadius;
    // The second sample is at its own scale (its texels per unit follow its repeat count) and offset, so the two
    // patterns drift apart round the loop.
    float v2 = 0.5 + 0.5 * p.y / VanillaRadius * uMaterial._Repeats2 / uMaterial._Repeats1;
    vec2 uv1 = vec2(ringU * uMaterial._Repeats1 - shadowUnits * perUnit1, v1);
    vec2 uv2 = vec2(ringU * uMaterial._Repeats2 - shadowUnits * perUnit2 + SecondOffset, v2);
    float cover = (CalculateCloudCover(lod, uv1).z + SecondWeight * CalculateCloudCover(lod, uv2).z) / (1.0 + SecondWeight);
    cover = clamp(cover * uMaterial._DensityScale, 0.0, 1.0);
    cover = smoothstep(0.2, 0.5, pow(cover, 0.5)) * cover;
    return cover;
}

void main()
{
    vec3 p = vWorldPos;

    // Vanilla's diffuse light and sunset share, from the night shadow instead of the sun.
    float diffuse = 1.0 - ringNightDarkness(p, uMaterial._RingShadow);
    float sunsetPercent = 1.0 - diffuse;

    vec3 atmosphere = textureLod(_SunsetColorRamp, vec2(sunsetPercent, 0.0), 0.0).rgb;
    float lod = 0.0;

    // Sample clouds, and again a little way off to fake self-shadowing.
    float cloudSample = CloudCover(lod, p, 0.0);
    float shadow = CloudCover(lod, p, CloudShadowWorldDistance);

    float alpha = CloudOpacity * cloudSample * uMaterial._FinalOpacityMult;

    vec3 applyShadow     = mix(atmosphere, ShadowTint, clamp(shadow - cloudSample, 0.0, 1.0));
    vec3 applyCloud      = mix(applyShadow, CloudColor, cloudSample);
    vec3 applyNightCloud = mix(applyCloud, mix(NightColor, NightCloudColor, cloudSample), 1.0 - diffuse);

    // The haze: vanilla's daytime atmosphere colour, darkened by the night shadow, thicker towards the rim walls.
    float across = clamp(abs(p.y) / max(uMaterial._RimLine, 1e-6), 0.0, 1.0);
    float hazeAlpha = (uMaterial._HazeBase + uMaterial._HazeEdge * across * across) * uMaterial._FinalOpacityMult;
    vec3 dayAtmosphere = textureLod(_SunsetColorRamp, vec2(0.0, 0.0), 0.0).rgb;
    vec3 hazeColor = mix(dayAtmosphere, NightColor, 1.0 - diffuse);

    // Clouds over the haze (straight alpha).
    float outAlpha = alpha + hazeAlpha * (1.0 - alpha);
    vec3 outRgb = (applyNightCloud * alpha + hazeColor * hazeAlpha * (1.0 - alpha)) / max(outAlpha, 1e-6);
    outColor = vec4(outRgb, outAlpha);
}
