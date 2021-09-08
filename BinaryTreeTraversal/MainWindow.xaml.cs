using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.WpfGraphControl;
using Microsoft.Win32;
using Point = Microsoft.Msagl.Core.Geometry.Point;

namespace BinaryTreeTraversal {
	public partial class MainWindow {
		#region Constructors
		public MainWindow() {
			InitializeComponent();
			SelectedNodes = new ObservableCollection<IViewerNode>();
			GraphControl.Graph = new Graph();
			//Retrieve private field by reflection
			Viewer = ViewerFieldInfo.GetValue(GraphControl) as GraphViewer;
			Viewer!.LayoutEditor.ChangeInUndoRedoList += (_, _) => _upToDate = false;
			SelectedNodes.CollectionChanged += (_, _) => RefreshToolbar();
			GraphChange += OnGraphChanged;

			//ToolBar button events assignment
			GraphControl.Loaded += GraphControlLoaded;
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
			HighlightLeafButton.Click += HighlightLeafButtonClick;
			PreorderButton.Click += GetTraversalButtonClickHandler(TraversalOrder.PreOrder);
			InorderButton.Click += GetTraversalButtonClickHandler(TraversalOrder.InOrder);
			PostorderButton.Click += GetTraversalButtonClickHandler(TraversalOrder.PostOrder);
		}
		#endregion

		#region Events
		/// <summary>
		///     Triggers when the layout or structure of the graph is updated
		/// </summary>
		public event EventHandler GraphChange = delegate { };
		#endregion

		#region Fields
		private static readonly FieldInfo ViewerFieldInfo = typeof(AutomaticGraphLayoutControl).GetField("_graphViewer", BindingFlags.NonPublic | BindingFlags.Instance);

		private readonly OpenFileDialog _openFileDialog = new();

		private readonly SaveFileDialog _saveFileDialog = new();

		private readonly List<IViewerEdge> _threadEdges = new();

		private Exception _buildTreeException;

		private Node _treeRoot;

		private bool _upToDate;
		#endregion

		#region Properties
		public GraphViewer Viewer { get; }

		public Graph Graph {
			get => GraphControl.Graph;
			private set {
				value.Attr.LayerDirection = LayerDirection.TB;
				value.Attr.BackgroundColor = Color.Transparent;
				value.LayoutAlgorithmSettings.NodeSeparation = 30;
				GraphControl.Graph = value;
				GraphChange(this, EventArgs.Empty);
				if (Viewer is not null)
					AttachEventToAll();
			}
		}

		public Node TreeRoot {
			get {
				if (_upToDate)
					return _treeRoot;
				try {
					_upToDate = true;
					_treeRoot = Graph.BuildBinaryTree(
						(a, b, _, _) => (a.BoundingBox.Center.X - b.BoundingBox.Center.X) switch {
							< 0 => -1,
							0   => 0,
							> 0 => 1,
							_   => throw new ArgumentOutOfRangeException()
						}
					);
					_buildTreeException = null;
				}
				catch (Exception exception) {
					_treeRoot = null;
					_buildTreeException = exception;
				}
				return _treeRoot;
			}
		}

		public IEnumerable<IViewerNode> ViewerNodes => Viewer.Entities.OfType<IViewerNode>();

		public ObservableCollection<IViewerNode> SelectedNodes { get; }

		private bool Traversing { get; set; }

		#region Context Menu
		private ContextMenu BackgroundMenu => new();

		private ContextMenu SingleNodeMenu {
			get {
				var menu = new ContextMenu {
					Items = {
						NewMenuItem("删除节点", () => DeleteNodes(SelectedNodes)),
						NewMenuItem("删除自环", () => DeleteEdges(SelectedNodes))
					}
				};
				return menu;
			}
		}

		private ContextMenu MultipleNodesMenu
			=> new() {
				Items = {
					NewMenuItem("删除多个节点", () => DeleteNodes(SelectedNodes)),
					NewMenuItem("删除边", () => DeleteEdges(SelectedNodes))
				}
			};
		#endregion
		#endregion

