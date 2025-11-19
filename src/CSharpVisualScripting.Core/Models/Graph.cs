namespace CSharpVisualScripting.Core.Models;

/// <summary>
/// Represents a complete visual scripting graph
/// </summary>
public class Graph
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Graph";
    public List<Node> Nodes { get; set; } = new();
    public List<Connection> Connections { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// Adds a node to the graph
    /// </summary>
    public void AddNode(Node node)
    {
        if (Nodes.Any(n => n.Id == node.Id))
            throw new InvalidOperationException($"Node with ID {node.Id} already exists");
            
        Nodes.Add(node);
    }
    
    /// <summary>
    /// Removes a node and all connected connections
    /// </summary>
    public void RemoveNode(Guid nodeId)
    {
        var node = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
            return;
            
        // Remove all connections involving this node's pins
        var pinIds = node.GetAllPins().Select(p => p.Id).ToHashSet();
        Connections.RemoveAll(c => pinIds.Contains(c.SourcePinId) || pinIds.Contains(c.TargetPinId));
        
        Nodes.Remove(node);
    }
    
    /// <summary>
    /// Adds a connection between two pins
    /// </summary>
    public Connection? AddConnection(Guid sourcePinId, Guid targetPinId)
    {
        var sourcePin = FindPin(sourcePinId);
        var targetPin = FindPin(targetPinId);
        
        if (sourcePin == null || targetPin == null)
            return null;
            
        var connection = new Connection
        {
            SourcePinId = sourcePinId,
            TargetPinId = targetPinId,
            SourcePin = sourcePin,
            TargetPin = targetPin
        };
        
        if (!connection.IsValid())
            return null;
            
        // Remove existing connections to input pin (inputs can only have one connection)
        if (targetPin.Direction == PinDirection.Input && targetPin.Kind == PinKind.Data)
        {
            Connections.RemoveAll(c => c.TargetPinId == targetPinId);
        }
        
        Connections.Add(connection);
        sourcePin.IsConnected = true;
        targetPin.IsConnected = true;
        
        return connection;
    }
    
    /// <summary>
    /// Removes a connection
    /// </summary>
    public void RemoveConnection(Guid connectionId)
    {
        var connection = Connections.FirstOrDefault(c => c.Id == connectionId);
        if (connection == null)
            return;
            
        Connections.Remove(connection);
        UpdatePinConnectionState(connection.SourcePinId);
        UpdatePinConnectionState(connection.TargetPinId);
    }
    
    /// <summary>
    /// Finds a pin by ID across all nodes
    /// </summary>
    public Pin? FindPin(Guid pinId)
    {
        foreach (var node in Nodes)
        {
            var pin = node.FindPin(pinId);
            if (pin != null)
                return pin;
        }
        return null;
    }
    
    /// <summary>
    /// Validates the entire graph
    /// </summary>
    public GraphValidationResult Validate()
    {
        var errors = new Dictionary<Guid, List<string>>();
        
        // Validate each node
        foreach (var node in Nodes)
        {
            var result = node.Validate();
            if (!result.IsValid)
            {
                errors[node.Id] = result.Errors;
            }
        }
        
        // Check for cycles in execution flow
        var cycles = DetectExecutionCycles();
        if (cycles.Any())
        {
            errors[Guid.Empty] = new List<string> { "Execution flow contains cycles" };
        }
        
        return new GraphValidationResult(errors.Count == 0, errors);
    }
    
    /// <summary>
    /// Gets all connections from a specific pin
    /// </summary>
    public IEnumerable<Connection> GetConnectionsFromPin(Guid pinId)
    {
        return Connections.Where(c => c.SourcePinId == pinId || c.TargetPinId == pinId);
    }
    
    /// <summary>
    /// Gets execution order using topological sort
    /// </summary>
    public List<Node> GetExecutionOrder()
    {
        var visited = new HashSet<Guid>();
        var result = new List<Node>();
        var tempMarks = new HashSet<Guid>();
        
        foreach (var node in Nodes)
        {
            if (!visited.Contains(node.Id))
            {
                Visit(node, visited, tempMarks, result);
            }
        }
        
        return result;
    }
    
    private void Visit(Node node, HashSet<Guid> visited, HashSet<Guid> tempMarks, List<Node> result)
    {
        if (tempMarks.Contains(node.Id))
            throw new InvalidOperationException("Cycle detected in execution graph");
            
        if (visited.Contains(node.Id))
            return;
            
        tempMarks.Add(node.Id);
        
        // Visit nodes connected via execution pins
        var execOutputs = node.OutputPins.Where(p => p.Kind == PinKind.Execution);
        foreach (var pin in execOutputs)
        {
            var connections = GetConnectionsFromPin(pin.Id);
            foreach (var connection in connections)
            {
                var targetNode = Nodes.FirstOrDefault(n => n.GetAllPins().Any(p => p.Id == connection.TargetPinId));
                if (targetNode != null)
                {
                    Visit(targetNode, visited, tempMarks, result);
                }
            }
        }
        
        tempMarks.Remove(node.Id);
        visited.Add(node.Id);
        result.Insert(0, node);
    }
    
    private List<List<Guid>> DetectExecutionCycles()
    {
        // Simplified cycle detection - returns empty for now
        // TODO: Implement proper cycle detection algorithm
        return new List<List<Guid>>();
    }
    
    private void UpdatePinConnectionState(Guid pinId)
    {
        var pin = FindPin(pinId);
        if (pin != null)
        {
            pin.IsConnected = Connections.Any(c => c.SourcePinId == pinId || c.TargetPinId == pinId);
        }
    }
}

/// <summary>
/// Result of graph validation
/// </summary>
public record GraphValidationResult(bool IsValid, Dictionary<Guid, List<string>> ErrorsByNode);
