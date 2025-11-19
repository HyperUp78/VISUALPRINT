using System.Globalization;
using System.Text;
using CSharpVisualScripting.Core.Models;
using CSharpVisualScripting.Nodes.Functions;
using CSharpVisualScripting.Nodes.Variables;
using System.Linq;

namespace CSharpVisualScripting.CodeGen;

public class GraphCodeGenerator
{
    private readonly Graph _graph;
    private readonly Dictionary<Guid, string> _pinExpressions = new();
    private readonly Dictionary<Guid, Connection> _connectionByTarget;

    public GraphCodeGenerator(Graph graph)
    {
        _graph = graph;
        _connectionByTarget = graph.Connections
            .GroupBy(c => c.TargetPinId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public string Generate(BuildTarget target = BuildTarget.Dll)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("namespace Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public class VisualprintGeneratedClass");
        sb.AppendLine("    {");
        sb.AppendLine("        public void Execute()");
        sb.AppendLine("        {");

        EmitLiteralNodes(sb);
        EmitExecutionNodes(sb);

        sb.AppendLine("        }");
        sb.AppendLine("    }");

        if (target != BuildTarget.Dll)
        {
            sb.AppendLine("    public static class Program");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void Main(string[] args)");
            sb.AppendLine("        {");
            sb.AppendLine("            new VisualprintGeneratedClass().Execute();");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void EmitLiteralNodes(StringBuilder sb)
    {
        foreach (var literal in _graph.Nodes.OfType<LiteralNode>())
        {
            if (!literal.OutputPins.Any())
                continue;

            var literalType = literal.OutputPins.First().DataType ?? typeof(object);
            var value = literal.Properties.TryGetValue("Value", out var stored)
                ? FormatLiteral(stored, literalType)
                : GetDefaultExpression(literalType);
            var variableName = $"literal_{literal.Id.ToString("N")[..8]}";
            // If the value is null, we must use an explicit type instead of 'var'
            var decl = string.Equals(value, "null", StringComparison.Ordinal)
                ? GetTypeName(literalType)
                : "var";
            sb.AppendLine($"            {decl} {variableName} = {value};");
            RegisterExpression(literal.OutputPins.First().Id, variableName);
        }
    }

    private void EmitExecutionNodes(StringBuilder sb)
    {
        var ordered = _graph.GetExecutionOrder();
        foreach (var node in ordered)
        {
            switch (node)
            {
                case MethodCallNode methodNode:
                    EmitMethodCall(sb, methodNode);
                    break;
                case PrintNode printNode:
                    EmitPrint(sb, printNode);
                    break;
                default:
                    break;
            }
        }
    }

    private void EmitPrint(StringBuilder sb, PrintNode printNode)
    {
        var valuePin = printNode.InputPins.FirstOrDefault(p => p.Kind == PinKind.Data);
        var expr = ResolveInputExpression(valuePin, typeof(object)) ?? "string.Empty";
        sb.AppendLine($"            Console.WriteLine({expr});");
    }

    private void EmitMethodCall(StringBuilder sb, MethodCallNode node)
    {
        var method = node.MethodInfo;
        if (method == null)
            return;

        var dataInputs = node.InputPins.Where(p => p.Kind == PinKind.Data).ToList();
        Pin? targetPin = null;
        if (!method.IsStatic && dataInputs.Count > 0)
        {
            targetPin = dataInputs[0];
            dataInputs.RemoveAt(0);
        }

        var parameters = method.GetParameters();
        var args = new List<string>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var pin = i < dataInputs.Count ? dataInputs[i] : null;
            args.Add(ResolveInputExpression(pin, parameters[i].ParameterType)
                     ?? (parameters[i].HasDefaultValue
                        ? FormatLiteral(parameters[i].DefaultValue, parameters[i].ParameterType)
                        : GetDefaultExpression(parameters[i].ParameterType)));
        }

        var callTarget = method.IsStatic
            ? GetTypeName(method.DeclaringType)
            : ResolveInstanceExpression(targetPin, method.DeclaringType);

        var invocation = method.IsStatic
            ? $"{callTarget}.{method.Name}({string.Join(", ", args)})"
            : $"{callTarget}.{method.Name}({string.Join(", ", args)})";

        if (method.ReturnType == typeof(void))
        {
            sb.AppendLine($"            {invocation};");
        }
        else
        {
            var resultVar = $"result_{node.Id.ToString("N")[..8]}";
            sb.AppendLine($"            var {resultVar} = {invocation};");
            var returnPin = node.OutputPins.FirstOrDefault(p => p.Kind == PinKind.Data);
            if (returnPin != null)
            {
                RegisterExpression(returnPin.Id, resultVar);
            }
        }
    }

    private string ResolveInstanceExpression(Pin? targetPin, Type? declaringType)
    {
        var expr = ResolveInputExpression(targetPin, declaringType ?? typeof(object));
        if (!string.IsNullOrWhiteSpace(expr))
        {
            return expr;
        }

        if (declaringType != null && declaringType.GetConstructor(Type.EmptyTypes) != null)
        {
            return $"new {GetTypeName(declaringType)}()";
        }

        return $"({GetTypeName(declaringType ?? typeof(object))})null";
    }

    private string? ResolveInputExpression(Pin? pin, Type expectedType)
    {
        if (pin == null)
            return null;

        if (_connectionByTarget.TryGetValue(pin.Id, out var connection))
        {
            if (_pinExpressions.TryGetValue(connection.SourcePinId, out var expression))
            {
                return expression;
            }
        }

        return null;
    }

    private void RegisterExpression(Guid pinId, string expression)
    {
        _pinExpressions[pinId] = expression;
    }

    private static string FormatLiteral(object? value, Type type)
    {
        if (value == null)
            return "null";

        if (type == typeof(string))
        {
            return $"\"{value.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }

        if (type == typeof(char))
        {
            return $"'{value}'";
        }

        if (type == typeof(bool))
        {
            return (bool)value ? "true" : "false";
        }

        if (type == typeof(float))
        {
            return ((float)value).ToString(CultureInfo.InvariantCulture) + "f";
        }

        if (type == typeof(double))
        {
            return ((double)value).ToString(CultureInfo.InvariantCulture) + "d";
        }

        if (type == typeof(decimal))
        {
            return ((decimal)value).ToString(CultureInfo.InvariantCulture) + "m";
        }

        if (type == typeof(long))
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture) + "L";
        }

        if (type.IsEnum)
        {
            return $"{GetTypeName(type)}.{Enum.GetName(type, value)}";
        }

        if (type == typeof(Guid))
        {
            return $"new {GetTypeName(type)}(\"{value}\")";
        }

        if (type.IsPrimitive || type == typeof(int) || type == typeof(short) || type == typeof(byte))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? $"default({GetTypeName(type)})";
        }

        return $"default({GetTypeName(type)})";
    }

    private static string GetDefaultExpression(Type type)
        => type.IsValueType ? $"default({GetTypeName(type)})" : "null";

    private static string GetTypeName(Type? type)
    {
        if (type == null)
            return "object";

        return type.FullName?.Replace('+', '.') ?? type.Name;
    }
}
