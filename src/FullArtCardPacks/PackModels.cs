using System.Text.Json.Serialization;

namespace FullArtCardPacks;

internal sealed record FullArtConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("enabled_packs")]
    public List<string>? EnabledPacks { get; init; } = new();

    [JsonPropertyName("enable_effects")]
    public bool EnableEffects { get; init; }

    [JsonPropertyName("effect_intensity")]
    public float EffectIntensity { get; init; } = 0.3f;

    [JsonPropertyName("rare_first_effects")]
    public bool RareFirstEffects { get; init; } = true;
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

    [JsonPropertyName("effects")]
    public FullArtEffectMetadata? Effects { get; init; } = new();
}

internal sealed record FullArtEffectMetadata
{
    [JsonPropertyName("surface")]
    public string Surface { get; init; } = "none";

    [JsonPropertyName("rarity_policy")]
    public string RarityPolicy { get; init; } = "rare_first";

    [JsonPropertyName("mask")]
    public string Mask { get; init; } = "";

    [JsonPropertyName("intensity")]
    public float Intensity { get; init; } = 0.3f;
}
