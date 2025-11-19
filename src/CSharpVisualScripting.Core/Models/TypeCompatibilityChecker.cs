namespace CSharpVisualScripting.Core.Models;

/// <summary>
/// Checks type compatibility for pin connections
/// </summary>
public static class TypeCompatibilityChecker
{
    /// <summary>
    /// Checks if source type can be assigned to target type
    /// </summary>
    public static bool AreTypesCompatible(Type sourceType, Type targetType)
    {
        // Exact match
        if (sourceType == targetType)
            return true;
            
        // Target can be assigned from source
        if (targetType.IsAssignableFrom(sourceType))
            return true;
            
        // Handle nullable types
        var sourceUnderlying = Nullable.GetUnderlyingType(sourceType);
        var targetUnderlying = Nullable.GetUnderlyingType(targetType);
        
        if (sourceUnderlying != null && targetUnderlying != null)
            return AreTypesCompatible(sourceUnderlying, targetUnderlying);
            
        if (targetUnderlying != null)
            return AreTypesCompatible(sourceType, targetUnderlying);
            
        // Numeric conversions
        if (IsNumericType(sourceType) && IsNumericType(targetType))
            return CanConvertNumeric(sourceType, targetType);
            
        // String conversions (everything can convert to string)
        if (targetType == typeof(string))
            return true;
            
        // Object can accept anything
        if (targetType == typeof(object))
            return true;
            
        // Check for implicit/explicit conversion operators
        if (HasConversionOperator(sourceType, targetType))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// Checks if a type is numeric
    /// </summary>
    public static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }
    
    /// <summary>
    /// Checks if numeric conversion is safe
    /// </summary>
    private static bool CanConvertNumeric(Type source, Type target)
    {
        // Allow all numeric conversions for now
        // In production, might want to warn about narrowing conversions
        return true;
    }
    
    /// <summary>
    /// Checks if there's an implicit or explicit conversion operator
    /// </summary>
    private static bool HasConversionOperator(Type source, Type target)
    {
        // Check source type for conversion operators
        var methods = source.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var method in methods)
        {
            if ((method.Name == "op_Implicit" || method.Name == "op_Explicit") &&
                method.ReturnType == target &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == source)
            {
                return true;
            }
        }
        
        // Check target type for conversion operators
        methods = target.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var method in methods)
        {
            if ((method.Name == "op_Implicit" || method.Name == "op_Explicit") &&
                method.ReturnType == target &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == source)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets the required conversion method name, if any
    /// </summary>
    public static string? GetConversionMethod(Type sourceType, Type targetType)
    {
        if (AreTypesCompatible(sourceType, targetType))
        {
            if (sourceType != targetType && targetType != typeof(object))
            {
                if (targetType == typeof(string))
                    return "ToString";
                    
                if (IsNumericType(sourceType) && IsNumericType(targetType))
                    return $"Convert.To{targetType.Name}";
            }
        }
        
        return null;
    }
}
