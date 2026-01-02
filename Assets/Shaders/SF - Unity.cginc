
#ifndef SF_SHADER_UNITY
#define SF_SHADER_UNITY

#include "Unity/UnityCG.cginc"
#include "Unity/Lighting.cginc"
#include "Unity/AutoLight.cginc"

#ifndef UNITY_DECLARE_SHADOWMAP
    #define UNITY_DECLARE_SHADOWMAP(tex) sampler2D_float tex
#endif
UNITY_DECLARE_SHADOWMAP( _ShadowMapTexture );

#ifndef UNITY_DECLARE_DEPTH_TEXTURE
    #define UNITY_DECLARE_DEPTH_TEXTURE(tex) sampler2D_float tex
#endif
UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );

sampler3D _DitherMaskLOD;
float _ShadowIntensity;

#endif
