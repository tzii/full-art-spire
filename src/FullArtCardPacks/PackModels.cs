using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullArtCardPacks;

internal sealed record FullArtConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("enabled_packs")]
    public List<string>? EnabledPacks { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }
}

internal sealed record FullArtPackManifest
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = "";

    [JsonPropertyName("pool")]
    public string Pool { get; init; } = "";

    [JsonPropertyName("cards")]
    public List<FullArtCardEntry>? Cards { get; init; } = new();
}

internal sealed record FullArtCardEntry
{
    [JsonPropertyName("canonical_asset_name")]
    public string CanonicalAssetName { get; init; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("pool")]
    public string Pool { get; init; } = "";

    [JsonPropertyName("runtime_model")]
    public string RuntimeModel { get; init; } = "";

    [JsonPropertyName("art")]
    public string Art { get; init; } = "";

    [JsonPropertyName("preserve_original_layout")]
    public bool PreserveOriginalLayout { get; init; }

    [JsonPropertyName("effects")]
    public FullArtEffectMetadata? Effects { get; init; } = new();

    [JsonPropertyName("layers")]
    public FullArtLayerMetadata? Layers { get; init; }
}

internal sealed record FullArtLayerMetadata
{
    [JsonPropertyName("finish_mask")]
    public string FinishMask { get; init; } = "";
    [JsonPropertyName("background")]
    public string Background { get; init; } = "";
    [JsonPropertyName("foreground")]
    public string Foreground { get; init; } = "";
    [JsonPropertyName("foreground_mask")]
    public string ForegroundMask { get; init; } = "";
    [JsonPropertyName("foreground_mask_channel")]
    public string ForegroundMaskChannel { get; init; } = "red";
    // Preserve malformed values for per-card fail-flat validation. A bad
    // registration must not prevent unrelated entries from deserializing.
    [JsonPropertyName("registration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Registration { get; init; }
    [JsonPropertyName("depth")]
    public float Depth { get; init; } = 1f;
    [JsonPropertyName("background_shift")]
    public float BackgroundShift { get; init; } = 0.010f;
    [JsonPropertyName("foreground_shift")]
    public float ForegroundShift { get; init; } = 0.022f;
    [JsonPropertyName("foreground_scale")]
    public float ForegroundScale { get; init; } = 1.035f;
    // Static placement corrects a generated cutout without resampling its PNG.
    // foreground_scale remains the separate depth-dependent lift.
    [JsonPropertyName("subject_scale")]
    public float SubjectScale { get; init; } = 1f;
    [JsonPropertyName("subject_offset_x")]
    public float SubjectOffsetX { get; init; } = 0f;
    [JsonPropertyName("subject_offset_y")]
    public float SubjectOffsetY { get; init; } = 0f;
    [JsonPropertyName("shadow_strength")]
    public float ShadowStrength { get; init; } = 0.28f;
    [JsonPropertyName("frame_inset")]
    public float FrameInset { get; init; } = 0.055f;
}

internal sealed record FullArtEffectMetadata
{
    [JsonPropertyName("surface")]
    public string Surface { get; init; } = "none";

    [JsonPropertyName("rarity_policy")]
    public string RarityPolicy { get; init; } = "rare_first";

    [JsonPropertyName("mask")]
    public string Mask { get; init; } = "";

    [JsonPropertyName("effect_mask")]
    public string EffectMask { get; init; } = "";

    [JsonPropertyName("protection_mask")]
    public string ProtectionMask { get; init; } = "";

    [JsonPropertyName("permission_mask")]
    public string PermissionMask { get; init; } = "";

    [JsonPropertyName("intensity")]
    public float Intensity { get; init; } = 1f;

    [JsonPropertyName("phase_seed")]
    public float? PhaseSeed { get; init; }
}