		#region Methods
		#region Event Methods
		private void OnGraphChanged(object sender, EventArgs args) {
			_upToDate = false;
			// Rerender leaf nodes
			if (HighlightLeafButton.IsChecked == true) {
				if (TreeRoot is null) {
					StatusBarTextBlock.Text = $"当前图形不是二叉树：{_buildTreeException.Message}";
					foreach (var node in Graph.Nodes)
						node.Attr.Color = node.Label.FontColor = Color.Black;
					return;
				}
				var count = 0;
				foreach (var node in Graph.Nodes) {
					var info = node.UserData as BinaryInfo;
					if (!info!.HasLeftChild && !info.HasRightChild) {
						++count;
						node.Attr.Color = node.Label.FontColor = Color.Violet;
					}
					else
						node.Attr.Color = node.Label.FontColor = Color.Black;
				}
				StatusBarTextBlock.Text = $"共有{count}个叶子结点";
			}
		}

		private void GraphControlLoaded(object sender, RoutedEventArgs args) => Graph = Graph.Read("Examples/Tree.msagl");

		/// <summary>
		///     Show context menu according to the number of selected nodes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void GraphControlMouseRightButtonUp(object sender, MouseButtonEventArgs args) {
			(SelectedNodes.Count switch {
				0   => BackgroundMenu,
				1   => SingleNodeMenu,
				> 1 => MultipleNodesMenu,
				_   => throw new ArgumentOutOfRangeException()
			}).IsOpen = true;
		}

		/// <summary>
		///     Open graph file through OpenFileDialog
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void LoadGraphButtonClick(object sender, RoutedEventArgs args) {
			_openFileDialog.Title = "选择关系图文件";
			_openFileDialog.Filter = "关系图文件|*.msagl";
			if (_openFileDialog.ShowDialog() == true)
				Graph = Graph.Read(_openFileDialog.FileName);
		}

		/// <summary>
		///     Save graph file through SaveFileDialog
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void SaveGraphButtonClick(object sender, RoutedEventArgs args) {
			_saveFileDialog.Title = "保存关系图文件";
			_saveFileDialog.Filter = "关系图文件|*.msagl";
			if (_saveFileDialog.ShowDialog() == true)
				Graph.WriteToStream(_saveFileDialog.OpenFile());
		}

