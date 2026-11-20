using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtSpire;

internal static class PortraitResolver
{
    private static readonly FieldInfo? PortraitField = typeof(NCard).GetField("_portrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? AncientPortraitField = typeof(NCard).GetField("_ancientPortrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly HashSet<string> WarnedExternalViewports = new(StringComparer.OrdinalIgnoreCase);

    internal static TextureRect? Resolve(NCard cardNode)
    {
        for (Node? ancestor = cardNode; ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is SubViewportContainer
                && (ancestor.Name.ToString().Contains("Balatro", StringComparison.OrdinalIgnoreCase)
                    || ancestor.GetType().FullName?.Contains("Balatro", StringComparison.OrdinalIgnoreCase) == true))
            {
                string key = ancestor.GetPath().ToString();
                if (WarnedExternalViewports.Add(key))
                {
                    GD.PushWarning($"[FullArtSpire] Skipping effects inside known external viewport {key}; full art remains active.");
                }
                return null;
            }
        }

        if (FullArtPackRegistry.TryGetCard(cardNode.Model?.GetType().FullName, out FullArtCardEntry? entry)
            && entry is not null && entry.PreserveOriginalLayout)
        {
            return PortraitField?.GetValue(cardNode) as TextureRect;
        }

        if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancient && ancient.Visible)
        {
            return ancient;
        }
        return PortraitField?.GetValue(cardNode) as TextureRect;
    }

    internal static IEnumerable<TextureRect> All(NCard cardNode)
    {
        if (PortraitField?.GetValue(cardNode) is TextureRect portrait)
            yield return portrait;
        if (AncientPortraitField?.GetValue(cardNode) is TextureRect ancientPortrait)
            yield return ancientPortrait;
    }
}
