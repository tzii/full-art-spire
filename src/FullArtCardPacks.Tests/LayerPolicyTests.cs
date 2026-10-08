using System.Text.Json;
using FullArtCardPacks;

internal static class LayerPolicyTests
{
    internal static void Run()
    {
        var layers = JsonSerializer.Deserialize<FullArtLayerMetadata>("""
            {
                "background": "layers/silent/survivor/background.png",
                "foreground": "layers/silent/survivor/foreground.png"
            }
            """)!;
        var entry = new FullArtCardEntry { Layers = layers };
        Require(LayerPolicy.TryDecide(entry, true, true, 0.5f, out var decision), "valid pilot enabled");
        Require(decision.Depth == 0.5f, "global depth scales authored depth");
        Require(decision.SubjectScale == 1f && decision.SubjectOffsetX == 0f && decision.SubjectOffsetY == 0f,
            "legacy metadata retains identity subject placement");
        Require(decision.BackgroundShift == 0.010f && decision.ForegroundShift == 0.022f
            && decision.ForegroundScale == 1.035f && decision.ShadowStrength == 0.28f && decision.FrameInset == 0.055f,
            "legacy motion, shadow, and trim defaults remain unchanged");
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
        var pinned = JsonSerializer.Deserialize<FullArtLayerMetadata>("""
            {
                "background": "layers/silent/survivor/background.png",
                "foreground": "layers/silent/survivor/foreground.png",
                "foreground_shift": 0,
                "foreground_scale": 1
            }
            """)!;
        Require(LayerPolicy.TryDecide(entry with { Layers = pinned }, true, true, 1f, out var fixedSubject),
            "reviewed pinned subject accepted");
        Require(LayerPolicy.WithPortraitParallax(fixedSubject, 0f) == fixedSubject,
            "zero portrait parallax keeps the reviewed pinned subject");
        Require(LayerPolicy.WithPortraitParallax(fixedSubject, float.NaN) == fixedSubject,
            "invalid portrait parallax keeps the reviewed decision");
        var moving = LayerPolicy.WithPortraitParallax(fixedSubject, 1f);
        Require(moving.ForegroundShift == LayerPolicy.MaxPortraitShift
            && moving.ForegroundScale >= 1f + 2f * moving.ForegroundShift,
            "full portrait parallax moves the subject with enough lift to cover canvas edges");
        Require(LayerPolicy.WithPortraitParallax(fixedSubject, 9f) == moving, "portrait parallax is bounded");
        Require(moving with { ForegroundShift = 0f, ForegroundScale = 1f } == fixedSubject,
            "portrait parallax changes only subject shift and lift");
        Require(LayerPolicy.TryDecide(entry, true, true, 1f, out decision)
            && LayerPolicy.WithPortraitParallax(decision, 0.25f) == decision,
            "authored motion above the slider keeps the legacy decision unchanged");
        var positioned = JsonSerializer.Deserialize<FullArtLayerMetadata>("""
            {
                "background": "layers/silent/survivor/background.png",
                "foreground": "layers/silent/survivor/foreground.png",
                "depth": 0.8,
                "subject_scale": 0.625,
                "subject_offset_x": -0.125,
                "subject_offset_y": 0.375
            }
            """)!;
        foreach (var (globalDepth, expectedDepth) in new[] { (0f, 0f), (0.25f, 0.2f), (1f, 0.8f), (9f, 0.8f), (-1f, 0f) })
        {
            Require(LayerPolicy.TryDecide(entry with { Layers = positioned }, true, true, globalDepth, out decision),
                $"authored subject placement accepted at global depth {globalDepth}");
            Require(decision.Depth == expectedDepth && decision.ForegroundScale == 1.035f,
                "static placement preserves authored depth and lift");
            Require(decision.SubjectScale == 0.625f && decision.SubjectOffsetX == -0.125f && decision.SubjectOffsetY == 0.375f,
                "JSON subject placement survives independently of global depth");
        }
        foreach (var boundary in new[]
        {
            layers with { SubjectScale = 0.25f, SubjectOffsetX = -0.5f, SubjectOffsetY = 0.5f },
            layers with { SubjectScale = 1.5f, SubjectOffsetX = 0.5f, SubjectOffsetY = -0.5f },
        })
        {
            Require(LayerPolicy.TryDecide(entry with { Layers = boundary }, true, true, 1f, out decision),
                "subject placement endpoints are inclusive");
            Require(decision.SubjectScale == boundary.SubjectScale && decision.SubjectOffsetX == boundary.SubjectOffsetX
                && decision.SubjectOffsetY == boundary.SubjectOffsetY, "valid placement is not clamped or recentered");
        }
        foreach (float invalid in new[]
        {
            0f, -1f, MathF.BitDecrement(0.25f), MathF.BitIncrement(1.5f),
            float.NaN, float.NegativeInfinity, float.PositiveInfinity,
        })
            RequireInvalidPlacement(entry, layers with { SubjectScale = invalid }, $"subject scale {invalid}");
        foreach (float invalid in new[]
        {
            MathF.BitDecrement(-0.5f), MathF.BitIncrement(0.5f),
            float.NaN, float.NegativeInfinity, float.PositiveInfinity,
        })
        {
            RequireInvalidPlacement(entry, layers with { SubjectOffsetX = invalid }, $"subject X offset {invalid}");
            RequireInvalidPlacement(entry, layers with { SubjectOffsetY = invalid }, $"subject Y offset {invalid}");
        }
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
        TestMasks(layers, entry);
        TestCanvases();
        TestRegistration();
        Console.WriteLine("Layered card policy tests passed");
    }

