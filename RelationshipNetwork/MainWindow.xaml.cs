using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.WpfGraphControl;
using Microsoft.Win32;
using Point = Microsoft.Msagl.Core.Geometry.Point;

namespace RelationshipNetwork {
	public partial class MainWindow {
		#region Fields
		private static readonly FieldInfo ViewerFieldInfo = typeof(AutomaticGraphLayoutControl).GetField("_graphViewer", BindingFlags.NonPublic | BindingFlags.Instance);

		private readonly OpenFileDialog _openFileDialog = new();

		private readonly SaveFileDialog _saveFileDialog = new();

		private bool _highlighting;
		#endregion

		#region Constructors
		public MainWindow() {
			InitializeComponent();
			SelectedNodes = new ObservableCollection<IViewerNode>();
			GraphControl.Graph = new Graph {
				Attr = {
					LayerDirection = LayerDirection.LR,
					BackgroundColor = Color.Transparent
				}
			};
			Viewer = ViewerFieldInfo.GetValue(GraphControl) as GraphViewer;
			RefreshToolbar();

			GraphChanged += (_, _) => RefreshHighlight();
			GraphControl.MouseRightButtonUp += GraphControlMouseRightButtonUp;
			LoadGraphButton.Click += LoadGraphButtonClick;
			SaveGraphButton.Click += SaveGraphButtonClick;
			UndoButton.Click += UndoButtonClick;
			RedoButton.Click += RedoButtonClick;
			RefreshLayoutButton.Click += RefreshLayoutButtonClick;
			AddNodeButton.Click += AddNodeButtonClick;
			NodeNameTextBox.KeyUp += NodeNameTextBoxKeyUp;
			DeleteNodeButton.Click += DeleteNodeButtonClick;
			AddEdgeButton.Click += AddEdgeButtonClick;
			DeleteEdgeButton.Click += DeleteEdgeButtonClick;
			AutoHighlightButton.Click += AutoHighlightButtonClick;
			SortByRelativityButton.Click += SortByRelativityButtonClick;
			SelectedNodes.CollectionChanged += SelectedNodesCollectionChanged;
		}
		#endregion

		#region Properties
		public GraphViewer Viewer { get; }

		public Graph Graph {
			get => GraphControl.Graph;
			private set {
				GraphControl.Graph = value;
				GraphChanged(this, EventArgs.Empty);
				if (Viewer is not null)
					foreach (var viewerNode in ViewerNodes)
						AttachEvent(viewerNode);
			}
		}

		public IEnumerable<IViewerNode> ViewerNodes => Viewer.Entities.OfType<IViewerNode>();

		public ObservableCollection<IViewerNode> SelectedNodes { get; }

		/// <summary>
		/// Whether the graph is currently highlighted
		/// </summary>
		public bool Highlighting {
			get => _highlighting;
			private set {
				_highlighting = value;
				SortByRelativityButton.IsEnabled = value;
			}
		}

		#region Context Menus
		private ContextMenu BackgroundMenu => new();

		private ContextMenu SingleNodeMenu {
			get {
				var menu = new ContextMenu {
					Items = {
						NewMenuItem("删除节点", () => DeleteNodes(SelectedNodes))
					}
				};
				return menu;
			}
		}

		private ContextMenu MultipleNodesMenu
			=> new() {
				Items = {
					NewMenuItem("删除多个节点", () => DeleteNodes(SelectedNodes)),
					NewMenuItem("删除关系", () => DeleteEdges(SelectedNodes))
				}
			};
		#endregion
		#endregion

		#region Events
		public event EventHandler GraphChanged = delegate { };
		#endregion

		#region Methods
		#region Event Handlers
		private void GraphControlMouseRightButtonUp(object sender, RoutedEventArgs args) {
			(SelectedNodes.Count switch {
				0  => BackgroundMenu,
				1  => SingleNodeMenu,
				>1 => MultipleNodesMenu,
				_  => throw new ArgumentOutOfRangeException()
			}).IsOpen = true;
		}

