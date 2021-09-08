using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
		private static readonly FieldInfo ViewerFieldInfo = typeof(AutomaticGraphLayoutControl).GetField("_graphViewer", BindingFlags.NonPublic | BindingFlags.Instance);

		private readonly OpenFileDialog _openFileDialog = new();

		private readonly SaveFileDialog _saveFileDialog = new();

		private bool _highlighting;

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

			Loaded += MainWindow_Loaded;
			GraphChanged += (_, _) => RefreshHighlight();
			GraphControl.MouseRightButtonUp += (_, _) => {
				(SelectedNodes.Count switch {
					0  => BackgroundMenu,
					1  => SingleNodeMenu,
					>1 => MultipleNodesMenu,
					_  => throw new ArgumentOutOfRangeException()
				}).IsOpen = true;
			};
			LoadGraphButton.Click += (_, _) => {
				_openFileDialog.Title = "选择关系图文件";
				_openFileDialog.Filter = "关系图文件|*.msagl";
				if (_openFileDialog.ShowDialog() == true)
					Graph = Graph.Read(_openFileDialog.FileName);
			};
			SaveGraphButton.Click += (_, _) => {
				_saveFileDialog.Title = "保存关系图文件";
				_saveFileDialog.Filter = "关系图文件|*.msagl";
				if (_saveFileDialog.ShowDialog() == true)
					Graph.WriteToStream(_saveFileDialog.OpenFile());
			};
			UndoButton.Click += (_, _) => {
				if (Viewer.LayoutEditor.CanUndo)
					Viewer.LayoutEditor.Undo();
			};
			RedoButton.Click += (_, _) => {
				if (Viewer.LayoutEditor.CanRedo)
					Viewer.LayoutEditor.Redo();
			};
			RefreshLayoutButton.Click += (_, _) => {
				foreach (var vNode in ViewerNodes)
					vNode.Node.Attr.LineWidth = 1;
				SelectedNodes.Clear();
				Viewer.NeedToCalculateLayout = true;
				Viewer.Graph = Viewer.Graph;
				Viewer.NeedToCalculateLayout = false;
				AttachEventToAll();
			};
			AddNodeButton.Click += (_, _) => {
				AddNode(NodeNameTextBox.Text);
				NodeNameTextBox.Text = "";
			};
			NodeNameTextBox.KeyUp += (_, args) => {
				if (args.Key == Key.Enter) {
					AddNode(NodeNameTextBox.Text);
					NodeNameTextBox.Text = "";
				}
			};
			DeleteNodeButton.Click += (_, _) => DeleteNodes(SelectedNodes);
			AddEdgeButton.Click += (_, _) => {
				Viewer.InsertingEdge = AddEdgeButton.IsChecked == true;
				if (AddEdgeButton.IsChecked == true)
					Viewer.LayoutEditor.PrepareForEdgeDragging();
				else
					Viewer.LayoutEditor.ForgetEdgeDragging();
			};
			DeleteEdgeButton.Click += (_, _) => DeleteEdges(SelectedNodes);
			AutoHighlightButton.Click += (_, _) => {
				if (AutoHighlightButton.IsChecked == true) {
					if (SelectedNodes.Count == 1)
						StartHighlighting();
				}
				else if (Highlighting)
					StopHighlighting();
			};
			SortByRelativityButton.Click += (_, _) => {
				var resultWindow = new SortResultWindow(Graph);
				resultWindow.ShowDialog();
			};

			SelectedNodes.CollectionChanged += (_, _) => {
				RefreshToolbar();
				if (AutoHighlightButton.IsChecked == true) {
					if (SelectedNodes.Count == 1)
						StartHighlighting();
					else if (SelectedNodes.Count != 1 && Highlighting)
						StopHighlighting();
				}
			};
		}

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

		public bool Highlighting {
			get => _highlighting;
			private set {
				_highlighting = value;
				SortByRelativityButton.IsEnabled = value;
			}
		}

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

		public event EventHandler GraphChanged = delegate { };

		private static MenuItem NewMenuItem(string header, RoutedEventHandler onClick) {
			var result = new MenuItem {Header = header};
			result.Click += onClick;
			return result;
		}

		private static MenuItem NewMenuItem(string header, Action onClick) => NewMenuItem(header, (_, _) => onClick());

		private void RefreshToolbar() {
			DeleteNodeButton.IsEnabled = SelectedNodes.Count > 0;
			DeleteEdgeButton.IsEnabled = SelectedNodes.Count > 1;
		}

		private void AttachEvent(IViewerNode viewerNode) {
			viewerNode.MarkedForDraggingEvent += (_, _) => { SelectedNodes.Add(viewerNode); };
			viewerNode.UnmarkedForDraggingEvent += (_, _) => SelectedNodes.Remove(viewerNode);
		}

		private void AttachEventToAll() {
			foreach (var vNode in ViewerNodes)
				AttachEvent(vNode);
		}

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

		private void HighlightRelationship() {
			foreach (var node in Graph.Nodes)
				node.Attr.Color = (int)node.UserData switch {
					-2 => Color.Black,
					-1 => Color.Cyan,
					0  => Color.Gray,
					>0 => Color.Yellow,
					_  => throw new ArgumentOutOfRangeException()
				};
		}

		private void HideRelationship() {
			foreach (var node in Graph.Nodes)
				node.Attr.Color = Color.Black;
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

		private void MainWindow_Loaded(object sender, RoutedEventArgs e) { }
	}
}