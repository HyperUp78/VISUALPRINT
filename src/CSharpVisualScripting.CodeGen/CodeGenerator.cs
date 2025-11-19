using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpVisualScripting.Core.Models;
using System.Reflection;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CSharpVisualScripting.CodeGen;

/// <summary>
/// Generates C# code from visual scripting graphs using Roslyn
/// </summary>
public class CodeGenerator
{
    private readonly Graph _graph;
    private readonly Dictionary<Guid, string> _nodeVariableNames = new();
    private int _tempVariableCounter = 0;
    
    public CodeGenerator(Graph graph)
    {
        _graph = graph;
    }
    
    /// <summary>
    /// Generates a complete C# class from the graph
    /// </summary>
    public CompilationUnitSyntax GenerateCompilationUnit(string className = "GeneratedClass", string namespaceName = "Generated")
    {
        var usings = new[]
        {
            UsingDirective(ParseName("System")),
            UsingDirective(ParseName("System.Collections.Generic")),
            UsingDirective(ParseName("System.Linq")),
            UsingDirective(ParseName("System.Threading.Tasks"))
        };
        
        var classDeclaration = GenerateClass(className);
        
        var namespaceDeclaration = NamespaceDeclaration(ParseName(namespaceName))
            .AddMembers(classDeclaration);
        
        return CompilationUnit()
            .AddUsings(usings)
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace();
    }
    
    /// <summary>
    /// Generates the class declaration
    /// </summary>
    private ClassDeclarationSyntax GenerateClass(string className)
    {
        var methods = new List<MethodDeclarationSyntax>();
        
        // Generate main execution method
        var executeMethod = GenerateExecuteMethod();
        methods.Add(executeMethod);
        
        return ClassDeclaration(className)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddMembers(methods.ToArray());
    }
    