		private void LoadGraphButtonClick(object sender, RoutedEventArgs args) {
			_openFileDialog.Title = "选择关系图文件";
			_openFileDialog.Filter = "关系图文件|*.msagl";
			if (_openFileDialog.ShowDialog() == true)
				Graph = Graph.Read(_openFileDialog.FileName);
		}

		private void SaveGraphButtonClick(object sender, RoutedEventArgs args) {
			_saveFileDialog.Title = "保存关系图文件";
			_saveFileDialog.Filter = "关系图文件|*.msagl";
			if (_saveFileDialog.ShowDialog() == true)
				Graph.WriteToStream(_saveFileDialog.OpenFile());
		}

		private void UndoButtonClick(object sender, RoutedEventArgs args) {
			if (Viewer.LayoutEditor.CanUndo)
				Viewer.LayoutEditor.Undo();
		}

		private void RedoButtonClick(object sender, RoutedEventArgs args) {
			if (Viewer.LayoutEditor.CanRedo)
				Viewer.LayoutEditor.Redo();
		}

		private void RefreshLayoutButtonClick(object sender, RoutedEventArgs args) {
			foreach (var vNode in ViewerNodes)
				vNode.Node.Attr.LineWidth = 1;
			SelectedNodes.Clear();
			Viewer.NeedToCalculateLayout = true;
			Viewer.Graph = Viewer.Graph;
			Viewer.NeedToCalculateLayout = false;
			AttachEventToAll();
		}

		private void AddNodeButtonClick(object sender, RoutedEventArgs args) {
			AddNode(NodeNameTextBox.Text);
			NodeNameTextBox.Text = "";
		}

		private void NodeNameTextBoxKeyUp(object sender, KeyEventArgs args) {
			if (args.Key == Key.Enter) {
				AddNode(NodeNameTextBox.Text);
				NodeNameTextBox.Text = "";
			}
		}

		private void DeleteNodeButtonClick(object sender, RoutedEventArgs args) => DeleteNodes(SelectedNodes);

		private void AddEdgeButtonClick(object sender, RoutedEventArgs args) {
			Viewer.InsertingEdge = AddEdgeButton.IsChecked == true;
			if (AddEdgeButton.IsChecked == true)
				Viewer.LayoutEditor.PrepareForEdgeDragging();
			else
				Viewer.LayoutEditor.ForgetEdgeDragging();
		}

		private void DeleteEdgeButtonClick(object sender, RoutedEventArgs args) => DeleteEdges(SelectedNodes);

		private void AutoHighlightButtonClick(object sender, RoutedEventArgs args) {
			if (AutoHighlightButton.IsChecked == true) {
				if (SelectedNodes.Count == 1)
					StartHighlighting();
			}
			else if (Highlighting)
				StopHighlighting();
		}

		private void SortByRelativityButtonClick(object sender, RoutedEventArgs args) {
			var resultWindow = new SortResultWindow(Graph);
			resultWindow.ShowDialog();
		}

		private void SelectedNodesCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
			RefreshToolbar();
			if (AutoHighlightButton.IsChecked == true) {
				if (SelectedNodes.Count == 1)
					StartHighlighting();
				else if (SelectedNodes.Count != 1 && Highlighting)
					StopHighlighting();
			}
		}
		#endregion

		#region Utilities
		private static MenuItem NewMenuItem(string header, RoutedEventHandler onClick) {
			var result = new MenuItem {Header = header};
			result.Click += onClick;
			return result;
		}

		private static MenuItem NewMenuItem(string header, Action onClick) => NewMenuItem(header, (_, _) => onClick());

		private void AttachEvent(IViewerNode viewerNode) {
			viewerNode.MarkedForDraggingEvent += (_, _) => { SelectedNodes.Add(viewerNode); };
			viewerNode.UnmarkedForDraggingEvent += (_, _) => SelectedNodes.Remove(viewerNode);
		}

		private void AttachEventToAll() {
			foreach (var vNode in ViewerNodes)
				AttachEvent(vNode);
		}
		#endregion

