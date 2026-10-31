#version 450

// The ring's copy of vanilla WorldOcean.frag.glsl: the ocean and macro textures follow the unrolled ring (RW_RingUV.glsl),
// and the night shadow darkens it (RW_RingShadow.glsl).

#include "RW_RingUV.glsl"
#include "RW_RingShadow.glsl"

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;       // x = adjusted elevation, y = raw elevation (negative under sea level)
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
    vec4 _Color;      // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _RingRadius; // @default = 100.0
    vec4 _RingShadow;  // global, no default: see RW_RingShadow.glsl
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

layout(set = 2, binding = 2) uniform sampler2D _MacroTex; // global

const float BiomeTextureUVMultiplier = 40.0;
const float MacroTextureUVMultiplier = 3.0;

const float MaxSeaDepthAtElevation = -320.0;
const float MaxSeaDepthBiomeTexColMultiplier = 0.2;
const float SeaDepthPowFactor = 1.5;
const vec4  MaxSeaDepthColor = vec4(0.173, 0.204, 0.259, 1.0);

const float CoastMinElevation = -100.0;
const float CoastColorStrength = 0.07;

const vec3 NightColor = vec3(0.482, 0.603, 0.682);
const vec3 DayColor   = vec3(1.0, 1.0, 1.0);
const float DayNightStart = 0.45;
const float DayNightEnd   = 0.55;

const float WaterSpecularAttenuation = 0.15;
const float WaterShininess = 15.0;

vec3 sunDiffuseLight(vec3 normal, vec3 lightDir)
{
    if (dot(lightDir, lightDir) < 1e-6)
        return DayColor;
    float light = 1.0 - (dot(normal, normalize(lightDir)) * 0.5 + 0.5);
    float t = (light - DayNightStart) / (DayNightEnd - DayNightStart);
    return mix(NightColor, DayColor, clamp(t, 0.0, 1.0));
}

vec4 oceanSpecularColor(vec3 normal, vec3 lightDir)
{
    vec3 viewDirection = -vec3(uFragment.WorldToCamera[0][2], uFragment.WorldToCamera[1][2], uFragment.WorldToCamera[2][2]); 
    return vec4(WaterSpecularAttenuation * pow(max(0.0, dot(reflect(-lightDir, normal), viewDirection)), WaterShininess));
}

void main()
{
    vec3 normal = normalize(vWorldPos);
    vec4 fin = ringTexture(_MainTex, ringUV(vWorldPos, uMaterial._RingRadius, ringRepeatsPerUnit(BiomeTextureUVMultiplier)));
    fin *= ringTexture(_MacroTex, ringUV(vWorldPos, uMaterial._RingRadius, ringRepeatsPerUnit(MacroTextureUVMultiplier)));   // macro overlay, before sea depth (per getOceanColor)

    float elevY = min(vUv.y, 0.0);
    float depthFactor = pow(min(elevY / MaxSeaDepthAtElevation, 1.0), SeaDepthPowFactor);
    fin *= 1.0 - depthFactor * MaxSeaDepthBiomeTexColMultiplier;
    fin = mix(fin, MaxSeaDepthColor, depthFactor);

    // Coast glow (shallow water brightens slightly).
    fin += (1.0 - min(elevY / CoastMinElevation, 1.0)) * CoastColorStrength;

    fin.rgb *= sunDiffuseLight(normal, uFragment.SunDir.xyz) * ringNightShadow(vWorldPos, uMaterial._RingShadow);
    fin += oceanSpecularColor(normal, uFragment.SunDir.xyz);
    fin.a = 1.0;
    outColor = fin;
}
