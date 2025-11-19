using CSharpVisualScripting.Core.Models;

namespace CSharpVisualScripting.Nodes.Variables;

/// <summary>
/// Get variable value node
/// </summary>
public class GetVariableNode : Node
{
    public GetVariableNode(string variableName, Type variableType)
    {
        Title = $"Get {variableName}";
        Category = "Variables";
        Description = $"Read value of variable '{variableName}'";
        
        Properties["VariableName"] = variableName;
        Properties["VariableType"] = variableType;
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = variableName,
            Kind = PinKind.Data,
            Direction = PinDirection.Output,
            DataType = variableType,
            NodeId = Id
        });
    }
}

/// <summary>
/// Set variable value node
/// </summary>
public class SetVariableNode : Node
{
    public SetVariableNode(string variableName, Type variableType)
    {
        Title = $"Set {variableName}";
        Category = "Variables";
        Description = $"Write value to variable '{variableName}'";
        
        Properties["VariableName"] = variableName;
        Properties["VariableType"] = variableType;
        
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
            Name = variableName,
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = variableType,
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

/// <summary>
/// Literal/constant value node
/// </summary>
public class LiteralNode : Node
{
    public LiteralNode(Type literalType, object? defaultValue = null)
    {
        Title = $"{literalType.Name} Literal";
        Category = "Literals";
        Description = $"Constant {literalType.Name} value";
        
        Properties["LiteralType"] = literalType;
        Properties["Value"] = defaultValue ?? GetDefaultValue(literalType);
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Value",
            Kind = PinKind.Data,
            Direction = PinDirection.Output,
            DataType = literalType,
            DefaultValue = defaultValue,
            NodeId = Id
        });
    }
    
    private static object? GetDefaultValue(Type type)
    {
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return null;
    }
}
