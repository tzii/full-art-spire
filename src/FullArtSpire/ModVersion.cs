using System.Reflection;
using MegaCrit.Sts2.Core.Modding;

namespace FullArtSpire;

// The version the player sees: the one in the mod's own manifest, as the game
// read it, so the settings page and the game's mod list always agree.
internal static class ModVersion
{
    private static string? _current;

    internal static string Current => _current ??= Read();

    private static string Read()
    {
        try
        {
            string? version = ModManager.Mods
                .FirstOrDefault(mod => mod.manifest?.id == ModIdentity.ModId)?.manifest?.version;
            if (!string.IsNullOrWhiteSpace(version)) return version.Trim();
        }
        catch (Exception)
        {
            // Fall through to the assembly's own version.
        }

        string? assembly = typeof(ModVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(assembly) ? "unknown" : assembly.Split('+')[0];
    }
}
