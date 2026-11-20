using System.Reflection;
using System.Security.Cryptography;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;

internal static class GameApiTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run()
    {
        // Compilation cannot catch renamed private portrait fields or ambiguous
        // name-only Harmony targets. Inspect the actual referenced game assembly
        // without creating Godot nodes or touching the user's installation/saves.
        foreach (string fieldName in new[] { "_portrait", "_ancientPortrait" })
        {
            FieldInfo? field = typeof(NCard).GetField(fieldName, Instance);
            Require(field is not null && typeof(TextureRect).IsAssignableFrom(field.FieldType),
                $"NCard.{fieldName} must remain a TextureRect portrait field");
        }

        MethodInfo update = UniqueMethod(typeof(NCard), "UpdateVisuals");
        Require(update.GetParameters().Any(p => p.Name == "pileType" && p.ParameterType == typeof(PileType)),
            "NCard.UpdateVisuals must expose pileType for the Harmony postfix");
        Require(update.GetParameters().Any(p => p.Name == "previewMode" && p.ParameterType == typeof(CardPreviewMode)),
            "NCard.UpdateVisuals must expose previewMode for the Harmony postfix");
        foreach (string name in new[] { "_EnterTree", "Reload", "OnFreedToPool" })
            UniqueMethod(typeof(NCard), name);
        // LiveCardRefresh invokes Reload() itself when pack switches change.
        Require(UniqueMethod(typeof(NCard), "Reload").GetParameters().Length == 0,
            "NCard.Reload must stay parameterless for live pack switching");

        Require(typeof(CardModel).GetProperty("Rarity", Instance)?.GetMethod is { ReturnType: var rarity }
            && rarity == typeof(CardRarity), "CardModel.Rarity must remain a CardRarity getter");
        Require(typeof(NCardLibraryGrid).GetMethod("FilterCards", Instance, new[] { typeof(Func<CardModel, bool>) }) is not null,
            "NCardLibraryGrid.FilterCards(predicate) Harmony target must exist");
        Require(typeof(NCardLibraryGrid).GetMethod("FilterCards", Instance,
            new[] { typeof(Func<CardModel, bool>), typeof(List<SortingOrders>) }) is not null,
            "NCardLibraryGrid.FilterCards(predicate, sorting) Harmony target must exist");

        string assembly = typeof(NCard).Assembly.Location;
        using var stream = File.OpenRead(assembly);
        Console.WriteLine($"Game render API contracts passed: {assembly}, sha256={Convert.ToHexStringLower(SHA256.HashData(stream))}");
        Console.WriteLine("API checks do not establish Harmony installation, scene layout or in-game compatibility.");
    }

    private static MethodInfo UniqueMethod(Type type, string name)
    {
        MethodInfo[] matches = type.GetMethods(Instance).Where(m => m.Name == name).ToArray();
        Require(matches.Length == 1, $"{type.Name}.{name} name-only Harmony target must be unambiguous; found {matches.Length}");
        return matches[0];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
