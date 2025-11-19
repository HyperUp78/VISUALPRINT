using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using MahApps.Metro.Controls;
using CSharpVisualScripting.UI.ViewModels;
using System.Windows.Controls.Primitives;

namespace CSharpVisualScripting.UI;

public partial class MainWindow : MetroWindow
{
    private MainViewModel? _currentViewModel;
    private readonly Dictionary<ConnectionViewModel, System.Windows.Shapes.Path> _connectionPaths = new();
    private readonly Dictionary<ConnectorViewModel, FrameworkElement> _connectorElements = new();
    private readonly System.Windows.Shapes.Path _pendingConnectionPath = new()
    {
        Visibility = Visibility.Collapsed,
        Stroke = Brushes.White,
        StrokeThickness = 1.5,
        StrokeDashArray = new DoubleCollection { 2, 2 },
        Fill = Brushes.Transparent,
        IsHitTestVisible = false
    };

    private Point _libraryDragStart;
    private bool _isNodeDragging;
    private NodeViewModel? _draggedNode;
    private Point _nodeDragOrigin;
    private readonly Dictionary<NodeViewModel, Point> _nodeDragStartPositions = new();

    private bool _isPanning;
    private Point _panStart;
    private Point _panOrigin;
    private double _currentZoom = 1.0;
    private const double MinZoom = 0.3;
    private const double MaxZoom = 2.5;
    private bool _isConnectionDragging;

