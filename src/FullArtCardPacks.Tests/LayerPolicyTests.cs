using FullArtCardPacks;

internal static class LayerPolicyTests
{
    internal static void Run()
    {
        var layers = new FullArtLayerMetadata
        {
            Background = "layers/silent/survivor/background.png",
            Foreground = "layers/silent/survivor/foreground.png",
        };
        var entry = new FullArtCardEntry { Layers = layers };
        Require(LayerPolicy.TryDecide(entry, true, true, 0.5f, out var decision), "valid pilot enabled");
        Require(decision.Depth == 0.5f, "global depth scales authored depth");
        Require(LayerPolicy.TryDecide(entry, true, true, 0f, out decision) && decision.Depth == 0f,
            "zero depth retains a static separated composition");
        Require(LayerPolicy.TryDecide(entry, true, true, 9f, out decision) && decision.Depth == 1f,
            "global slider values are bounded");
        Require(!LayerPolicy.TryDecide(entry, false, true, 1f, out _), "disable returns flat art");
        Require(!LayerPolicy.TryDecide(entry, true, false, 1f, out _), "hidden cards cannot reveal sharp subjects");
        Require(!LayerPolicy.TryDecide(entry with { PreserveOriginalLayout = true }, true, true, 1f, out _),
            "native layout remains native");
        Require(!LayerPolicy.TryDecide(entry with { Layers = null }, true, true, 1f, out _), "no guessed layers");
        Require(!LayerPolicy.TryDecide(entry, true, true, float.NaN, out _), "invalid global settings fail closed");
        FullArtLayerMetadata[] malformed =
        {
            layers with { Background = "" }, layers with { Foreground = layers.Background },
            layers with { Depth = float.NaN }, layers with { Depth = -1f },
            layers with { BackgroundShift = 0.5f }, layers with { ForegroundShift = float.PositiveInfinity },
            layers with { ForegroundScale = 0.8f }, layers with { ForegroundScale = 1.2f },
            layers with { ShadowStrength = -0.1f }, layers with { FrameInset = 0f },
        };
        foreach (var invalid in malformed)
            Require(!LayerPolicy.TryDecide(entry with { Layers = invalid }, true, true, 1f, out _),
                "malformed layer parameters fail closed");
        Require(LayerPolicy.Pointer(0.75f, -0.5f, false) == (0.75f, -0.5f), "pointer uses local coordinates");
        Require(LayerPolicy.Pointer(0.75f, -0.5f, true) == (0f, 0f), "reduced motion freezes travel");
        Require(LayerPolicy.Pointer(1.01f, 0f, false) == (0f, 0f), "off-card pointer resets to center");
        Require(LayerPolicy.Pointer(float.NaN, 0f, false) == (0f, 0f), "nonfinite pointer resets to center");
        Require(LayerPolicy.CanComposite(1f), "opaque cards composite once over their base");
        Require(!LayerPolicy.CanComposite(0.5f), "native fades use flat fallback without double opacity");
        Require(!LayerPolicy.CanComposite(0f), "fully faded cards have no layer draw");
        Require(!LayerPolicy.CanComposite(float.NaN), "invalid opacity fails closed");
        Console.WriteLine("Layered card policy tests passed");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
