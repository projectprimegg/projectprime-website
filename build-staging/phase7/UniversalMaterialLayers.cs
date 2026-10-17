using System.Collections.Generic;

namespace MphRead.Mods.MapGen;

public sealed class UniversalMaterialStack
{
    public const int MaxLayers = 8;

    public int SourceMaterial { get; set; }
    public string Name { get; set; } = "material";
    public UniversalCullMode Cull { get; set; } = UniversalCullMode.Back;
    public List<UniversalMaterialLayer> Layers { get; set; } = new();
}

public sealed class UniversalMaterialLayer
{
    public string Texture { get; set; } = "";
    public int UvChannel { get; set; }
    public UniversalBlendMode Blend { get; set; } = UniversalBlendMode.Replace;
    public bool IsLightmap { get; set; }
    public bool DepthWrite { get; set; } = true;
    public UniversalDepthFunction DepthFunction { get; set; } = UniversalDepthFunction.LessOrEqual;
    public UniversalAlphaMode AlphaMode { get; set; }
    public float AlphaCutoff { get; set; } = 0.5f;
    public float[]? UvScroll { get; set; }
    public float UvRotationDegreesPerSecond { get; set; }
    public float[]? UvScale { get; set; }
    public UniversalWave? ColorWave { get; set; }
    public UniversalWave? AlphaWave { get; set; }
    public List<string> AnimationFrames { get; set; } = new();
    public float AnimationFramesPerSecond { get; set; }
}

public enum UniversalBlendMode
{
    Replace,
    Multiply,
    Add,
    Alpha,
    Filter,
    SourceAlphaOneMinusSourceAlpha,
    Custom
}

public enum UniversalCullMode
{
    Back,
    Front,
    None
}

public enum UniversalDepthFunction
{
    Less,
    LessOrEqual,
    Equal,
    Always
}

public enum UniversalAlphaMode
{
    Opaque,
    Mask,
    Blend,
    Additive
}

public sealed class UniversalWave
{
    public string Function { get; set; } = "sin";
    public float Base { get; set; }
    public float Amplitude { get; set; }
    public float Phase { get; set; }
    public float Frequency { get; set; }
}