    private static void TestCanvases()
    {
        foreach (var size in new[] { (64, 96), (1024, 1536), (2730, 4095) })
            foreach (bool masked in new[] { false, true })
                Require(LayerPolicy.IsValidCanvas(size.Item1, size.Item2, masked),
                    $"old 2:3 canvas {size} remains valid, masked={masked}");
        foreach (var size in new[] { (606, 852), (1023, 1537), (1365, 2048), (1029, 1528), (64, 65), (2729, 4096) })
        {
            Require(LayerPolicy.IsValidCanvas(size.Item1, size.Item2, true),
                $"matching masked original portrait canvas {size} accepted");
            Require(!LayerPolicy.IsValidCanvas(size.Item1, size.Item2, false),
                $"unmasked RGBA {size} cannot bypass the established 2:3 contract");
        }
        foreach (var size in new[] { (0, 0), (-64, -96), (63, 96), (96, 96), (144, 96),
            (2731, 4095), (64, 4097), (2730, 4096), (int.MaxValue, int.MaxValue), (64, int.MinValue) })
            foreach (bool masked in new[] { false, true })
                Require(!LayerPolicy.IsValidCanvas(size.Item1, size.Item2, masked),
                    $"invalid, landscape or over-budget canvas {size} rejected, masked={masked}");
    }

    private static void TestRegistration()
    {
        const string valid = """{"mode":"normalized_source_uv","source_dimensions":[606,852],"mask_dimensions":[1058,1487],"background_dimensions":[303,426]}""";
        var layers = new FullArtLayerMetadata { Background = "layers/card/plate.png", Foreground = "art/card.png",
            ForegroundMask = "layers/card/mask.png", ForegroundMaskChannel = "alpha",
            Registration = JsonDocument.Parse(valid).RootElement.Clone() };
        var entry = new FullArtCardEntry { Art = "art/card.png", Layers = layers };
        Require(LayerPolicy.TryDecide(entry, true, true, 1, out _), "explicit normalized registration accepted");
        Require(LayerPolicy.TryGetRegistration(layers, out var registration)
            && registration!.Value.Source == new LayerCanvas(606, 852)
            && registration.Value.Mask == new LayerCanvas(1058, 1487), "declared canvases survive JSON");
        foreach (string invalid in new[] { "null", "[]", "false", "42", "\"normalized_source_uv\"", "{}",
            valid.Replace("normalized_source_uv", "normalized"), valid.Replace("[606,852]", "[606]"),
            valid.Replace("[606,852]", "[606,852,1]"), valid.Replace("[606,852]", "[true,852]"),
            valid.Replace("[606,852]", "[\"606\",852]"), valid.Replace("[606,852]", "[606.5,852]"),
            valid.Replace("[606,852]", "[606.0,852]"), valid.Replace("[606,852]", "[2147483648,852]"),
            valid.Replace("[1058,1487]", "[1058,1480]"), valid.Replace("[303,426]", "[303,430]"),
            valid.Replace("[303,426]", "[2730,4096]"), valid.Replace("[1058,1487]", "[63,89]"),
            valid.Replace("[303,426]}", "[303,426],\"aspect_tolerance\":1}"),
            valid.Replace("[303,426]}", "[303,426],\"mode\":\"normalized_source_uv\"}") })
        {
            var malformed = layers with { Registration = JsonDocument.Parse(invalid).RootElement.Clone() };
            Require(!LayerPolicy.TryGetRegistration(malformed, out _), "malformed registration rejected: " + invalid);
            Require(!LayerPolicy.TryDecide(entry with { Layers = malformed }, true, true, 1, out _), "malformed registration fails flat");
        }
        Require(!LayerPolicy.TryDecide(entry with { Art = "art/copy.png" }, true, true, 1, out _), "same-size source substitution fails flat");
        Require(!LayerPolicy.TryDecide(entry with { Layers = layers with { ForegroundMask = "" } }, true, true, 1, out _), "registration requires separate mask");
        Require(LayerPolicy.TryDecide(entry with { Layers = layers with { Foreground = "art\\card.png" } }, true, true, 1, out _), "normalized separators retain path identity");
        byte[] mask = MaskBytes(8 * 12, index => (byte)(index % 8 < 4 ? 255 : 0));
        byte[] source = MaskBytes(4 * 6, _ => 255);
        Require(LayerPolicy.IsValidForegroundMask(mask, source, 8, 12, 4, 6), "different-size red mask uses normalized source alpha");
        for (int y = 0; y < 6; y++) for (int x = 0; x < 4; x++) source[(y * 4 + x) * 4 + 3] = (byte)(x < 3 ? 0 : 255);
        Require(!LayerPolicy.IsValidForegroundMask(mask, source, 8, 12, 4, 6), "disjoint source alpha cannot produce visible coverage");
        for (int i = 3; i < source.Length; i += 4) source[i] = 128;
        Require(LayerPolicy.IsValidForegroundMask(mask, source, 8, 12, 4, 6), "partial normalized source alpha remains visible");
    }

