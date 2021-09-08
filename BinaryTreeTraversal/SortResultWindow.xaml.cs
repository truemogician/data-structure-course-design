using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Msagl.Drawing;

namespace RelationshipNetwork {
	/// <summary>
	///     Interaction logic for SortResultWindow.xaml
	/// </summary>
	public partial class SortResultWindow : Window {
		public SortResultWindow(Graph graph) {
			List = graph.Nodes.Where(node => (int)node.UserData > 0).Select(node => new ListRow(node.Id, (int)node.UserData)).ToList();
			List.Sort((a, b) => b.Relativity - a.Relativity);
			InitializeComponent();
		}

		public List<ListRow> List { get; }
	}

	public record ListRow(string Name, int Relativity);
}