using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class EffectOverlayManager
{
    private const string OverlayName = "_full_art_effect_overlay";
    private const string TrackedCardGroup = "full_art_card_packs_seen_cards";
    private const string OverlayGroup = "full_art_card_packs_effect_overlays";
    private static ulong _dimmingTreeId;
    private static readonly StringName PresentationMetadata = "full_art_card_packs_effect_presentation";

    internal static void ApplyOrUpdate(NCard cardNode, EffectContext context)
    {
        if (cardNode.Model is not { } model
            || !FullArtPackRegistry.TryGetCard(model.GetType().FullName, out FullArtCardEntry? entry)
            || entry is null)
        {
            Clear(cardNode);
            if (cardNode.IsInGroup(TrackedCardGroup))
            {
                cardNode.RemoveFromGroup(TrackedCardGroup);
            }
            cardNode.RemoveMeta(PresentationMetadata);
            return;
        }

        if (!cardNode.IsInGroup(TrackedCardGroup))
        {
            cardNode.AddToGroup(TrackedCardGroup);
        }
        cardNode.SetMeta(PresentationMetadata, (int)context.Presentation);

        // Static foil masks do not follow the separated planes. Preserve the
        // authored layered treatment while it is active, and restore foil on
        // disabling it or when layer resources fail closed.
        if (LayeredCardRenderer.HasActiveLayer(cardNode))
        {
            Clear(cardNode);
            return;
        }

        EffectDecision decision = EffectPolicy.Decide(model, entry);
        TextureRect? portrait = PortraitResolver.Resolve(cardNode);
        if (!decision.Enabled || portrait?.Texture is null)
        {
            Clear(cardNode);
            return;
        }

        // Native portraits can be AtlasTextures: their UVs address the atlas,
        // while permission masks address the exported portrait's 0..1 space.
        // Use the audited export only for the overlay, preserving native art
        // and layout on the parent TextureRect. Missing exports fail closed.
        Texture2D? overlayTexture = portrait.Texture;
        if (entry.PreserveOriginalLayout
            && (!TextureCache.TryGetTexture(entry, out overlayTexture) || overlayTexture is null))
        {
            Clear(cardNode);
            return;
        }

        TextureRect overlay = FindOverlay(portrait) ?? CreateOverlay(portrait);
        ClearInactivePortraits(cardNode, portrait);
        ShaderMaterial? material = EffectRenderer.CreateOrUpdateEffectMaterial(
            overlay.Material as ShaderMaterial,
            decision,
            context,
            portrait);
        if (material is null)
        {
            overlay.QueueFree();
            return;
        }

        overlay.Texture = overlayTexture;
        overlay.ExpandMode = portrait.ExpandMode;
        overlay.StretchMode = portrait.StretchMode;
        overlay.Material = material;
        overlay.Visible = portrait.Visible;
    }

    internal static void RefreshTrackedCards()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }
        foreach (Node node in tree.GetNodesInGroup(TrackedCardGroup))
        {
            if (node is not NCard cardNode || !GodotObject.IsInstanceValid(cardNode))
            {
                continue;
            }
            EffectPresentation presentation = EffectPresentation.Unknown;
            if (cardNode.HasMeta(PresentationMetadata))
            {
                int raw = cardNode.GetMeta(PresentationMetadata).AsInt32();
                if (Enum.IsDefined(typeof(EffectPresentation), raw))
                {
                    presentation = (EffectPresentation)raw;
                }
            }
            CardEffectPatch.ApplyOrUpdate(cardNode, new EffectContext(presentation));
        }
    }

    private static TextureRect CreateOverlay(TextureRect portrait)
    {
        var overlay = new TextureRect
        {
            Name = OverlayName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = false,
            ZIndex = 1,
            SelfModulate = portrait.SelfModulate,
        };
        portrait.AddChild(overlay);
        overlay.AddToGroup(OverlayGroup);
        // Signal delegates work without source-generated user callbacks.
        // One tree-owned subscription retains no card or portrait nodes.
        if (Engine.GetMainLoop() is SceneTree tree && tree.GetInstanceId() != _dimmingTreeId)
        {
            tree.ProcessFrame += RefreshOverlayDimming;
            _dimmingTreeId = tree.GetInstanceId();
        }
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return overlay;
    }

    private static void RefreshOverlayDimming()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        foreach (Node node in tree.GetNodesInGroup(OverlayGroup))
        {
            if (node is TextureRect overlay && !overlay.IsQueuedForDeletion()
                && overlay.IsVisibleInTree() && overlay.GetParent() is TextureRect portrait)
            {
                // Modulate is inherited; copying it here would dim twice.
                overlay.SelfModulate = portrait.SelfModulate;
                UpdateLightTilt(overlay, portrait);
            }
        }
    }

    private static void UpdateLightTilt(TextureRect overlay, TextureRect portrait)
    {
        if (overlay.Material is not ShaderMaterial material) return;
        Vector2 target = Vector2.Zero;
        Vector2 size = portrait.Size;
        if (FullArtModConfig.EffectFollowPointer && !FullArtModConfig.ReducedMotion
            && portrait.IsInsideTree() && overlay.IsInsideTree()
            && float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0f && size.Y > 0f
            && portrait.GetViewport() is { } viewport && GodotObject.IsInstanceValid(viewport)
            && overlay.GetViewport() == viewport)
        {
            Vector2 position = portrait.GetLocalMousePosition();
            var pointer = LayerPolicy.Pointer(position.X / size.X * 2f - 1f,
                position.Y / size.Y * 2f - 1f, false);
            target = new Vector2(pointer.X, pointer.Y);
        }
        Vector2 previous = material.GetShaderParameter("view_tilt").AsVector2();
        float blend = FullArtModConfig.ReducedMotion || !FullArtModConfig.EffectFollowPointer
            ? 1f : 1f - MathF.Exp(-12f * (float)overlay.GetProcessDeltaTime());
        material.SetShaderParameter("view_tilt", previous.Lerp(target, blend));
    }

    private static TextureRect? FindOverlay(TextureRect portrait)
    {
        return portrait.GetNodeOrNull<TextureRect>(OverlayName);
    }

    private static void ClearInactivePortraits(NCard cardNode, TextureRect activePortrait)
    {
        foreach (TextureRect candidate in PortraitResolver.All(cardNode))
        {
            if (candidate != activePortrait)
            {
                FindOverlay(candidate)?.QueueFree();
            }
        }
    }

    private static void Clear(NCard cardNode)
    {
        foreach (TextureRect portrait in PortraitResolver.All(cardNode))
        {
            FindOverlay(portrait)?.QueueFree();
        }
    }
}