    public MainWindow()
    {
        try
        {
            CSharpVisualScripting.UI.Diagnostics.DiagLogger.Startup("MainWindow ctor entered");
            InitializeComponent();
            ConnectionsLayer.Children.Add(_pendingConnectionPath);
            Panel.SetZIndex(_pendingConnectionPath, int.MaxValue);
            DataContextChanged += MainWindow_DataContextChanged;
            HookViewModel(DataContext as MainViewModel);
            CSharpVisualScripting.UI.Diagnostics.DiagLogger.Startup("MainWindow InitializeComponent succeeded");
        }
        catch (System.Exception ex)
        {
            CSharpVisualScripting.UI.Diagnostics.DiagLogger.Error($"MainWindow InitializeComponent failed: {ex.Message}");
            throw;
        }
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            UnhookViewModel(oldVm);
        }
        if (e.NewValue is MainViewModel newVm)
        {
            HookViewModel(newVm);
        }
    }

    private void HookViewModel(MainViewModel? viewModel)
    {
        if (viewModel == null || _currentViewModel == viewModel)
            return;

        _currentViewModel = viewModel;
        viewModel.Connections.CollectionChanged += Connections_CollectionChanged;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        foreach (var connection in viewModel.Connections)
        {
            AddConnectionPath(connection);
        }
        UpdatePendingConnectionVisual();
    }

    private void UnhookViewModel(MainViewModel viewModel)
    {
        viewModel.Connections.CollectionChanged -= Connections_CollectionChanged;
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        foreach (var connection in viewModel.Connections.ToList())
        {
            RemoveConnectionPath(connection);
        }
        if (_currentViewModel == viewModel)
        {
            _currentViewModel = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PendingConnection))
        {
            UpdatePendingConnectionVisual();
        }
    }

    private void Connections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (ConnectionViewModel connection in e.NewItems)
            {
                AddConnectionPath(connection);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (ConnectionViewModel connection in e.OldItems)
            {
                RemoveConnectionPath(connection);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var connection in _connectionPaths.Keys.ToList())
            {
                RemoveConnectionPath(connection);
            }
        }
    }

    private void AddConnectionPath(ConnectionViewModel connection)
    {
        var path = new System.Windows.Shapes.Path
        {
            Stroke = connection.Color,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = true,
            Cursor = Cursors.Hand,
            Tag = connection
        };
        path.MouseRightButtonDown += ConnectionPath_MouseRightButtonDown;
        _connectionPaths[connection] = path;
        ConnectionsLayer.Children.Add(path);
        Panel.SetZIndex(path, 0);

        connection.Source.PropertyChanged += Connector_PropertyChanged;
        connection.Target.PropertyChanged += Connector_PropertyChanged;
        UpdateConnectionPath(connection);
    }

    private void RemoveConnectionPath(ConnectionViewModel connection)
    {
        if (_connectionPaths.TryGetValue(connection, out var path))
        {
            path.MouseRightButtonDown -= ConnectionPath_MouseRightButtonDown;
            ConnectionsLayer.Children.Remove(path);
            _connectionPaths.Remove(connection);
        }
        connection.Source.PropertyChanged -= Connector_PropertyChanged;
        connection.Target.PropertyChanged -= Connector_PropertyChanged;
    }

    private void Connector_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectorViewModel.Anchor) && sender is ConnectorViewModel connector)
        {
            foreach (var kvp in _connectionPaths)
            {
                if (kvp.Key.Source == connector || kvp.Key.Target == connector)
                {
                    UpdateConnectionPath(kvp.Key);
                }
            }
        }
    }

    private void ConnectionPath_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Shapes.Path path || path.Tag is not ConnectionViewModel connection)
            return;

        if (_currentViewModel?.DisconnectCommand.CanExecute(connection) == true)
        {
            _currentViewModel.DisconnectCommand.Execute(connection);
            e.Handled = true;
        }
    }

    private void UpdateConnectionPath(ConnectionViewModel connection)
    {
        if (!_connectionPaths.TryGetValue(connection, out var path))
            return;

        var geometry = BuildConnectionGeometry(connection.Source.Anchor, connection.Target.Anchor);
        path.Data = geometry;
    }

    private void NodeTemplate_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.Tag is NodeTemplateViewModel template)
        {
            var viewModel = DataContext as MainViewModel;
            viewModel?.CreateNodeFromTemplate(template);
        }
    }

    private void NodeTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _libraryDragStart = e.GetPosition(null);
    }

    private void NodeTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is TreeViewItem item &&
            item.DataContext is NodeTemplateViewModel template)
        {
            DragDrop.DoDragDrop(item, new DataObject(typeof(NodeTemplateViewModel), template), DragDropEffects.Copy);
        }
    }

    private void GraphViewport_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(NodeTemplateViewModel)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void GraphViewport_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(NodeTemplateViewModel)))
            return;

        if (DataContext is MainViewModel vm)
        {
            var template = (NodeTemplateViewModel)e.Data.GetData(typeof(NodeTemplateViewModel))!;
            var graphPosition = GetGraphPosition(e.GetPosition(GraphViewport));
            vm.CreateNodeFromTemplate(template, graphPosition);
            e.Handled = true;
        }
    }

    private void NodeContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NodeViewModel node)
            return;

        if (DataContext is not MainViewModel vm)
            return;

        var isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (isShift)
        {
            vm.ToggleNodeSelection(node);
        }
        else if (!node.IsSelected)
        {
            vm.SelectNode(node, additive: false);
        }

        var nodesToDrag = vm.SelectedNodes.Count > 0
            ? vm.SelectedNodes.ToList()
            : new List<NodeViewModel> { node };

        _nodeDragStartPositions.Clear();
        _nodeDragOrigin = GetGraphPosition(e.GetPosition(GraphViewport));
        foreach (var selected in nodesToDrag)
        {
            if (!_nodeDragStartPositions.ContainsKey(selected))
            {
                _nodeDragStartPositions[selected] = selected.Position;
            }
        }

        _draggedNode = node;
        element.CaptureMouse();
        _isNodeDragging = true;
        e.Handled = true;
    }

    private void NodeContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isNodeDragging || _draggedNode == null)
            return;

        var graphPosition = GetGraphPosition(e.GetPosition(GraphViewport));
        var delta = graphPosition - _nodeDragOrigin;

        foreach (var kvp in _nodeDragStartPositions)
        {
            var start = kvp.Value;
            var rawX = start.X + delta.X;
            var rawY = start.Y + delta.Y;
            // Snap to 20x20 grid for clipped movement
            var snappedX = Math.Round(rawX / 20.0) * 20.0;
            var snappedY = Math.Round(rawY / 20.0) * 20.0;
            var newPosition = new Point(snappedX, snappedY);
            kvp.Key.Position = newPosition;
            UpdateConnectorsForNode(kvp.Key);
        }

        e.Handled = true;
    }

    private void NodeContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && _isNodeDragging)
        {
            element.ReleaseMouseCapture();
        }
        _isNodeDragging = false;
        _draggedNode = null;
        _nodeDragStartPositions.Clear();
        e.Handled = true;
    }

    private void Connector_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.LayoutUpdated += Connector_LayoutUpdated;
            if (fe.Tag is ConnectorViewModel connector)
            {
                _connectorElements[connector] = fe;
            }
            UpdateConnectorAnchor(fe);
        }
    }

    private void Connector_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.LayoutUpdated -= Connector_LayoutUpdated;
            if (fe.Tag is ConnectorViewModel connector)
            {
                _connectorElements.Remove(connector);
            }
        }
    }

    private void Connector_LayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            UpdateConnectorAnchor(fe);
        }
    }

    private void UpdateConnectorAnchor(FrameworkElement element)
    {
        if (element.Tag is not ConnectorViewModel connector)
            return;

        if (ConnectionsLayer == null)
            return;

        var center = new Point(element.ActualWidth / 2, element.ActualHeight / 2);
        var graphPoint = element.TranslatePoint(center, ConnectionsLayer);
        connector.Anchor = graphPoint;
    }

    private void Connector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement fe || fe.Tag is not ConnectorViewModel connector)
            return;

        if (vm.PendingConnection?.Source != null && vm.PendingConnection.Source != connector)
        {
            if (vm.TryCompleteConnection(connector))
            {
                UpdatePendingConnectionVisual();
            }
            EndConnectionDrag();
        }
        else
        {
            if (vm.PendingConnection?.Source == connector)
            {
                vm.CancelPendingConnection();
                UpdatePendingConnectionVisual();
                EndConnectionDrag();
            }
            else if (vm.TryBeginConnection(connector))
            {
                BeginConnectionDrag();
                UpdatePendingConnectionVisual();
            }
        }

        e.Handled = true;
    }

    private void Connector_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement fe || fe.Tag is not ConnectorViewModel connector)
            return;

        if (vm.PendingConnection?.Source != null && vm.PendingConnection.Source != connector)
        {
            if (vm.TryCompleteConnection(connector))
            {
                UpdatePendingConnectionVisual();
                e.Handled = true;
            }
            EndConnectionDrag();
        }
    }

    private void GraphViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CancelPendingConnection())
        {
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(GraphViewport);
        _panOrigin = new Point(GraphTranslateTransform.X, GraphTranslateTransform.Y);
        // Change cursor to indicate panning/move on right-drag
        try { GraphViewport.Cursor = Cursors.SizeAll; } catch { }
        GraphViewport.CaptureMouse();
        e.Handled = true;
    }

    private void GraphViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            vm.ClearSelection();
        }
    }

    private void GraphViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        if (GraphViewport.IsMouseCaptured)
        {
            GraphViewport.ReleaseMouseCapture();
        }
        // Restore default cursor when panning stops
        try { GraphViewport.Cursor = Cursors.Arrow; } catch { }
        e.Handled = true;
    }

    private void GraphViewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            if (GraphViewport.IsMouseCaptured)
            {
                GraphViewport.ReleaseMouseCapture();
            }
            try { GraphViewport.Cursor = Cursors.Arrow; } catch { }
        }
    }

    private void GraphViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var current = e.GetPosition(GraphViewport);
            var delta = current - _panStart;
            GraphTranslateTransform.X = _panOrigin.X + delta.X;
            GraphTranslateTransform.Y = _panOrigin.Y + delta.Y;
        }

        if (DataContext is MainViewModel vm && vm.PendingConnection?.Source != null)
        {
            var graphPosition = GetGraphPosition(e.GetPosition(GraphViewport));
            vm.UpdatePendingConnection(graphPosition);
            UpdatePendingConnectionVisual();
        }
    }

    private void GraphViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
        var newZoom = Math.Clamp(_currentZoom * zoomFactor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _currentZoom) < 0.001)
        {
            e.Handled = true;
            return;
        }

        var mousePosition = e.GetPosition(GraphViewport);
        var graphPoint = GetGraphPosition(mousePosition);

        _currentZoom = newZoom;
        GraphScaleTransform.ScaleX = GraphScaleTransform.ScaleY = _currentZoom;
        GraphTranslateTransform.X = mousePosition.X - (graphPoint.X * _currentZoom);
        GraphTranslateTransform.Y = mousePosition.Y - (graphPoint.Y * _currentZoom);
        e.Handled = true;
    }

    private Point GetGraphPosition(Point viewportPoint)
    {
        if (GraphContent?.RenderTransform == null)
            return viewportPoint;

        var matrix = GraphContent.RenderTransform.Value;
        if (matrix.HasInverse)
        {
            matrix.Invert();
            return matrix.Transform(viewportPoint);
        }
        return viewportPoint;
    }

    private void UpdatePendingConnectionVisual()
    {
        if (DataContext is not MainViewModel vm || vm.PendingConnection?.Source == null)
        {
            _pendingConnectionPath.Visibility = Visibility.Collapsed;
            EndConnectionDrag();
            return;
        }

        _pendingConnectionPath.Visibility = Visibility.Visible;
        var source = vm.PendingConnection.Source.Anchor;
        var target = vm.PendingConnection.TargetPosition;
        _pendingConnectionPath.Data = BuildConnectionGeometry(source, target);
    }

    private void UpdateConnectorsForNode(NodeViewModel node)
    {
        foreach (var connector in node.InputConnectors.Concat(node.OutputConnectors))
        {
            if (_connectorElements.TryGetValue(connector, out var element))
            {
                UpdateConnectorAnchor(element);
            }
        }
    }

    private static PathGeometry BuildConnectionGeometry(Point start, Point end)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start };
        var dx = Math.Max(60, Math.Abs(end.X - start.X) * 0.5);
        var control1 = new Point(start.X + dx, start.Y);
        var control2 = new Point(end.X - dx, end.Y);
        figure.Segments.Add(new BezierSegment(control1, control2, end, true));
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T typed)
                return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private bool CancelPendingConnection()
    {
        if (_currentViewModel?.PendingConnection?.Source != null)
        {
            _currentViewModel.CancelPendingConnection();
            UpdatePendingConnectionVisual();
            EndConnectionDrag();
            return true;
        }
        return false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CancelPendingConnection())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && DataContext is MainViewModel vm)
        {
            if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
                return;
            
            // Handle class deletion ONLY if classes list has focus AND a class is selected
            if (vm.SelectedClass != null && 
                (Keyboard.FocusedElement == ClassesListBox || 
                 FindAncestor<ListBox>(Keyboard.FocusedElement as DependencyObject) == ClassesListBox))
            {
                vm.DeleteClass(vm.SelectedClass);
                e.Handled = true;
            }
            // Handle node deletion if NOT focused on classes list OR no class is selected
            else if (vm.DeleteSelectedNodes())
            {
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F2 && DataContext is MainViewModel vm2)
        {
            if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
                return;
            // Handle class renaming ONLY - don't touch nodes
            if (vm2.SelectedClass != null && 
                (Keyboard.FocusedElement == ClassesListBox || 
                 FindAncestor<ListBox>(Keyboard.FocusedElement as DependencyObject) == ClassesListBox))
            {
                vm2.SelectedClass.IsEditing = true;
                e.Handled = true;
            }
        }
    }

    private void BeginConnectionDrag()
    {
        if (_isConnectionDragging || GraphViewport == null)
            return;

        if (Mouse.Capture(GraphViewport, CaptureMode.SubTree))
        {
            _isConnectionDragging = true;
        }
    }

    private void EndConnectionDrag()
    {
        if (!_isConnectionDragging)
            return;

        _isConnectionDragging = false;
        if (!_isPanning && Mouse.Captured == GraphViewport)
        {
            Mouse.Capture(null);
        }
    }

    private void BrowseOutputPath_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select build output folder",
            UseDescriptionForTitle = true,
            SelectedPath = System.IO.Directory.Exists(vm.BuildOutputPath) ? vm.BuildOutputPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            var selected = dialog.SelectedPath.Trim();
            var root = System.IO.Path.GetPathRoot(selected) ?? string.Empty;
            var relative = selected.Length > root.Length ? selected.Substring(root.Length) : string.Empty;

            vm.BuildOutputDrive = root;
            vm.BuildOutputDirectory = relative.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
    }

    private void SaveGraphButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save VISUALPRINT",
            Filter = "VISUALPRINT Save Files (*.saveprint)|*.saveprint|All files (*.*)|*.*",
            DefaultExt = ".saveprint",
            AddExtension = true,
            FileName = "MyVisualprint.saveprint"
        };

        if (dialog.ShowDialog() == true)
        {
            if (vm.SaveGraph(dialog.FileName))
            {
                vm.OutputText += $"\n✓ Saved VISUALPRINT to {dialog.FileName}";
            }
        }
    }

    private void OpenGraphButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open VISUALPRINT",
            Filter = "VISUALPRINT Save Files (*.saveprint)|*.saveprint|All files (*.*)|*.*",
            DefaultExt = ".saveprint",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            vm.LoadGraph(dialog.FileName);
        }
    }

    private void AddLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // Show dialog to choose between files and folders
        var result = MessageBox.Show(
            "Do you want to select individual files (Yes) or search in a folder (No)?", 
            "Add API Libraries", 
            MessageBoxButton.YesNoCancel, 
            MessageBoxImage.Question);
            
        if (result == MessageBoxResult.Cancel)
            return;
            
        if (result == MessageBoxResult.Yes)
        {
            // File selection mode
            var fileDialog = new OpenFileDialog
            {
                Title = "Add API Libraries",
                Filter = "Assemblies (*.dll)|*.dll|C# source (*.cs)|*.cs|All files (*.*)|*.*",
                Multiselect = true
            };

            if (fileDialog.ShowDialog() == true)
            {
                ProcessSelectedFiles(vm, fileDialog.FileNames);
            }
        }
        else
        {
            // Folder selection mode
            using var folderDialog = new Forms.FolderBrowserDialog
            {
                Description = "Select folder to search for C# source files and assemblies",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            
            if (folderDialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
            {
                var foundFiles = SearchLibraryFiles(folderDialog.SelectedPath);
                if (foundFiles.Length > 0)
                {
                    ProcessSelectedFiles(vm, foundFiles);
                    vm.OutputText += $"\n✓ Found and processed {foundFiles.Length} library files in folder and subfolders.";
                }
                else
                {
                    vm.OutputText += $"\n⚠ No .dll or .cs files found in '{folderDialog.SelectedPath}' and its subfolders.";
                }
            }
        }
    }
    
    private void ProcessSelectedFiles(MainViewModel vm, string[] files)
    {
        var csFiles = files.Where(f => string.Equals(System.IO.Path.GetExtension(f), ".cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        var nonCs = files.Except(csFiles).ToArray();

        // If the selection is all .cs files and more than one, compile them as a single source library
        if (csFiles.Length > 1 && nonCs.Length == 0)
        {
            vm.TryAddExternalSourceLibrary(csFiles);
        }
        else
        {
            foreach (var file in files)
            {
                vm.TryAddExternalLibrary(file);
            }
        }
    }
    
    private string[] SearchLibraryFiles(string rootPath)
    {
        try
        {
            var supportedExtensions = new[] { ".dll", ".cs" };
            var foundFiles = new List<string>();
            
            // Search recursively for supported file types
            foreach (var extension in supportedExtensions)
            {
                var pattern = $"*{extension}";
                var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories);
                foundFiles.AddRange(files);
            }
            
            return foundFiles.OrderBy(f => f).ToArray();
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OutputText += $"\n⚠ Error searching folder: {ex.Message}";
            }
            return Array.Empty<string>();
        }
    }

    private void RemoveLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.RemoveExternalLibrary(vm.SelectedExternalLibrary);
    }

    private void NewClassButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.CreateNewClass();
    }

    private void ClassesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (vm.SelectedClass != null)
        {
            vm.EnterClass(vm.SelectedClass);
        }
    }
    
    private void ClassNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ClassViewModel classViewModel)
            return;
            
        if (e.Key == Key.Enter)
        {
            if (DataContext is MainViewModel mainVm)
                classViewModel.CompleteEditing(mainVm);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            classViewModel.CancelEditing();
            e.Handled = true;
        }
    }
    
    private void ClassNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not ClassViewModel classViewModel)
            return;
            
        if (DataContext is MainViewModel mainVm)
            classViewModel.CompleteEditing(mainVm);
    }
    
    private void ClassNameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }
}

