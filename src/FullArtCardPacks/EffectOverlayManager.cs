using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FullArtCardPacks;

internal static class EffectOverlayManager
{
    private const string OverlayName = "_full_art_effect_overlay";
    private const string TrackedCardGroup = "full_art_card_packs_seen_cards";
    private const string OverlayGroup = "full_art_card_packs_effect_overlays";
    private static ulong _dimmingTreeId;
    private static readonly StringName PresentationMetadata = "full_art_card_packs_effect_presentation";
    private static readonly StringName HandBaseMetadata = "full_art_card_packs_hand_base_intensity";
    private static readonly StringName HandFocusMetadata = "full_art_card_packs_hand_focus";
    // Seconds^-1 for easing a hand card's finish toward its hover strength.
    private const float HandFocusResponse = 10f;

    internal static void ApplyOrUpdate(NCard cardNode, EffectContext context)
    {
        if (cardNode.Visibility != ModelVisibility.Visible
            || cardNode.Model is not { } model
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
        if (!entry.PreserveOriginalLayout)
        {
            TextureState artState = TextureCache.Request(entry.Art, out _);
            if (artState != TextureState.Ready)
            {
                if (artState == TextureState.Pending) PendingArtRefresh.Track(cardNode);
                Clear(cardNode);
                return;
            }
        }
        Texture2D? overlayTexture = portrait.Texture;
        if (entry.PreserveOriginalLayout)
        {
            TextureState sourceState = TextureCache.Request(entry.Art, out overlayTexture);
            if (sourceState == TextureState.Pending) PendingArtRefresh.Track(cardNode);
            if (sourceState != TextureState.Ready || overlayTexture is null)
            {
                Clear(cardNode);
                return;
            }
        }
        TextureState maskState = TextureCache.Request(decision.PermissionMask, out _);
        if (maskState != TextureState.Ready)
        {
            if (maskState == TextureState.Pending) PendingArtRefresh.Track(cardNode);
            Clear(cardNode);
            return;
        }

        TextureRect overlay = FindOverlay(portrait) ?? CreateOverlay(portrait);
        // The depth layer can be recreated after the finish; keep the finish on top of it.
        if (overlay.GetIndex() != portrait.GetChildCount() - 1)
        {
            portrait.MoveChild(overlay, -1);
        }
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
        if (context.Presentation == EffectPresentation.Hand)
        {
            bool tracked = overlay.HasMeta(HandFocusMetadata);
            overlay.SetMeta(HandBaseMetadata, decision.Intensity);
            UpdateHandFocus(overlay, cardNode, snap: !tracked);
        }
        else
        {
            overlay.RemoveMeta(HandBaseMetadata);
            overlay.RemoveMeta(HandFocusMetadata);
        }
        SyncLayerCoordinates(overlay, portrait);
        overlay.Visible = portrait.Visible;
    }

    internal static void RefreshTrackedCards()
    {
        TextureCache.SettingsChanged();
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
            PortraitPatch.Refresh(cardNode);
        }
    }

    internal static void Forget(NCard cardNode) => cardNode.RemoveMeta(PresentationMetadata);

    internal static EffectContext StoredContext(NCard cardNode)
    {
        EffectPresentation presentation = EffectPresentation.Unknown;
        if (cardNode.HasMeta(PresentationMetadata))
        {
            int raw = cardNode.GetMeta(PresentationMetadata).AsInt32();
            if (Enum.IsDefined(typeof(EffectPresentation), raw))
            {
                presentation = (EffectPresentation)raw;
            }
        }
        return new EffectContext(presentation);
    }

    private static TextureRect CreateOverlay(TextureRect portrait)
    {
        var overlay = new TextureRect
        {
            Name = OverlayName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = false,
            // A raised z_index escapes the card's draw order, so grid finishes
            // would paint over popups such as the Compendium inspect view.
            ZIndex = 0,
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
                SyncLayerCoordinates(overlay, portrait);
                UpdateLightTilt(overlay, portrait);
                if (overlay.HasMeta(HandBaseMetadata) && OwningCard(portrait) is { } card)
                    UpdateHandFocus(overlay, card, snap: false);
            }
        }
    }

    private static NCard? OwningCard(Node node)
    {
        for (Node? ancestor = node.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is NCard card) return card;
        }
        return null;
    }

    // Hovered, dragged, drawn and played cards ease up to the inspect strength.
    private static void UpdateHandFocus(TextureRect overlay, NCard cardNode, bool snap)
    {
        if (overlay.Material is not ShaderMaterial material) return;
        float target = EffectContext.HandFocusTarget(cardNode);
        float focus = overlay.HasMeta(HandFocusMetadata) ? overlay.GetMeta(HandFocusMetadata).AsSingle() : target;
        float blend = snap || FullArtModConfig.ReducedMotion
            ? 1f : 1f - MathF.Exp(-HandFocusResponse * (float)overlay.GetProcessDeltaTime());
        focus += (target - focus) * blend;
        overlay.SetMeta(HandFocusMetadata, focus);
        float baseIntensity = overlay.GetMeta(HandBaseMetadata).AsSingle();
        material.SetShaderParameter("intensity", baseIntensity * EffectContext.HandIntensityAt(focus));
    }

    private static void SyncLayerCoordinates(TextureRect overlay, TextureRect portrait)
    {
        if (overlay.Material is not ShaderMaterial material) return;
        ShaderMaterial? layer = LayeredCardRenderer.ActiveMaterial(portrait);
        material.SetShaderParameter("use_layered_uv", layer is not null);
        if (layer is null) return;
        material.SetShaderParameter("layer_coverage_texture", layer.GetShaderParameter("foreground_mask_texture"));
        material.SetShaderParameter("layer_coverage_use_alpha",
            !layer.GetShaderParameter("use_foreground_mask").AsBool()
            || layer.GetShaderParameter("foreground_mask_use_alpha").AsBool());
        // The effect samples the same eased subject UV as the depth shader.
        foreach (string name in new[] { "view_pointer", "depth", "foreground_shift", "foreground_scale",
            "subject_scale", "subject_offset_x", "subject_offset_y", "background_shift" })
            material.SetShaderParameter(name, layer.GetShaderParameter(name));
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