    private static void TestMasks(FullArtLayerMetadata layers, FullArtCardEntry entry)
    {
        Require(layers.ForegroundMask == "", "legacy metadata has no foreground mask");
        Require(layers.ForegroundMaskChannel == "red", "legacy mask channel defaults to red");
        var masked = JsonSerializer.Deserialize<FullArtLayerMetadata>("""
            {"background":"layers/card/background.png", "foreground":"art/card.png",
             "foreground_mask":"layers/card/mask.png"}
            """)!;
        Require(masked.ForegroundMask == "layers/card/mask.png"
            && LayerPolicy.TryDecide(entry with { Layers = masked }, true, true, 1f, out _),
            "JSON mask path selects the optional mask route");
        foreach (string? invalid in new string?[]
        {
            null, " ", "../mask.png", "layers/../mask.png", "layers/.. /mask.png", "C:/mask.png",
            "/mask.png", "\\\\server\\mask.png", "file://mask.png", "layers//mask.png", "layers/mask.jpg",
            "layers/mask.png\0", layers.Background, layers.Foreground.Replace('/', '\\'),
        })
            Require(!LayerPolicy.TryDecide(entry with { Layers = layers with { ForegroundMask = invalid! } },
                true, true, 1f, out _), $"invalid mask path fails closed: {invalid ?? "null"}");

        byte[] mask = MaskBytes(16, index => (byte)(index < 8 ? 255 : 0));
        byte[] foreground = MaskBytes(16, _ => 255);
        Require(LayerPolicy.IsValidForegroundMask(mask, foreground, 4, 4), "opaque cutout mask accepted");
        Require(!LayerPolicy.IsValidForegroundMask(mask, foreground, 4, 5), "mask canvas byte count must match");
        Require(!LayerPolicy.IsValidForegroundMask(mask, foreground.AsSpan(0, 60), 4, 4), "foreground canvas must match mask");
        Require(!LayerPolicy.IsValidForegroundMask(mask, foreground, int.MaxValue, int.MaxValue), "mask dimensions cannot overflow");
        foreach (byte level in new byte[] { 0, 128, 255 })
            Require(!LayerPolicy.IsValidForegroundMask(MaskBytes(16, _ => level), foreground, 4, 4),
                $"flat mask {level} cannot reveal the source canvas");
        byte[] invalidAlpha = (byte[])mask.Clone();
        invalidAlpha[3] = 254;
        Require(!LayerPolicy.IsValidForegroundMask(invalidAlpha, foreground, 4, 4), "mask alpha must be fully opaque");
        byte[] nearGray = (byte[])mask.Clone();
        nearGray[1] = 239;
        Require(LayerPolicy.IsValidForegroundMask(nearGray, foreground, 4, 4), "16-level RGB spread is tolerated");
        nearGray[1] = 238;
        Require(!LayerPolicy.IsValidForegroundMask(nearGray, foreground, 4, 4), "greater RGB spread rejects colored masks");
        byte[] sourceAlpha = (byte[])foreground.Clone();
        for (int index = 3; index < sourceAlpha.Length; index += 4) sourceAlpha[index] = 128;
        Require(LayerPolicy.IsValidForegroundMask(mask, sourceAlpha, 4, 4), "partial source alpha can multiply the mask");
        for (int index = 3; index < 8 * 4; index += 4) sourceAlpha[index] = 0;
        Require(!LayerPolicy.IsValidForegroundMask(mask, sourceAlpha, 4, 4), "mask must overlap visible source alpha");
        byte[] largeSource = MaskBytes(2000, _ => 255);
        Require(!LayerPolicy.IsValidForegroundMask(MaskBytes(2000, index => (byte)(index == 0 ? 255 : 0)),
            largeSource, 40, 50), "single visible speck is not a meaningful foreground");
        Require(LayerPolicy.IsValidForegroundMask(MaskBytes(2000, index => (byte)(index < 2 ? 255 : 0)),
            largeSource, 40, 50), "0.1 percent visible-region boundary is inclusive");
        Require(!LayerPolicy.IsValidForegroundMask(MaskBytes(2000, index => (byte)(index == 0 ? 0 : 255)),
            largeSource, 40, 50), "single hidden speck is not a meaningful cutout");
        foreach (string? invalid in new string?[] { null, "", "green", "Alpha", "RED", " alpha " })
        {
            Require(!LayerPolicy.TryDecide(entry with { Layers = masked with { ForegroundMaskChannel = invalid! } },
                true, true, 1f, out _), $"unknown mask channel fails closed: {invalid ?? "null"}");
            Require(!LayerPolicy.IsValidForegroundMask(mask, foreground, 4, 4, invalid!),
                "unknown mask channel cannot reach a sampler");
        }
        var alphaMetadata = JsonSerializer.Deserialize<FullArtLayerMetadata>("""
            {"background":"layers/card/background.png", "foreground":"art/card.png",
             "foreground_mask":"layers/card/subject.png", "foreground_mask_channel":"alpha"}
            """)!;
        Require(alphaMetadata.ForegroundMaskChannel == "alpha"
            && LayerPolicy.TryDecide(entry with { Layers = alphaMetadata }, true, true, 1f, out _),
            "JSON alpha channel is explicit");
        byte[] alphaMask = MaskBytes(16, _ => 0);
        for (int index = 0; index < 16; index++)
        {
            alphaMask[index * 4] = 255;
            alphaMask[index * 4 + 1] = 0;
            alphaMask[index * 4 + 2] = 200;
            alphaMask[index * 4 + 3] = (byte)(index < 8 ? 254 : 0);
        }
        Require(LayerPolicy.IsValidForegroundMask(alphaMask, foreground, 4, 4, "alpha"),
            "colored cutout RGB is irrelevant when alpha has meaningful visible and hidden regions");
        Require(!LayerPolicy.IsValidForegroundMask(alphaMask, foreground, 4, 4, "red"),
            "alpha cutout cannot accidentally use the red-mask contract");
        Require(!LayerPolicy.IsValidForegroundMask(mask, foreground, 4, 4, "alpha"),
            "opaque RGB-style mask cannot supply alpha coverage");
        Require(!LayerPolicy.IsValidForegroundMask(alphaMask, sourceAlpha, 4, 4, "alpha"),
            "alpha mask must overlap visible source alpha");
    }

    private static byte[] MaskBytes(int pixels, Func<int, byte> luminance)
    {
        var bytes = new byte[pixels * 4];
        for (int index = 0; index < pixels; index++)
        {
            byte level = luminance(index);
            bytes[index * 4] = bytes[index * 4 + 1] = bytes[index * 4 + 2] = level;
            bytes[index * 4 + 3] = 255;
        }
        return bytes;
    }

    private static void RequireInvalidPlacement(FullArtCardEntry entry, FullArtLayerMetadata layers, string message)
    {
        Require(!LayerPolicy.TryDecide(entry with { Layers = layers }, true, true, 1f, out var decision),
            $"{message} must fall back to flat art");
        Require(decision == default, $"{message} must not leak an invalid shader decision");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