    /// <summary>
    /// Generates the main execution method from the graph
    /// </summary>
    private MethodDeclarationSyntax GenerateExecuteMethod()
    {
        var statements = new List<StatementSyntax>();
        
        // Get execution order
        var executionOrder = _graph.GetExecutionOrder();
        
        // Generate statements for each node
        foreach (var node in executionOrder)
        {
            var nodeStatements = GenerateStatementsForNode(node);
            statements.AddRange(nodeStatements);
        }
        
        return MethodDeclaration(
                PredefinedType(Token(SyntaxKind.VoidKeyword)),
                Identifier("Execute"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .WithBody(Block(statements));
    }
    
    /// <summary>
    /// Generates statements for a specific node
    /// </summary>
    private List<StatementSyntax> GenerateStatementsForNode(Node node)
    {
        var statements = new List<StatementSyntax>();
        
        // Use type pattern matching to handle different node types
        switch (node)
        {
            case CSharpVisualScripting.Nodes.Flow.BranchNode:
                statements.AddRange(GenerateBranchNode(node));
                break;
                
            case CSharpVisualScripting.Nodes.Flow.ForLoopNode:
                statements.AddRange(GenerateForLoopNode(node));
                break;
                
            case CSharpVisualScripting.Nodes.Variables.SetVariableNode:
                statements.AddRange(GenerateSetVariableNode(node));
                break;
                
            case CSharpVisualScripting.Nodes.Functions.PrintNode:
                statements.AddRange(GeneratePrintNode(node));
                break;
                
            case CSharpVisualScripting.Nodes.Functions.MethodCallNode methodCall:
                statements.AddRange(GenerateMethodCallNode(methodCall));
                break;
                
            // Add more node types as needed
        }
        
        return statements;
    }
    
    /// <summary>
    /// Generates if statement for Branch node
    /// </summary>
    private List<StatementSyntax> GenerateBranchNode(Node node)
    {
        var conditionPin = node.InputPins.FirstOrDefault(p => p.Name == "Condition");
        if (conditionPin == null)
            return new List<StatementSyntax>();
            
        var conditionExpr = GetExpressionForPin(conditionPin);
        
        var truePin = node.OutputPins.FirstOrDefault(p => p.Name == "True");
        var falsePin = node.OutputPins.FirstOrDefault(p => p.Name == "False");
        
        var trueStatements = GetStatementsForConnectedNodes(truePin);
        var falseStatements = GetStatementsForConnectedNodes(falsePin);
        
        var ifStatement = IfStatement(
            conditionExpr,
            Block(trueStatements),
            falseStatements.Any() ? ElseClause(Block(falseStatements)) : null
        );
        
        return new List<StatementSyntax> { ifStatement };
    }
    
    /// <summary>
    /// Generates for loop for ForLoop node
    /// </summary>
    private List<StatementSyntax> GenerateForLoopNode(Node node)
    {
        var startPin = node.InputPins.FirstOrDefault(p => p.Name == "Start");
        var endPin = node.InputPins.FirstOrDefault(p => p.Name == "End");
        var loopBodyPin = node.OutputPins.FirstOrDefault(p => p.Name == "Loop Body");
        
        if (startPin == null || endPin == null)
            return new List<StatementSyntax>();
            
        var startExpr = GetExpressionForPin(startPin);
        var endExpr = GetExpressionForPin(endPin);
        
        var indexVarName = GetTempVariableName();
        var indexVar = IdentifierName(indexVarName);
        
        var loopStatements = GetStatementsForConnectedNodes(loopBodyPin);
        
        var forStatement = ForStatement(Block(loopStatements))
            .WithDeclaration(
                VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword)))
                    .AddVariables(VariableDeclarator(indexVarName).WithInitializer(EqualsValueClause(startExpr))))
            .WithCondition(
                BinaryExpression(SyntaxKind.LessThanExpression, indexVar, endExpr))
            .WithIncrementors(
                SingletonSeparatedList<ExpressionSyntax>(
                    PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, indexVar)));
        
        return new List<StatementSyntax> { forStatement };
    }
    
    /// <summary>
    /// Generates assignment for SetVariable node
    /// </summary>
    private List<StatementSyntax> GenerateSetVariableNode(Node node)
    {
        if (!node.Properties.TryGetValue("VariableName", out var varNameObj) || varNameObj is not string varName)
            return new List<StatementSyntax>();
            
        var valuePin = node.InputPins.FirstOrDefault(p => p.Kind == PinKind.Data);
        if (valuePin == null)
            return new List<StatementSyntax>();
            
        var valueExpr = GetExpressionForPin(valuePin);
        
        var assignment = ExpressionStatement(
            AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                IdentifierName(varName),
                valueExpr));
        
        return new List<StatementSyntax> { assignment };
    }
    
    /// <summary>
    /// Generates Console.WriteLine for Print node
    /// </summary>
    private List<StatementSyntax> GeneratePrintNode(Node node)
    {
        var valuePin = node.InputPins.FirstOrDefault(p => p.Name == "Value");
        if (valuePin == null)
            return new List<StatementSyntax>();
            
        var valueExpr = GetExpressionForPin(valuePin);
        
        var printStatement = ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Console"),
                    IdentifierName("WriteLine")))
            .AddArgumentListArguments(Argument(valueExpr)));
        
        return new List<StatementSyntax> { printStatement };
    }
    
    /// <summary>
    /// Generates method invocation for MethodCall node
    /// </summary>
    private List<StatementSyntax> GenerateMethodCallNode(CSharpVisualScripting.Nodes.Functions.MethodCallNode node)
    {
        var methodInfo = node.MethodInfo;
        var arguments = new List<ArgumentSyntax>();
        
        // Build arguments
        foreach (var param in methodInfo.GetParameters())
        {
            var pin = node.InputPins.FirstOrDefault(p => p.Name == param.Name);
            if (pin != null)
            {
                var argExpr = GetExpressionForPin(pin);
                arguments.Add(Argument(argExpr));
            }
        }
        
        ExpressionSyntax invocationExpr;
        
        if (methodInfo.IsStatic)
        {
            // Static method call: TypeName.MethodName(args)
            invocationExpr = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseName(methodInfo.DeclaringType!.FullName!),
                    IdentifierName(methodInfo.Name)))
                .WithArgumentList(ArgumentList(SeparatedList(arguments)));
        }
        else
        {
            // Instance method call: target.MethodName(args)
            var targetPin = node.InputPins.FirstOrDefault(p => p.Name == "Target");
            var targetExpr = targetPin != null ? GetExpressionForPin(targetPin) : IdentifierName("this");
            
            invocationExpr = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    targetExpr,
                    IdentifierName(methodInfo.Name)))
                .WithArgumentList(ArgumentList(SeparatedList(arguments)));
        }
        
        // If method returns value, store it
        if (methodInfo.ReturnType != typeof(void))
        {
            var resultVarName = GetTempVariableName();
            _nodeVariableNames[node.Id] = resultVarName;
            
            var declaration = LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName(methodInfo.ReturnType.Name))
                    .AddVariables(
                        VariableDeclarator(resultVarName)
                            .WithInitializer(EqualsValueClause(invocationExpr))));
            
            return new List<StatementSyntax> { declaration };
        }
        
        return new List<StatementSyntax> { ExpressionStatement(invocationExpr) };
    }
    
    /// <summary>
    /// Gets the expression for a pin (either from connected node or default value)
    /// </summary>
    private ExpressionSyntax GetExpressionForPin(Pin pin)
    {
        // Find connection to this pin
        var connection = _graph.Connections.FirstOrDefault(c => c.TargetPinId == pin.Id);
        if (connection?.SourcePin != null)
        {
            var sourceNode = _graph.Nodes.FirstOrDefault(n => n.Id == connection.SourcePin.NodeId);
            if (sourceNode != null)
            {
                // If source node has a stored variable, use it
                if (_nodeVariableNames.TryGetValue(sourceNode.Id, out var varName))
                {
                    return IdentifierName(varName);
                }
                
                // For literal nodes, use the value directly
                if (sourceNode is CSharpVisualScripting.Nodes.Variables.LiteralNode)
                {
                    if (sourceNode.Properties.TryGetValue("Value", out var value))
                    {
                        return CreateLiteralExpression(value, pin.DataType);
                    }
                }
                
                // For get variable nodes
                if (sourceNode is CSharpVisualScripting.Nodes.Variables.GetVariableNode)
                {
                    if (sourceNode.Properties.TryGetValue("VariableName", out var varNameObj) && varNameObj is string varName2)
                    {
                        return IdentifierName(varName2);
                    }
                }
            }
        }
        
        // Use default value
        return CreateLiteralExpression(pin.DefaultValue, pin.DataType);
    }
    
    /// <summary>
    /// Creates a literal expression from a value
    /// </summary>
    private ExpressionSyntax CreateLiteralExpression(object? value, Type? type)
    {
        if (value == null)
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
            
        return value switch
        {
            int i => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i)),
            double d => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(d)),
            float f => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(f)),
            bool b => LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
            string s => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(s)),
            _ => LiteralExpression(SyntaxKind.DefaultLiteralExpression)
        };
    }
    
    /// <summary>
    /// Gets statements for nodes connected to a pin
    /// </summary>
    private List<StatementSyntax> GetStatementsForConnectedNodes(Pin? pin)
    {
        if (pin == null)
            return new List<StatementSyntax>();
            
        var statements = new List<StatementSyntax>();
        var connections = _graph.GetConnectionsFromPin(pin.Id).Where(c => c.SourcePinId == pin.Id);
        
        foreach (var connection in connections)
        {
            var targetNode = _graph.Nodes.FirstOrDefault(n => n.GetAllPins().Any(p => p.Id == connection.TargetPinId));
            if (targetNode != null)
            {
                statements.AddRange(GenerateStatementsForNode(targetNode));
            }
        }
        
        return statements;
    }
    
    private string GetTempVariableName()
    {
        return $"temp{_tempVariableCounter++}";
    }
}
