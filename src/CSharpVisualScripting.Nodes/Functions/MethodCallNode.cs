using CSharpVisualScripting.Core.Models;
using System.Reflection;

namespace CSharpVisualScripting.Nodes.Functions;

/// <summary>
/// Node that calls a C# method via reflection
/// </summary>
public class MethodCallNode : Node
{
    public MethodInfo MethodInfo { get; }
    
    public MethodCallNode(MethodInfo methodInfo)
    {
        MethodInfo = methodInfo;
        
        // Get the actual type that defines this method, not just the declaring type
        var actualType = GetActualDeclaringType(methodInfo);
        var typeName = actualType?.Name ?? methodInfo.DeclaringType?.Name ?? "Unknown";
        
        Title = methodInfo.IsStatic 
            ? $"{typeName}.{methodInfo.Name}"
            : $"{typeName}.{methodInfo.Name} (inst)";
        Category = $"Methods/{typeName}";
        Description = $"Call {methodInfo.DeclaringType?.Name}.{methodInfo.Name}";
        
        Properties["MethodInfo"] = methodInfo;
        
        // Add execution pins for non-pure functions
        if (!IsPureFunction(methodInfo))
        {
            InputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = "Exec",
                Kind = PinKind.Execution,
                Direction = PinDirection.Input,
                NodeId = Id
            });
            
            OutputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = "Exec",
                Kind = PinKind.Execution,
                Direction = PinDirection.Output,
                NodeId = Id
            });
        }
        
        // Add target pin for instance methods
        if (!methodInfo.IsStatic)
        {
            InputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = "Target",
                Kind = PinKind.Data,
                Direction = PinDirection.Input,
                DataType = methodInfo.DeclaringType,
                NodeId = Id
            });
        }
        
        // Add parameter pins
        foreach (var param in methodInfo.GetParameters())
        {
            InputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = param.Name ?? "param",
                Kind = PinKind.Data,
                Direction = PinDirection.Input,
                DataType = param.ParameterType,
                DefaultValue = param.HasDefaultValue ? param.DefaultValue : null,
                NodeId = Id
            });
        }
        
        // Add return value pin
        if (methodInfo.ReturnType != typeof(void))
        {
            OutputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = "Return Value",
                Kind = PinKind.Data,
                Direction = PinDirection.Output,
                DataType = methodInfo.ReturnType,
                NodeId = Id
            });
        }
    }
    
    private bool IsPureFunction(MethodInfo method)
    {
        // Pure functions are those that don't have side effects
        // Heuristic: static methods on Math, String manipulation, etc.
        var declaringType = method.DeclaringType;
        if (declaringType == typeof(Math) || 
            declaringType == typeof(string) ||
            method.ReturnType != typeof(void))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets the actual type that declares this method, looking up the inheritance chain
    /// to find the most specific type that actually defines the method.
    /// </summary>
    private static Type? GetActualDeclaringType(MethodInfo methodInfo)
    {
        var declaringType = methodInfo.DeclaringType;
        if (declaringType == null) return null;
        
        // If this is a method from Object (like GetHashCode, ToString, etc.)
        // we want to find which specific type is being used, not just Object
        if (declaringType == typeof(object) && methodInfo.ReflectedType != null)
        {
            return methodInfo.ReflectedType;
        }
        
        return declaringType;
    }
}

/// <summary>
/// Node for printing/logging to console
/// </summary>
public class PrintNode : Node
{
    public PrintNode()
    {
        Title = "Print";
        Category = "Debug";
        Description = "Print value to console";
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Exec",
            Kind = PinKind.Execution,
            Direction = PinDirection.Input,
            NodeId = Id
        });
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Value",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = typeof(object),
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Exec",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
    }
}
