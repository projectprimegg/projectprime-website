#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ReFuel.Stb;

namespace MphRead;

/// <summary>
/// Applies imported baked lighting to the scene color immediately after the
/// world pass and before outlines, HUD models, directional shadows and authored
/// real-time lights. The pass uses depth-equal, fixed-point-matched geometry,
/// so it cannot darken players or unrelated map surfaces.
/// </summary>
public partial class Scene
{
    private sealed record SourceLightmapGpuGroup(int Page, int Texture, int DisplayList, int Vertices);

    private MapDefinition? _sourceBakedDefinition;
    private SourceLightmapGpuGroup[] _sourceLightmapGroups = Array.Empty<SourceLightmapGpuGroup>();
    private int _sourceLightmapProgram;
    private int _sourceLightmapSampler;
    private int _sourceLightmapProjection;
    private int _sourceLightmapView;
    private bool _sourceLightmapReady;
    private bool _sourceLightmapRefused;

    internal void BindSourceBakedLighting(string roomName)
    {
        DisposeSourceBakedLighting();
        try
        {
            _sourceBakedDefinition = CustomRooms.Definitions.FirstOrDefault(map =>
                map.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException)
        {
            _sourceBakedDefinition = null;
            DebugLog.Line("lighting", "Could not bind source baked lighting for "
                + roomName + ": " + ex.Message);
        }
        _sourceLightmapRefused = false;
    }

    private void ApplySourceBakedLighting()
    {
#if ANDROID
        return; // OpenGL ES fallback remains vertex lighting until a retained UV1 path is available.
#else
        UniversalNativeMapData? native = _sourceBakedDefinition?.NativeFidelity;
        if (_sourceLightmapRefused || native == null
            || native.LightingMode is UniversalLightingMode.RealtimeOnly
                or UniversalLightingMode.VertexFallback
            || String.IsNullOrWhiteSpace(native.LightmapOverlayAsset)
            || native.Lightmaps.Count == 0)
            return;

        try
        {
            EnsureSourceBakedLighting(native);
            if (!_sourceLightmapReady || _sourceLightmapGroups.Length == 0) return;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
            GL.Viewport(0, 0, _targetSize.X, _targetSize.Y);
            GL.PushAttrib(AttribMask.ColorBufferBit | AttribMask.DepthBufferBit
                | AttribMask.EnableBit | AttribMask.PolygonBit | AttribMask.TextureBit);
            try
            {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Equal);
                GL.DepthMask(false);
                GL.Disable(EnableCap.CullFace);
                GL.Enable(EnableCap.Blend);
                bool debug = native.LightingMode == UniversalLightingMode.LightmapDebug;
                GL.BlendFunc(debug ? BlendingFactor.One : BlendingFactor.DstColor,
                    BlendingFactor.Zero);
                GL.UseProgram(_sourceLightmapProgram);
                Matrix4 projection = _perspectiveMatrix;
                Matrix4 view = _viewMatrix;
                GL.UniformMatrix4(_sourceLightmapProjection, false, ref projection);
                GL.UniformMatrix4(_sourceLightmapView, false, ref view);
                GL.Uniform1(_sourceLightmapSampler, 0);
                GL.ActiveTexture(TextureUnit.Texture0);

                foreach (SourceLightmapGpuGroup group in _sourceLightmapGroups)
                {
                    GL.BindTexture(TextureTarget.Texture2D, group.Texture);
                    GL.CallList(group.DisplayList);
                }
                RenderPostProcessCount++;
                CheckGlError("SourceBakedLighting");
            }
            finally
            {
                GL.UseProgram(0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.PopAttrib();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _sourceLightmapRefused = true;
            DisposeSourceBakedLightingGpu();
            DebugLog.Line("lighting", "Source baked lighting disabled: " + ex.Message);
        }
#endif
    }

    private void EnsureSourceBakedLighting(UniversalNativeMapData native)
    {
        if (_sourceLightmapReady) return;
        MapDefinition definition = _sourceBakedDefinition
            ?? throw new InvalidOperationException("No source map is bound.");
        string overlayPath = native.LightmapOverlayAsset
            ?? throw new InvalidDataException("Map has no baked-lighting overlay asset.");
        Q3LightmapOverlayAsset.Overlay overlay = Q3LightmapOverlayAsset.Decode(
            MapAssets.Read(definition, overlayPath));
        var pages = native.Lightmaps.ToDictionary(page => page.Id);
        var groups = new List<SourceLightmapGpuGroup>(overlay.Groups.Count);
        EnsureSourceLightmapProgram();

        try
        {
            foreach (Q3LightmapOverlayAsset.Group group in overlay.Groups)
            {
                if (!pages.TryGetValue(group.Page, out UniversalLightmapPage? page))
                    throw new InvalidDataException(
                        $"Overlay references missing lightmap page {group.Page}.");
                int texture = UploadSourceLightmap(definition, page);
                int list = GL.GenLists(1);
                if (list == 0)
                {
                    GL.DeleteTexture(texture);
                    throw new ProgramException("Could not allocate a lightmap display list.");
                }
                GL.NewList(list, ListMode.Compile);
                GL.Begin(PrimitiveType.Triangles);
                foreach (Q3LightmapOverlayAsset.Vertex vertex in group.Vertices)
                {
                    GL.TexCoord2(vertex.LightmapUv.X, vertex.LightmapUv.Y);
                    GL.Vertex3(vertex.Position);
                }
                GL.End();
                GL.EndList();
                groups.Add(new SourceLightmapGpuGroup(
                    group.Page, texture, list, group.Vertices.Count));
            }
            _sourceLightmapGroups = groups.ToArray();
            _sourceLightmapReady = true;
        }
        catch
        {
            foreach (SourceLightmapGpuGroup group in groups)
            {
                if (group.DisplayList != 0) GL.DeleteLists(group.DisplayList, 1);
                if (group.Texture != 0) GL.DeleteTexture(group.Texture);
            }
            throw;
        }
    }

    private static int UploadSourceLightmap(MapDefinition definition, UniversalLightmapPage page)
    {
        byte[] encoded = MapAssets.Read(definition, page.Asset);
        using var stream = new MemoryStream(encoded, writable: false);
        using StbImage image = StbImage.Load(stream, StbiImageFormat.Rgb);
        if (image.Width != page.Width || image.Height != page.Height)
            throw new InvalidDataException(
                $"Lightmap page {page.Id} dimensions do not match its metadata.");
        byte[] pixels = image.AsSpan<byte>().ToArray();
        int texture = GL.GenTexture();
        if (texture == 0) throw new ProgramException("Could not allocate a lightmap texture.");
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8,
            image.Width, image.Height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)(page.LinearFiltering ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)(page.LinearFiltering ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)(page.Clamp ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)(page.Clamp ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat));
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }

    private void EnsureSourceLightmapProgram()
    {
        if (_sourceLightmapProgram != 0) return;
        int vertex = CompileSourceLightmapShader(ShaderType.VertexShader, @"#version 120
uniform mat4 projection;
uniform mat4 view;
varying vec2 lightmap_uv;
void main() {
    gl_Position = projection * view * gl_Vertex;
    lightmap_uv = gl_MultiTexCoord0.xy;
}
");
        int fragment = 0;
        try
        {
            fragment = CompileSourceLightmapShader(ShaderType.FragmentShader, @"#version 120
uniform sampler2D lightmap_tex;
varying vec2 lightmap_uv;
void main() {
    gl_FragColor = vec4(texture2D(lightmap_tex, lightmap_uv).rgb, 1.0);
}
");
            _sourceLightmapProgram = GL.CreateProgram();
            GL.AttachShader(_sourceLightmapProgram, vertex);
            GL.AttachShader(_sourceLightmapProgram, fragment);
            GL.LinkProgram(_sourceLightmapProgram);
            GL.GetProgram(_sourceLightmapProgram, GetProgramParameterName.LinkStatus,
                out int linked);
            if (linked == 0)
                throw new ProgramException(GL.GetProgramInfoLog(_sourceLightmapProgram));
            _sourceLightmapSampler = GL.GetUniformLocation(
                _sourceLightmapProgram, "lightmap_tex");
            _sourceLightmapProjection = GL.GetUniformLocation(
                _sourceLightmapProgram, "projection");
            _sourceLightmapView = GL.GetUniformLocation(
                _sourceLightmapProgram, "view");
        }
        catch
        {
            if (_sourceLightmapProgram != 0)
            {
                GL.DeleteProgram(_sourceLightmapProgram);
                _sourceLightmapProgram = 0;
            }
            throw;
        }
        finally
        {
            GL.DeleteShader(vertex);
            if (fragment != 0) GL.DeleteShader(fragment);
        }
    }

    private static int CompileSourceLightmapShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status != 0) return shader;
        string log = GL.GetShaderInfoLog(shader);
        GL.DeleteShader(shader);
        throw new ProgramException(log);
    }

    private void DisposeSourceBakedLightingGpu()
    {
#if !ANDROID
        foreach (SourceLightmapGpuGroup group in _sourceLightmapGroups)
        {
            if (group.DisplayList != 0) GL.DeleteLists(group.DisplayList, 1);
            if (group.Texture != 0) GL.DeleteTexture(group.Texture);
        }
        _sourceLightmapGroups = Array.Empty<SourceLightmapGpuGroup>();
        if (_sourceLightmapProgram != 0)
        {
            GL.DeleteProgram(_sourceLightmapProgram);
            _sourceLightmapProgram = 0;
        }
#endif
        _sourceLightmapReady = false;
    }

    private void DisposeSourceBakedLighting()
    {
        DisposeSourceBakedLightingGpu();
        _sourceBakedDefinition = null;
        _sourceLightmapRefused = false;
    }
}
#endif
