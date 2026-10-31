#version 450

// The ring's copy of vanilla Rivers.frag.glsl: the night shadow darkens it (RW_RingShadow.glsl).

#include "RW_RingShadow.glsl"

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;       // x = elevation
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
    float _Alpha; // @default = 1.0
    float _Specular; // @default = 1.0
    vec4 _RingShadow; // global, no default: see RW_RingShadow.glsl
} uMaterial;

const float MaxHillsHighlightAtElevation = 4500.0;
const float MaxHillsHighlight = 0.12;
const float HillsHighlightPowFactor = 0.8;

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

    vec4 fin = vColor;

    // Anti-aliased mask edge; clip (discard) the sub-threshold border before saturating.
    float blur = max(length(vec2(dFdx(vColor.a), dFdy(vColor.a))), 1e-5);
    float aa = (vColor.a - 0.5) / blur + 0.5;
    if (aa < 0.0)
        discard;
    fin.a = clamp(aa, 0.0, 1.0) * uMaterial._Alpha;

    // Hills highlight from elevation
    fin += pow(min(vUv.x / MaxHillsHighlightAtElevation, 1.0), HillsHighlightPowFactor) * MaxHillsHighlight;

    fin.rgb *= sunDiffuseLight(normal, uFragment.SunDir.xyz) * ringNightShadow(vWorldPos, uMaterial._RingShadow);
    fin += oceanSpecularColor(normal, uFragment.SunDir.xyz) * uMaterial._Specular;
    outColor = fin;
}
