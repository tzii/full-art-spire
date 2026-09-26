namespace FullArtCardPacks;

internal readonly record struct LayerDecision(
    float Depth, float BackgroundShift, float ForegroundShift,
    float ForegroundScale, float SubjectScale, float SubjectOffsetX, float SubjectOffsetY,
    float ShadowStrength, float FrameInset);

internal static class LayerPolicy
{
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
    {
        long pixels = (long)width * height;
        if (channel is not "red" and not "alpha" || width <= 0 || height <= 0 || pixels > int.MaxValue / 4
            || mask.Length != pixels * 4 || foreground.Length != mask.Length)
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
            if (coverage >= 239 && foreground[index + 3] >= 16) visible++;
        }
        long minimumRegion = Math.Max(1, (pixels + 999) / 1000);
        return hidden >= minimumRegion && visible >= minimumRegion;
    }

    private static bool SamePath(string first, string second)
        => string.Equals(first.Replace('\\', '/'), second.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool InRange(float value, float minimum, float maximum)
        => float.IsFinite(value) && value >= minimum && value <= maximum;
}
