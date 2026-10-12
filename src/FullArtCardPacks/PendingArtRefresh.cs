using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

/// <summary>Cards whose art is still loading in the background. Each is
/// refreshed once results arrive, provided the node still shows the same card;
/// a pooled node rebound to another card waits on its own request instead.</summary>
internal static class PendingArtRefresh
{
    private static readonly Dictionary<ulong, string> Waiting = new();
    private static bool _subscribed;

    internal static int Count => Waiting.Count;

    internal static void Track(NCard card)
    {
        if (card.Model?.GetType().FullName is not { } cardId) return;
        Waiting[card.GetInstanceId()] = cardId;
        if (_subscribed) return;
        TextureCache.Delivered += Refresh;
        _subscribed = true;
    }

    internal static void Forget(NCard card) => Waiting.Remove(card.GetInstanceId());

    private static void Refresh()
    {
        if (Waiting.Count == 0) return;
        // Still-pending cards track themselves again during their refresh.
        var waiting = Waiting.ToArray();
        Waiting.Clear();
        foreach (var (id, cardId) in waiting)
        {
            if (GodotObject.InstanceFromId(id) is not NCard card || !GodotObject.IsInstanceValid(card)
                || card.IsQueuedForDeletion() || card.Model?.GetType().FullName != cardId)
                continue;
            PortraitPatch.Refresh(card);
        }
    }
}
