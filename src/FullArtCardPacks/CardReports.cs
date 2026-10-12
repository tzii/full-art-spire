using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class CardReports
{
    private static bool _wasPressed;
    private static ulong _treeId;

    internal static void Initialize()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.GetInstanceId() == _treeId) return;
        tree.ProcessFrame += Poll;
        _treeId = tree.GetInstanceId();
        _wasPressed = false;
    }

    private static void Poll()
    {
        bool pressed = Input.IsPhysicalKeyPressed(Key.F8);
        bool triggered = pressed && !_wasPressed;
        _wasPressed = pressed;
        if (!triggered || !FullArtModConfig.EnableCardReports
            || Engine.GetMainLoop() is not SceneTree tree) return;
        // Follow the actual GUI hit test, rather than selecting cards behind a modal.
        for (Node? node = tree.Root.GuiGetHoveredControl(); node is not null; node = node.GetParent())
        {
            if (node is NCard card && CanReport(card))
            {
                try
                {
                    string path = Write(card);
                    GD.Print($"[FullArtCardPacks] Card report saved: {path}");
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[FullArtCardPacks] Could not save card report: {ex.Message}");
                }
                return;
            }
        }
        GD.Print("[FullArtCardPacks] F8: hover a visible card to record it.");
    }

    internal static bool CanReport(NCard card) => GodotObject.IsInstanceValid(card)
        && !card.IsQueuedForDeletion() && card.IsInsideTree() && card.IsVisibleInTree()
        && card.Visibility == ModelVisibility.Visible && card.Model is not null;

    internal static string Write(NCard card, string? outputDirectory = null)
    {
        if (!CanReport(card)) throw new InvalidOperationException("Card is not visible or has no current model.");
        string model = card.Model!.GetType().FullName!;
        FullArtPackRegistry.TryGetCard(model, out var entry);
        var settings = typeof(FullArtModConfig).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.CanRead)
            .ToDictionary(property => property.Name, property => property.GetValue(null));
        TextureRect? portrait = PortraitResolver.Resolve(card);
        Vector2 local = portrait is not null ? portrait.GetLocalMousePosition() : Vector2.Zero;
        string directory = outputDirectory ?? Path.Combine(OS.GetUserDataDir(), "full_art_card_packs");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "card-reports.jsonl");
        var report = new
        {
            schema_version = 1, recorded_at = DateTimeOffset.UtcNow.ToString("O"),
            mod_version = typeof(MainFile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            runtime_model = model, pool = entry?.Pool, card = entry?.CanonicalAssetName,
            display_name = entry?.DisplayName, source = entry?.Art, layers = entry?.Layers,
            presentation = EffectOverlayManager.StoredContext(card).Presentation.ToString(),
            layered_active = LayeredCardRenderer.HasActiveLayer(card),
            pointer_pixels = new[] { local.X, local.Y }, settings
        };
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        File.AppendAllText(path, JsonSerializer.Serialize(report, options) + System.Environment.NewLine);
        return path;
    }
}