		/// <summary>
		///     Undo layout changes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void UndoButtonClick(object sender, RoutedEventArgs args) {
			if (Viewer.LayoutEditor.CanUndo)
				Viewer.LayoutEditor.Undo();
		}

		/// <summary>
		///     Redo layout changes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void RedoButtonClick(object sender, RoutedEventArgs args) {
			if (Viewer.LayoutEditor.CanRedo)
				Viewer.LayoutEditor.Redo();
		}

		/// <summary>
		///     Rebuild tree and refresh layout
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void RefreshLayoutButtonClick(object sender, RoutedEventArgs args) {
			var _ = TreeRoot;
			RefreshLayout();
		}

		/// <summary>
		///     Add node when ENTER is pressed
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void NodeNameTextBoxKeyUp(object sender, KeyEventArgs args) {
			if (args.Key == Key.Enter) {
				AddNode(NodeNameTextBox.Text);
				NodeNameTextBox.Text = "";
			}
		}

		/// <summary>
		///     Add node
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void AddNodeButtonClick(object sender, RoutedEventArgs args) {
			AddNode(NodeNameTextBox.Text);
			NodeNameTextBox.Text = "";
		}

		/// <summary>
		///     Delete selected nodes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void DeleteNodeButtonClick(object sender, RoutedEventArgs args) => DeleteNodes(SelectedNodes);

		/// <summary>
		///     Enter adding edge mode
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void AddEdgeButtonClick(object sender, RoutedEventArgs args) {
			Viewer.InsertingEdge = AddEdgeButton.IsChecked == true;
			if (AddEdgeButton.IsChecked == true) {
				_upToDate = false;
				Viewer.LayoutEditor.PrepareForEdgeDragging();
			}
			else {
				Viewer.LayoutEditor.ForgetEdgeDragging();
				GraphChange(this, EventArgs.Empty);
			}
		}

		/// <summary>
		///     Delete all edges among selected nodes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		private void DeleteEdgeButtonClick(object sender, RoutedEventArgs args) => DeleteEdges(SelectedNodes);

		private static MenuItem NewMenuItem(string header, RoutedEventHandler onClick) {
			var result = new MenuItem {Header = header};
			result.Click += onClick;
			return result;
		}

		private void HighlightLeafButtonClick(object sender, RoutedEventArgs args) {
			if (TreeRoot is null)
				return;
			if (HighlightLeafButton.IsChecked == true)
				OnGraphChanged(this, EventArgs.Empty);
			else {
				StatusBarTextBlock.Text = "";
				foreach (var node in Graph.Nodes)
					node.Attr.Color = node.Label.FontColor = Color.Black;
			}
		}

		private RoutedEventHandler GetTraversalButtonClickHandler(TraversalOrder order) {
			return (_, _) => {
				ToolBar.IsEnabled = false;
				Traversing = true;
				IEnumerable<Node> nodes;
				try {
					nodes = ThreadifyButton.IsChecked == true ? TraverseWithThread(order) : Traverse(order);
				}
				catch (Exception ex) {
					MessageBox.Show(ex.Message);
					ToolBar.IsEnabled = true;
					Traversing = false;
					return;
				}
				Task.Run(
					async () => {
						Dispatcher.Invoke(
							() => {
								if (ThreadifyButton.IsChecked == true) {
									TreeRoot.Threadify(order);
									ShowThread();
								}
							}
						);
						Node preNode = null;
						var preColor = Color.Black;
						foreach (var node in nodes) {
							if (preNode is not null) {
								var copy = preNode;
								Dispatcher.Invoke(() => copy.Attr.Color = copy.Label.FontColor = preColor);
							}
							Dispatcher.Invoke(
								() => {
									preColor = node.Attr.Color;
									return node.Attr.Color = node.Label.FontColor = Color.Yellow;
								}
							);
							preNode = node;
							await Task.Delay(500);
						}
						if (preNode is not null) {
							var copy = preNode;
							Dispatcher.Invoke(() => copy.Attr.Color = copy.Label.FontColor = preColor);
						}
						Dispatcher.Invoke(
							() => {
								if (ThreadifyButton.IsChecked == true) {
									HideThread();
									TreeRoot.Unthreadify();
								}
								ToolBar.IsEnabled = true;
							}
						);
						Traversing = false;
					}
				);
			};
		}
		#endregion

		#region Utility Methods
		private static MenuItem NewMenuItem(string header, Action onClick) => NewMenuItem(header, (_, _) => onClick());
		#endregion

		#region Viewer Methods
		/// <summary>
		///     Attach mark and unmark event to given viewer node
		/// </summary>
		/// <param name="viewerNode"></param>
		private void AttachEvent(IViewerNode viewerNode) {
			viewerNode.MarkedForDraggingEvent += (_, _) => SelectedNodes.Add(viewerNode);
			viewerNode.UnmarkedForDraggingEvent += (_, _) => SelectedNodes.Remove(viewerNode);
		}

		/// <summary>
		///     Attach mark and unmark event to all viewer node of the current graph
		/// </summary>
		private void AttachEventToAll() {
			foreach (var vNode in ViewerNodes)
				AttachEvent(vNode);
		}

		/// <summary>
		///     Refresh the enablility of some toolbar buttons
		/// </summary>
		private void RefreshToolbar() {
			DeleteNodeButton.IsEnabled = SelectedNodes.Count > 0;
			DeleteEdgeButton.IsEnabled = SelectedNodes.Count > 1;
		}

		/// <summary>
		///     Refresh the entire layout
		/// </summary>
		private void RefreshLayout() {
			foreach (var vNode in ViewerNodes)
				vNode.Node.Attr.LineWidth = 1;
			SelectedNodes.Clear();
			Viewer.NeedToCalculateLayout = true;
			Viewer.Graph = Viewer.Graph;
			var edges = Graph.Edges.ToArray();
			foreach (var edge in edges)
				Graph.RemoveEdge(edge);
			foreach (var edge in edges)
				Viewer.AddEdge(Viewer.CreateIViewerEdge(new Edge(edge.SourceNode, edge.TargetNode, ConnectionToGraph.Connected)), false);
			Viewer.Graph = Viewer.Graph;
			Viewer.NeedToCalculateLayout = false;
			AttachEventToAll();
		}

		/// <summary>
		///     Render threaded edges after the tree is built and threadified
		/// </summary>
		private void ShowThread() {
			if (TreeRoot is null)
				return;
			Queue<Node> queue = new();
			queue.Enqueue(TreeRoot);
			while (queue.Count > 0) {
				var node = queue.Dequeue();
				var info = node.UserData as BinaryInfo;
				if (info!.IsLeftThread && info.LeftChild is not null) {
					var edge = new Edge(node, info.LeftChild, ConnectionToGraph.Connected);
					edge.Attr.Color = Color.Green;
					edge.Attr.AddStyle(Microsoft.Msagl.Drawing.Style.Dashed);
					var viewerEdge = Viewer.CreateIViewerEdge(edge);
					Viewer.AddEdge(viewerEdge, false);
					_threadEdges.Add(viewerEdge);
				}
				else if (info.HasLeftChild)
					queue.Enqueue(info.LeftChild);
				if (info!.IsRightThread && info.RightChild is not null) {
					var edge = new Edge(node, info.RightChild, ConnectionToGraph.Connected);
					edge.Attr.Color = Color.Cyan;
					edge.Attr.AddStyle(Microsoft.Msagl.Drawing.Style.Dashed);
					var viewerEdge = Viewer.CreateIViewerEdge(edge);
					Viewer.AddEdge(viewerEdge, false);
					_threadEdges.Add(viewerEdge);
				}
				else if (info.HasRightChild)
					queue.Enqueue(info.RightChild);
			}
		}

		/// <summary>
		///     Remove threaded edges when traversal animation ends
		/// </summary>
		private void HideThread() {
			foreach (var viewerEdge in _threadEdges)
				Viewer.RemoveEdge(viewerEdge, false);
			_threadEdges.Clear();
		}
		#endregion

		#region Graph Manipulation
		/// <summary>
		///     Add new node
		/// </summary>
		/// <param name="id">Id of the node</param>
		/// <param name="point">The position of the new node to be created</param>
		/// <returns>False if <paramref name="id" /> conflicts with existing nodes</returns>
		private bool AddNode(string id, Point? point = null) {
			try {
				var node = new Node(id) {Attr = {Shape = Shape.Circle}};
				var viewerNode = point is null ? Viewer.CreateIViewerNode(node) : Viewer.CreateIViewerNode(node, point.Value, null);
				AttachEvent(viewerNode);
				Viewer.AddNode(viewerNode, true);
				GraphChange(this, EventArgs.Empty);
				return true;
			}
			catch {
				return false;
			}
		}

		/// <summary>
		///     Delete nodes and all their edges
		/// </summary>
		/// <param name="nodes">Nodes to be deleted</param>
		private void DeleteNodes(IEnumerable<IViewerNode> nodes) {
			foreach (var node in nodes) {
				foreach (var edge in node.InEdges.Concat(node.OutEdges).Concat(node.SelfEdges))
					Viewer.RemoveEdge(edge, true);
				Viewer.RemoveNode(node, true);
			}
			GraphChange(this, EventArgs.Empty);
		}

		/// <summary>
		///     Delete edges among <paramref name="nodes" />
		/// </summary>
		/// <param name="nodes"></param>
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
				GraphChange(this, EventArgs.Empty);
		}
		#endregion

		#region Algorithm Methods
		/// <summary>
		///     Traverse the tree by <paramref name="order" />
		/// </summary>
		/// <param name="order"></param>
		/// <exception cref="InvalidOperationException">Throws if tree building failed</exception>
		private IEnumerable<Node> Traverse(TraversalOrder order) {
			if (TreeRoot is null)
				throw _buildTreeException;
			return Traverse(TreeRoot, order);
		}

		private IEnumerable<Node> Traverse(Node node, TraversalOrder order) {
			if (node is null)
				yield break;
			if (order == TraversalOrder.PreOrder)
				yield return node;
			foreach (var n in Traverse((node.UserData as BinaryInfo)!.LeftChild, order))
				yield return n;
			if (order == TraversalOrder.InOrder)
				yield return node;
			foreach (var n in Traverse((node.UserData as BinaryInfo)!.RightChild, order))
				yield return n;
			if (order == TraversalOrder.PostOrder)
				yield return node;
		}

		/// <summary>
		///     Traverse the tree by <paramref name="order" /> using thread information
		/// </summary>
		/// <param name="order"></param>
		/// <exception cref="InvalidOperationException">Throws if tree building failed</exception>
		private IEnumerable<Node> TraverseWithThread(TraversalOrder order) {
			if (TreeRoot is null)
				throw _buildTreeException;
			return TraverseWithThread(TreeRoot, order);
		}

		private IEnumerable<Node> TraverseWithThread(Node node, TraversalOrder order) {
			if (node is null)
				yield break;
			if (order != TraversalOrder.PreOrder)
				while ((node.UserData as BinaryInfo)!.HasLeftChild)
					node = (node.UserData as BinaryInfo)!.LeftChild;
			while (node is not null) {
				var info = node.UserData as BinaryInfo;
				yield return node;
				switch (order) {
					case TraversalOrder.PreOrder:
						node = info!.HasLeftChild ? info.LeftChild : info.RightChild;
						break;
					case TraversalOrder.InOrder:
						node = info!.RightChild;
						if (!info.IsRightThread && node is not null)
							while ((node.UserData as BinaryInfo)!.HasLeftChild)
								node = (node.UserData as BinaryInfo)!.LeftChild;
						break;
					case TraversalOrder.PostOrder:
						if (info!.IsRightThread)
							node = info.RightChild;
						else if (info.Parent is null)
							node = null;
						else {
							var parentInfo = info.Parent.UserData as BinaryInfo;
							bool isLeftToParent = parentInfo!.HasLeftChild && Equals(node, parentInfo.LeftChild);
							node = info.Parent;
							info = node.UserData as BinaryInfo;
							if (isLeftToParent && !info!.IsRightThread) {
								node = info.RightChild;
								while (true) {
									info = node.UserData as BinaryInfo;
									if (info!.HasLeftChild)
										node = info.LeftChild;
									else if (info.HasRightChild)
										node = info.RightChild;
									else
										break;
								}
							}
						}
						break;
				}
			}
		}
		#endregion
		#endregion
	}

	/// <summary>
	///     Extra data that describes the binary tree structure of a node. Stored in <see cref="Node.UserData" />
	/// </summary>
	public class BinaryInfo {
		public BinaryInfo() { }

		public BinaryInfo(Node parent) => Parent = parent;

		public Node Parent { get; set; }

		public Node LeftChild { get; set; }

		/// <summary>
		///     Whether the left child is a thread
		/// </summary>
		public bool IsLeftThread { get; set; }

		public bool HasLeftChild => LeftChild is not null && !IsLeftThread;

		public Node RightChild { get; set; }

		/// <summary>
		///     Whether the right child is a thread
		/// </summary>
		public bool IsRightThread { get; set; }

		public bool HasRightChild => RightChild is not null && !IsRightThread;
	}

	public enum TraversalOrder : byte {
		PreOrder,

		InOrder,

		PostOrder
	}
}