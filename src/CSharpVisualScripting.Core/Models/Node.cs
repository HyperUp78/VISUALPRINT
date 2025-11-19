using System.Windows;

namespace CSharpVisualScripting.Core.Models;

/// <summary>
/// Base class for all visual scripting nodes
/// </summary>
public abstract class Node
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Node";
    public Point Position { get; set; }
    public List<Pin> InputPins { get; set; } = new();
    public List<Pin> OutputPins { get; set; } = new();
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Node-specific data/state
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
    
    /// <summary>
    /// Gets all pins (inputs and outputs)
    /// </summary>
    public IEnumerable<Pin> GetAllPins() => InputPins.Concat(OutputPins);
    
    /// <summary>
    /// Finds a pin by ID
    /// </summary>
    public Pin? FindPin(Guid pinId) => GetAllPins().FirstOrDefault(p => p.Id == pinId);
    
    /// <summary>
    /// Validates the node's current state
    /// </summary>
    public virtual ValidationResult Validate()
    {
        var errors = new List<string>();
        
        // Check for disconnected execution pins (if node requires them)
        var requiredExecInputs = InputPins.Where(p => p.Kind == PinKind.Execution && IsExecutionInputRequired(p));
        foreach (var pin in requiredExecInputs)
        {
            if (!pin.IsConnected)
                errors.Add($"Execution input '{pin.Name}' must be connected");
        }
        
        return new ValidationResult(errors.Count == 0, errors);
    }
    
    protected virtual bool IsExecutionInputRequired(Pin pin) => true;
}

/// <summary>
/// Result of node validation
/// </summary>
public record ValidationResult(bool IsValid, List<string> Errors);
