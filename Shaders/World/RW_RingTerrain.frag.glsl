#version 450

// The ring's copy of vanilla WorldTerrain.frag.glsl: biome and macro textures follow the unrolled ring (RW_RingUV.glsl),
// and the night shadow darkens it (RW_RingShadow.glsl).

#include "RW_RingUV.glsl"
#include "RW_RingShadow.glsl"

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;       // x = adjusted elevation, y = raw elevation
layout(location = 2) in vec3 vWorldPos;
layout(location = 3) in vec2 vUv2;      // landmark mask UV (mesh UV1; default 0 on non-landmark tiles)

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
    float _Blend;     // @default = 1.0 (0 = landmark "replace terrain with mask colour" mode)
    float _RingRadius; // @default = 100.0
    vec4 _RingShadow;  // global, no default: see RW_RingShadow.glsl
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _MaskTex;      // landmark mask; white default -> pure terrain
layout(set = 2, binding = 3) uniform sampler2D _SecondaryTex; // background (ocean) biome for the coastal blend

layout(set = 2, binding = 4) uniform sampler2D _MacroTex; // global

const float BiomeTextureUVMultiplier = 40.0;
const float MacroTextureUVMultiplier = 3.0;

const float MaxHillsHighlightAtElevation = 4500.0;
const float MaxHillsHighlight = 0.12;
const float HillsHighlightPowFactor = 0.8;

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

vec4 mixLighting(vec4 color, vec3 normal)
{
    color.rgb *= sunDiffuseLight(normal, uFragment.SunDir.xyz) * ringNightShadow(vWorldPos, uMaterial._RingShadow);
    color.a = 1.0;
    return color;
}

vec4 getTerrainColor(vec2 uvElev, vec3 normal, vec4 biomeTexCol, vec4 macroTexCol)
{
    vec4 fin = biomeTexCol * macroTexCol;
    fin += pow(min(uvElev.x / MaxHillsHighlightAtElevation, 1.0), HillsHighlightPowFactor) * MaxHillsHighlight;
    return mixLighting(fin, normal);
}

vec4 getOceanColor(vec2 uvElev, vec3 normal, vec4 biomeTexCol, vec4 macroTexCol)
{
    vec4 fin = biomeTexCol * macroTexCol;
    float elevY = min(uvElev.y, 0.0);
    float depthFactor = pow(min(elevY / MaxSeaDepthAtElevation, 1.0), SeaDepthPowFactor);
    fin *= 1.0 - depthFactor * MaxSeaDepthBiomeTexColMultiplier;
    fin = mix(fin, MaxSeaDepthColor, depthFactor);
    fin += (1.0 - min(elevY / CoastMinElevation, 1.0)) * CoastColorStrength;
    fin = mixLighting(fin, normal);
    fin.rgb += oceanSpecularColor(normal, uFragment.SunDir.xyz).rgb;
    return fin;
}

void main()
{
    vec3 normal = normalize(vWorldPos);
    RingUV biomeUV = ringUV(vWorldPos, uMaterial._RingRadius, ringRepeatsPerUnit(BiomeTextureUVMultiplier));
    RingUV macroUV = ringUV(vWorldPos, uMaterial._RingRadius, ringRepeatsPerUnit(MacroTextureUVMultiplier));

    vec4 maskTexCol = texture(_MaskTex, vec2(vUv2.x, 1.0 - vUv2.y));

    vec4 landTexCol      = ringTexture(_MainTex, biomeUV);
    vec4 secondaryTexCol = ringTexture(_SecondaryTex, biomeUV);
    vec4 macroTexCol     = ringTexture(_MacroTex, macroUV);

    vec4 terrain = getTerrainColor(vUv, normal, landTexCol, macroTexCol);
    vec4 ocean   = getOceanColor(vUv, normal, secondaryTexCol, macroTexCol);

    if (uMaterial._Blend == 0.0)
    {
        vec4 fin = mixLighting(maskTexCol, normal);
        outColor = mix(fin, ocean, 1.0 - maskTexCol.a);
        return;
    }

    const float cutoff = 0.5;
    if (maskTexCol.r < cutoff && maskTexCol.g < cutoff && maskTexCol.b < cutoff)
        terrain = mixLighting(maskTexCol, normal);

    outColor = mix(terrain, ocean, 1.0 - maskTexCol.a);
}
