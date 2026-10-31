#version 450

// The ring's copy of vanilla Roads.frag.glsl: the night shadow darkens it (RW_RingShadow.glsl). Vanilla's has no material
// uniforms; this one has a block just for the night shadow.

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
    vec4 _RingShadow; // global, no default: see RW_RingShadow.glsl
} uMaterial;

const float MaxHillsHighlightAtElevation = 4500.0;
const float MaxHillsHighlight = 0.12;
const float HillsHighlightPowFactor = 0.8;

const vec3 NightColor = vec3(0.482, 0.603, 0.682);
const vec3 DayColor   = vec3(1.0, 1.0, 1.0);
const float DayNightStart = 0.45;
const float DayNightEnd   = 0.55;

vec3 sunDiffuseLight(vec3 normal, vec3 lightDir)
{
    if (dot(lightDir, lightDir) < 1e-6)
        return DayColor;
    float light = 1.0 - (dot(normal, normalize(lightDir)) * 0.5 + 0.5);
    float t = (light - DayNightStart) / (DayNightEnd - DayNightStart);
    return mix(NightColor, DayColor, clamp(t, 0.0, 1.0));
}

void main()
{
    vec3 normal = normalize(vWorldPos);

    vec4 fin = vColor;

    // Anti-aliased mask edge: widen/narrow the 0.5 alpha threshold by the screen-space gradient.
    float blur = max(length(vec2(dFdx(vColor.a), dFdy(vColor.a))), 1e-5);
    fin.a = clamp((vColor.a - 0.5) / blur + 0.5, 0.0, 1.0);

    // Hills highlight from elevation
    fin += pow(min(vUv.x / MaxHillsHighlightAtElevation, 1.0), HillsHighlightPowFactor) * MaxHillsHighlight;

    fin.rgb *= sunDiffuseLight(normal, uFragment.SunDir.xyz) * ringNightShadow(vWorldPos, uMaterial._RingShadow);
    outColor = fin;
}
