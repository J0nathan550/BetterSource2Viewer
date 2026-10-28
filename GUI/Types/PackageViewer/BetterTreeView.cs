using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Represents a TreeView of the folders in a package.
    /// </summary>
    partial class BetterTreeView : TreeViewDoubleBuffered
    {
        public VirtualPackageNode? Root;
        public Dictionary<string, int> ExtensionIconList = [];
        public int FolderImage;

        public VrfGuiContext? VrfGuiContext { get; set; }

        // The native tree only knows one selected node (SelectedNode), any others picked with Ctrl or Shift live here
        private readonly List<BetterTreeNode> extraSelectedNodes = [];
        private bool isChangingSelection;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        /// <summary>
        /// Returns every selected node, starting with <see cref="TreeView.SelectedNode"/>.
        /// </summary>
        public List<IBetterBaseItem> GetSelectedItems()
        {
            var items = new List<IBetterBaseItem>();

            if (SelectedNode is BetterTreeNode primary)
            {
                items.Add(primary);
            }

            foreach (var node in extraSelectedNodes)
            {
                // Nodes can be removed from the tree while selected, e.g. when editing a package
                if (node.TreeView == this && node != SelectedNode)
                {
                    items.Add(node);
                }
            }

            return items;
        }

        public bool IsNodeSelected(TreeNode node) => node == SelectedNode || (node is BetterTreeNode betterNode && extraSelectedNodes.Contains(betterNode));

        protected override void OnBeforeSelect(TreeViewCancelEventArgs e)
        {
            base.OnBeforeSelect(e);

            // A plain click, keyboard navigation or code picking a node starts a fresh selection
            if (!e.Cancel && !isChangingSelection)
            {
                ClearExtraSelection();
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;

            if (m.Msg == WM_LBUTTONDOWN)
            {
                var modifiers = ModifierKeys & (Keys.Shift | Keys.Control);

                if (modifiers != Keys.None
                    && HitTest(PointToClient(Cursor.Position)) is { Node: BetterTreeNode node } hit
                    && hit.Location != TreeViewHitTestLocations.PlusMinus)
                {
                    Focus();

                    if ((modifiers & Keys.Shift) != 0)
                    {
                        SelectRange(node);
                    }
                    else
                    {
                        ToggleNodeSelection(node);
                    }

                    return;
                }
            }

            base.WndProc(ref m);
        }

        private void ToggleNodeSelection(BetterTreeNode node)
        {
            if (SelectedNode == null)
            {
                SetPrimarySelection(node);
                return;
            }

            if (node != SelectedNode)
            {
                if (extraSelectedNodes.Contains(node))
                {
                    RemoveExtraSelection(node);
                }
                else
                {
                    AddExtraSelection(node);
                }

                return;
            }

            // The native selection cannot be cleared, so hand it over to another selected node instead
            var next = extraSelectedNodes.Find(other => other.TreeView == this && other != node);

            if (next != null)
            {
                RemoveExtraSelection(next);
                SetPrimarySelection(next);
            }
        }

        private void SelectRange(BetterTreeNode node)
        {
            var anchor = SelectedNode;

            if (anchor == null || anchor == node)
            {
                ClearExtraSelection();
                SetPrimarySelection(node);
                return;
            }

            BeginUpdate();

            try
            {
                ClearExtraSelection();

                var inRange = false;

                for (var current = Nodes.Count > 0 ? Nodes[0] : null; current != null; current = current.NextVisibleNode)
                {
                    var isEdge = current == anchor || current == node;

                    if (isEdge || inRange)
                    {
                        if (current != anchor && current is BetterTreeNode betterNode)
                        {
                            AddExtraSelection(betterNode);
                        }

                        if (isEdge && inRange)
                        {
                            break;
                        }

                        inRange = true;
                    }
                }
            }
            finally
            {
                EndUpdate();
            }
        }

        private void SetPrimarySelection(BetterTreeNode node)
        {
            isChangingSelection = true;

            try
            {
                SelectedNode = node;
            }
            finally
            {
                isChangingSelection = false;
            }
        }

        private void AddExtraSelection(BetterTreeNode node)
        {
            extraSelectedNodes.Add(node);
            node.BackColor = Themer.CurrentThemeColors.HoverAccent;
            node.ForeColor = Themer.CurrentThemeColors.Contrast;
        }

        private void RemoveExtraSelection(BetterTreeNode node)
        {
            extraSelectedNodes.Remove(node);
            node.BackColor = Color.Empty;
            node.ForeColor = Color.Empty;
        }

        private void ClearExtraSelection()
        {
            foreach (var node in extraSelectedNodes)
            {
                node.BackColor = Color.Empty;
                node.ForeColor = Color.Empty;
            }

            extraSelectedNodes.Clear();
        }

        public void GenerateIconList(IEnumerable<string> extensions)
        {
            var defaultImage = AppIcons.Icons["File"];

            foreach (var originalExtension in extensions)
            {
                var image = AppIcons.GetImageIndexForExtension(originalExtension.ToLowerInvariant());

                if (image == defaultImage)
                {
                    continue;
                }

                ExtensionIconList.TryAdd(originalExtension, image);
            }

            FolderImage = AppIcons.Icons["Folder"];
        }

        public static VirtualPackageNode AddFolderNode(VirtualPackageNode currentNode, string directory, uint size)
        {
            var directorySpan = directory.AsSpan();

            foreach (var subPathRange in directorySpan.Split([Package.DirectorySeparatorChar]))
            {
                var subPath = directorySpan[subPathRange];

                if (!currentNode.Folders.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(subPath, out var subNode))
                {
                    var subPathString = subPath.ToString();
                    var toAdd = new VirtualPackageNode(subPathString, size, currentNode);
                    currentNode.Folders.Add(subPathString, toAdd);
                    currentNode = toAdd;
                }
                else
                {
                    currentNode = subNode;
                    currentNode.TotalSize += size;
                }
            }

            return currentNode;
        }

        public static VirtualPackageNode AddFileNode(VirtualPackageNode currentNode, PackageEntry file)
        {
            var directory = file.GetDirectoryPath();

            if (directory.Length > 0)
            {
                currentNode = AddFolderNode(currentNode, directory, file.TotalLength);
            }

            currentNode.Files.Add(file);

            return currentNode;
        }
    }
}
