using CSharpVisualScripting.Core.Models;

namespace CSharpVisualScripting.Nodes.Operators;

/// <summary>
/// Base class for binary operator nodes (two inputs, one output)
/// </summary>
public abstract class BinaryOperatorNode : Node
{
    protected BinaryOperatorNode(string operatorSymbol, string operatorName, Type inputType, Type outputType)
    {
        Title = operatorName;
        Category = "Operators";
        Description = $"{operatorName} operator ({operatorSymbol})";
        
        Properties["OperatorSymbol"] = operatorSymbol;
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "A",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = inputType,
            NodeId = Id
        });
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "B",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = inputType,
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Result",
            Kind = PinKind.Data,
            Direction = PinDirection.Output,
            DataType = outputType,
            NodeId = Id
        });
    }
    
    protected override bool IsExecutionInputRequired(Pin pin) => false;
}

/// <summary>
/// Math operator nodes
/// </summary>
public class AddNode : BinaryOperatorNode
{
    public AddNode(Type numericType = null!) 
        : base("+", "Add", numericType ?? typeof(double), numericType ?? typeof(double)) { }
}

public class SubtractNode : BinaryOperatorNode
{
    public SubtractNode(Type numericType = null!) 
        : base("-", "Subtract", numericType ?? typeof(double), numericType ?? typeof(double)) { }
}

public class MultiplyNode : BinaryOperatorNode
{
    public MultiplyNode(Type numericType = null!) 
        : base("*", "Multiply", numericType ?? typeof(double), numericType ?? typeof(double)) { }
}

public class DivideNode : BinaryOperatorNode
{
    public DivideNode(Type numericType = null!) 
        : base("/", "Divide", numericType ?? typeof(double), numericType ?? typeof(double)) { }
}

public class ModuloNode : BinaryOperatorNode
{
    public ModuloNode(Type numericType = null!) 
        : base("%", "Modulo", numericType ?? typeof(int), numericType ?? typeof(int)) { }
}

/// <summary>
/// Comparison operator nodes
/// </summary>
public class EqualsNode : BinaryOperatorNode
{
    public EqualsNode(Type compareType = null!) 
        : base("==", "Equals", compareType ?? typeof(object), typeof(bool)) { }
}

public class NotEqualsNode : BinaryOperatorNode
{
    public NotEqualsNode(Type compareType = null!) 
        : base("!=", "Not Equals", compareType ?? typeof(object), typeof(bool)) { }
}

public class GreaterThanNode : BinaryOperatorNode
{
    public GreaterThanNode(Type compareType = null!) 
        : base(">", "Greater Than", compareType ?? typeof(double), typeof(bool)) { }
}

public class LessThanNode : BinaryOperatorNode
{
    public LessThanNode(Type compareType = null!) 
        : base("<", "Less Than", compareType ?? typeof(double), typeof(bool)) { }
}

public class GreaterOrEqualNode : BinaryOperatorNode
{
    public GreaterOrEqualNode(Type compareType = null!) 
        : base(">=", "Greater Or Equal", compareType ?? typeof(double), typeof(bool)) { }
}

public class LessOrEqualNode : BinaryOperatorNode
{
    public LessOrEqualNode(Type compareType = null!) 
        : base("<=", "Less Or Equal", compareType ?? typeof(double), typeof(bool)) { }
}

/// <summary>
/// Logical operator nodes
/// </summary>
public class AndNode : BinaryOperatorNode
{
    public AndNode() : base("&&", "And", typeof(bool), typeof(bool)) { }
}

public class OrNode : BinaryOperatorNode
{
    public OrNode() : base("||", "Or", typeof(bool), typeof(bool)) { }
}

/// <summary>
/// Unary operator node (one input, one output)
/// </summary>
public class NotNode : Node
{
    public NotNode()
    {
        Title = "Not";
        Category = "Operators";
        Description = "Logical NOT operator (!)";
        
        Properties["OperatorSymbol"] = "!";
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Value",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = typeof(bool),
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Result",
            Kind = PinKind.Data,
            Direction = PinDirection.Output,
            DataType = typeof(bool),
            NodeId = Id
        });
    }
    
    protected override bool IsExecutionInputRequired(Pin pin) => false;
}
