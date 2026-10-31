#version 450

// The ring structure shader: the ring's thickness (outer face, rim walls) and the shadow squares. Not a copy of a vanilla
// shader; its pipeline settings are vanilla's WorldVertexColor's, which drew these faces flat before. Unlit, like that.
// Three things keep one PNG from showing as an obvious tile (RingStructure.cs, which also documents the UV channels):
// - Variants: each panel segment's material has one of the surface's PNGs as _MainTex, picked when the mesh is built.
// - Two scales: the texture is blended with the surface's first variant (_MacroTex, the same on every segment, so the
//   blend has no seams) at MacroScale times the size. A Wang set has no blend (_MacroBlend 0): its tiles already vary, and
//   one tile repeating every MacroScale x 4 hexes would show as a regular ghost pattern.
// - Wear: vanilla's noise texture, read in world space over hundreds of hexes, varies the brightness and adds rust and
//   dust streaks.
// The vertex colour is each face's shade, so the structure reads as solid.
// The night shadow (RW_RingShadow.glsl) darkens the faces of materials with _NightShadow 1 (RingStructure.NightShadowID):
// the rim wall's inner face and top, so they darken with the ground below them. The outer face, the wall's outer side
// and the shadow squares stay lit.

#include "RW_RingShadow.glsl"

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUv;       // texture, in repeats
layout(location = 2) in vec3 vWorldPos;
layout(location = 3) in vec2 vUv2;      // wear noise, in repeats

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;        // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    float _NightShadow; // @default = 0.0
    float _MacroBlend;  // @default = 0.3
    vec4 _RingShadow;   // global, no default: see RW_RingShadow.glsl
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _MacroTex;
layout(set = 2, binding = 3) uniform sampler2D _NoiseTex;

// Macro repeats per wear repeat (RingStructure.MacroScale): a whole number, so the macro texture wraps round the loop
// without a seam too.
const float MacroScale = 10.0;

const vec3 RustColor = vec3(0.55, 0.30, 0.16);
const vec3 DustColor = vec3(0.72, 0.68, 0.60);

void main()
{
    vec3 detail = texture(_MainTex, vUv).rgb;
    vec3 macro = texture(_MacroTex, vUv2 * MacroScale).rgb;
    vec3 color = mix(detail, macro, uMaterial._MacroBlend);

    // Whole-number scales and offsets keep the noise seamless round the loop. Streaks are narrow round the loop and long
    // across it.
    float wear = texture(_NoiseTex, vUv2).r;
    float rust = texture(_NoiseTex, vUv2 * vec2(40.0, 3.0) + vec2(0.37, 0.11)).r;
    float dust = texture(_NoiseTex, vUv2 * vec2(24.0, 2.0) + vec2(0.71, 0.53)).r;
    color *= mix(0.82, 1.12, wear);
    float rustMask = smoothstep(0.65, 0.92, rust) * smoothstep(0.4, 0.8, wear);
    color = mix(color, color * RustColor * 1.8, 0.3 * rustMask);
    color = mix(color, DustColor * dot(color, vec3(0.333)) * 1.6, 0.3 * smoothstep(0.6, 0.9, dust));

    if (uMaterial._NightShadow > 0.5)
        color *= ringNightShadow(vWorldPos, uMaterial._RingShadow);

    outColor = vec4(color * vColor.rgb * uMaterial._Color.rgb, 1.0);
}
