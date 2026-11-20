using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtSpire;

// Pack switches apply while the game runs. A card node records the registry
// generation it was last reloaded under. When the switches change, nodes in the
// scene tree reload at once; a node outside it (pooled, or a screen not shown)
// reloads when it is next shown. Reload rebuilds the frame from the card's rarity
// (spoofed for cards with full art), resets the native portrait, and runs the
// Reload postfix that applies art, layers and finish for the new table.
internal static class LiveCardRefresh
{
    private static readonly StringName GenerationMeta = "full_art_spire_generation";
    private static readonly MethodInfo? ReloadMethod = AccessTools.Method(typeof(NCard), "Reload");
    private static bool _warned;

    // Until the first switch, every node is current and no metadata is written.
    internal static bool IsStale(NCard card)
        => FullArtPackRegistry.Generation != 0
            && card.GetMeta(GenerationMeta, 0).AsInt32() != FullArtPackRegistry.Generation;

    internal static void MarkCurrent(NCard card)
    {
        if (FullArtPackRegistry.Generation != 0)
            card.SetMeta(GenerationMeta, FullArtPackRegistry.Generation);
    }

    /// <summary>Reloads a ready card shown under an older table. True when it reloaded.</summary>
    internal static bool ReloadIfStale(NCard card)
    {
        if (!IsStale(card) || ReloadMethod is null || !card.IsNodeReady() || card.Model is null)
            return false;
        try
        {
            ReloadMethod.Invoke(card, null);
        }
        catch (TargetInvocationException ex)
        {
            if (!_warned)
            {
                _warned = true;
                GD.PushWarning($"[FullArtSpire] Could not reload a card after a pack switch: {ex.InnerException?.Message}");
            }
        }
        MarkCurrent(card);
        return true;
    }

    /// <summary>Reloads every stale card in the scene tree; returns how many.</summary>
    internal static int ReloadStaleCards()
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } root }) return 0;
        int reloaded = 0;
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if (node is NCard card && !card.IsQueuedForDeletion() && ReloadIfStale(card))
                reloaded++;
            foreach (Node child in node.GetChildren())
                pending.Push(child);
        }
        return reloaded;
    }
}
