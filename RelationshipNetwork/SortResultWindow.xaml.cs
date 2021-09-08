using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
			SourceInitialized += (_, _) => this.HideMinimizeAndMaximizeButtons();
		}

		public List<ListRow> List { get; }
	}

	public record ListRow(string Name, int Relativity);

	internal static class WindowExtensions {
		private const int GWL_STYLE = -16;

		private const int WS_MAXIMIZEBOX = 0x10000;

		private const int WS_MINIMIZEBOX = 0x20000;

		[DllImport("user32.dll")]
		private static extern int GetWindowLong(IntPtr hWnd, int index);

		[DllImport("user32.dll")]
		private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

		internal static void HideMinimizeAndMaximizeButtons(this Window window) {
			var hWnd = new WindowInteropHelper(window).Handle;
			int currentStyle = GetWindowLong(hWnd, GWL_STYLE);

			SetWindowLong(hWnd, GWL_STYLE, currentStyle & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
		}
	}
}