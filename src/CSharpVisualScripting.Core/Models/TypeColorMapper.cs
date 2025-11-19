using System.Windows.Media;

namespace CSharpVisualScripting.Core.Models;

/// <summary>
/// Maps C# types to visual colors for pin display (Blueprint-style)
/// </summary>
public static class TypeColorMapper
{
    private static readonly Dictionary<Type, Color> TypeColors = new()
    {
        // Primitive types
        { typeof(bool), Color.FromRgb(220, 48, 48) },      // Red
        { typeof(byte), Color.FromRgb(72, 176, 176) },     // Cyan
        { typeof(sbyte), Color.FromRgb(72, 176, 176) },    // Cyan
        { typeof(short), Color.FromRgb(72, 176, 176) },    // Cyan
        { typeof(ushort), Color.FromRgb(72, 176, 176) },   // Cyan
        { typeof(int), Color.FromRgb(72, 176, 176) },      // Cyan
        { typeof(uint), Color.FromRgb(72, 176, 176) },     // Cyan
        { typeof(long), Color.FromRgb(72, 176, 176) },     // Cyan
        { typeof(ulong), Color.FromRgb(72, 176, 176) },    // Cyan
        { typeof(float), Color.FromRgb(163, 220, 116) },   // Green
        { typeof(double), Color.FromRgb(163, 220, 116) },  // Green
        { typeof(decimal), Color.FromRgb(163, 220, 116) }, // Green
        { typeof(string), Color.FromRgb(252, 102, 196) },  // Magenta/Pink
        { typeof(char), Color.FromRgb(252, 102, 196) },    // Magenta/Pink
        
        // Common types
        { typeof(object), Color.FromRgb(72, 128, 255) },   // Blue
        { typeof(DateTime), Color.FromRgb(176, 130, 255) }, // Purple
        { typeof(TimeSpan), Color.FromRgb(176, 130, 255) }, // Purple
        { typeof(Guid), Color.FromRgb(176, 176, 176) },    // Gray
        { typeof(void), Color.FromRgb(128, 128, 128) },    // Dark Gray
    };
    
    /// <summary>
    /// Gets the display color for a given type
    /// </summary>
    public static Color GetColorForType(Type type)
    {
        // Direct match
        if (TypeColors.TryGetValue(type, out var color))
            return color;
            
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
            return GetColorForType(underlyingType);
            
        // Handle arrays
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            if (elementType != null)
            {
                var elementColor = GetColorForType(elementType);
                // Make array color slightly darker
                return Color.FromRgb(
                    (byte)(elementColor.R * 0.8),
                    (byte)(elementColor.G * 0.8),
                    (byte)(elementColor.B * 0.8)
                );
            }
        }
        
        // Handle generic types (List<T>, Dictionary<K,V>, etc.)
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            
            // Collections get a golden/yellow color
            if (genericDef == typeof(List<>) || 
                genericDef == typeof(IList<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(ICollection<>))
            {
                return Color.FromRgb(255, 215, 0); // Gold
            }
            
            // Dictionaries get an orange color
            if (genericDef == typeof(Dictionary<,>) || 
                genericDef == typeof(IDictionary<,>))
            {
                return Color.FromRgb(255, 140, 0); // Dark Orange
            }
            
            // Use first generic argument's color
            var genericArgs = type.GetGenericArguments();
            if (genericArgs.Length > 0)
                return GetColorForType(genericArgs[0]);
        }
        
        // Enums get a lime green color
        if (type.IsEnum)
            return Color.FromRgb(124, 252, 0); // Lime Green
            
        // Delegates/Events get a red-orange color
        if (typeof(Delegate).IsAssignableFrom(type))
            return Color.FromRgb(255, 69, 0); // Red-Orange
            
        // Value types (structs) get a yellow-green color
        if (type.IsValueType)
            return Color.FromRgb(192, 220, 116);
            
        // Reference types (classes) default to blue
        return Color.FromRgb(72, 128, 255);
    }
    
    /// <summary>
    /// Gets a brush for UI rendering
    /// </summary>
    public static SolidColorBrush GetBrushForType(Type type)
    {
        return new SolidColorBrush(GetColorForType(type));
    }
    
    /// <summary>
    /// Registers a custom color for a type
    /// </summary>
    public static void RegisterTypeColor(Type type, Color color)
    {
        TypeColors[type] = color;
    }
}
