using System.Globalization;
using System.Reflection;

namespace FullArtSpire;

// BaseLib's tickbox binds to a static bool PropertyInfo and only reads and writes
// it. This stands in for one per pack, backed by FullArtModConfig.DisabledPacks,
// so the switches reuse BaseLib's own control, styling and save-on-change.
internal sealed class PackToggleProperty : PropertyInfo
{
    private readonly string _packId;

    internal PackToggleProperty(string packId)
    {
        _packId = packId;
        // Also the option row's node name: Godot names cannot contain '.'.
        Name = "Pack_" + packId.Replace('.', '_');
    }

    public override string Name { get; }
    public override Type PropertyType => typeof(bool);
    public override PropertyAttributes Attributes => PropertyAttributes.None;
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override Type DeclaringType => typeof(FullArtModConfig);
    public override Type ReflectedType => typeof(FullArtModConfig);

    public override object GetValue(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index, CultureInfo? culture)
        => !FullArtModConfig.DisabledPackIds().Contains(_packId);

    public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder, object?[]? index, CultureInfo? culture)
        => FullArtModConfig.SetPackEnabled(_packId, value is true);

    public override MethodInfo[] GetAccessors(bool nonPublic) => Array.Empty<MethodInfo>();
    public override MethodInfo? GetGetMethod(bool nonPublic) => null;
    public override MethodInfo? GetSetMethod(bool nonPublic) => null;
    public override ParameterInfo[] GetIndexParameters() => Array.Empty<ParameterInfo>();
    public override object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => (object[])Array.CreateInstance(attributeType, 0);
    public override bool IsDefined(Type attributeType, bool inherit) => false;
}