		#region Render
		private void RefreshToolbar() {
			DeleteNodeButton.IsEnabled = SelectedNodes.Count > 0;
			DeleteEdgeButton.IsEnabled = SelectedNodes.Count > 1;
		}

		/// <summary>
		/// Render nodes by their relationship
		/// </summary>
		private void HighlightRelationship() {
			foreach (var node in Graph.Nodes)
				node.Attr.Color = node.Label.FontColor = (int)node.UserData switch {
					-2 => Color.Black,
					-1 => Color.Cyan,
					0  => Color.Gray,
					>0 => Color.Yellow,
					_  => throw new ArgumentOutOfRangeException()
				};
		}

		/// <summary>
		/// Hide nodes highlight
		/// </summary>
		private void HideRelationship() {
			foreach (var node in Graph.Nodes)
				node.Attr.Color = node.Label.FontColor = Color.Black;
		}

		private void StartHighlighting() {
			CalculateRelativity(SelectedNodes.Single().Node);
			HighlightRelationship();
			Highlighting = true;
		}

		private void StopHighlighting() {
			HideRelationship();
			Highlighting = false;
		}

		private void RefreshHighlight() {
			if (Highlighting) {
				CalculateRelativity(SelectedNodes.Single().Node);
				HighlightRelationship();
			}
		}
		#endregion

		#region Graph Manipulation
		private bool AddNode(string name, Point? point = null) {
			try {
				var node = new Node(name);
				var viewerNode = point is null ? Viewer.CreateIViewerNode(node) : Viewer.CreateIViewerNode(node, point.Value, null);
				AttachEvent(viewerNode);
				Viewer.AddNode(viewerNode, true);
				GraphChanged(this, EventArgs.Empty);
				return true;
			}
			catch {
				return false;
			}
		}

		private void DeleteNodes(IEnumerable<IViewerNode> nodes) {
			foreach (var node in nodes) {
				foreach (var edge in node.InEdges.Concat(node.OutEdges).Concat(node.SelfEdges))
					Viewer.RemoveEdge(edge, true);
				Viewer.RemoveNode(node, true);
			}
			GraphChanged(this, EventArgs.Empty);
		}

		private void DeleteEdges(IEnumerable<IViewerNode> nodes) {
			var vNodeArray = nodes.ToArray();
			var nodeArray = vNodeArray.Select(n => n.Node).ToList();
			var changed = false;
			foreach (var node in vNodeArray) {
				foreach (var edge in node.SelfEdges) {
					changed = true;
					Viewer.RemoveEdge(edge, true);
				}
				foreach (var edge in node.OutEdges)
					if (nodeArray.Contains(edge.Edge.TargetNode)) {
						changed = true;
						Viewer.RemoveEdge(edge, true);
					}
			}
			if (changed)
				GraphChanged(this, EventArgs.Empty);
		}
		#endregion

		#region Algortithm
		/// <summary>
		/// Calculate node relativity to <paramref name="target"/> of all nodes in the graph
		/// </summary>
		/// <param name="target"></param>
		private void CalculateRelativity(Node target) {
			foreach (var node in Graph.Nodes)
				node.UserData = 0;
			target.UserData = -2;
			HashSet<Node> directNodes = new();
			foreach (var edge in target.InEdges) {
				edge.SourceNode.UserData = -1;
				directNodes.Add(edge.SourceNode);
			}
			foreach (var edge in target.OutEdges) {
				edge.TargetNode.UserData = -1;
				directNodes.Add(edge.TargetNode);
			}
			foreach (var nd in directNodes.Select(
					node => node.InEdges.Select(e => e.SourceNode)
						.Concat(node.OutEdges.Select(e => e.TargetNode))
						.Where(n => (int)n.UserData >= 0)
						.ToHashSet()
				)
				.SelectMany(nodes => nodes))
				nd.UserData = (int)nd.UserData + 1;
		}
		#endregion
		#endregion
	}
}