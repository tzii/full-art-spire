namespace FullArtCardPacks;

internal readonly record struct LayerDecision(
    float Depth, float BackgroundShift, float ForegroundShift,
    float ForegroundScale, float ShadowStrength, float FrameInset);

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
            || string.IsNullOrWhiteSpace(layers.Background)
            || string.IsNullOrWhiteSpace(layers.Foreground)
            || string.Equals(layers.Background, layers.Foreground, StringComparison.OrdinalIgnoreCase)
            || !InRange(layers.Depth, 0f, 1f)
            || !InRange(layers.BackgroundShift, 0f, 0.025f)
            || !InRange(layers.ForegroundShift, 0f, 0.04f)
            || !InRange(layers.ForegroundScale, 1f, 1.10f)
            || !InRange(layers.ShadowStrength, 0f, 0.5f)
            || !InRange(layers.FrameInset, 0.025f, 0.10f)
            || !float.IsFinite(globalDepth))
            return false;

        decision = new(layers.Depth * Math.Clamp(globalDepth, 0f, 1f),
            layers.BackgroundShift, layers.ForegroundShift,
            layers.ForegroundScale, layers.ShadowStrength, layers.FrameInset);
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

    private static bool InRange(float value, float minimum, float maximum)
        => float.IsFinite(value) && value >= minimum && value <= maximum;
}
