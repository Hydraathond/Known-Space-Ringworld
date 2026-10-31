// The night shadow, shared by the ring's copies of vanilla's lit world shaders (ADR 0002). Each night zone gets one,
// centred on its local midnight, darkened with vanilla's night-side tint. RingNightShadow sets the global every frame
// and mirrors this in C# for the self-check.
//
// Declare it in each shader's MaterialUniforms as "vec4 _RingShadow;" with no @default comment: the engine fills a
// material uniform from the global of the same name only when it has no authored default.
//   x = angle round the ring of one night shadow's centre, in radians; the others follow every 2 pi / z
//   y = half-width of each night shadow, in radians
//   z = number of night zones; 0 = no night shadow (the global's unset value too)
//   w = width of the soft edge centred on each boundary, in radians

const vec3 RingNightColor = vec3(0.482, 0.603, 0.682);

// How dark the night shadow is at world position p: 1 inside a night shadow, 0 in daylight (RingNightShadow.Darkness).
float ringNightDarkness(vec3 p, vec4 shadow)
{
    if (shadow.z < 0.5)
        return 0.0;
    float zone = 6.28318531 / shadow.z;
    float fromCentre = abs(fract((atan(p.x, -p.z) - shadow.x) / zone + 0.5) - 0.5) * zone;
    return 1.0 - clamp((fromCentre - shadow.y) / max(shadow.w, 1e-6) + 0.5, 0.0, 1.0);
}

// The tint to multiply a colour by at world position p: RingNightColor inside a night shadow, white in daylight.
vec3 ringNightShadow(vec3 p, vec4 shadow)
{
    return mix(vec3(1.0), RingNightColor, ringNightDarkness(p, shadow));
}
