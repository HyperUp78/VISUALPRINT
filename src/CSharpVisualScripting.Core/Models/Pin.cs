using System.Windows.Media;

namespace CSharpVisualScripting.Core.Models;

/// <summary>
/// Defines the type of a pin (execution flow or typed data)
/// </summary>
public enum PinKind
{
    /// <summary>Execution flow pin (white, controls program flow)</summary>
    Execution,
    /// <summary>Typed data pin (colored based on data type)</summary>
    Data
}

/// <summary>
/// Defines the direction of a pin relative to the node
/// </summary>
public enum PinDirection
{
    /// <summary>Input pin (left side of node)</summary>
    Input,
    /// <summary>Output pin (right side of node)</summary>
    Output
}

/// <summary>
/// Represents a connection point on a node
/// </summary>
public class Pin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public PinKind Kind { get; set; }
    public PinDirection Direction { get; set; }
    public Type? DataType { get; set; }
    public object? DefaultValue { get; set; }
    public Guid NodeId { get; set; }
    public bool IsConnected { get; set; }
    
    /// <summary>
    /// Gets the display color for this pin based on its type
    /// </summary>
    public Color GetColor()
    {
        if (Kind == PinKind.Execution)
            return Colors.White;
            
        if (DataType == null)
            return Colors.Gray;
            
        return TypeColorMapper.GetColorForType(DataType);
    }
    
    /// <summary>
    /// Checks if this pin can connect to another pin
    /// </summary>
    public bool CanConnectTo(Pin other)
    {
        // Must be different directions
        if (Direction == other.Direction)
            return false;
            
        // Must be different nodes
        if (NodeId == other.NodeId)
            return false;
            
        // Same kind
        if (Kind != other.Kind)
            return false;
            
        // Execution pins can always connect
        if (Kind == PinKind.Execution)
            return true;
            
        // Data pins must have compatible types
        if (DataType == null || other.DataType == null)
            return true; // Wildcard/untyped
            
        return TypeCompatibilityChecker.AreTypesCompatible(DataType, other.DataType);
    }
}
