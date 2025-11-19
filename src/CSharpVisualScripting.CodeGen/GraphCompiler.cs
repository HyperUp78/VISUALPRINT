using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CSharpVisualScripting.CodeGen;

public enum BuildTarget
{
    Dll,
    ExeConsole,
    ExeWindows
}

/// <summary>
/// Compiles generated C# code into assemblies using Roslyn
/// </summary>
public class GraphCompiler
{
    private readonly List<MetadataReference> _references = new();
    
    public GraphCompiler()
    {
        // Add standard references
        AddDefaultReferences();
    }
    
    /// <summary>
    /// Adds default .NET references
    /// </summary>
    private void AddDefaultReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,                    // System.Private.CoreLib
            typeof(Console).Assembly,                   // System.Console
            typeof(IEnumerable<>).Assembly,            // System.Collections
            typeof(System.Linq.Enumerable).Assembly    // System.Linq
        };

        foreach (var assembly in assemblies)
        {
            AddReference(assembly);
        }

        var assemblyNames = new[]
        {
            "System.Runtime",
            "netstandard",
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Xaml"
        };

        foreach (var name in assemblyNames)
        {
            try
            {
                var assembly = Assembly.Load(name);
                AddReference(assembly);
            }
            catch (FileNotFoundException)
            {
                // Skip optional assemblies that are not present in the current runtime.
            }
        }
    }
    
    /// <summary>
    /// Adds a reference to an assembly
    /// </summary>
    public void AddReference(Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location))
        {
            _references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
    }
    
    /// <summary>
    /// Adds a reference from a file path
    /// </summary>
    public void AddReference(string assemblyPath)
    {
        _references.Add(MetadataReference.CreateFromFile(assemblyPath));
    }
    
    /// <summary>
    /// Compiles source code to an in-memory assembly
    /// </summary>
    public CompilationResult Compile(string sourceCode, string assemblyName = "GeneratedAssembly", string? outputDirectory = null, BuildTarget target = BuildTarget.Dll)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        
        var outputKind = target switch
        {
            BuildTarget.ExeConsole => OutputKind.ConsoleApplication,
            BuildTarget.ExeWindows => OutputKind.WindowsApplication,
            _ => OutputKind.DynamicallyLinkedLibrary
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: _references,
            options: new CSharpCompilationOptions(
                outputKind,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false
            ));
        
        using var ms = new MemoryStream();
        using var pdbStream = new MemoryStream();
        
        var emitOptions = new EmitOptions(
            debugInformationFormat: DebugInformationFormat.PortablePdb,
            pdbFilePath: $"{assemblyName}.pdb"
        );
        
        var emitResult = compilation.Emit(ms, pdbStream, options: emitOptions);
        
        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            
            return new CompilationResult(false, null, null, errors, sourceCode, null, null);
        }
        
        var assemblyBytes = ms.ToArray();
        var pdbBytes = pdbStream.ToArray();

        string? dllPath = null;
        string? pdbPath = null;

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var fileExt = target == BuildTarget.Dll ? ".dll" : ".exe";
                dllPath = Path.Combine(outputDirectory, $"{assemblyName}{fileExt}");
                pdbPath = Path.Combine(outputDirectory, $"{assemblyName}.pdb");
                File.WriteAllBytes(dllPath, assemblyBytes);
                File.WriteAllBytes(pdbPath, pdbBytes);
                
                // For EXE targets, create self-contained executable using dotnet publish
                if (target != BuildTarget.Dll)
                {
                    try
                    {
                        // Create a temporary project file for publishing
                        var tempProjectDir = Path.Combine(outputDirectory, "temp_publish");
                        if (Directory.Exists(tempProjectDir))
                        {
                            Directory.Delete(tempProjectDir, true);
                        }
                        Directory.CreateDirectory(tempProjectDir);
                        
                        var tempProjectFile = Path.Combine(tempProjectDir, $"{assemblyName}.csproj");
                        var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>{(target == BuildTarget.ExeConsole ? "Exe" : "WinExe")}</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>{assemblyName}</AssemblyName>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
</Project>";
                        File.WriteAllText(tempProjectFile, projectContent);
                        
                        // Write the source code
                        var tempSourceFile = Path.Combine(tempProjectDir, "Program.cs");
                        File.WriteAllText(tempSourceFile, sourceCode);
                        
                        // Publish as self-contained single file
                        var publishDir = Path.Combine(outputDirectory, "publish");
                        if (Directory.Exists(publishDir))
                        {
                            Directory.Delete(publishDir, true);
                        }
                        
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"publish \"{tempProjectFile}\" -c Release --no-restore -o \"{publishDir}\"",
                            WorkingDirectory = tempProjectDir,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using var process = System.Diagnostics.Process.Start(startInfo);
                        if (process != null)
                        {
                            var output = process.StandardOutput.ReadToEnd();
                            var error = process.StandardError.ReadToEnd();
                            process.WaitForExit();
                            
                            if (process.ExitCode == 0)
                            {
                                // Copy the published exe to the main output directory
                                var publishedExe = Path.Combine(publishDir, $"{assemblyName}.exe");
                                if (File.Exists(publishedExe))
                                {
                                    File.Copy(publishedExe, dllPath, true);
                                    
                                    // Also copy any runtime files that might be needed
                                    foreach (var file in Directory.GetFiles(publishDir, "*.dll"))
                                    {
                                        var destFile = Path.Combine(outputDirectory, Path.GetFileName(file));
                                        if (!File.Exists(destFile))
                                        {
                                            File.Copy(file, destFile);
                                        }
                                    }
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Published executable not found at {publishedExe}");
                                }
                            }
                            else
                            {
                                var errorMsg = string.IsNullOrEmpty(error) ? "Unknown publish error" : error;
                                throw new InvalidOperationException($"Publish failed (exit code {process.ExitCode}): {errorMsg}");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Failed to start dotnet publish process");
                        }
                        
                        // Cleanup temp directory
                        try
                        {
                            if (Directory.Exists(tempProjectDir))
                            {
                                Directory.Delete(tempProjectDir, true);
                            }
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Self-contained publish failed: {ex.Message}");
                        
                        // Instead of creating a framework-dependent executable that will fail,
                        // try a simpler self-contained approach using dotnet build
                        try
                        {
                            var tempBuildDir = Path.Combine(outputDirectory, "temp_build");
                            if (Directory.Exists(tempBuildDir))
                            {
                                Directory.Delete(tempBuildDir, true);
                            }
                            Directory.CreateDirectory(tempBuildDir);
                            
                            var tempProjectFile = Path.Combine(tempBuildDir, $"{assemblyName}.csproj");
                            var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>{(target == BuildTarget.ExeConsole ? "Exe" : "WinExe")}</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>{assemblyName}</AssemblyName>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseAppHost>true</UseAppHost>
    <TrimMode>link</TrimMode>
  </PropertyGroup>
</Project>";
                            File.WriteAllText(tempProjectFile, projectContent);
                            
                            var tempSourceFile = Path.Combine(tempBuildDir, "Program.cs");
                            File.WriteAllText(tempSourceFile, sourceCode);
                            
                            // Build self-contained executable
                            var buildInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "dotnet",
                                Arguments = $"build \"{tempProjectFile}\" -c Release --self-contained -o \"{outputDirectory}\"",
                                WorkingDirectory = tempBuildDir,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            
                            using var buildProcess = System.Diagnostics.Process.Start(buildInfo);
                            if (buildProcess != null)
                            {
                                var buildOutput = buildProcess.StandardOutput.ReadToEnd();
                                var buildError = buildProcess.StandardError.ReadToEnd();
                                buildProcess.WaitForExit();
                                
                                if (buildProcess.ExitCode == 0)
                                {
                                    var builtExe = Path.Combine(outputDirectory, $"{assemblyName}.exe");
                                    if (File.Exists(builtExe))
                                    {
                                        // Success - the file should now be self-contained
                                        System.Diagnostics.Debug.WriteLine($"Successfully created self-contained executable: {builtExe}");
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException($"Built executable not found at {builtExe}");
                                    }
                                }
                                else
                                {
                                    var errorMsg = string.IsNullOrEmpty(buildError) ? buildOutput : buildError;
                                    throw new InvalidOperationException($"Build failed (exit code {buildProcess.ExitCode}): {errorMsg}");
                                }
                            }
                            
                            // Cleanup temp directory
                            try
                            {
                                if (Directory.Exists(tempBuildDir))
                                {
                                    Directory.Delete(tempBuildDir, true);
                                }
                            }
                            catch { }
                        }
                        catch (Exception buildEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Self-contained build also failed: {buildEx.Message}");
                            
                            // Final fallback - create framework-dependent with warning in debug output
                            System.Diagnostics.Debug.WriteLine("WARNING: Creating framework-dependent executable - will require .NET runtime installed");
                            var runtimeConfigPath = Path.Combine(outputDirectory, $"{assemblyName}.runtimeconfig.json");
                            var runtimeConfig = @"{
  ""runtimeOptions"": {
    ""tfm"": ""net8.0"",
    ""frameworks"": [
      {
        ""name"": ""Microsoft.NETCore.App"",
        ""version"": ""8.0.0""
      }
    ]
  }
}";
                            File.WriteAllText(runtimeConfigPath, runtimeConfig);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                dllPath = null;
                pdbPath = null;
                System.Diagnostics.Debug.WriteLine($"GraphCompiler disk write failed: {ex.Message}");
            }
        }

        var assemblyLoadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        using var assemblyStream = new MemoryStream(assemblyBytes);
        using var pdbLoadStream = new MemoryStream(pdbBytes);
        var assembly = assemblyLoadContext.LoadFromStream(assemblyStream, pdbLoadStream);
        
        return new CompilationResult(true, assembly, assemblyLoadContext, new List<string>(), sourceCode, dllPath, pdbPath);
    }
    
    /// <summary>
    /// Compiles a syntax tree to an assembly
    /// </summary>
    public CompilationResult Compile(Microsoft.CodeAnalysis.SyntaxTree syntaxTree, string assemblyName = "GeneratedAssembly", string? outputDirectory = null)
    {
        return Compile(syntaxTree.ToString(), assemblyName, outputDirectory);
    }
}

/// <summary>
/// Result of compilation
/// </summary>
public record CompilationResult(
    bool Success,
    Assembly? Assembly,
    AssemblyLoadContext? LoadContext,
    List<string> Errors,
    string GeneratedCode,
    string? AssemblyFilePath = null,
    string? PdbFilePath = null
)
{
    /// <summary>
    /// Unloads the compiled assembly
    /// </summary>
    public void Unload()
    {
        LoadContext?.Unload();
    }
    
    /// <summary>
    /// Gets a type from the compiled assembly
    /// </summary>
    public Type? GetType(string typeName)
    {
        return Assembly?.GetType(typeName);
    }
    
    /// <summary>
    /// Creates an instance of a type from the compiled assembly
    /// </summary>
    public object? CreateInstance(string typeName)
    {
        var type = GetType(typeName);
        return type != null ? Activator.CreateInstance(type) : null;
    }
}
