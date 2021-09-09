Shader "Quake/Brush Texture Array"
{
  Properties
  {
      [HideInInspector] _MainTex("Fake Texture", 2D) = "white" {}
      _PaletteTex("Palette Texture", 2D) = "white" {}
      _StyleTex("Light Style Texture", 2D) = "white" {}
      _LMTex("Lightmap Texture", 2D) = "white" {}
      _MainTexArray ("Texture", 2DArray) = "white" {}
      _MipBias("Mip Range", Int) = 20
      _LightFlags("Light Flags", Int) = 0
      [ToggleUI] _FullBright("Fullbright", Int) = 0
  }
  SubShader
  {
    Tags {"Rendertype"="Opaque" "Queue"="Geometry"}
    
    Pass
    {
       Tags {
          "LightMode" = "Forwardbase"
      }
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      // make fog work
      //#pragma multi_compile_fog
      #pragma multi_compile _ LIGHTMAP_ON
      
      #include "AutoLight.cginc"
      #include "Lighting.cginc"
      #include "UnityCG.cginc"
      #include "Q1Lightmap.cginc"

      struct appdata
      {
          float4 vertex : POSITION;
          float2 uv : TEXCOORD0;
          float2 uv_lightmap : TEXCOORD1;
          float4 lightmap_dim : TEXCOORD2;
          float4 color : COLOR;
          float3 normal : NORMAL;
      };

      struct v2f
      {
          float3 uv : TEXCOORD0;
          float4 uv_lightmap : TEXCOORD1;
          //float2 lightmap_dim : TEXCOORD2;
          int2 lmOffset_mipLevel : TEXCOORD2;
          float4 lightStyles : TEXCOORD3;
          float3 normal : NORMAL;
          float4 vertex : SV_POSITION;
      };

      UNITY_DECLARE_TEX2DARRAY(_MainTexArray);
      UNITY_DECLARE_TEX2D(_PaletteTex);
      UNITY_DECLARE_TEX2D(_LMTex);
      UNITY_DECLARE_TEX2D(_StyleTex);
      //Texture2D _LMTex;
      float4 _LMTex_TexelSize;
      float4 _StyleTex_TexelSize;
      float4 _PaletteTex_TexelSize;
      int _LightFlags;
      int _FullBright;
      float _MipBias;

      v2f vert (appdata v)
      {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        float index = floor(v.color.r * 255.0 + 0.001);
        o.uv.xy = v.uv;
        o.uv.z = asint(v.lightmap_dim.w);
        o.uv_lightmap = float4(v.uv_lightmap, v.lightmap_dim.xy);
        o.normal = UnityObjectToWorldNormal(v.normal);
        o.lmOffset_mipLevel.x = asint(v.lightmap_dim.z);

        //
        float4 camPos = mul(UNITY_MATRIX_MV, v.vertex);
        o.lmOffset_mipLevel.y = clamp(0, 3, floor(abs(camPos.z) / _MipBias));
        o.lightStyles = v.color;
        return o;
      }
  
      float4 frag(v2f i) : SV_Target
      {
        int paletteIndex = floor(255*UNITY_SAMPLE_TEX2DARRAY_LOD(_MainTexArray, i.uv, i.lmOffset_mipLevel.y).r + 0.01);
        int lightmap_offset = i.lmOffset_mipLevel.x;
        float lightmap = decodeQ1Lightmap(_LMTex, _StyleTex, i.uv_lightmap.xy, i.uv_lightmap.zw, _LMTex_TexelSize.zw, _StyleTex_TexelSize.zw, i.lmOffset_mipLevel.x, i.lightStyles.rgba, _LightFlags, _FullBright);

        //Colormap contains 0 to +2 brightness colors for each color in the color palette, with pixels at 63 being 2 brightness
        int value = floor(31.5 * (lightmap));
        value = clamp(0, 63, value);

        float4 col = _PaletteTex.Load(int3(paletteIndex, value, 0));
        return col;        
      }
      ENDCG
    }

    Pass
    {
      Tags { "LightMode" = "ShadowCaster" }
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma multi_compile_shadowcaster
      #pragma multi_compile UNITY_PASS_SHADOWCASTER
      
      #include "UnityCG.cginc"
      #include "Lighting.cginc"
      #include "AutoLight.cginc"
      #include "UnityPBSLighting.cginc"
      
      struct v2f
      {
          V2F_SHADOW_CASTER;
      };
      
      v2f vert(appdata_base v)
      {
          v2f o;
          TRANSFER_SHADOW_CASTER_NOPOS(o, o.pos);
          return o;
      }
      
      float4 frag(v2f i) : SV_Target
      {
          SHADOW_CASTER_FRAGMENT(i)
      }
      ENDCG
    }
  }
}
