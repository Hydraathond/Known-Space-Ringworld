#version 450

// The ring's copy of vanilla WorldOverlayLitPollution.frag.glsl: the pollution texture follows the unrolled ring
// (RW_RingUV.glsl), and the night shadow darkens it (RW_RingShadow.glsl).

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
    vec4 _Color;      // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _RingRadius; // @default = 100.0
    vec4 _RingShadow;  // global, no default: see RW_RingShadow.glsl
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

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
    vec4 fin = ringTexture(_MainTex, ringUV(vWorldPos, uMaterial._RingRadius, ringRepeatsPerUnit(16.0)));
    fin *= vColor * uMaterial._Color;
    fin.rgb *= sunDiffuseLight(normal, uFragment.SunDir.xyz) * ringNightShadow(vWorldPos, uMaterial._RingShadow);
    outColor = fin;
}
