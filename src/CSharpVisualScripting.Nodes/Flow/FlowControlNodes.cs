using CSharpVisualScripting.Core.Models;

namespace CSharpVisualScripting.Nodes.Flow;

/// <summary>
/// Entry point node for graph execution
/// </summary>
public class StartNode : Node
{
    public StartNode()
    {
        Title = "Start";
        Category = "Flow Control";
        Description = "Entry point for execution";
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Start",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
    }
    
    protected override bool IsExecutionInputRequired(Pin pin) => false;
}

/// <summary>
/// Conditional branch node (if statement)
/// </summary>
public class BranchNode : Node
{
    public BranchNode()
    {
        Title = "Branch";
        Category = "Flow Control";
        Description = "Conditional execution based on boolean condition";
        
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
            Name = "Condition",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = typeof(bool),
            DefaultValue = false,
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "True",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "False",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
    }
}

/// <summary>
/// Sequence node - executes pins in order
/// </summary>
public class SequenceNode : Node
{
    public SequenceNode(int outputCount = 2)
    {
        Title = "Sequence";
        Category = "Flow Control";
        Description = "Executes multiple outputs in sequence";
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Exec",
            Kind = PinKind.Execution,
            Direction = PinDirection.Input,
            NodeId = Id
        });
        
        for (int i = 0; i < outputCount; i++)
        {
            OutputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = $"Then {i}",
                Kind = PinKind.Execution,
                Direction = PinDirection.Output,
                NodeId = Id
            });
        }
    }
}

/// <summary>
/// For loop node
/// </summary>
public class ForLoopNode : Node
{
    public ForLoopNode()
    {
        Title = "For Loop";
        Category = "Flow Control";
        Description = "Iterate from start to end index";
        
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
            Name = "Start",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = typeof(int),
            DefaultValue = 0,
            NodeId = Id
        });
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "End",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = typeof(int),
            DefaultValue = 10,
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Loop Body",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Index",
            Kind = PinKind.Data,
            Direction = PinDirection.Output,
            DataType = typeof(int),
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Completed",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
    }
}

/// <summary>
/// While loop node
/// </summary>
public class WhileLoopNode : Node
{
    public WhileLoopNode()
    {
        Title = "While Loop";
        Category = "Flow Control";
        Description = "Loop while condition is true";
        
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
            Name = "Loop Body",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Condition",
            Kind = PinKind.Data,
            Direction = PinDirection.Input,
            DataType = typeof(bool),
            NodeId = Id
        });
        
        OutputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Completed",
            Kind = PinKind.Execution,
            Direction = PinDirection.Output,
            NodeId = Id
        });
    }
}

/// <summary>
/// Return node - exits function with optional value
/// </summary>
public class ReturnNode : Node
{
    public ReturnNode(Type? returnType = null)
    {
        Title = "Return";
        Category = "Flow Control";
        Description = "Exit function and return value";
        
        InputPins.Add(new Pin
        {
            Id = Guid.NewGuid(),
            Name = "Exec",
            Kind = PinKind.Execution,
            Direction = PinDirection.Input,
            NodeId = Id
        });
        
        if (returnType != null && returnType != typeof(void))
        {
            InputPins.Add(new Pin
            {
                Id = Guid.NewGuid(),
                Name = "Return Value",
                Kind = PinKind.Data,
                Direction = PinDirection.Input,
                DataType = returnType,
                NodeId = Id
            });
        }
    }
}
