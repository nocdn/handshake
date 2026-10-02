using System.Numerics;
using System.Runtime.InteropServices;
using HsWin.App.Native;

namespace HsWin.App.Magnifier;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct MagnifierShaderParameters(Vector4 SourceArea, Vector4 Lens)
{
    internal static MagnifierShaderParameters Create(MagnifierGeometry geometry, NativeRect monitor, int border)
    {
        var width = (float)(monitor.Right - monitor.Left);
        var height = (float)(monitor.Bottom - monitor.Top);
        return new(new Vector4((geometry.Source.Left - monitor.Left) / width,
            (geometry.Source.Top - monitor.Top) / height,
            (geometry.Source.Right - geometry.Source.Left) / width,
            (geometry.Source.Bottom - geometry.Source.Top) / height), new Vector4(geometry.Diameter, border, 0, 0));
    }
}

internal static class MagnifierShader
{
    internal const string Source = """
        cbuffer Settings : register(b0) { float4 sourceArea; float4 lens; }
        Texture2D desktopImage : register(t0);
        SamplerState imageSampler : register(s0);
        struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        Vertex VS(uint id : SV_VertexID) {
            Vertex o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.position = float4(o.uv.x * 2 - 1, 1 - o.uv.y * 2, 0, 1);
            return o;
        }
        float4 PS(Vertex input) : SV_TARGET {
            float2 pixel = input.uv * lens.x;
            float radius = lens.x * 0.5;
            float distance = length(pixel - radius);
            float alpha = saturate(radius - distance);
            if (distance >= radius - lens.y) return float4(alpha, alpha, alpha, alpha);
            float2 uv = (pixel - lens.y) / (lens.x - 2 * lens.y);
            float3 color = desktopImage.Sample(imageSampler, sourceArea.xy + uv * sourceArea.zw).rgb;
            return float4(color * alpha, alpha);
        }
        """;
}
