using System.Text.Json;

namespace FullArtSpire;

internal readonly record struct LayerCanvas(int Width, int Height);
internal readonly record struct LayerRegistration(LayerCanvas Source, LayerCanvas Mask, LayerCanvas Background);

internal readonly record struct LayerDecision(
    float Depth, float BackgroundShift, float ForegroundShift,
    float ForegroundScale, float SubjectScale, float SubjectOffsetX, float SubjectOffsetY,
    float ShadowStrength, float FrameInset);

internal static class LayerPolicy
{
    internal const double MaximumRegistrationAspectError = 0.001;
    // The clean plate must cover the entire plane. Unsupported or malformed
    // metadata falls back to the established flat card, never guessed layers.
    internal static bool TryDecide(FullArtCardEntry entry, bool enabled,
        bool visible, float globalDepth, out LayerDecision decision)
    {
        decision = default;
        if (!enabled || !visible || entry.PreserveOriginalLayout
            || entry.Layers is not { } layers
            || !IsRelativeImagePath(layers.Background)
            || !IsRelativeImagePath(layers.Foreground)
            || SamePath(layers.Background, layers.Foreground)
            || layers.ForegroundMask is null
            || layers.ForegroundMaskChannel is not "red" and not "alpha"
            || (layers.ForegroundMask.Length != 0 && (!IsRelativeImagePath(layers.ForegroundMask)
                || SamePath(layers.ForegroundMask, layers.Background) || SamePath(layers.ForegroundMask, layers.Foreground)))
            || !TryGetRegistration(layers, out var registration)
            || (registration is not null && (!IsRelativeImagePath(entry.Art) || !SamePath(entry.Art, layers.Foreground)))
            || !InRange(layers.Depth, 0f, 1f)
            || !InRange(layers.BackgroundShift, 0f, 0.025f)
            || !InRange(layers.ForegroundShift, 0f, 0.04f)
            || !InRange(layers.ForegroundScale, 1f, 1.10f)
            || !InRange(layers.SubjectScale, 0.25f, 1.5f)
            || !InRange(layers.SubjectOffsetX, -0.5f, 0.5f)
            || !InRange(layers.SubjectOffsetY, -0.5f, 0.5f)
            || !InRange(layers.ShadowStrength, 0f, 0.5f)
            || !InRange(layers.FrameInset, 0.025f, 0.10f)
            || !float.IsFinite(globalDepth))
            return false;

        decision = new(layers.Depth * Math.Clamp(globalDepth, 0f, 1f),
            layers.BackgroundShift, layers.ForegroundShift,
            layers.ForegroundScale, layers.SubjectScale, layers.SubjectOffsetX, layers.SubjectOffsetY,
            layers.ShadowStrength, layers.FrameInset);
        return true;
    }

    internal const float MaxPortraitShift = 0.035f;

    // Reviewed packs pin subjects in place; the player may let them follow the
    // pointer. Lift grows with the shift so a subject cut by the canvas edge
    // never reveals a gap. Authored motion at or above the slider is kept as is.
    internal static LayerDecision WithPortraitParallax(LayerDecision decision, float strength)
    {
        if (!float.IsFinite(strength)) return decision;
        float shift = Math.Clamp(strength, 0f, 1f) * MaxPortraitShift;
        if (shift <= decision.ForegroundShift) return decision;
        float scale = Math.Clamp(MathF.Max(decision.ForegroundScale, 1f + 2f * shift), 1f, 1.10f);
        return decision with { ForegroundShift = shift, ForegroundScale = scale };
    }

