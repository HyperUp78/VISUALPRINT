namespace CSharpVisualScripting.Core.Models;

/// <summary>
/// Represents a connection between two pins
/// </summary>
public class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourcePinId { get; set; }
    public Guid TargetPinId { get; set; }
    
    public Pin? SourcePin { get; set; }
    public Pin? TargetPin { get; set; }
    
    /// <summary>
    /// Validates that this connection is legal
    /// </summary>
    public bool IsValid()
    {
        if (SourcePin == null || TargetPin == null)
            return false;
            
        // Source must be output, target must be input
        if (SourcePin.Direction != PinDirection.Output || TargetPin.Direction != PinDirection.Input)
            return false;
            
        return SourcePin.CanConnectTo(TargetPin);
    }
}
