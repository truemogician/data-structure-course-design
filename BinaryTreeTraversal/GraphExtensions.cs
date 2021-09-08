using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.WpfGraphControl;
using Edge = Microsoft.Msagl.Drawing.Edge;
using Node = Microsoft.Msagl.Drawing.Node;

namespace BinaryTreeTraversal {
	public static class GraphExtensions {
		public delegate int LeftRightDecider(Node nodeA, Node nodeB, Node parent, Graph graph);

		public static Node GetTreeRoot(this Graph graph) {
			var nodeIds = graph.Nodes.Select(node => node.Id);
			var nodes = new Dictionary<string, NodeInfo>();
			foreach (string id in nodeIds)
				nodes[id] = new NodeInfo(null, 0);
			foreach (var edge in graph.Edges) {
				if (nodes[edge.Target].Parent != null)
					throw new InvalidOperationException($"结点{edge.Target}含有多条入边");
				nodes[edge.Target].Parent = edge.Source;
			}
			int rootCount = nodes.Count(pair => pair.Value.Parent is null);
			string rootId = rootCount switch {
				0   => throw new InvalidOperationException("图中存在环"),
				1   => nodes.Single(pair => pair.Value.Parent is null).Key,
				> 1 => throw new InvalidOperationException("图中存在多个根节点"),
				_   => throw new ArgumentOutOfRangeException()
			};
			var tag = 0;
			foreach ((string nodeId, var nodeInfo) in nodes) {
				if (nodeInfo.Tag > 0)
					continue;
				++tag;
				string curId = nodeId;
				while (true) {
					var curInfo = nodes[curId];
					if (curInfo.Tag == 0)
						curInfo.Tag = tag;
					else if (curInfo.Tag == tag)
						throw new InvalidOperationException("图中存在环");
					else
						break;
					if (curInfo.Parent is null)
						break;
					curId = curInfo.Parent;
				}
			}
			return graph.NodeMap[rootId] as Node;
		}

		public static Node BuildBinaryTree(this Graph graph, LeftRightDecider leftRightDecider, bool addLeftRightConstraint = true) {
			var root = GetTreeRoot(graph);
			if (addLeftRightConstraint)
				graph.LayerConstraints.RemoveAllConstraints();
			var invalidNode = graph.Nodes.FirstOrDefault(node => node.OutEdges.Count() > 2);
			if (invalidNode is not null)
				throw new InvalidOperationException($"结点{invalidNode.Id}拥有{invalidNode.OutEdges.Count()}个子节点");
			root.UserData = new BinaryInfo();
			var queue = new Queue<Node>();
			queue.Enqueue(root);
			while (queue.Count > 0) {
				var node = queue.Dequeue();
				var children = node.OutEdges.Select(edge => edge.TargetNode).ToArray();
				if (children.Length == 1) {
					children[0].UserData = new BinaryInfo(node);
					(node.UserData as BinaryInfo)!.LeftChild = children[0];
					queue.Enqueue(children[0]);
				}
				else if (children.Length == 2) {
					Node left, right;
					if (leftRightDecider(children[0], children[1], node, graph) < 0)
						(left, right) = (children[0], children[1]);
					else
						(left, right) = (children[1], children[0]);
					var info = node.UserData as BinaryInfo;
					info!.LeftChild = left;
					left.UserData = new BinaryInfo(node);
					queue.Enqueue(left);
					info.RightChild = right;
					right.UserData = new BinaryInfo(node);
					queue.Enqueue(right);
					graph.LayerConstraints.AddLeftRightConstraint(left, right);
				}
			}
			return root;
		}

		private static void Threadify(this Node node, ref Node prior, TraversalOrder order) {
			var nodeInfo = node.UserData as BinaryInfo;
			void SelfThreadify(ref Node prior) {
				var priorInfo = prior?.UserData as BinaryInfo;
				if (!nodeInfo!.IsLeftThread && nodeInfo.LeftChild is null) {
					nodeInfo.IsLeftThread = true;
					nodeInfo.LeftChild = prior;
				}
				if (prior is not null && !nodeInfo.IsRightThread && priorInfo!.RightChild is null) {
					priorInfo.IsRightThread = true;
					priorInfo.RightChild = node;
				}
				prior = node;
			}
			if (order == TraversalOrder.PreOrder)
				SelfThreadify(ref prior);
			if (nodeInfo!.HasLeftChild)
				nodeInfo.LeftChild.Threadify(ref prior, order);
			if (order == TraversalOrder.InOrder)
				SelfThreadify(ref prior);
			if (nodeInfo.HasRightChild)
				nodeInfo.RightChild.Threadify(ref prior, order);
			if (order == TraversalOrder.PostOrder)
				SelfThreadify(ref prior);
		}

		public static void Threadify(this Node root, TraversalOrder order) {
			Node prior = null;
			root.Threadify(ref prior, order);
			if (order != TraversalOrder.PostOrder)
				(prior.UserData as BinaryInfo)!.IsRightThread = true;
		}

		public static void Unthreadify(this Node node) {
			var info = node.UserData as BinaryInfo;
			if (info!.LeftChild is not null) {
				if (info.IsLeftThread) {
					info.IsLeftThread = false;
					info.LeftChild = null;
				}
				else
					info.LeftChild.Unthreadify();
			}
			if (info!.RightChild is not null) {
				if (info.IsRightThread) {
					info.IsRightThread = false;
					info.RightChild = null;
				}
				else
					info.RightChild.Unthreadify();
			}
		}

		public static void AddLeftRightConstraints(this LayerConstraints constraints, params Node[] nodes) {
			for (var i = 0; i < nodes.Length - 1; ++i)
				constraints.AddLeftRightConstraint(nodes[i], nodes[i + 1]);
		}

		public static IViewerEdge CreateIViewerEdge(this GraphViewer viewer, Edge edge) {
			edge.GeometryObject = new Microsoft.Msagl.Core.Layout.Edge(edge.SourceNode.GeometryNode, edge.TargetNode.GeometryNode);
			edge.SourcePort = new FloatingPort(edge.SourceNode.GeometryNode.BoundaryCurve, edge.SourceNode.GeometryNode.Center);
			edge.TargetPort = new FloatingPort(edge.TargetNode.GeometryNode.BoundaryCurve, edge.TargetNode.GeometryNode.Center);
			return viewer.RouteEdge(edge);
		}

		public static void AddLeftRightConstraints(this LayerConstraints constraints, IEnumerable<Node> nodes) => AddLeftRightConstraints(constraints, nodes.ToArray());

		private record NodeInfo(string Parent, int Tag) {
			public string Parent { get; set; } = Parent;

			public int Tag { get; set; } = Tag;
		}
	}
}