    internal static bool TryGetRegistration(FullArtLayerMetadata layers, out LayerRegistration? registration)
    {
        registration = null;
        JsonElement value = layers.Registration;
        if (value.ValueKind == JsonValueKind.Undefined) return true;
        if (value.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(layers.ForegroundMask)) return false;
        // Reject duplicate and unknown fields instead of interpreting an
        // ambiguous declaration differently from the browser/package builder.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Add(property.Name) || property.Name is not
                ("mode" or "source_dimensions" or "mask_dimensions" or "background_dimensions")) return false;
        if (names.Count != 4 || !value.TryGetProperty("mode", out JsonElement mode)
            || mode.ValueKind != JsonValueKind.String || mode.GetString() != "normalized_source_uv"
            || !TryCanvas(value, "source_dimensions", out var source)
            || !TryCanvas(value, "mask_dimensions", out var mask)
            || !TryCanvas(value, "background_dimensions", out var background)
            || !HasCompatibleAspect(source, mask) || !HasCompatibleAspect(source, background)) return false;
        registration = new(source, mask, background);
        return true;
    }

    internal static bool HasCompatibleAspect(LayerCanvas source, LayerCanvas other)
        => IsValidCanvas(source.Width, source.Height, true) && IsValidCanvas(other.Width, other.Height, true)
            && Math.Abs((double)other.Width * source.Height / ((double)other.Height * source.Width) - 1.0)
                <= MaximumRegistrationAspectError;

    private static bool TryCanvas(JsonElement value, string name, out LayerCanvas canvas)
    {
        canvas = default;
        if (!value.TryGetProperty(name, out var dimensions) || dimensions.ValueKind != JsonValueKind.Array
            || dimensions.GetArrayLength() != 2 || dimensions[0].ValueKind != JsonValueKind.Number
            || dimensions[1].ValueKind != JsonValueKind.Number || !dimensions[0].TryGetInt32(out int width)
            || !dimensions[1].TryGetInt32(out int height) || !IsValidCanvas(width, height, true)) return false;
        canvas = new(width, height);
        return true;
    }

    internal static (float X, float Y) Pointer(float x, float y, bool reducedMotion)
    {
        if (reducedMotion || !float.IsFinite(x) || !float.IsFinite(y))
            return (0f, 0f);
        // Cards react only while the pointer is over their local canvas.
        if (MathF.Abs(x) > 1f || MathF.Abs(y) > 1f)
            return (0f, 0f);
        return (x, y);
    }

    internal static bool CanComposite(float opacity)
        => float.IsFinite(opacity) && opacity >= 0.9999f;

    internal static bool IsValidCanvas(int width, int height, bool hasMask)
    {
        // Exact 2:3 integer canvases previously topped out at 2730x4095.
        // A separate mask may retain another original portrait aspect ratio,
        // without increasing either the width or total texture-pixel budget.
        if (width < 64 || width > 2730 || height <= width || height > 4096
            || (long)width * height > 2730L * 4095)
            return false;
        return hasMask || (long)width * 3 == (long)height * 2;
    }

    internal static bool IsRelativeImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            || path.Contains(':') || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;
        return path.Replace('\\', '/').Split('/').All(part => part.Length > 0 && part is not "." and not ".."
            && part == part.TrimEnd(' ', '.')
            && part.All(character => !char.IsControl(character) && "<>:\"|?*".IndexOf(character) < 0));
    }

    // Generated mattes may contain small RGB encoding differences. Red is the
    // canonical data channel for red masks, without sRGB conversion. Alpha
    // masks ignore RGB. Both need meaningful visible and hidden regions.
    internal static bool IsValidForegroundMask(ReadOnlySpan<byte> mask, ReadOnlySpan<byte> foreground,
        int width, int height, string channel = "red")
        => IsValidForegroundMask(mask, foreground, width, height, width, height, channel);

    internal static bool IsValidForegroundMask(ReadOnlySpan<byte> mask, ReadOnlySpan<byte> foreground,
        int width, int height, int sourceWidth, int sourceHeight, string channel = "red")
    {
        long pixels = (long)width * height;
        long sourcePixels = (long)sourceWidth * sourceHeight;
        if (channel is not "red" and not "alpha" || width <= 0 || height <= 0 || pixels > int.MaxValue / 4
            || sourceWidth <= 0 || sourceHeight <= 0 || sourcePixels > int.MaxValue / 4
            || mask.Length != pixels * 4 || foreground.Length != sourcePixels * 4)
            return false;
        int hidden = 0, visible = 0;
        for (int index = 0; index < mask.Length; index += 4)
        {
            if (channel == "red")
            {
                int low = Math.Min(mask[index], Math.Min(mask[index + 1], mask[index + 2]));
                int high = Math.Max(mask[index], Math.Max(mask[index + 1], mask[index + 2]));
                if (mask[index + 3] != 255 || high - low > 16) return false;
            }
            int coverage = mask[index + (channel == "alpha" ? 3 : 0)];
            if (coverage <= 16) hidden++;
            if (coverage >= 239 && SourceAlpha(foreground, sourceWidth, sourceHeight,
                index / 4 % width, index / 4 / width, width, height) >= 16) visible++;
        }
        long minimumRegion = Math.Max(1, (pixels + 999) / 1000);
        return hidden >= minimumRegion && visible >= minimumRegion;
    }

    private static double SourceAlpha(ReadOnlySpan<byte> source, int width, int height,
        int maskX, int maskY, int maskWidth, int maskHeight)
    {
        // Match normalized GPU sampling at the mask texel center, including
        // partial source alpha. No source or mask bitmap is resized.
        double x = Math.Clamp((maskX + 0.5) * width / maskWidth - 0.5, 0, width - 1);
        double y = Math.Clamp((maskY + 0.5) * height / maskHeight - 0.5, 0, height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        double dx = x - x0, dy = y - y0;
        double top = source[(y0 * width + x0) * 4 + 3] * (1 - dx) + source[(y0 * width + x1) * 4 + 3] * dx;
        double bottom = source[(y1 * width + x0) * 4 + 3] * (1 - dx) + source[(y1 * width + x1) * 4 + 3] * dx;
        return top * (1 - dy) + bottom * dy;
    }

    private static bool SamePath(string first, string second)
        => string.Equals(first.Replace('\\', '/'), second.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool InRange(float value, float minimum, float maximum)
        => float.IsFinite(value) && value >= minimum && value <= maximum;
}
