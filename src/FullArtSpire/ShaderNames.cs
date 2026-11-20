using Godot;

namespace FullArtSpire;

// Shader parameter names, created once. Passing a string instead allocates a new
// StringName on every call, and the layer and finish overlays set several of
// these for every visible card on every frame.
internal static class ShaderNames
{
    internal static readonly StringName Intensity = "intensity";
    internal static readonly StringName ViewTilt = "view_tilt";
    internal static readonly StringName ViewPointer = "view_pointer";
    internal static readonly StringName Pointer = "pointer";
    internal static readonly StringName Depth = "depth";
    internal static readonly StringName UseLayeredUv = "use_layered_uv";
    internal static readonly StringName UseNormalizedSourceUv = "use_normalized_source_uv";
    internal static readonly StringName LayerCoverageTexture = "layer_coverage_texture";
    internal static readonly StringName LayerCoverageUseAlpha = "layer_coverage_use_alpha";
    internal static readonly StringName BackgroundTexture = "background_texture";
    internal static readonly StringName ForegroundTexture = "foreground_texture";
    internal static readonly StringName ForegroundMaskTexture = "foreground_mask_texture";
    internal static readonly StringName UseForegroundMask = "use_foreground_mask";
    internal static readonly StringName ForegroundMaskUseAlpha = "foreground_mask_use_alpha";
    internal static readonly StringName BackgroundShift = "background_shift";
    internal static readonly StringName ForegroundShift = "foreground_shift";
    internal static readonly StringName ForegroundScale = "foreground_scale";
    internal static readonly StringName SubjectScale = "subject_scale";
    internal static readonly StringName SubjectOffsetX = "subject_offset_x";
    internal static readonly StringName SubjectOffsetY = "subject_offset_y";
    internal static readonly StringName ShadowStrength = "shadow_strength";
    internal static readonly StringName PopStrength = "pop_strength";
    internal static readonly StringName FrameInset = "frame_inset";

    // The finish overlay samples the same eased subject UV as the depth shader;
    // these are copied from one to the other every frame.
    internal static readonly StringName[] SharedLayerUv =
    {
        ViewPointer, Depth, ForegroundShift, ForegroundScale,
        SubjectScale, SubjectOffsetX, SubjectOffsetY, BackgroundShift,
    };
}
