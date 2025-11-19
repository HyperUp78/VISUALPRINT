using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Reflection;
using CSharpVisualScripting.Core.Models;
using CSharpVisualScripting.CodeGen;
using CSharpVisualScripting.Nodes.Functions;
using CSharpVisualScripting.Nodes.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpVisualScripting.UI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _nodeSearchText = string.Empty;
    private string _outputText = string.Empty;
    private Graph _graph = new();
    private CompilationResult? _lastCompilation;
    private string _buildOutputDrive = "C:";
    private string _buildOutputDirectory = "GREENPRINTS\\Builds";
    private BuildTarget _buildTarget = BuildTarget.Dll;
    
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<NodeCategoryViewModel> NodeCategories { get; } = new();
    public ObservableCollection<NodeCategoryViewModel> FilteredNodeCategories { get; } = new();
    public ObservableCollection<PropertyViewModel> SelectedNodeProperties { get; } = new();
    public ObservableCollection<NodeViewModel> SelectedNodes { get; } = new();
    public ObservableCollection<ExternalLibraryViewModel> ExternalLibraries { get; } = new();
    public ObservableCollection<ClassViewModel> Classes { get; } = new();
    private ClassViewModel? _selectedClass;
    public ClassViewModel? SelectedClass 
    { 
        get => _selectedClass; 
        set 
        { 
            _selectedClass = value; 
            // Clear node selection when selecting a class to avoid confusion
            if (value != null)
            {
                ClearSelection();
            }
            OnPropertyChanged(); 
        } 
    }
    public bool IsInSubgraph => _contextStack.Count > 0;
    public string CurrentContextTitle => IsInSubgraph ? ($"Class: {_currentContext?.Name ?? ""}") : "Root";
    private ExternalLibraryViewModel? _selectedExternalLibrary;
    public ExternalLibraryViewModel? SelectedExternalLibrary
    {
        get => _selectedExternalLibrary;
        set
        {
            if (_selectedExternalLibrary != value)
            {
                _selectedExternalLibrary = value;
                OnPropertyChanged();
            }
        }
    }
    
    private PendingConnectionViewModel? _pendingConnection;
    public PendingConnectionViewModel? PendingConnection
    {
        get => _pendingConnection;
        set { _pendingConnection = value; OnPropertyChanged(); }
    }

    private Point _nextNodePosition = new(150, 150);
    
    public ICommand CompileCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand SaveDetailsCommand { get; }
    
    public string NodeSearchText
    {
        get => _nodeSearchText;
        set
        {
            if (_nodeSearchText != value)
            {
                _nodeSearchText = value;
                OnPropertyChanged();
                RefreshNodeFilter();
            }
        }
    }
    
    public string OutputText
    {
        get => _outputText;
        set { _outputText = value; OnPropertyChanged(); }
    }

    public string BuildOutputDrive
    {
        get => _buildOutputDrive;
        set
        {
            if (_buildOutputDrive != value)
            {
                _buildOutputDrive = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BuildOutputPath));
            }
        }
    }

    public string BuildOutputDirectory
    {
        get => _buildOutputDirectory;
        set
        {
            if (_buildOutputDirectory != value)
            {
                _buildOutputDirectory = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BuildOutputPath));
            }
        }
    }

    public string BuildOutputPath => ComposeOutputPath();
    public BuildTarget BuildTarget
    {
        get => _buildTarget;
        set { if (_buildTarget != value) { _buildTarget = value; OnPropertyChanged(); } }
    }

    public ICommand SaveGraphCommand { get; }
    
    public MainViewModel()
    {
        try
        {
            CompileCommand = new RelayCommand(Compile);
            RunCommand = new RelayCommand(Run, () => _lastCompilation?.Success == true);
            DisconnectCommand = new RelayCommand<ConnectionViewModel>(Disconnect);
            SaveGraphCommand = new RelayCommand(SaveGraphInteractive);
            GoBackCommand = new RelayCommand(ExitCurrentClass, () => IsInSubgraph);
            SaveDetailsCommand = new RelayCommand(SaveSelectedNodeDetails);

            InitializeNodeLibrary();
            CreateSampleGraph();
            RefreshNodeFilter();

            // Initialize context management with root context
            _currentContext = new GraphContext("Root")
            {
                Graph = _graph,
                Nodes = new List<NodeViewModel>(Nodes),
                Connections = new List<ConnectionViewModel>(Connections)
            };
            OnPropertyChanged(nameof(IsInSubgraph));
            OnPropertyChanged(nameof(CurrentContextTitle));
        }
        catch (Exception ex)
        {
            OutputText = $"Initialization error: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private readonly Stack<GraphContext> _contextStack = new();
    private GraphContext? _currentContext;

    public void CreateNewClass()
    {
        // Generate unique name
        int i = 1;
        string name;
        var existing = new HashSet<string>(Classes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        do { name = $"Class{i++}"; } while (existing.Contains(name));

        var ctx = new GraphContext(name)
        {
            Graph = new Graph(),
            Nodes = new List<NodeViewModel>(),
            Connections = new List<ConnectionViewModel>()
        };
        var cls = new ClassViewModel(name, ctx);
        Classes.Add(cls);
        SelectedClass = cls;
        OutputText += $"\n✓ Created class '{name}'. Double-click to open.";
    }

    public void EnterClass(ClassViewModel cls)
    {
        if (cls == null) return;
        SaveCurrentContext();
        _contextStack.Push(_currentContext!);
        _currentContext = cls.Context;
        RestoreContext(_currentContext);
        OnPropertyChanged(nameof(IsInSubgraph));
        OnPropertyChanged(nameof(CurrentContextTitle));
        (GoBackCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void ExitCurrentClass()
    {
        if (_contextStack.Count == 0) return;
        SaveCurrentContext();
        _currentContext = _contextStack.Pop();
        RestoreContext(_currentContext);
        OnPropertyChanged(nameof(IsInSubgraph));
        OnPropertyChanged(nameof(CurrentContextTitle));
        (GoBackCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void SaveCurrentContext()
    {
        if (_currentContext == null) return;
        _currentContext.Graph = _graph;
        _currentContext.Nodes = Nodes.ToList();
        _currentContext.Connections = Connections.ToList();
    }

    private void RestoreContext(GraphContext? ctx)
    {
        if (ctx == null) return;
        _graph = ctx.Graph ?? new Graph();
        Nodes.Clear();
        foreach (var n in ctx.Nodes) Nodes.Add(n);
        Connections.Clear();
        foreach (var c in ctx.Connections) Connections.Add(c);
        SelectedNodes.Clear();
        SelectedNodeProperties.Clear();
        PendingConnection = null;
        RefreshDetailsPanel();
    }

    private void RefreshDetailsPanel()
    {
        SelectedNodeProperties.Clear();
        if (SelectedNodes.Count != 1)
            return;

        var vm = SelectedNodes[0];
        var node = vm.Node;

        // Core fields
        SelectedNodeProperties.Add(new PropertyViewModel { Name = "Title", Key = "__Title", Value = node.Title, ValueType = typeof(string), IsEditable = true });
        SelectedNodeProperties.Add(new PropertyViewModel { Name = "Description", Key = "__Description", Value = node.Description, ValueType = typeof(string), IsEditable = true });

        foreach (var kvp in node.Properties)
        {
            var t = kvp.Value?.GetType();
            // Ensure correct editor for Literal nodes even when Value is null
            if (t == null && node is CSharpVisualScripting.Nodes.Variables.LiteralNode && kvp.Key == "Value")
            {
                t = node.OutputPins.FirstOrDefault(p => p.Name == "Value")?.DataType ?? typeof(string);
            }
            var editable = t == typeof(string) || t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(bool);
            SelectedNodeProperties.Add(new PropertyViewModel
            {
                Name = kvp.Key,
                Key = kvp.Key,
                Value = kvp.Value is Type ty ? (object?)ty.FullName : kvp.Value,
                ValueType = t,
                IsEditable = editable
            });
        }

        if (node is CSharpVisualScripting.Nodes.Functions.MethodCallNode m)
        {
            SelectedNodeProperties.Add(new PropertyViewModel
            {
                Name = "Method",
                Key = "__Method",
                Value = m.MethodInfo.ToString(),
                ValueType = typeof(string),
                IsEditable = false
            });
        }
    }

    private void SaveSelectedNodeDetails()
    {
        if (SelectedNodes.Count != 1)
            return;

        var vm = SelectedNodes[0];
        var node = vm.Node;

        foreach (var prop in SelectedNodeProperties)
        {
            if (prop.Key == "__Title")
            {
                node.Title = Convert.ToString(prop.Value) ?? node.Title;
                vm.NotifyTitleChanged();
                continue;
            }
            if (prop.Key == "__Description")
            {
                node.Description = Convert.ToString(prop.Value) ?? node.Description;
                continue;
            }
            if (string.IsNullOrEmpty(prop.Key) || !node.Properties.ContainsKey(prop.Key))
                continue;

            var original = node.Properties[prop.Key];
            var targetType = original?.GetType();
            // If original is null (e.g., new Literal Value), infer from UI-provided ValueType
            if (targetType == null)
            {
                targetType = prop.ValueType;
                if (targetType == null && node is CSharpVisualScripting.Nodes.Variables.LiteralNode && prop.Key == "Value")
                {
                    targetType = node.OutputPins.FirstOrDefault(p => p.Name == "Value")?.DataType ?? typeof(string);
                }
            }

            object? converted = prop.Value;
            try
            {
                if (targetType == typeof(string))
                    converted = Convert.ToString(prop.Value);
                else if (targetType == typeof(int))
                    converted = Convert.ToInt32(prop.Value);
                else if (targetType == typeof(float))
                    converted = Convert.ToSingle(prop.Value);
                else if (targetType == typeof(double))
                    converted = Convert.ToDouble(prop.Value);
                else if (targetType == typeof(bool))
                    converted = Convert.ToBoolean(prop.Value);
                else if (prop.Value != null && targetType != null)
                    converted = System.Convert.ChangeType(prop.Value, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }

            node.Properties[prop.Key] = converted!;

            if (node is CSharpVisualScripting.Nodes.Variables.LiteralNode && prop.Key == "Value")
            {
                var valPin = node.OutputPins.FirstOrDefault(p => p.Name == "Value");
                if (valPin != null)
                {
                    valPin.DefaultValue = converted;
                }
            }
        }

        OutputText += "\n✓ Node details saved.";
    }

    private void SaveGraphInteractive()
    {
        // Placeholder – actual dialog handled in code-behind; command remains for binding/testing.
    }
    
    private void InitializeNodeLibrary()
    {
        // Flow Control Nodes
        var flowCategory = new NodeCategoryViewModel("Flow Control");
        flowCategory.Nodes.Add(new NodeTemplateViewModel("Start", "Entry point for execution", typeof(CSharpVisualScripting.Nodes.Flow.StartNode)));
        flowCategory.Nodes.Add(new NodeTemplateViewModel("Branch", "If/else conditional", typeof(CSharpVisualScripting.Nodes.Flow.BranchNode)));
        flowCategory.Nodes.Add(new NodeTemplateViewModel("For Loop", "Iterate range", typeof(CSharpVisualScripting.Nodes.Flow.ForLoopNode)));
        flowCategory.Nodes.Add(new NodeTemplateViewModel("While Loop", "Loop while condition", typeof(CSharpVisualScripting.Nodes.Flow.WhileLoopNode)));
        flowCategory.Nodes.Add(new NodeTemplateViewModel("Sequence", "Execute in order", typeof(CSharpVisualScripting.Nodes.Flow.SequenceNode)));
        NodeCategories.Add(flowCategory);
        
        // Operators
        var opCategory = new NodeCategoryViewModel("Operators");
        opCategory.Nodes.Add(new NodeTemplateViewModel("Add", "Addition (+)", typeof(CSharpVisualScripting.Nodes.Operators.AddNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Subtract", "Subtraction (-)", typeof(CSharpVisualScripting.Nodes.Operators.SubtractNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Multiply", "Multiplication (*)", typeof(CSharpVisualScripting.Nodes.Operators.MultiplyNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Divide", "Division (/)", typeof(CSharpVisualScripting.Nodes.Operators.DivideNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Equals", "Equality (==)", typeof(CSharpVisualScripting.Nodes.Operators.EqualsNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Greater Than", "Compare (>)", typeof(CSharpVisualScripting.Nodes.Operators.GreaterThanNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Less Than", "Compare (<)", typeof(CSharpVisualScripting.Nodes.Operators.LessThanNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("And", "Logical AND", typeof(CSharpVisualScripting.Nodes.Operators.AndNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Or", "Logical OR", typeof(CSharpVisualScripting.Nodes.Operators.OrNode)));
        opCategory.Nodes.Add(new NodeTemplateViewModel("Not", "Logical NOT", typeof(CSharpVisualScripting.Nodes.Operators.NotNode)));
        NodeCategories.Add(opCategory);
        
        // Variables & Literals
        var varCategory = new NodeCategoryViewModel("Variables");
        varCategory.Nodes.Add(new NodeTemplateViewModel("Int Literal", "Constant integer", typeof(CSharpVisualScripting.Nodes.Variables.LiteralNode), typeof(int)));
        varCategory.Nodes.Add(new NodeTemplateViewModel("Float Literal", "Constant float", typeof(CSharpVisualScripting.Nodes.Variables.LiteralNode), typeof(float)));
        varCategory.Nodes.Add(new NodeTemplateViewModel("String Literal", "Constant string", typeof(CSharpVisualScripting.Nodes.Variables.LiteralNode), typeof(string)));
        varCategory.Nodes.Add(new NodeTemplateViewModel("Bool Literal", "Constant boolean", typeof(CSharpVisualScripting.Nodes.Variables.LiteralNode), typeof(bool)));
        NodeCategories.Add(varCategory);
        
        // Debug
        var debugCategory = new NodeCategoryViewModel("Debug");
        debugCategory.Nodes.Add(new NodeTemplateViewModel("Print", "Console output", typeof(CSharpVisualScripting.Nodes.Functions.PrintNode)));
        NodeCategories.Add(debugCategory);

        // Discover COM/Interop assemblies in 'com' folder and add method nodes
        AddComLibraryNodes();
        RefreshNodeFilter();
    }

    private void AddComLibraryNodes()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var comDir = System.IO.Path.Combine(baseDir, "com");
            if (!System.IO.Directory.Exists(comDir))
                return;

            var comCategory = new NodeCategoryViewModel("COM Libraries");

            foreach (var dll in System.IO.Directory.EnumerateFiles(comDir, "*.dll", System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetExportedTypes())
                    {
                        if (!type.IsClass) continue;

                        // Methods (public static and instance)
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                        {
                            if (method.IsSpecialName) continue;

                            var title = method.IsStatic ? $"{type.Name}.{method.Name}" : $"{type.Name}.{method.Name} (inst)";
                            var desc = $"COM: {type.FullName} in {System.IO.Path.GetFileName(dll)}";
                            comCategory.Nodes.Add(NodeTemplateViewModel.ForMethod(title, desc, method));
                        }
                    }
                }
                catch { /* ignore per-file issues */ }
            }

            if (comCategory.Nodes.Count > 0)
                NodeCategories.Add(comCategory);
        }
        catch { /* best-effort */ }
    }

    public bool TryAddExternalLibrary(string filePath, bool suppressOutput = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                if (!suppressOutput)
                {
                    OutputText += $"\n✗ Library not found: {filePath}";
                }
                return false;
            }

            if (ExternalLibraries.Any(lib => string.Equals(lib.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                if (!suppressOutput)
                {
                    OutputText += $"\n⚠ Library already loaded: {System.IO.Path.GetFileName(filePath)}";
                }
                return false;
            }

            var ext = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant();
            var displayName = System.IO.Path.GetFileNameWithoutExtension(filePath) ?? "Library";

            Assembly assembly;
            if (ext == ".dll")
            {
                assembly = Assembly.LoadFrom(filePath);
            }
            else if (ext == ".cs")
            {
                var result = CompileSourceToAssembly(new[] { filePath }, displayName);
                if (!result.Success || result.Assembly == null)
                {
                    if (!suppressOutput)
                    {
                        OutputText += $"\n✗ Failed to compile '{System.IO.Path.GetFileName(filePath)}'.";
                        foreach (var m in result.Messages.Take(20)) OutputText += $"\n  - {m}";
                    }
                    return false;
                }
                assembly = result.Assembly;
            }
            else
            {
                if (!suppressOutput)
                {
                    OutputText += $"\n✗ Unsupported library type: {System.IO.Path.GetFileName(filePath)}";
                }
                return false;
            }

            var category = CreateLibraryCategory(assembly, displayName);
            if (category.Nodes.Count == 0 && !suppressOutput)
            {
                OutputText += $"\n⚠ No supported nodes discovered in {displayName}.";
            }

            NodeCategories.Add(category);
            var libraryVm = new ExternalLibraryViewModel(displayName, filePath, category, assembly);
            ExternalLibraries.Add(libraryVm);
            RefreshNodeFilter();

            if (!suppressOutput)
            {
                OutputText += $"\n✓ Loaded API library '{displayName}'.";
            }

            return true;
        }
        catch (Exception ex)
        {
            if (!suppressOutput)
            {
                OutputText += $"\n✗ Failed to load library: {ex.Message}";
            }
            return false;
        }
    }

    public void RemoveExternalLibrary(ExternalLibraryViewModel? library)
    {
        if (library == null)
            return;

        if (ExternalLibraries.Remove(library))
        {
            NodeCategories.Remove(library.Category);
            if (SelectedExternalLibrary == library)
            {
                SelectedExternalLibrary = null;
            }
            RefreshNodeFilter();
            OutputText += $"\n✓ Unloaded API library '{library.Name}'.";
        }
    }

    public bool TryAddExternalSourceLibrary(string[] filePaths, string? name = null, bool suppressOutput = false)
    {
        try
        {
            var sources = filePaths.Where(f => string.Equals(Path.GetExtension(f), ".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (sources.Length == 0)
            {
                if (!suppressOutput) OutputText += "\n✗ No .cs files selected.";
                return false;
            }

            // Simple duplicate guard: treat the group as a single identity
            var identity = string.Join("|", sources.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            if (ExternalLibraries.Any(lib => string.Equals(lib.FilePath, identity, StringComparison.OrdinalIgnoreCase)))
            {
                if (!suppressOutput)
                {
                    OutputText += "\n⚠ Source library already loaded.";
                }
                return false;
            }

            var displayName = !string.IsNullOrWhiteSpace(name)
                ? name!
                : (sources.Length == 1
                    ? Path.GetFileNameWithoutExtension(sources[0])
                    : $"{Path.GetFileNameWithoutExtension(sources[0])}+{sources.Length - 1}");

            var compileResult = CompileSourceToAssembly(sources, displayName);
            if (!compileResult.Success || compileResult.Assembly == null)
            {
                if (!suppressOutput)
                {
                    OutputText += $"\n✗ Failed to compile source library '{displayName}'.";
                    foreach (var line in compileResult.Messages.Take(50))
                        OutputText += $"\n  - {line}";
                }
                return false;
            }

            var category = CreateLibraryCategory(compileResult.Assembly, displayName);
            NodeCategories.Add(category);
            var libraryVm = new ExternalLibraryViewModel(displayName, identity, category, compileResult.Assembly);
            ExternalLibraries.Add(libraryVm);
            RefreshNodeFilter();
            if (!suppressOutput)
            {
                OutputText += $"\n✓ Loaded source library '{displayName}' ({sources.Length} file(s)).";
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!suppressOutput) OutputText += $"\n✗ Failed to load source library: {ex.Message}";
            return false;
        }
    }

    private NodeCategoryViewModel CreateLibraryCategory(Assembly assembly, string displayName)
    {
        var category = new NodeCategoryViewModel(displayName);

        Type[] exportedTypes;
        try
        {
            exportedTypes = assembly.GetExportedTypes();
        }
        catch
        {
            exportedTypes = Array.Empty<Type>();
        }

        foreach (var type in exportedTypes)
        {
            if (!type.IsClass)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName)
                    continue;

                var title = method.IsStatic ? $"{type.Name}.{method.Name}" : $"{type.Name}.{method.Name} (inst)";
                var description = $"{assembly.GetName().Name}: {type.FullName}";
                category.Nodes.Add(NodeTemplateViewModel.ForMethod(title, description, method));
            }
        }

        return category;
    }
    
    private (bool Success, Assembly? Assembly, string[] Messages) CompileSourceToAssembly(string[] sourceFiles, string assemblyName)
    {
        try
        {
            var syntaxTrees = sourceFiles.Select(path =>
            {
                var text = File.ReadAllText(path);
                return CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(languageVersion: LanguageVersion.Preview), path);
            }).ToList();

            var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty;
            var references = tpa.Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => MetadataReference.CreateFromFile(p))
                .Cast<MetadataReference>()
                .ToList();

            var currentAssemblies = new[]
            {
                typeof(object).Assembly,
                typeof(CSharpVisualScripting.Core.Models.Node).Assembly,
                typeof(MainViewModel).Assembly
            };
            foreach (var asm in currentAssemblies.Distinct())
            {
                try { references.Add(MetadataReference.CreateFromFile(asm.Location)); } catch { }
            }

            var compilation = CSharpCompilation.Create(
                string.IsNullOrWhiteSpace(assemblyName) ? "DynamicNodes" : assemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            var messages = emitResult.Diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Info)
                .Select(d =>
                {
                    var loc = d.Location;
                    var lineSpan = loc.IsInSource ? loc.GetLineSpan() : default;
                    var file = lineSpan.Path ?? "<unknown>";
                    var line = lineSpan.StartLinePosition.Line + 1;
                    var col = lineSpan.StartLinePosition.Character + 1;
                    return $"{d.Severity}: {d.Id}: {d.GetMessage()} (at {System.IO.Path.GetFileName(file)}:{line},{col})";
                })
                .ToArray();

            if (!emitResult.Success)
            {
                return (false, null, messages);
            }

            ms.Position = 0;
            var asmLoaded = Assembly.Load(ms.ToArray());
            return (true, asmLoaded, messages);
        }
        catch (Exception ex)
        {
            return (false, null, new[] { ex.Message });
        }
    }
    public NodeViewModel CreateNodeFromTemplate(NodeTemplateViewModel template, Point? desiredPosition = null)
    {
        Node node;
        
        if (template.AdditionalMethod is MethodInfo mi)
        {
            node = (Node)Activator.CreateInstance(template.NodeType, mi)!;
        }
        else if (template.AdditionalType != null)
        {
            // For literal nodes, construct with (Type, object?) or fallback to (Type)
            var ctorArgs = new object?[] { template.AdditionalType, null };
            node = TryCreateNode(template.NodeType, ctorArgs)
                   ?? TryCreateNode(template.NodeType, new object?[] { template.AdditionalType })
                   ?? throw new MissingMethodException($"No matching constructor found for {template.NodeType.FullName} with (Type[, object?])");
        }
        else
        {
            node = TryCreateNode(template.NodeType, Array.Empty<object?>())
                   ?? TryCreateNode(template.NodeType, new object?[] { null })
                   ?? throw new MissingMethodException($"No constructor found for {template.NodeType.FullName}");
        }

        node.Position = desiredPosition ?? _nextNodePosition;
        UpdateNextNodePosition();
        _graph.AddNode(node);
        
        var nodeVm = new NodeViewModel(node);
        Nodes.Add(nodeVm);
        return nodeVm;
    }

    internal static Node? TryCreateNode(Type nodeType, object?[] args)
    {
        try
        {
            var instance = Activator.CreateInstance(nodeType, args);
            return instance as Node;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateNextNodePosition()
    {
        const double step = 40;
        _nextNodePosition = new Point(_nextNodePosition.X + step, _nextNodePosition.Y + step);
        if (_nextNodePosition.X > 800 || _nextNodePosition.Y > 600)
        {
            _nextNodePosition = new Point(150, 150);
        }
    }
    
    private void CreateSampleGraph()
    {
        try
        {
            // Create a simple sample: Start -> Print "Hello World"
            var startNode = new CSharpVisualScripting.Nodes.Flow.StartNode { Position = new Point(100, 100) };
            var printNode = new CSharpVisualScripting.Nodes.Functions.PrintNode { Position = new Point(400, 100) };
            var stringLiteral = new CSharpVisualScripting.Nodes.Variables.LiteralNode(typeof(string), "Hello, Visual Scripting!") 
            { 
                Position = new Point(400, 200) 
            };
            
            _graph.AddNode(startNode);
            _graph.AddNode(printNode);
            _graph.AddNode(stringLiteral);
            
            Nodes.Add(new NodeViewModel(startNode));
            Nodes.Add(new NodeViewModel(printNode));
            Nodes.Add(new NodeViewModel(stringLiteral));
            
            OutputText = "Sample graph loaded successfully. Click Compile to generate C# code.";
        }
        catch (Exception ex)
        {
            OutputText = $"Error creating sample graph: {ex.Message}";
        }
    }
    
    private void Compile()
    {
        try
        {
            OutputText += "\nCompiling...\n";
            
            var codeGenerator = new GraphCodeGenerator(_graph);
            var generatedCode = codeGenerator.Generate(_buildTarget);
            OutputText += "\n=== Generated Code ===\n" + generatedCode + "\n";
            
            var compiler = new GraphCompiler();
            // Add references for any COM/interop assemblies found under 'com' folder
            try
            {
                var comDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "com");
                if (System.IO.Directory.Exists(comDir))
                {
                    foreach (var dll in System.IO.Directory.EnumerateFiles(comDir, "*.dll", System.IO.SearchOption.AllDirectories))
                    {
                        compiler.AddReference(dll);
                    }
                }
            }
            catch { }

            foreach (var library in ExternalLibraries)
            {
                if (library.CompiledAssembly != null)
                {
                    compiler.AddReference(library.CompiledAssembly);
                }
                else if (File.Exists(library.FilePath) && library.FilePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // Fallback for .dll files that weren't compiled by us
                    compiler.AddReference(library.FilePath);
                }
            }
            
            var desiredOutput = string.IsNullOrWhiteSpace(BuildOutputPath) ? null : BuildOutputPath;
            
            // Add debug info for console applications
            if (_buildTarget == BuildTarget.ExeConsole)
            {
                OutputText += $"→ Building console application to: {desiredOutput ?? "memory"}\n";
                OutputText += "→ Using self-contained publishing for standalone executable...\n";
            }
            
            _lastCompilation = compiler.Compile(generatedCode, assemblyName: "VisualprintGenerated", outputDirectory: desiredOutput, target: _buildTarget);
            
            if (_lastCompilation.Success)
            {
                OutputText += "\n✓ Compilation successful!\n";
                if (!string.IsNullOrWhiteSpace(desiredOutput))
                {
                    if (!string.IsNullOrWhiteSpace(_lastCompilation.AssemblyFilePath))
                    {
                        OutputText += $"→ Assembly written to {_lastCompilation.AssemblyFilePath}\n";
                        if (_buildTarget != BuildTarget.Dll)
                        {
                            // Check if the file actually exists and provide more detailed info
                            if (File.Exists(_lastCompilation.AssemblyFilePath))
                            {
                                var fileInfo = new FileInfo(_lastCompilation.AssemblyFilePath);
                                OutputText += $"→ Executable size: {fileInfo.Length:N0} bytes\n";
                                if (_buildTarget == BuildTarget.ExeConsole)
                                {
                                    OutputText += "→ Self-contained console application created\n";
                                }
                                OutputText += $"Run directly: \"{_lastCompilation.AssemblyFilePath}\"\n";
                            }
                            else
                            {
                                OutputText += $"⚠ Executable not found at expected location: {_lastCompilation.AssemblyFilePath}\n";
                                OutputText += "→ This might indicate a self-contained publishing failure\n";
                            }
                        }
                    }
                    else
                    {
                        OutputText += $"⚠ Unable to copy assembly to {desiredOutput}. Check permissions or path.\n";
                    }
                }
            }
            else
            {
                OutputText += "\n✗ Compilation failed:\n";
                foreach (var error in _lastCompilation.Errors)
                {
                    OutputText += $"  {error}\n";
                }
            }
        }
        catch (Exception ex)
        {
            OutputText += $"\n✗ Compilation Error: {ex.Message}\n{ex.StackTrace}\n";
        }
    }
    
    private void Run()
    {
        if (_lastCompilation?.Success != true)
        {
            OutputText += "\n✗ No successful compilation to run. Click Compile first.\n";
            return;
        }
            
        try
        {
            OutputText += "\n=== Execution ===\n";
            
            var instance = _lastCompilation.CreateInstance("Generated.VisualprintGeneratedClass");
            if (instance != null)
            {
                // Redirect console output
                var originalOutput = Console.Out;
                using var writer = new System.IO.StringWriter();
                Console.SetOut(writer);
                
                var executeMethod = instance.GetType().GetMethod("Execute");
                executeMethod?.Invoke(instance, null);
                
                Console.SetOut(originalOutput);
                var output = writer.ToString();
                OutputText += string.IsNullOrEmpty(output) ? "(No output)" : output;
                OutputText += "\n✓ Execution completed\n";
            }
            else
            {
                OutputText += "\n✗ Could not create instance of generated class\n";
            }
        }
        catch (Exception ex)
        {
            OutputText += $"\n✗ Execution error: {ex.Message}\n{ex.StackTrace}\n";
        }
    }
    
    private void Disconnect(ConnectionViewModel? connection)
    {
        if (connection != null)
        {
            Connections.Remove(connection);
            _graph.RemoveConnection(connection.Connection.Id);
        }
    }

    public bool TryBeginConnection(ConnectorViewModel connector)
    {
        if (connector == null)
            return false;

        PendingConnection = new PendingConnectionViewModel
        {
            Source = connector,
            TargetPosition = connector.Anchor
        };
        return true;
    }

    public void UpdatePendingConnection(Point position)
    {
        if (PendingConnection != null)
        {
            PendingConnection.TargetPosition = position;
        }
    }

    public void CancelPendingConnection()
    {
        PendingConnection = null;
    }

    public bool TryCompleteConnection(ConnectorViewModel target)
    {
        if (PendingConnection?.Source == null || target == null)
            return false;

        if (!TryOrderConnectors(PendingConnection.Source, target, out var sourceConnector, out var targetConnector))
        {
            CancelPendingConnection();
            return false;
        }

        if (!sourceConnector.Pin.CanConnectTo(targetConnector.Pin))
        {
            CancelPendingConnection();
            return false;
        }

        var connection = _graph.AddConnection(sourceConnector.Pin.Id, targetConnector.Pin.Id);
        if (connection != null)
        {
            Connections.Add(new ConnectionViewModel(connection, sourceConnector, targetConnector));
        }

        CancelPendingConnection();
        return connection != null;
    }

    private static bool TryOrderConnectors(ConnectorViewModel first, ConnectorViewModel second, out ConnectorViewModel source, out ConnectorViewModel target)
    {
        if (first.Pin.Direction == PinDirection.Output && second.Pin.Direction == PinDirection.Input)
        {
            source = first;
            target = second;
            return true;
        }

        if (first.Pin.Direction == PinDirection.Input && second.Pin.Direction == PinDirection.Output)
        {
            source = second;
            target = first;
            return true;
        }

        source = first;
        target = second;
        return false;
    }

    public void SelectNode(NodeViewModel node, bool additive)
    {
        if (!additive)
        {
            ClearSelection();
        }

        if (!node.IsSelected)
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
            // Clear class selection when selecting nodes to avoid confusion
            SelectedClass = null;
        }
        RefreshDetailsPanel();
    }

    public void ToggleNodeSelection(NodeViewModel node)
    {
        if (node.IsSelected)
        {
            node.IsSelected = false;
            SelectedNodes.Remove(node);
        }
        else
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
            // Clear class selection when selecting nodes to avoid confusion
            SelectedClass = null;
        }
        RefreshDetailsPanel();
    }

    public void ClearSelection()
    {
        foreach (var node in SelectedNodes.ToList())
        {
            node.IsSelected = false;
        }
        SelectedNodes.Clear();
        RefreshDetailsPanel();
    }

    public bool SaveGraph(string filePath)
    {
        try
        {
            var payload = new SavePrintDocument
            {
                Nodes = Nodes.Select(n => SavePrintNode.FromNode(n.Node)).ToList(),
                Connections = Connections.Select(c => new SavePrintConnection
                {
                    SourcePinId = c.Connection.SourcePinId,
                    TargetPinId = c.Connection.TargetPinId
                }).ToList(),
                ExternalLibraries = ExternalLibraries.Select(lib => new SavePrintLibrary
                {
                    Name = lib.Name,
                    FilePath = lib.FilePath
                }).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, JsonSerializer.Serialize(payload, options));
            return true;
        }
        catch (Exception ex)
        {
            OutputText += $"\n✗ Save failed: {ex.Message}";
            return false;
        }
    }

    public bool LoadGraph(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                OutputText += $"\n✗ Save file not found: {filePath}";
                return false;
            }

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<SavePrintDocument>(json);
            if (payload == null)
            {
                OutputText += "\n✗ Save file format invalid.";
                return false;
            }

            ResetGraphState();

            if (payload.ExternalLibraries?.Any() == true)
            {
                foreach (var library in payload.ExternalLibraries)
                {
                    if (File.Exists(library.FilePath))
                    {
                        TryAddExternalLibrary(library.FilePath, suppressOutput: true);
                    }
                    else
                    {
                        OutputText += $"\n⚠ Missing library: {library.FilePath}";
                    }
                }
            }

            foreach (var nodeData in payload.Nodes)
            {
                var node = nodeData.InstantiateNode();
                if (node == null)
                {
                    OutputText += $"\n⚠ Unable to recreate node '{nodeData.Type}'.";
                    continue;
                }

                _graph.AddNode(node);
                var nodeVm = new NodeViewModel(node);
                Nodes.Add(nodeVm);
            }

            foreach (var connection in payload.Connections)
            {
                var created = _graph.AddConnection(connection.SourcePinId, connection.TargetPinId);
                if (created == null)
                {
                    OutputText += "\n⚠ Skipped invalid connection.";
                    continue;
                }

                var sourceConnector = FindConnectorByPinId(connection.SourcePinId);
                var targetConnector = FindConnectorByPinId(connection.TargetPinId);
                if (sourceConnector != null && targetConnector != null)
                {
                    Connections.Add(new ConnectionViewModel(created, sourceConnector, targetConnector));
                }
            }

            OutputText += "\n✓ Save file loaded.";
            return true;
        }
        catch (Exception ex)
        {
            OutputText += $"\n✗ Load failed: {ex.Message}";
            return false;
        }
    }

    private void ResetGraphState()
    {
        _graph = new Graph();
        Nodes.Clear();
        Connections.Clear();
        SelectedNodes.Clear();
        SelectedNodeProperties.Clear();
        _nextNodePosition = new Point(150, 150);
        PendingConnection = null;
        ClearExternalLibraries();
    }

    private void ClearExternalLibraries()
    {
        foreach (var library in ExternalLibraries.ToList())
        {
            NodeCategories.Remove(library.Category);
        }
        ExternalLibraries.Clear();
        SelectedExternalLibrary = null;
        RefreshNodeFilter();
    }

    public bool DeleteSelectedNodes()
    {
        if (SelectedNodes.Count == 0)
            return false;

        var nodesToRemove = SelectedNodes.ToList();
        foreach (var node in nodesToRemove)
        {
            RemoveNode(node);
        }
        SelectedNodes.Clear();
        SelectedNodeProperties.Clear();
        return nodesToRemove.Count > 0;
    }
    
    public void DeleteClass(ClassViewModel classToDelete)
    {
        if (classToDelete == null)
            return;
            
        if (Classes.Remove(classToDelete))
        {
            if (SelectedClass == classToDelete)
            {
                SelectedClass = null;
            }
            OutputText += $"\n✓ Deleted class '{classToDelete.Name}'.";
        }
    }

    private void RemoveNode(NodeViewModel node)
    {
        var pinIds = node.InputConnectors.Concat(node.OutputConnectors)
            .Select(c => c.Pin.Id)
            .ToHashSet();

        var relatedConnections = Connections
            .Where(c => pinIds.Contains(c.Connection.SourcePinId) || pinIds.Contains(c.Connection.TargetPinId))
            .ToList();

        foreach (var connection in relatedConnections)
        {
            Connections.Remove(connection);
        }

        _graph.RemoveNode(node.Node.Id);
        Nodes.Remove(node);
        SelectedNodes.Remove(node);
        RefreshDetailsPanel();
    }

    private ConnectorViewModel? FindConnectorByPinId(Guid pinId)
    {
        foreach (var node in Nodes)
        {
            var connector = node.InputConnectors.Concat(node.OutputConnectors)
                .FirstOrDefault(c => c.Pin.Id == pinId);
            if (connector != null)
            {
                return connector;
            }
        }
        return null;
    }

    private void RefreshNodeFilter()
    {
        var search = NodeSearchText?.Trim() ?? string.Empty;
        foreach (var category in NodeCategories)
        {
            category.UpdateFilter(search);
        }

        FilteredNodeCategories.Clear();
        foreach (var category in NodeCategories)
        {
            if (category.HasVisibleNodes)
            {
                FilteredNodeCategories.Add(category);
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string ComposeOutputPath()
    {
        var normalizedDrive = NormalizeDrive(BuildOutputDrive);
        if (string.IsNullOrWhiteSpace(BuildOutputDirectory))
        {
            return normalizedDrive;
        }

        return Path.Combine(normalizedDrive, BuildOutputDirectory.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string NormalizeDrive(string drive)
    {
        if (string.IsNullOrWhiteSpace(drive))
            return string.Empty;

        var trimmed = drive.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            trimmed += ":";
        }

        if (!trimmed.EndsWith(Path.DirectorySeparatorChar) && !trimmed.EndsWith(Path.AltDirectorySeparatorChar))
        {
            trimmed += Path.DirectorySeparatorChar;
        }

        return trimmed;
    }
}

// Helper ViewModels
public class NodeViewModel : INotifyPropertyChanged
{
    public Node Node { get; }
    public string Title => Node.Title;
    public Point Position
    {
        get => Node.Position;
        set
        {
            if (Node.Position != value)
            {
                Node.Position = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Position)));
            }
        }
    }
    
    public ObservableCollection<ConnectorViewModel> InputConnectors { get; } = new();
    public ObservableCollection<ConnectorViewModel> OutputConnectors { get; } = new();
    
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
    

    
    public NodeViewModel(Node node)
    {
        Node = node;
        
        foreach (var pin in node.InputPins)
            InputConnectors.Add(new ConnectorViewModel(pin));
            
        foreach (var pin in node.OutputPins)
            OutputConnectors.Add(new ConnectorViewModel(pin));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyTitleChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
    }
    

}

public class SavePrintDocument
{
    public List<SavePrintNode> Nodes { get; set; } = new();
    public List<SavePrintConnection> Connections { get; set; } = new();
    public List<SavePrintLibrary> ExternalLibraries { get; set; } = new();
}

public class SavePrintNode
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public Point Position { get; set; }
    public List<SavePrintPin> Pins { get; set; } = new();
    public string? LiteralType { get; set; }
    public object? LiteralValue { get; set; }
    public SavePrintMethodDescriptor? Method { get; set; }

    public static SavePrintNode FromNode(Node node)
    {
        var saveNode = new SavePrintNode
        {
            Id = node.Id,
            Type = node.GetType().AssemblyQualifiedName ?? node.GetType().FullName ?? string.Empty,
            Position = node.Position,
            Pins = node.GetAllPins().Select(p => SavePrintPin.FromPin(p)).ToList()
        };

        if (node is LiteralNode && node.Properties.TryGetValue("LiteralType", out var literalTypeObj) && literalTypeObj is Type literalType)
        {
            saveNode.LiteralType = literalType.AssemblyQualifiedName ?? literalType.FullName;
            if (node.Properties.TryGetValue("Value", out var literalValue))
            {
                saveNode.LiteralValue = literalValue;
            }
        }

        if (node is MethodCallNode methodNode && methodNode.MethodInfo != null)
        {
            saveNode.Method = SavePrintMethodDescriptor.FromMethod(methodNode.MethodInfo);
        }

        return saveNode;
    }

    public Node? InstantiateNode()
    {
        var nodeType = System.Type.GetType(Type);
        if (nodeType == null)
            return null;

        var instance = CreateInstance(nodeType);
        if (instance == null)
            return null;

        instance.Id = Id;
        instance.Position = Position;

        foreach (var pin in instance.GetAllPins())
        {
            var savedPin = Pins.FirstOrDefault(p => p.Matches(pin));
            if (savedPin != null)
            {
                savedPin.Apply(pin, instance.Id);
            }
            else
            {
                pin.NodeId = instance.Id;
            }
        }

        return instance;
    }

    private Node? CreateInstance(Type nodeType)
    {
        try
        {
            if (typeof(LiteralNode).IsAssignableFrom(nodeType))
            {
                var literalType = !string.IsNullOrWhiteSpace(LiteralType) ? System.Type.GetType(LiteralType) : null;
                if (literalType == null)
                    return null;
                var literalValue = ConvertLiteralValue(literalType);
                return Activator.CreateInstance(nodeType, literalType, literalValue) as Node;
            }

            if (typeof(MethodCallNode).IsAssignableFrom(nodeType))
            {
                var methodInfo = Method?.Resolve();
                if (methodInfo == null)
                    return null;
                return Activator.CreateInstance(nodeType, methodInfo) as Node;
            }

            return MainViewModel.TryCreateNode(nodeType, Array.Empty<object?>())
                   ?? MainViewModel.TryCreateNode(nodeType, new object?[] { null })
                   ?? MainViewModel.TryCreateNode(nodeType, new object?[] { null, null });
        }
        catch
        {
            return null;
        }
    }

    private object? ConvertLiteralValue(Type literalType)
    {
        if (LiteralValue is JsonElement element)
        {
            return element.Deserialize(literalType);
        }
        if (LiteralValue == null)
            return null;

        if (literalType.IsInstanceOfType(LiteralValue))
            return LiteralValue;

        try
        {
            return Convert.ChangeType(LiteralValue, literalType);
        }
        catch
        {
            return LiteralValue;
        }
    }
}

public class GraphContext
{
    public string Name { get; set; }
    public Graph? Graph { get; set; }
    public List<NodeViewModel> Nodes { get; set; } = new();
    public List<ConnectionViewModel> Connections { get; set; } = new();
    public GraphContext(string name) { Name = name; }
}

public class ClassViewModel : INotifyPropertyChanged
{
    private string _name;
    public string Name 
    { 
        get => _name;
        private set
        {
            if (_name != value)
            {
                _name = value;
                Context.Name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }
    
    public GraphContext Context { get; }
    
    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing != value)
            {
                _isEditing = value;
                if (_isEditing)
                    EditingName = Name;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            }
        }
    }
    
    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set
        {
            if (_editingName != value)
            {
                _editingName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingName)));
            }
        }
    }
    
    public ClassViewModel(string name, GraphContext context)
    {
        _name = name;
        Context = context;
        _editingName = name;
    }
    
    public void CompleteEditing(MainViewModel parentViewModel)
    {
        if (_isEditing && !string.IsNullOrWhiteSpace(EditingName))
        {
            var newName = EditingName.Trim();
            // Check if name already exists
            var existing = new HashSet<string>(parentViewModel.Classes.Where(c => c != this).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(newName))
            {
                Name = newName;
            }
        }
        IsEditing = false;
    }
    
    public void CancelEditing()
    {
        IsEditing = false;
        EditingName = Name;
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class SavePrintConnection
{
    public Guid SourcePinId { get; set; }
    public Guid TargetPinId { get; set; }
}

public class SavePrintLibrary
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class SavePrintPin
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PinDirection Direction { get; set; }
    public PinKind Kind { get; set; }

    public static SavePrintPin FromPin(Pin pin) => new SavePrintPin
    {
        Id = pin.Id,
        Name = pin.Name,
        Direction = pin.Direction,
        Kind = pin.Kind
    };

    public bool Matches(Pin pin)
        => string.Equals(pin.Name, Name, StringComparison.Ordinal)
           && pin.Direction == Direction
           && pin.Kind == Kind;

    public void Apply(Pin pin, Guid nodeId)
    {
        pin.Id = Id;
        pin.NodeId = nodeId;
    }
}

public class SavePrintMethodDescriptor
{
    public string? DeclaringType { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> ParameterTypes { get; set; } = new();

    public static SavePrintMethodDescriptor FromMethod(MethodInfo method)
    {
        return new SavePrintMethodDescriptor
        {
            DeclaringType = method.DeclaringType?.AssemblyQualifiedName ?? method.DeclaringType?.FullName,
            Name = method.Name,
            ParameterTypes = method.GetParameters()
                .Select(p => p.ParameterType.AssemblyQualifiedName ?? p.ParameterType.FullName ?? string.Empty)
                .ToList()
        };
    }

    public MethodInfo? Resolve()
    {
        if (string.IsNullOrWhiteSpace(DeclaringType))
            return null;

        var declaringType = System.Type.GetType(DeclaringType);
        
        // If type resolution failed, try to find it in loaded assemblies by name
        if (declaringType == null && DeclaringType.Contains(","))
        {
            var typeName = DeclaringType.Split(',')[0]; // Get just the type name without assembly info
            
            // Search in all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    declaringType = assembly.GetType(typeName);
                    if (declaringType != null) break;
                    
                    // Also try searching by simple name in case of nested classes
                    declaringType = assembly.GetTypes().FirstOrDefault(t => t.FullName == typeName);
                    if (declaringType != null) break;
                }
                catch
                {
                    // Skip assemblies that can't be introspected
                }
            }
        }
        
        if (declaringType == null)
            return null;

        var parameterTypes = ParameterTypes
            .Select(name => {
                if (string.IsNullOrWhiteSpace(name)) return typeof(object);
                
                var paramType = System.Type.GetType(name);
                if (paramType != null) return paramType;
                
                // If parameter type resolution failed, try the same fallback approach
                if (name.Contains(","))
                {
                    var paramTypeName = name.Split(',')[0];
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            paramType = assembly.GetType(paramTypeName);
                            if (paramType != null) return paramType;
                            
                            paramType = assembly.GetTypes().FirstOrDefault(t => t.FullName == paramTypeName);
                            if (paramType != null) return paramType;
                        }
                        catch { }
                    }
                }
                
                return typeof(object); // Fallback to object if type can't be resolved
            })
            .ToArray();

        return declaringType.GetMethod(Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, binder: null, types: parameterTypes, modifiers: null);
    }
}

public class ConnectorViewModel : INotifyPropertyChanged
{
    public Pin Pin { get; }
    public string Name => Pin.Name;
    public bool IsConnected => Pin.IsConnected;
    public Brush Color => new SolidColorBrush(Pin.GetColor());
    private Point _anchor;
    public Point Anchor
    {
        get => _anchor;
        set
        {
            if (_anchor != value)
            {
                _anchor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Anchor)));
            }
        }
    }
    
    public ConnectorViewModel(Pin pin)
    {
        Pin = pin;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ExternalLibraryViewModel : INotifyPropertyChanged
{
    public string Name { get; }
    public string FilePath { get; } // Keep for identity/display purposes
    public Assembly? CompiledAssembly { get; } // Store the actual compiled assembly
    public NodeCategoryViewModel Category { get; }
    public int NodeCount => Category.Nodes.Count;

    public ExternalLibraryViewModel(string name, string filePath, NodeCategoryViewModel category, Assembly? compiledAssembly = null)
    {
        Name = name;
        FilePath = filePath;
        Category = category;
        CompiledAssembly = compiledAssembly;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ConnectionViewModel
{
    public Connection Connection { get; }
    public ConnectorViewModel Source { get; }
    public ConnectorViewModel Target { get; }
    public Brush Color => Source.Color;
    
    public ConnectionViewModel(Connection connection, ConnectorViewModel source, ConnectorViewModel target)
    {
        Connection = connection;
        Source = source;
        Target = target;
    }
}

public class PendingConnectionViewModel : INotifyPropertyChanged
{
    public ConnectorViewModel? Source { get; set; }
    private Point _targetPosition;
    public Point TargetPosition
    {
        get => _targetPosition;
        set
        {
            if (_targetPosition != value)
            {
                _targetPosition = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetPosition)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class NodeCategoryViewModel : INotifyPropertyChanged
{
    public string CategoryName { get; }
    public ObservableCollection<NodeTemplateViewModel> Nodes { get; } = new();
    public ObservableCollection<NodeTemplateViewModel> FilteredNodes { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
    }

    public bool HasVisibleNodes => FilteredNodes.Count > 0;

    public NodeCategoryViewModel(string name)
    {
        CategoryName = name;
    }

    public void UpdateFilter(string filter)
    {
        FilteredNodes.Clear();
        foreach (var node in Nodes)
        {
            if (node.Matches(filter))
            {
                FilteredNodes.Add(node);
            }
        }
        OnPropertyChanged(nameof(HasVisibleNodes));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class NodeTemplateViewModel
{
    public string Title { get; }
    public string Description { get; }
    public Type NodeType { get; }
    public Type? AdditionalType { get; }
    public MethodInfo? AdditionalMethod { get; private set; }

    public NodeTemplateViewModel(string title, string description, Type nodeType, Type? additionalType = null)
    {
        Title = title;
        Description = description;
        NodeType = nodeType;
        AdditionalType = additionalType;
    }

    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        filter = filter.Trim();
        return Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || NodeType.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public static NodeTemplateViewModel ForMethod(string title, string description, MethodInfo method)
        => new NodeTemplateViewModel(title, description, typeof(CSharpVisualScripting.Nodes.Functions.MethodCallNode))
        {
            AdditionalMethod = method
        };
}

public class PropertyViewModel
{
    public string Name { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string? Key { get; set; }
    public Type? ValueType { get; set; }
    public bool IsEditable { get; set; } = true;
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;
    
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
    
    public event EventHandler? CanExecuteChanged;
}
