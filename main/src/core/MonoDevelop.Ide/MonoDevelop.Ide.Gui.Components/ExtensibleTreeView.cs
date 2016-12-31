//
// ExtensibleTreeView.cs
//
// Author:
//   Lluis Sanchez Gual
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2005-2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

//#define TREE_VERIFY_INTEGRITY

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Gdk;
using GLib;
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide.Commands;
using MonoDevelop.Ide.Gui.Pads;
using Pango;
using CairoHelper = Gdk.CairoHelper;
using Color = Gdk.Color;
using Drag = Gtk.Drag;
using Image = Xwt.Drawing.Image;
using Key = Gdk.Key;
using Layout = Pango.Layout;
using Rectangle = Gdk.Rectangle;
using Size = Xwt.Size;
using Style = Gtk.Style;
using Timeout = GLib.Timeout;

[assembly: InternalsVisibleTo("MonoDevelop.UnitTesting")]

namespace MonoDevelop.Ide.Gui.Components
{
    public partial class ExtensibleTreeView : Control, ICommandRouter
    {
        internal const int NodeInfoColumn = 0;
        internal const int DataItemColumn = 1;
        internal const int BuilderChainColumn = 2;
        internal const int FilledColumn = 3;
        internal const int ShowPopupColumn = 4;

        private readonly Dictionary<Type, NodeBuilder[]> builderChains = new Dictionary<Type, NodeBuilder[]>();

        private readonly ExtensibleTreeViewWidget widget;
        private readonly ExtensibleTreeViewTree tree;
        private ZoomableCellRendererPixbuf pixRender;
        private CustomCellRendererText textRender;
        private TreeBuilderContext builderContext;
        private readonly Hashtable callbacks = new Hashtable();
        private bool editingText;

        private TreePadOption[] options;
        private TreeOptions globalOptions;

        private TreeNodeNavigator workNode;
        private TreeNodeNavigator compareNode1;
        private TreeNodeNavigator compareNode2;

        internal bool Sorting;

        private TransactedNodeStore transactionStore;
        private int updateLockCount;
        private string contextMenuPath;

        public IDictionary<string, string> ContextMenuTypeNameAliases { get; set; }

        internal TreeStore Store { get; private set; }

        internal TreeView Tree => tree;

        public event EventHandler SelectionChanged;

        public bool AllowsMultipleSelection
        {
            get { return Tree.Selection.Mode == SelectionMode.Multiple; }
            set { Tree.Selection.Mode = value ? SelectionMode.Multiple : SelectionMode.Single; }
        }

        public string Id { get; set; }


        private class ExtensibleTreeViewWidget : CompactScrolledWindow, ICommandRouter
        {
            private readonly ExtensibleTreeView control;

            public ExtensibleTreeViewWidget(ExtensibleTreeView control)
            {
                this.control = control;
                ShadowType = ShadowType.None;
                ShowBorderLine = false;
            }

            protected override void OnStyleSet(Style previousStyle)
            {
                base.OnStyleSet(previousStyle);
                control.UpdateFont();
            }

            protected override bool OnScrollEvent(EventScroll evnt)
            {
                var modifier = !Platform.IsMac ? ModifierType.ControlMask
                                    //Mac window manager already uses control-scroll, so use command
                                    //Command might be either meta or mod1, depending on GTK version
                                    : (ModifierType.MetaMask | ModifierType.Mod1Mask);

                if ((evnt.State & modifier) != 0)
                {
                    if (evnt.Direction == ScrollDirection.Up)
                        control.ZoomIn();
                    else if (evnt.Direction == ScrollDirection.Down)
                        control.ZoomOut();

                    return true;
                }
                return base.OnScrollEvent(evnt);
            }

            protected override void OnDestroyed()
            {
                control.Destroy();
                base.OnDestroyed();
            }

            public object GetNextCommandTarget()
            {
                return control;
            }
        }

        protected override object CreateNativeWidget<T>()
        {
            return widget;
        }

        public ExtensibleTreeView()
        {
            widget = new ExtensibleTreeViewWidget(this);
            tree = new ExtensibleTreeViewTree(this);
        }

        public ExtensibleTreeView(NodeBuilder[] builders, TreePadOption[] options) : this()
        {
            Initialize(builders, options);
        }

        private void CustomFontPropertyChanged(object sender, EventArgs a)
        {
            UpdateFont();
        }

        private void UpdateFont()
        {
            textRender.CustomFont = IdeApp.Preferences.CustomPadFont ?? tree.Style.FontDescription;
            tree.ColumnsAutosize();
        }

        public void Initialize(NodeBuilder[] builders, TreePadOption[] options, string contextMenuPath = null)
        {
            OnInitialize(builders, options, contextMenuPath);
        }

        protected virtual void OnInitialize(NodeBuilder[] builders, TreePadOption[] options, string contextMenuPath)
        {
            this.contextMenuPath = contextMenuPath;
            builderContext = new TreeBuilderContext(this);

            SetBuilders(builders, options);

            Store = new TreeStore(typeof(NodeInfo), typeof(object), typeof(object), typeof(bool), typeof(bool));
            tree.Model = Store;
            tree.Selection.Mode = SelectionMode.Multiple;

            Store.DefaultSortFunc = CompareNodes;
            Store.SetSortColumnId(/* GTK_TREE_SORTABLE_DEFAULT_SORT_COLUMN_ID */ -1, SortType.Ascending);

            tree.HeadersVisible = false;
            tree.SearchColumn = 0;
            tree.EnableSearch = true;
            CompleteColumn = new TreeViewColumn { Title = "column" };

            pixRender = new ZoomableCellRendererPixbuf { Xpad = 0 };
            CompleteColumn.PackStart(pixRender, false);

            textRender = new CustomCellRendererText(this) { Ypad = 0 };
            IdeApp.Preferences.CustomPadFont.Changed += CustomFontPropertyChanged;
            textRender.EditingStarted += HandleEditingStarted;
            textRender.Edited += HandleOnEdit;
            textRender.EditingCanceled += HandleOnEditCancelled;
            CompleteColumn.PackStart(textRender, true);

            CompleteColumn.SetCellDataFunc(pixRender, SetIconCellData);
            CompleteColumn.SetCellDataFunc(textRender, SetTextCellData);

            tree.AppendColumn(CompleteColumn);

            tree.TestExpandRow += OnTestExpandRow;
            tree.RowActivated += OnNodeActivated;
            tree.DoPopupMenu += ShowPopup;
            workNode = new TreeNodeNavigator(this);
            compareNode1 = new TreeNodeNavigator(this);
            compareNode2 = new TreeNodeNavigator(this);

            tree.CursorChanged += OnSelectionChanged;
            tree.KeyPressEvent += OnKeyPress;
            tree.MotionNotifyEvent += HandleMotionNotifyEvent;
            tree.LeaveNotifyEvent += HandleLeaveNotifyEvent;

            if (GtkGestures.IsSupported)
            {
                tree.AddGestureMagnifyHandler((sender, args) =>
                {
                    Zoom += Zoom * (args.Magnification / 4d);
                });
            }

            for (int n = 3; n < 16; n++)
            {
                Rc.ParseString("style \"MonoDevelop.ExtensibleTreeView_" + n + "\" {\n GtkTreeView::expander-size = " + n + "\n }\n");
                Rc.ParseString("widget \"*.MonoDevelop.ExtensibleTreeView_" + n + "\" style  \"MonoDevelop.ExtensibleTreeView_" + n + "\"\n");
            }

            Zoom = !string.IsNullOrEmpty(Id) ? PropertyService.Get("MonoDevelop.Ide.ExtensibleTreeView.Zoom." + Id, 1d) : 1d;

            widget.Add(tree);
            widget.ShowAll();

#if TREE_VERIFY_INTEGRITY
			GLib.Timeout.Add (3000, Checker);
#endif
        }
#if TREE_VERIFY_INTEGRITY
		// Verifies the consistency of the tree view. Disabled by default
		HashSet<object> ochecked = new HashSet<object> ();
		bool Checker ()
		{
			int nodes = 0;
			foreach (DictionaryEntry e in nodeHash) {
				if (e.Value is Gtk.TreeIter) {
					nodes++;
					if (!store.IterIsValid ((Gtk.TreeIter)e.Value) && ochecked.Add (e.Key)) {
						Console.WriteLine ("Found invalid iter in tree pad - Object: " + e.Key);
						MessageService.ShowError ("Found invalid iter in tree pad", "Object: " + e.Key);
					}
				} else {
					Gtk.TreeIter[] iters = (Gtk.TreeIter[]) e.Value;
					for (int n=0; n<iters.Length; n++) {
						Gtk.TreeIter it = iters [n];
						if (!store.IterIsValid (it) && ochecked.Add (e.Key)) {
							Console.WriteLine ("Found invalid iter in tree pad - Object: " + e.Key + ", index:" + n);
							MessageService.ShowError ("Found invalid iter in tree pad", "Object: " + e.Key + ", index:" + n);
						}
						nodes++;
					}
				}
			}
			return true;
		}
#endif

        private static void SetIconCellData(TreeViewColumn col, CellRenderer renderer, TreeModel model, TreeIter it)
        {
            if (model == null)
                return;

            var info = (NodeInfo)model.GetValue(it, NodeInfoColumn);
            var cell = (ZoomableCellRendererPixbuf)renderer;

            var img = info.Icon != null && info.Icon != CellRendererImage.NullImage && info.DisabledStyle ? info.Icon.WithAlpha(0.5) : info.Icon;
            cell.Image = img;
            cell.ImageExpanderOpen = img;
            cell.ImageExpanderClosed = info.ClosedIcon != null && info.ClosedIcon != CellRendererImage.NullImage && info.DisabledStyle ? info.ClosedIcon.WithAlpha(0.5) : info.ClosedIcon;
            cell.OverlayBottomLeft = info.OverlayBottomLeft;
            cell.OverlayBottomRight = info.OverlayBottomRight;
            cell.OverlayTopLeft = info.OverlayTopLeft;
            cell.OverlayTopRight = info.OverlayTopRight;
        }

        private static void SetTextCellData(TreeViewColumn col, CellRenderer renderer, TreeModel model, TreeIter it)
        {
            if (model == null)
                return;

            var info = (NodeInfo)model.GetValue(it, NodeInfoColumn);
            var cell = (CustomCellRendererText)renderer;

            cell.DisabledStyle = info.DisabledStyle;
            cell.TextMarkup = info.Label;
            cell.SecondaryTextMarkup = info.SecondaryLabel;

            cell.StatusIcon = info.StatusIconInternal;
        }

        public void UpdateBuilders(NodeBuilder[] builders, TreePadOption[] options)
        {
            // Save the current state
            ITreeNavigator root = GetRootNode();
            NodeState state = root?.SaveState();
            object obj = root?.DataItem;

            Clear();

            // Clean cached builder chains
            builderChains.Clear();

            // Update the builders
            SetBuilders(builders, options);

            // Restore the this
            if (obj != null)
                LoadTree(obj);

            root = GetRootNode();
            if (root != null && state != null)
                root.RestoreState(state);
        }

        private void SetBuilders(NodeBuilder[] buildersArray, TreePadOption[] options)
        {
            // Create default options

            List<NodeBuilder> builders = new List<NodeBuilder>();
            foreach (NodeBuilder nb in buildersArray)
            {
                if (!(nb is TreeViewItemBuilder))
                    builders.Add(nb);
            }
            builders.Add(new TreeViewItemBuilder());

            this.options = options;
            globalOptions = new TreeOptions();
            foreach (TreePadOption op in options)
                globalOptions[op.Id] = op.DefaultValue;
            globalOptions.Pad = this;

            // Check that there is only one TypeNodeBuilder per type

            Hashtable bc = new Hashtable();
            foreach (NodeBuilder nb in builders)
            {
                TypeNodeBuilder tnb = nb as TypeNodeBuilder;
                if (tnb != null)
                {
                    if (tnb.UseReferenceEquality)
                        NodeHash.RegisterByRefType(tnb.NodeDataType);
                    TypeNodeBuilder other = (TypeNodeBuilder)bc[tnb.NodeDataType];
                    if (other != null)
                        throw new ApplicationException(
                            $"The type node builder {nb.GetType()} can't be used in this context because the type {tnb.NodeDataType} is already handled by {other.GetType()}");
                    bc[tnb.NodeDataType] = tnb;
                }
                else if (!(nb is NodeBuilderExtension))
                    throw new InvalidOperationException(
                        $"Invalid NodeBuilder type: {nb.GetType()}. NodeBuilders must inherit either from TypeNodeBuilder or NodeBuilderExtension");
            }

            NodeBuilders = builders.ToArray();

            foreach (NodeBuilder nb in builders)
                nb.SetContext(builderContext);
        }

        public void EnableDragUriSource(Func<object, string> nodeToUri)
        {
            tree.EnableDragUriSource(nodeToUri);
        }

        private object[] GetDragObjects(out Image icon)
        {
            ITreeNavigator[] navs = GetSelectedNodes();
            if (navs.Length == 0)
            {
                icon = null;
                return null;
            }
            var dragObjects = new object[navs.Length];
            for (int n = 0; n < navs.Length; n++)
                dragObjects[n] = navs[n].DataItem;
            icon = ((NodeInfo)Store.GetValue(navs[0].CurrentPosition._iter, NodeInfoColumn)).Icon;
            return dragObjects;
        }

        private bool CheckAndDrop(int x, int y, bool drop, DragContext ctx, object[] obj)
        {
            TreePath path;
            TreeViewDropPosition pos;
            if (!tree.GetDestRowAtPos(x, y, out path, out pos))
                return false;

            TreeIter iter;
            if (!Store.GetIter(out iter, path))
                return false;

            TreeNodeNavigator nav = new TreeNodeNavigator(this, iter);
            NodeBuilder[] chain = nav.BuilderChain;
            bool foundHandler = false;

            DragOperation oper = ctx.Action == DragAction.Copy ? DragOperation.Copy : DragOperation.Move;
            DropPosition dropPos;
            if (pos == TreeViewDropPosition.After)
                dropPos = DropPosition.After;
            else if (pos == TreeViewDropPosition.Before)
                dropPos = DropPosition.Before;
            else
                dropPos = DropPosition.Into;

            bool updatesLocked = false;

            try
            {
                foreach (NodeBuilder nb in chain)
                {
                    try
                    {
                        NodeCommandHandler handler = nb.CommandHandler;
                        handler.SetCurrentNode(nav);
                        if (handler.CanDropMultipleNodes(obj, oper, dropPos))
                        {
                            foundHandler = true;
                            if (drop)
                            {
                                if (!updatesLocked)
                                {
                                    LockUpdates();
                                    updatesLocked = true;
                                }
                                handler.OnMultipleNodeDrop(obj, oper, dropPos);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError(ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // We're now in an indeterminate state, so report the exception
                // and exit.
                ExceptionManager.RaiseUnhandledException(ex, true);
                return false;
            }
            finally
            {
                if (updatesLocked)
                    UnlockUpdates();
            }
            return foundHandler;
        }

        [ConnectBefore]
        private void HandleMotionNotifyEvent(object o, MotionNotifyEventArgs args)
        {
            TreePath path;
            int cx, cy;
            TreeViewColumn col;
            bool popupShown = false;

            if (tree.GetPathAtPos((int)args.Event.X, (int)args.Event.Y, out path, out col, out cx, out cy))
            {
                TreeIter it;
                if (Store.GetIter(out it, path))
                {
                    var info = (NodeInfo)Store.GetValue(it, NodeInfoColumn);
                    if (info.StatusIconInternal != CellRendererImage.NullImage && info.StatusIconInternal != null)
                    {
                        var cellArea = tree.GetCellArea(path, tree.Columns[0]);
                        int sp, w;
                        col.CellGetPosition(textRender, out sp, out w);
                        cellArea.X += sp;
                        cellArea.Width = w;
                        var rect = textRender.GetStatusIconArea(tree, cellArea);
                        if (cx >= rect.X && cx <= rect.Right)
                        {
                            ShowStatusMessage(it, rect, info);
                            popupShown = true;
                        }
                    }
                }
            }
            if (!popupShown)
                HideStatusMessage();
        }

        private bool statusMessageVisible;
        private TreeIter statusIconIter;
        private TooltipPopoverWindow statusPopover;

        private void ShowStatusMessage(TreeIter it, Rectangle rect, NodeInfo info)
        {
            if (statusMessageVisible && Store.GetPath(it).Equals(Store.GetPath(statusIconIter)))
                return;
            statusPopover?.Destroy();
            statusMessageVisible = true;
            statusIconIter = it;

            statusPopover = new TooltipPopoverWindow
            {
                ShowArrow = true,
                Text = info.StatusMessage,
                Severity = info.StatusSeverity
            };
            rect.Y += 2;
            statusPopover.ShowPopup(this, rect, PopupPosition.Bottom);
        }

        private void HideStatusMessage()
        {
            if (statusMessageVisible)
            {
                statusMessageVisible = false;
                statusPopover.Destroy();
                statusPopover = null;
            }
        }

        [ConnectBefore]
        private void HandleLeaveNotifyEvent(object o, LeaveNotifyEventArgs args)
        {
            HideStatusMessage();
        }

        internal void LockUpdates()
        {
            if (++updateLockCount == 1)
                transactionStore = new TransactedNodeStore(this);
        }

        internal void UnlockUpdates()
        {
            if (--updateLockCount == 0)
            {
                TransactedNodeStore store = transactionStore;
                transactionStore = null;
                store.CommitChanges();
            }
        }

        internal ITreeBuilder CreateBuilder()
        {
            return CreateBuilder(TreeIter.Zero);
        }

        internal ITreeBuilder CreateBuilder(TreeIter it)
        {
            if (transactionStore != null)
                return new TransactedTreeBuilder(this, transactionStore, it);
            return new TreeBuilder(this, it);
        }

        protected NodeBuilder[] NodeBuilders { get; set; }

        internal TreeViewColumn CompleteColumn { get; private set; }

        private NodeHashtable NodeHash { get; set; } = new NodeHashtable();

        internal ITreeBuilderContext BuilderContext => builderContext;

        internal object[] CopyObjects { get; set; }

        internal DragOperation CurrentTransferOperation { get; private set; }

        public ITreeBuilder LoadTree(object nodeObject)
        {
            Clear();
            TreeBuilder builder = new TreeBuilder(this);
            builder.AddChild(nodeObject, true);
            builder.Expanded = true;
            InitialSelection();
            return builder;
        }

        public ITreeBuilder AddChild(object nodeObject)
        {
            TreeBuilder builder = new TreeBuilder(this);
            builder.AddChild(nodeObject, true);
            builder.Expanded = true;
            InitialSelection();
            return builder;
        }

        public void RemoveChild(object nodeObject)
        {
            TreeBuilder builder = new TreeBuilder(this);
            if (builder.MoveToObject(nodeObject))
            {
                builder.Remove();
                InitialSelection();
            }
        }

        private void InitialSelection()
        {
            if (tree.Selection.CountSelectedRows() == 0)
            {
                TreeIter it;
                if (Store.GetIterFirst(out it))
                {
                    tree.Selection.SelectIter(it);
                    tree.SetCursor(Store.GetPath(it), tree.Columns[0], false);
                }
            }
        }

        public void Clear()
        {
            CopyObjects = tree.DragObjects = null;

            object[] obs = new object[NodeHash.Count];
            NodeHash.Keys.CopyTo(obs, 0);

            foreach (object dataObject in obs)
                NotifyNodeRemoved(dataObject, null);

            NodeHash = new NodeHashtable();
            Store.Clear();
        }

        public ITreeNavigator GetSelectedNode()
        {
            TreePath[] sel = tree.Selection.GetSelectedRows();
            if (sel.Length == 0)
                return null;
            TreeIter iter;
            if (Store.GetIter(out iter, sel[0]))
                return new TreeNodeNavigator(this, iter);
            return null;
        }

        private class SelectionGroup
        {
            public NodeBuilder[] BuilderChain;
            public List<ITreeNavigator> Nodes;
            public TreeStore store;

            private NodePosition[] savedPos;
            private object[] dataItems;

            public object[] DataItems
            {
                get
                {
                    if (dataItems == null)
                    {
                        dataItems = new object[Nodes.Count];
                        for (int n = 0; n < Nodes.Count; n++)
                            dataItems[n] = Nodes[n].DataItem;
                    }
                    return dataItems;
                }
            }

            public void SavePositions()
            {
                savedPos = new NodePosition[Nodes.Count];
                for (int n = 0; n < Nodes.Count; n++)
                    savedPos[n] = Nodes[n].CurrentPosition;
            }

            public bool RestorePositions()
            {
                for (int n = 0; n < Nodes.Count; n++)
                {
                    if (store.IterIsValid(savedPos[n]._iter))
                        Nodes[n].MoveToPosition(savedPos[n]);
                    else
                        return false;
                }
                return true;
            }
        }

        private ICollection<SelectionGroup> GetSelectedNodesGrouped()
        {
            TreePath[] paths = tree.Selection.GetSelectedRows();
            if (paths.Length == 0)
            {
                return new SelectionGroup[0];
            }
            if (paths.Length == 1)
            {
                TreeIter it;
                Store.GetIter(out it, paths[0]);
                SelectionGroup grp = new SelectionGroup();
                TreeNodeNavigator nav = new TreeNodeNavigator(this, it);
                grp.BuilderChain = nav.BuilderChain;
                grp.Nodes = new List<ITreeNavigator> { nav };
                grp.store = Store;
                return new[] { grp };
            }

            Dictionary<NodeBuilder[], SelectionGroup> dict = new Dictionary<NodeBuilder[], SelectionGroup>();
            foreach (TreePath t in paths)
            {
                TreeIter it;
                Store.GetIter(out it, t);
                SelectionGroup grp;
                TreeNodeNavigator nav = new TreeNodeNavigator(this, it);
                if (!dict.TryGetValue(nav.BuilderChain, out grp))
                {
                    grp = new SelectionGroup
                    {
                        BuilderChain = nav.BuilderChain,
                        Nodes = new List<ITreeNavigator>(),
                        store = Store
                    };
                    dict[nav.BuilderChain] = grp;
                }
                grp.Nodes.Add(nav);
            }
            return dict.Values;
        }

        public bool MultipleNodesSelected()
        {
            return tree.Selection.GetSelectedRows().Length > 1;
        }

        public ITreeNavigator[] GetSelectedNodes()
        {
            TreePath[] paths = tree.Selection.GetSelectedRows();
            ITreeNavigator[] navs = new ITreeNavigator[paths.Length];
            for (int n = 0; n < paths.Length; n++)
            {
                TreeIter it;
                Store.GetIter(out it, paths[n]);
                navs[n] = new TreeNodeNavigator(this, it);
            }
            return navs;
        }

        public ITreeNavigator GetNodeAtPosition(NodePosition position)
        {
            return new TreeNodeNavigator(this, position._iter);
        }

        public ITreeNavigator GetNodeAtObject(object dataObject)
        {
            return GetNodeAtObject(dataObject, false);
        }

        public ITreeNavigator GetNodeAtObject(object dataObject, bool createTreeBranch)
        {
            object it;
            if (!NodeHash.TryGetValue(dataObject, out it))
            {
                if (createTreeBranch)
                {
                    TypeNodeBuilder tnb = GetTypeNodeBuilder(dataObject.GetType());

                    object parent = tnb?.GetParentObject(dataObject);
                    if (parent == null || parent == dataObject || dataObject.Equals(parent)) return null;

                    ITreeNavigator pnav = GetNodeAtObject(parent, true);
                    if (pnav == null) return null;

                    pnav.MoveToFirstChild();

                    // The child should be now in the this. Try again.
                    if (!NodeHash.TryGetValue(dataObject, out it))
                        return null;
                }
                else
                    return null;
            }

            var iters = it as TreeIter[];
            if (iters != null)
            {
                return new TreeNodeNavigator(this, iters[0]);
            }
            return new TreeNodeNavigator(this, (TreeIter)it);
        }

        public ITreeNavigator GetRootNode()
        {
            TreeIter iter;
            if (!Store.GetIterFirst(out iter)) return null;
            return new TreeNodeNavigator(this, iter);
        }

        public void AddNodeInsertCallback(object dataObject, TreeNodeCallback callback)
        {
            if (IsRegistered(dataObject))
            {
                callback(GetNodeAtObject(dataObject));
                return;
            }

            ArrayList list = callbacks[dataObject] as ArrayList;
            if (list != null)
                list.Add(callback);
            else
            {
                list = new ArrayList { callback };
                callbacks[dataObject] = list;
            }
        }

        internal new object GetNextCommandTarget()
        {
            return null;
        }

        private class MulticastNodeRouter : IMultiCastCommandRouter
        {
            private readonly ArrayList targets;

            public MulticastNodeRouter(ArrayList targets)
            {
                this.targets = targets;
            }

            public IEnumerable GetCommandTargets()
            {
                return targets;
            }
        }

        internal object GetDelegatedCommandTarget()
        {
            // If a node is being edited, don't delegate commands to the
            // node builders, since what's selected is not the node,
            // but the node label. In this way commands such as Delete
            // will be handled by the node Entry.
            if (editingText)
                return null;

            ArrayList targets = new ArrayList();

            foreach (SelectionGroup grp in GetSelectedNodesGrouped())
            {
                NodeBuilder[] chain = grp.BuilderChain;
                if (chain.Length > 0)
                {
                    ITreeNavigator[] nodes = grp.Nodes.ToArray();
                    NodeCommandTargetChain targetChain = null;
                    NodeCommandTargetChain lastNode = null;
                    foreach (NodeBuilder nb in chain)
                    {
                        NodeCommandTargetChain newNode = new NodeCommandTargetChain(nb.CommandHandler, nodes);
                        if (lastNode == null)
                            targetChain = lastNode = newNode;
                        else
                        {
                            lastNode.Next = newNode;
                            lastNode = newNode;
                        }
                    }

                    if (targetChain != null)
                        targets.Add(targetChain);
                }
            }
            if (targets.Count == 1)
                return targets[0];
            if (targets.Count > 1)
                return new MulticastNodeRouter(targets);
            return null;
        }

        private void ExpandCurrentItem()
        {
            try
            {
                LockUpdates();

                var nodeGroups = GetSelectedNodesGrouped();
                if (nodeGroups.Count == 1)
                {
                    SelectionGroup grp = nodeGroups.First();

                    if (grp.Nodes.Count == 1)
                    {
                        ITreeNavigator node = grp.Nodes.First();
                        if (node.Expanded)
                        {
                            grp.SavePositions();
                            node.Selected = node.MoveToFirstChild();

                            // This exit statement is so that it doesn't do 2 actions at a time.
                            // As in, navigate, then expand.
                            return;
                        }
                    }
                }

                foreach (SelectionGroup grp in nodeGroups)
                {
                    grp.SavePositions();

                    foreach (var node in grp.Nodes)
                    {
                        node.Expanded = true;
                    }
                }
            }
            finally
            {
                UnlockUpdates();
            }
        }

        private void CollapseCurrentItem()
        {
            try
            {
                LockUpdates();

                var nodeGroups = GetSelectedNodesGrouped();
                if (nodeGroups.Count == 1)
                {
                    SelectionGroup grp = nodeGroups.First();

                    if (grp.Nodes.Count == 1)
                    {
                        ITreeNavigator node = grp.Nodes.First();
                        if (!node.HasChildren() || !node.Expanded)
                        {
                            grp.SavePositions();
                            node.Selected = node.MoveToParent();

                            // This exit statement is so that it doesn't do 2 actions at a time.
                            // As in, navigate, then collapse.
                            return;
                        }
                    }
                }

                foreach (SelectionGroup grp in nodeGroups)
                {
                    grp.SavePositions();

                    foreach (var node in grp.Nodes)
                    {
                        node.Expanded = false;
                    }
                }
            }
            finally
            {
                UnlockUpdates();
            }
        }

        [CommandHandler(ViewCommands.Open)]
        public void ActivateCurrentItem()
        {
            OnActivateCurrentItem();
        }

        protected virtual void OnActivateCurrentItem()
        {
            try
            {
                LockUpdates();
                foreach (SelectionGroup grp in GetSelectedNodesGrouped())
                {
                    grp.SavePositions();
                    foreach (NodeBuilder b in grp.BuilderChain)
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNodes(grp.Nodes.ToArray());
                        handler.ActivateMultipleItems();
                        if (!grp.RestorePositions())
                            break;
                    }
                }
                OnCurrentItemActivated();
            }
            finally
            {
                UnlockUpdates();
            }
        }

        public void DeleteCurrentItem()
        {
            OnDeleteCurrentItem();
        }

        protected virtual void OnDeleteCurrentItem()
        {
            try
            {
                LockUpdates();
                foreach (SelectionGroup grp in GetSelectedNodesGrouped())
                {
                    NodeBuilder[] chain = grp.BuilderChain;
                    grp.SavePositions();
                    foreach (NodeBuilder b in chain)
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNodes(grp.Nodes.ToArray());
                        if (handler.CanDeleteMultipleItems())
                        {
                            if (!grp.RestorePositions())
                                return;
                            handler.DeleteMultipleItems();
                            // FIXME: fixes bug #396566, but it is not 100% correct
                            // It can only be fully fixed if updates to the tree are delayed
                            break;
                        }
                        if (!grp.RestorePositions())
                            return;
                    }
                }
            }
            finally
            {
                UnlockUpdates();
            }
        }

        protected virtual bool CanDeleteCurrentItem()
        {
            foreach (SelectionGroup grp in GetSelectedNodesGrouped())
            {
                NodeBuilder[] chain = grp.BuilderChain;
                grp.SavePositions();
                foreach (NodeBuilder b in chain)
                {
                    NodeCommandHandler handler = b.CommandHandler;
                    handler.SetCurrentNodes(grp.Nodes.ToArray());
                    if (handler.CanDeleteMultipleItems())
                        return true;
                    if (!grp.RestorePositions())
                        return false;
                }
            }
            return false;
        }

        [CommandHandler(ViewCommands.RefreshTree)]
        public void RefreshCurrentItem()
        {
            OnRefreshCurrentItem();
        }

        protected virtual void OnRefreshCurrentItem()
        {
            try
            {
                LockUpdates();
                foreach (SelectionGroup grp in GetSelectedNodesGrouped())
                {
                    NodeBuilder[] chain = grp.BuilderChain;
                    grp.SavePositions();
                    foreach (NodeBuilder b in chain)
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNodes(grp.Nodes.ToArray());
                        if (!grp.RestorePositions())
                            return;
                        handler.RefreshMultipleItems();
                        if (!grp.RestorePositions())
                            return;
                    }
                }
            }
            finally
            {
                UnlockUpdates();
            }
            RefreshTree();
        }

        protected virtual void OnCurrentItemActivated()
        {
            CurrentItemActivated?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler CurrentItemActivated;

        #region Zoom

        private const double ZOOM_FACTOR = 1.1f;
        private const int ZOOM_MIN_POW = -4;
        private const int ZOOM_MAX_POW = 8;
        private static readonly double ZoomMin = Math.Pow(ZOOM_FACTOR, ZOOM_MIN_POW);
        private static readonly double ZoomMax = Math.Pow(ZOOM_FACTOR, ZOOM_MAX_POW);
        private double zoom;

        public double Zoom
        {
            get
            {
                return zoom;
            }
            set
            {
                value = Math.Min(ZoomMax, Math.Max(ZoomMin, value));
                if (value > ZoomMax || value < ZoomMin)
                    return;
                //snap to one, if within 0.001d
                if ((Math.Abs(value - 1d)) < 0.001d)
                {
                    value = 1d;
                }
                if (zoom != value)
                {
                    zoom = value;
                    OnZoomChanged(value);
                }
            }
        }

        private void OnZoomChanged(double value)
        {
            pixRender.Zoom = value;
            textRender.Zoom = value;

            int expanderSize = (int)(12 * Zoom);
            if (expanderSize < 3) expanderSize = 3;
            if (expanderSize > 15) expanderSize = 15;
            if (expanderSize != 12)
                tree.Name = "MonoDevelop.ExtensibleTreeView_" + expanderSize;
            else
                tree.Name = "";
            tree.ColumnsAutosize();
            if (!string.IsNullOrEmpty(Id))
            {
                PropertyService.Set("MonoDevelop.Ide.ExtensibleTreeView.Zoom." + Id, Zoom);
            }
        }

        [CommandHandler(ViewCommands.ZoomIn)]
        public void ZoomIn()
        {
            int oldPow = (int)Math.Round(Math.Log(zoom) / Math.Log(ZOOM_FACTOR));
            Zoom = Math.Pow(ZOOM_FACTOR, oldPow + 1);
        }

        [CommandHandler(ViewCommands.ZoomOut)]
        public void ZoomOut()
        {
            int oldPow = (int)Math.Round(Math.Log(zoom) / Math.Log(ZOOM_FACTOR));
            Zoom = Math.Pow(ZOOM_FACTOR, oldPow - 1);
        }

        [CommandHandler(ViewCommands.ZoomReset)]
        public void ZoomReset()
        {
            Zoom = 1d;
        }

        [CommandUpdateHandler(ViewCommands.ZoomIn)]
        protected void UpdateZoomIn(CommandInfo cinfo)
        {
            cinfo.Enabled = zoom < ZoomMax - 0.000001d;
        }

        [CommandUpdateHandler(ViewCommands.ZoomOut)]
        protected void UpdateZoomOut(CommandInfo cinfo)
        {
            cinfo.Enabled = zoom > ZoomMin + 0.000001d;
        }

        [CommandUpdateHandler(ViewCommands.ZoomReset)]
        protected void UpdateZoomReset(CommandInfo cinfo)
        {
            cinfo.Enabled = zoom != 1d;
        }

        #endregion Zoom

        [CommandHandler(EditCommands.Copy)]
        public void CopyCurrentItem()
        {
            CancelTransfer();
            TransferCurrentItem(DragOperation.Copy);
        }

        [CommandHandler(EditCommands.Cut)]
        public void CutCurrentItem()
        {
            CancelTransfer();
            TransferCurrentItem(DragOperation.Move);

            if (CopyObjects != null)
            {
                foreach (object ob in CopyObjects)
                {
                    ITreeBuilder tb = CreateBuilder();
                    if (tb.MoveToObject(ob))
                        tb.Update();
                }
            }
        }

        [CommandUpdateHandler(EditCommands.Copy)]
        internal void UpdateCopyCurrentItem(CommandInfo info)
        {
            if (editingText)
            {
                info.Bypass = true;
                return;
            }
            info.Enabled = CanTransferCurrentItem(DragOperation.Copy);
        }

        [CommandUpdateHandler(EditCommands.Cut)]
        internal void UpdateCutCurrentItem(CommandInfo info)
        {
            if (editingText)
            {
                info.Bypass = true;
                return;
            }
            info.Enabled = CanTransferCurrentItem(DragOperation.Move);
        }

        private void TransferCurrentItem(DragOperation oper)
        {
            foreach (SelectionGroup grp in GetSelectedNodesGrouped())
            {
                NodeBuilder[] chain = grp.BuilderChain;
                grp.SavePositions();
                foreach (NodeBuilder b in chain)
                {
                    try
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNodes(grp.Nodes.ToArray());
                        if ((handler.CanDragNode() & oper) != 0)
                        {
                            grp.RestorePositions();
                            CopyObjects = grp.DataItems;
                            CurrentTransferOperation = oper;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError(ex.ToString());
                    }
                    grp.RestorePositions();
                }
            }
        }

        private bool CanTransferCurrentItem(DragOperation oper)
        {
            TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
            if (node != null)
            {
                NodeBuilder[] chain = node.NodeBuilderChain;
                NodePosition pos = node.CurrentPosition;
                foreach (NodeBuilder b in chain)
                {
                    try
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNode(node);
                        if ((handler.CanDragNode() & oper) != 0)
                            return true;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError(ex.ToString());
                    }
                    node.MoveToPosition(pos);
                }
            }
            return false;
        }

        [CommandHandler(EditCommands.Paste)]
        public void PasteToCurrentItem()
        {
            if (CopyObjects == null) return;

            try
            {
                LockUpdates();
                TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
                if (node != null)
                {
                    NodeBuilder[] chain = node.NodeBuilderChain;
                    NodePosition pos = node.CurrentPosition;
                    foreach (NodeBuilder b in chain)
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNode(node);
                        if (handler.CanDropMultipleNodes(CopyObjects, CurrentTransferOperation, DropPosition.Into))
                        {
                            node.MoveToPosition(pos);
                            handler.OnMultipleNodeDrop(CopyObjects, CurrentTransferOperation, DropPosition.Into);
                        }
                        node.MoveToPosition(pos);
                    }
                }
                if (CurrentTransferOperation == DragOperation.Move)
                    CancelTransfer();
            }
            finally
            {
                UnlockUpdates();
            }
        }

        [CommandUpdateHandler(EditCommands.Paste)]
        internal void UpdatePasteToCurrentItem(CommandInfo info)
        {
            if (editingText)
            {
                info.Bypass = true;
                return;
            }

            if (CopyObjects != null)
            {
                TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
                if (node != null)
                {
                    NodeBuilder[] chain = node.NodeBuilderChain;
                    NodePosition pos = node.CurrentPosition;
                    foreach (NodeBuilder b in chain)
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNode(node);
                        if (handler.CanDropMultipleNodes(CopyObjects, CurrentTransferOperation, DropPosition.Into))
                        {
                            info.Enabled = true;
                            return;
                        }
                        node.MoveToPosition(pos);
                    }
                }
            }
            info.Enabled = false;
        }

        private void CancelTransfer()
        {
            if (CopyObjects != null)
            {
                object[] oldCopyObjects = CopyObjects;
                CopyObjects = null;
                if (CurrentTransferOperation == DragOperation.Move)
                {
                    foreach (object ob in oldCopyObjects)
                    {
                        ITreeBuilder tb = CreateBuilder();
                        if (tb.MoveToObject(ob))
                            tb.Update();
                    }
                }
            }
        }

        private NodeInfo GetNodeInfo(TreeIter it)
        {
            return (NodeInfo)Store.GetValue(it, NodeInfoColumn);
        }

        private void StartLabelEditInternal()
        {
            if (editingText)
                return;

            TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
            if (node == null)
                return;

            TreeIter iter = node.CurrentPosition._iter;
            object dataObject = node.DataItem;
            NodeAttributes attributes = NodeAttributes.None;

            ITreeNavigator parentNode = node.Clone();
            parentNode.MoveToParent();
            NodePosition pos = parentNode.CurrentPosition;

            foreach (NodeBuilder b in node.NodeBuilderChain)
            {
                try
                {
                    b.GetNodeAttributes(parentNode, dataObject, ref attributes);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError(ex.ToString());
                }
                parentNode.MoveToPosition(pos);
            }

            if ((attributes & NodeAttributes.AllowRename) == 0)
                return;

            node.ExpandToNode(); //make sure the parent of the node that is being edited is expanded

            string nodeName = node.NodeName;

            GetNodeInfo(iter).Label = Markup.EscapeText(nodeName);
            Store.EmitRowChanged(Store.GetPath(iter), iter);

            // Get and validate the initial text selection
            int nameLength = nodeName?.Length ?? 0,
                selectionStart = 0, selectionLength = nameLength;
            foreach (NodeBuilder b in node.NodeBuilderChain)
            {
                try
                {
                    NodeCommandHandler handler = b.CommandHandler;
                    handler.SetCurrentNode(node);
                    handler.OnRenameStarting(ref selectionStart, ref selectionLength);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError(ex.ToString());
                }
            }
            if (selectionStart < 0 || selectionStart >= nameLength)
                selectionStart = 0;
            if (selectionStart + selectionLength > nameLength)
                selectionLength = nameLength - selectionStart;
            // This will apply the selection as soon as possible
            Idle.Add(() =>
            {
                var editable = currentLabelEditable;

                editable?.SelectRegion(selectionStart, selectionStart + selectionLength);
                return false;
            });
            // Ensure we set all our state variables before calling SetCursor
            // as this may directly invoke HandleOnEditCancelled
            textRender.Editable = true;
            editingText = true;
            tree.SetCursor(Store.GetPath(iter), CompleteColumn, true);
        }

        private Editable currentLabelEditable;

        private void HandleEditingStarted(object o, EditingStartedArgs e)
        {
            currentLabelEditable = e.Editable as Entry;
        }

        private void HandleOnEdit(object o, EditedArgs e)
        {
            try
            {
                editingText = false;
                textRender.Editable = false;
                currentLabelEditable = null;

                TreeIter iter;
                if (!Store.GetIterFromString(out iter, e.Path))
                    throw new Exception("Error calculating iter for path " + e.Path);

                if (!string.IsNullOrEmpty(e.NewText))
                {
                    ITreeNavigator nav = new TreeNodeNavigator(this, iter);
                    NodePosition pos = nav.CurrentPosition;

                    try
                    {
                        LockUpdates();
                        NodeBuilder[] chain = (NodeBuilder[])Store.GetValue(iter, BuilderChainColumn);
                        foreach (NodeBuilder b in chain)
                        {
                            try
                            {
                                NodeCommandHandler handler = b.CommandHandler;
                                handler.SetCurrentNode(nav);
                                handler.RenameItem(e.NewText);
                            }
                            catch (Exception ex)
                            {
                                LoggingService.LogInternalError(ex);
                            }
                            nav.MoveToPosition(pos);
                        }
                    }
                    finally
                    {
                        UnlockUpdates();
                    }
                }

                // Get the iter again since the this node may have been replaced.
                if (!Store.GetIterFromString(out iter, e.Path))
                    return;

                ITreeBuilder builder = CreateBuilder(iter);
                builder.Update();
            }
            catch (Exception ex)
            {
                LoggingService.LogInternalError("The item could not be renamed", ex);
            }
        }

        private void HandleOnEditCancelled(object s, EventArgs args)
        {
            editingText = false;
            textRender.Editable = false;
            currentLabelEditable = null;

            TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
            if (node == null)
                return;

            // Restore the original node label
            TreeIter iter = node.CurrentPosition._iter;
            ITreeBuilder builder = CreateBuilder(iter);
            builder.Update();
        }

        public NodeState SaveTreeState()
        {
            ITreeNavigator root = GetRootNode();
            if (root == null)
                return null;

            var rootState = NodeState.CreateRoot();
            List<NodeState> children = new List<NodeState>();
            rootState.ChildrenState = children;

            var s = new Dictionary<string, bool>();
            foreach (TreePadOption opt in options)
            {
                bool val;
                if (globalOptions.TryGetValue(opt.Id, out val) && val != opt.DefaultValue)
                    s[opt.Id] = val;
            }
            if (s.Count != 0)
                rootState.Options = s;

            do
            {
                rootState.ChildrenState.Add(root.SaveState());
            } while (root.MoveNext());

            return rootState;
        }

        public void RestoreTreeState(NodeState state)
        {
            if (state == null)
                return;

            ITreeNavigator nav = GetRootNode();
            if (nav == null)
                return;

            if (state.IsRoot)
            {
                if (state.ChildrenState != null)
                {
                    var pos = nav.CurrentPosition;
                    foreach (NodeState ces in state.ChildrenState)
                    {
                        do
                        {
                            if (nav.NodeName == ces.NodeName)
                            {
                                nav.RestoreState(ces);
                                break;
                            }
                        } while (nav.MoveNext());
                        nav.MoveToPosition(pos);
                    }
                }
            }
            else
                nav.RestoreState(state);

            globalOptions = new TreeOptions();
            foreach (TreePadOption opt in options)
            {
                bool val;
                if (state.Options == null || !state.Options.TryGetValue(opt.Id, out val))
                    val = opt.DefaultValue;
                globalOptions[opt.Id] = val;
            }
            globalOptions.Pad = this;
            RefreshTree();
        }

        private TypeNodeBuilder GetTypeNodeBuilder(Type type)
        {
            NodeBuilder[] chain = GetBuilderChain(type);
            return (TypeNodeBuilder)chain?[0];
        }

        internal NodeBuilder[] GetBuilderChain(Type type)
        {
            NodeBuilder[] chain;
            builderChains.TryGetValue(type, out chain);
            if (chain == null)
            {
                List<NodeBuilder> list = new List<NodeBuilder>();

                // Find the most specific node builder type.
                TypeNodeBuilder bestTypeNodeBuilder = null;
                Type bestNodeType = null;

                foreach (NodeBuilder nb in NodeBuilders)
                {
                    var builder = nb as TypeNodeBuilder;
                    if (builder != null)
                    {
                        TypeNodeBuilder tnb = builder;
                        if (tnb.NodeDataType.IsAssignableFrom(type))
                        {
                            if (bestNodeType == null || bestNodeType.IsAssignableFrom(tnb.NodeDataType))
                            {
                                bestNodeType = tnb.NodeDataType;
                                bestTypeNodeBuilder = tnb;
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            if (((NodeBuilderExtension)nb).CanBuildNode(type))
                                list.Add(nb);
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogError(ex.ToString());
                        }
                    }
                }

                if (bestTypeNodeBuilder != null)
                {
                    list.Insert(0, bestTypeNodeBuilder);
                    chain = list.ToArray();
                }

                builderChains[type] = chain;
            }
            return chain;
        }

        private TypeNodeBuilder GetTypeNodeBuilder(TreeIter iter)
        {
            NodeBuilder[] chain = (NodeBuilder[])Store.GetValue(iter, BuilderChainColumn);
            if (chain != null && chain.Length > 0)
                return chain[0] as TypeNodeBuilder;
            return null;
        }

        internal int CompareNodes(TreeModel model, TreeIter a, TreeIter b)
        {
            Sorting = true;
            try
            {
                NodeBuilder[] chain1 = (NodeBuilder[])Store.GetValue(a, BuilderChainColumn);
                if (chain1 == null) return -1;

                compareNode1.MoveToIter(a);
                compareNode2.MoveToIter(b);

                int sort = CompareObjects(chain1, compareNode1, compareNode2);
                if (sort != NodeBuilder.DefaultSort) return sort;

                NodeBuilder[] chain2 = (NodeBuilder[])Store.GetValue(b, BuilderChainColumn);
                if (chain2 == null) return 1;

                if (chain1 != chain2)
                {
                    sort = CompareObjects(chain2, compareNode2, compareNode1);
                    if (sort != NodeBuilder.DefaultSort) return sort * -1;
                }

                TypeNodeBuilder tb1 = (TypeNodeBuilder)chain1[0];
                TypeNodeBuilder tb2 = (TypeNodeBuilder)chain2[0];
                object o1 = Store.GetValue(a, DataItemColumn);
                object o2 = Store.GetValue(b, DataItemColumn);
                return String.Compare(tb1.GetNodeName(compareNode1, o1), tb2.GetNodeName(compareNode2, o2), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Sorting = false;
                compareNode1.MoveToIter(TreeIter.Zero);
                compareNode2.MoveToIter(TreeIter.Zero);
            }
        }

        private int CompareObjects(NodeBuilder[] chain, ITreeNavigator thisNode, ITreeNavigator otherNode)
        {
            int result = NodeBuilder.DefaultSort;
            foreach (NodeBuilder t in chain)
            {
                int sort = t.CompareObjects(thisNode, otherNode);
                if (sort != NodeBuilder.DefaultSort)
                    result = sort;
            }
            return result;
        }

        internal bool GetFirstNode(object dataObject, out TreeIter iter)
        {
            object it;
            if (!NodeHash.TryGetValue(dataObject, out it))
            {
                iter = TreeIter.Zero;
                return false;
            }
            if (it is TreeIter)
                iter = (TreeIter)it;
            else
                iter = ((TreeIter[])it)[0];
            return true;
        }

        internal bool GetNextNode(object dataObject, ref TreeIter iter)
        {
            object it;
            if (!NodeHash.TryGetValue(dataObject, out it))
                return false;
            if (it is TreeIter)
                return false; // There is only one node, GetFirstNode returned it
            TreeIter[] its = (TreeIter[])it;
            TreePath iterPath = Store.GetPath(iter);
            for (int n = 0; n < its.Length; n++)
            {
                if (Store.GetPath(its[n]).Equals(iterPath))
                {
                    if (n < its.Length - 1)
                    {
                        iter = its[n + 1];
                        return true;
                    }
                }
            }
            return false;
        }

        internal void RegisterNode(TreeIter it, object dataObject, NodeBuilder[] chain, bool fireAddedEvent)
        {
            object currentIt;
            if (!NodeHash.TryGetValue(dataObject, out currentIt))
            {
                NodeHash[dataObject] = it;
                if (chain == null) chain = GetBuilderChain(dataObject.GetType());
                if (fireAddedEvent)
                {
                    foreach (NodeBuilder nb in chain)
                    {
                        try
                        {
                            nb.OnNodeAdded(dataObject);
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogError(ex.ToString());
                        }
                    }
                }
            }
            else
            {
                var iters = currentIt as TreeIter[];
                if (iters != null)
                {
                    TreeIter[] arr = iters;
                    TreeIter[] newArr = new TreeIter[arr.Length + 1];
                    arr.CopyTo(newArr, 0);
                    newArr[arr.Length] = it;
                    NodeHash[dataObject] = newArr;
                }
                else
                {
                    NodeHash[dataObject] = new[] { it, (TreeIter)currentIt };
                }
            }
        }

        internal void UnregisterNode(object dataObject, TreeIter iter, NodeBuilder[] chain, bool fireRemovedEvent)
        {
            // Remove object from copy list

            if (CopyObjects != null)
            {
                int i = Array.IndexOf(CopyObjects, dataObject);
                if (i != -1)
                {
                    ArrayList list = new ArrayList(CopyObjects);
                    list.RemoveAt(i);
                    CopyObjects = list.Count > 0 ? list.ToArray() : null;
                }
            }

            // Remove object from drag list

            if (tree.DragObjects != null)
            {
                int i = Array.IndexOf(tree.DragObjects, dataObject);
                if (i != -1)
                {
                    ArrayList list = new ArrayList(tree.DragObjects);
                    list.RemoveAt(i);
                    tree.DragObjects = list.Count > 0 ? list.ToArray() : null;
                }
            }

            object currentIt;
            NodeHash.TryGetValue(dataObject, out currentIt);
            var treeIters = currentIt as TreeIter[];
            if (treeIters != null)
            {
                TreeIter[] arr = treeIters;
                TreePath path = null;
                List<TreeIter> iters = new List<TreeIter>();
                if (Store.IterIsValid(iter))
                    path = Store.GetPath(iter);

                // Iters can't be directly compared (TreeIter.Equals is broken), so we have
                // to compare paths.
                foreach (TreeIter it in arr)
                {
                    if (Store.IterIsValid(it) && (path == null || !path.Equals(Store.GetPath(it))))
                        iters.Add(it);
                }
                if (iters.Count > 1)
                    NodeHash[dataObject] = iters.ToArray();
                else if (iters.Count == 1)
                    NodeHash[dataObject] = iters[0];
                else
                    NodeHash.Remove(dataObject);
            }
            else
            {
                NodeHash.Remove(dataObject);
                if (fireRemovedEvent)
                    NotifyNodeRemoved(dataObject, chain);
            }
        }

        internal void RemoveChildren(TreeIter it)
        {
            TreeIter child;
            while (Store.IterChildren(out child, it))
            {
                RemoveChildren(child);
                object childData = Store.GetValue(child, DataItemColumn);
                if (childData != null)
                    UnregisterNode(childData, child, null, true);
                Store.Remove(ref child);
            }
        }

        private void NotifyNodeRemoved(object dataObject, NodeBuilder[] chain)
        {
            if (chain == null)
                chain = GetBuilderChain(dataObject.GetType());
            foreach (NodeBuilder nb in chain)
            {
                try
                {
                    nb.OnNodeRemoved(dataObject);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError(ex.ToString());
                }
            }
        }

        internal bool IsRegistered(object dataObject)
        {
            return NodeHash.ContainsKey(dataObject);
        }

        internal void NotifyInserted(TreeIter it, object dataObject)
        {
            if (callbacks.Count > 0)
            {
                ArrayList list = callbacks[dataObject] as ArrayList;
                if (list != null)
                {
                    ITreeNavigator nav = new TreeNodeNavigator(this, it);
                    NodePosition pos = nav.CurrentPosition;
                    foreach (TreeNodeCallback callback in list)
                    {
                        callback(nav);
                        nav.MoveToPosition(pos);
                    }
                    callbacks.Remove(dataObject);
                }
            }
        }

        internal string GetNamePathFromIter(TreeIter iter)
        {
            workNode.MoveToIter(iter);
            StringBuilder sb = new StringBuilder();
            do
            {
                string name = workNode.NodeName;
                if (sb.Length > 0) sb.Insert(0, '/');
                name = name.Replace("%", "%%");
                name = name.Replace("/", "_%_");
                sb.Insert(0, name);
            } while (workNode.MoveToParent());

            workNode.MoveToIter(TreeIter.Zero);

            return sb.ToString();
        }

        private void RefreshNode(TreeIter iter)
        {
            ITreeBuilder builder = CreateBuilder(iter);
            builder.UpdateAll();
        }

        public void RefreshNode(ITreeNavigator nav)
        {
            RefreshNode(nav.CurrentPosition._iter);
        }

        internal void ResetState(ITreeNavigator nav)
        {
            var treeBuilder = nav as TreeBuilder;
            if (treeBuilder != null)
                treeBuilder.ResetState();
            else if (nav is TransactedTreeBuilder)
                ((TransactedTreeBuilder)nav).ResetState();
            else
            {
                ITreeBuilder builder = CreateBuilder(nav.CurrentPosition._iter);
                ResetState(builder);
            }
        }

        internal bool GetIterFromNamePath(string path, out TreeIter iter)
        {
            if (!Store.GetIterFirst(out iter))
                return false;

            TreeNodeNavigator nav = new TreeNodeNavigator(this, iter);
            string[] names = path.Split('/');

            int n = 0;
            bool more;
            do
            {
                string name = names[n].Replace("_%_", "/");
                name = name.Replace("%%", "%");

                if (nav.NodeName == name)
                {
                    iter = nav.CurrentPosition._iter;
                    if (++n == names.Length) return true;
                    more = nav.MoveToFirstChild();
                }
                else
                    more = nav.MoveNext();
            } while (more);

            return false;
        }

        /// <summary>
        /// If you want to edit a node label. Select the node you want to edit and then
        /// call this method, instead of using the LabelEdit Property and the BeginEdit
        /// Method directly.
        /// </summary>
        [CommandHandler(EditCommands.Rename)]
        public void StartLabelEdit()
        {
            Timeout.Add(20, WantFocus);
        }

        [CommandUpdateHandler(EditCommands.Rename)]
        internal void UpdateStartLabelEdit(CommandInfo info)
        {
            if (editingText || GetSelectedNodes().Length != 1)
            {
                info.Visible = false;
                return;
            }

            TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
            NodeAttributes attributes = GetNodeAttributes(node);
            if ((attributes & NodeAttributes.AllowRename) == 0)
            {
                info.Visible = false;
            }
        }

        private NodeAttributes GetNodeAttributes(TreeNodeNavigator node)
        {
            object dataObject = node.DataItem;
            NodeAttributes attributes = NodeAttributes.None;

            ITreeNavigator parentNode = node.Clone();
            parentNode.MoveToParent();
            NodePosition pos = parentNode.CurrentPosition;

            foreach (NodeBuilder b in node.NodeBuilderChain)
            {
                try
                {
                    b.GetNodeAttributes(parentNode, dataObject, ref attributes);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError(ex.ToString());
                }
                parentNode.MoveToPosition(pos);
            }
            return attributes;
        }


        private bool WantFocus()
        {
            tree.GrabFocus();
            StartLabelEditInternal();
            return false;
        }

        private void OnTestExpandRow(object sender, TestExpandRowArgs args)
        {
            bool filled = (bool)Store.GetValue(args.Iter, FilledColumn);
            if (!filled)
            {
                TreeBuilder nb = new TreeBuilder(this, args.Iter);
                args.RetVal = !nb.FillNode();
            }
            else
                args.RetVal = false;
        }

        private void ShowPopup(EventButton evt)
        {
            var entryset = BuildEntrySet();
            if (entryset == null)
                return;

            if (evt == null)
            {
                var paths = tree.Selection.GetSelectedRows();
                if (paths != null)
                {
                    var area = tree.GetCellArea(paths[0], tree.Columns[0]);

                    IdeApp.CommandService.ShowContextMenu(this, area.Left, area.Top, entryset, this);
                }
            }
            else
            {
                IdeApp.CommandService.ShowContextMenu(this, evt, entryset, this);
            }
        }

        private CommandEntrySet BuildEntrySet()
        {
            ITreeNavigator tnav = GetSelectedNode();
            if (tnav == null)
                return null;
            TypeNodeBuilder nb = GetTypeNodeBuilder(tnav.CurrentPosition._iter);
            string menuPath = nb?.ContextMenuAddinPath ?? contextMenuPath;
            if (menuPath == null)
            {
                if (options.Length > 0)
                {
                    CommandEntrySet opset = new CommandEntrySet();
                    opset.AddItem(ViewCommands.TreeDisplayOptionList);
                    opset.AddItem(Command.Separator);
                    opset.AddItem(ViewCommands.ResetTreeDisplayOptions);
                    return opset;
                }
                return null;
            }
            CommandEntrySet eset = IdeApp.CommandService.CreateCommandEntrySet(menuPath);

            eset.AddItem(Command.Separator);
            if (!tnav.Clone().MoveToParent())
            {
                CommandEntrySet opset = eset.AddItemSet(("Display Options"));
                opset.AddItem(ViewCommands.TreeDisplayOptionList);
                opset.AddItem(Command.Separator);
                opset.AddItem(ViewCommands.ResetTreeDisplayOptions);
                //	opset.AddItem (ViewCommands.CollapseAllTreeNodes);
            }
            eset.AddItem(ViewCommands.RefreshTree);
            return eset;
        }

        [CommandUpdateHandler(ViewCommands.TreeDisplayOptionList)]
        internal void BuildTreeOptionsMenu(CommandArrayInfo info)
        {
            foreach (TreePadOption op in options)
            {
                CommandInfo ci = new CommandInfo(op.Label) { Checked = globalOptions[op.Id] };
                info.Add(ci, op.Id);
            }
        }

        [CommandHandler(ViewCommands.TreeDisplayOptionList)]
        internal void OptionToggled(string optionId)
        {
            globalOptions[optionId] = !globalOptions[optionId];
            RefreshRoots();
        }

        [CommandHandler(ViewCommands.ResetTreeDisplayOptions)]
        public void ResetOptions()
        {
            foreach (TreePadOption op in options)
                globalOptions[op.Id] = op.DefaultValue;

            RefreshRoots();
        }

        private void RefreshRoots()
        {
            TreeIter it;
            if (!Store.GetIterFirst(out it))
                return;
            do
            {
                ITreeBuilder tb = CreateBuilder(it);
                tb.UpdateAll();
            } while (Store.IterNext(ref it));
        }

        public void RefreshTree()
        {
            foreach (var treeNavigator in GetSelectedNodes())
            {
                var node = (TreeNodeNavigator)treeNavigator;
                TreeIter it = node.CurrentPosition._iter;
                if (Store.IterIsValid(it))
                {
                    ITreeBuilder tb = CreateBuilder(it);
                    tb.UpdateAll();
                }
            }
        }

        [CommandHandler(ViewCommands.CollapseAllTreeNodes)]
        public void CollapseTree()
        {
            tree.CollapseAll();
        }

        [ConnectBefore]
        private void OnKeyPress(object o, KeyPressEventArgs args)
        {
            if (args.Event.Key == Key.Delete || args.Event.Key == Key.KP_Delete)
            {
                DeleteCurrentItem();
                args.RetVal = true;
                return;
            }

            //HACK: to work around "bug 377810 - Many errors when expanding MonoDevelop treeviews with keyboard"
            //  The shift-right combo recursively expands all child nodes but the OnTestExpandRow callback
            //  modifies tree and successive calls get passed an invalid iter. Using the path to regenerate the iter
            //  causes a Gtk-Fatal.
            bool shift = (args.Event.State & ModifierType.ShiftMask) != 0;
            if (args.Event.Key == Key.asterisk || args.Event.Key == Key.KP_Multiply
                || (shift && (args.Event.Key == Key.Right || args.Event.Key == Key.KP_Right
                    || args.Event.Key == Key.plus || args.Event.Key == Key.KP_Add)))
            {
                foreach (TreePath path in tree.Selection.GetSelectedRows())
                {
                    TreeIter iter;
                    Store.GetIter(out iter, path);
                    Expand(iter);
                }
                args.RetVal = true;
                return;
            }

            if (args.Event.Key == Key.Right || args.Event.Key == Key.KP_Right)
            {
                ExpandCurrentItem();
                args.RetVal = true;
                return;
            }

            if (args.Event.Key == Key.Left || args.Event.Key == Key.KP_Left)
            {
                CollapseCurrentItem();
                args.RetVal = true;
                return;
            }

            if (args.Event.Key == Key.Return || args.Event.Key == Key.KP_Enter || args.Event.Key == Key.ISO_Enter)
            {
                ActivateCurrentItem();
                args.RetVal = true;
            }
        }

        private void Expand(TreeIter it)
        {
            tree.ExpandRow(Store.GetPath(it), false);
            TreeIter ci;
            if (Store.IterChildren(out ci, it))
            {
                do
                {
                    Expand(ci);
                } while (Store.IterNext(ref ci));
            }
        }

        private void OnNodeActivated(object sender, RowActivatedArgs args)
        {
            ActivateCurrentItem();
        }

        private void OnSelectionChanged(object sender, EventArgs args)
        {
            TreeNodeNavigator node = (TreeNodeNavigator)GetSelectedNode();
            if (node != null)
            {
                NodeBuilder[] chain = node.NodeBuilderChain;
                NodePosition pos = node.CurrentPosition;
                foreach (NodeBuilder b in chain)
                {
                    try
                    {
                        NodeCommandHandler handler = b.CommandHandler;
                        handler.SetCurrentNode(node);
                        handler.OnItemSelected();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError(ex.ToString());
                    }
                    node.MoveToPosition(pos);
                }
            }
            OnSelectionChanged();
        }

        protected virtual void OnSelectionChanged()
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Destroy()
        {
            IdeApp.Preferences.CustomPadFont.Changed -= CustomFontPropertyChanged;
            if (pixRender != null)
            {
                pixRender.Destroy();
                pixRender = null;
            }
            if (CompleteColumn != null)
            {
                CompleteColumn.Destroy();
                CompleteColumn = null;
            }
            if (textRender != null)
            {
                textRender.Destroy();
                textRender = null;
            }

            if (Store != null)
            {
                Clear();
                Store = null;
            }

            if (NodeBuilders != null)
            {
                foreach (NodeBuilder nb in NodeBuilders)
                {
                    try
                    {
                        nb.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError(ex.ToString());
                    }
                }
                NodeBuilders = null;
            }
            builderChains.Clear();
        }

        object ICommandRouter.GetNextCommandTarget()
        {
            return widget.Parent;
        }

        internal class PadCheckMenuItem : CheckMenuItem
        {
            internal string Id;

            public PadCheckMenuItem(string label, string id) : base(label)
            {
                Id = id;
            }
        }

        internal class TreeBuilderContext : ITreeBuilderContext
        {
            private readonly Hashtable icons = new Hashtable();
            private readonly Hashtable composedIcons = new Hashtable();

            internal TreeBuilderContext(ExtensibleTreeView pad)
            {
                Tree = pad;
            }

            public ITreeBuilder GetTreeBuilder()
            {
                TreeIter iter;
                if (!Tree.Store.GetIterFirst(out iter))
                    return Tree.CreateBuilder(TreeIter.Zero);
                return Tree.CreateBuilder(iter);
            }

            public ITreeBuilder GetTreeBuilder(object dataObject)
            {
                ITreeBuilder tb = Tree.CreateBuilder();
                if (tb.MoveToObject(dataObject))
                    return tb;
                return null;
            }

            public ITreeBuilder GetTreeBuilder(ITreeNavigator navigator)
            {
                return Tree.CreateBuilder(navigator.CurrentPosition._iter);
            }

            public Image GetIcon(string id)
            {
                Image icon = icons[id] as Image;
                if (icon == null)
                {
                    icon = ImageService.GetIcon(id).WithSize(IconSize.Menu);
                    icons[id] = icon;
                }
                return icon;
            }

            public Image GetComposedIcon(Image baseIcon, object compositionKey)
            {
                Hashtable itable = composedIcons[baseIcon] as Hashtable;
                return itable?[compositionKey] as Image;
            }

            public Image CacheComposedIcon(Image baseIcon, object compositionKey, Image composedIcon)
            {
                Hashtable itable = composedIcons[baseIcon] as Hashtable;
                if (itable == null)
                {
                    itable = new Hashtable();
                    composedIcons[baseIcon] = itable;
                }
                itable[compositionKey] = composedIcon;
                return composedIcon;
            }

            public ITreeNavigator GetTreeNavigator(object dataObject)
            {
                TreeIter iter;
                if (!Tree.GetFirstNode(dataObject, out iter)) return null;
                return new TreeNodeNavigator(Tree, iter);
            }

            public ExtensibleTreeView Tree { get; }
        }

        private class ExtensibleTreeViewTree : ContextMenuTreeView
        {
            private readonly ExtensibleTreeView tv;

            public ExtensibleTreeViewTree(ExtensibleTreeView tv)
            {
                this.tv = tv;
                EnableModelDragDest(TargetTable, DragAction.Copy | DragAction.Move);
                Drag.SourceSet(this, ModifierType.Button1Mask, TargetTable, DragAction.Copy | DragAction.Move);
            }

            private static readonly TargetEntry[] TargetTable = {
                new TargetEntry ("text/uri-list", 0, 11 ),
                new TargetEntry ("text/plain", 0, 22),
                new TargetEntry ("application/x-rootwindow-drop", 0, 33)
            };

            public object[] DragObjects;
            private bool dropping;
            private Func<object, string> nodeToUri;

            public void EnableDragUriSource(Func<object, string> nodeToUri)
            {
                this.nodeToUri = nodeToUri;
            }

            protected override void OnDragBegin(DragContext context)
            {
                Image dragIcon;
                DragObjects = tv.GetDragObjects(out dragIcon);
                Drag.SetIconPixbuf(context, dragIcon?.ToPixbuf(IconSize.Menu), -10, -10);

                base.OnDragBegin(context);
            }

            protected override void OnDragEnd(DragContext context)
            {
                DragObjects = null;
                base.OnDragEnd(context);
            }

            protected override bool OnDragMotion(DragContext context, int x, int y, uint time)
            {
                //OnDragDataReceived callback loses x/y values, so stash them
                this.x = x;
                this.y = y;

                if (DragObjects == null)
                {
                    //it's a drag from outside, need to retrieve the data. This will cause OnDragDataReceived to be called.
                    Atom atom = Drag.DestFindTarget(this, context, null);
                    Drag.GetData(this, context, atom, time);
                }
                else
                {
                    //it's from inside, can call OnDragDataReceived directly
                    OnDragDataReceived(context, x, y, null, 0, time);
                }
                return true;
            }

            private int x, y;

            protected override void OnDragDataReceived(DragContext context, int x, int y, SelectionData selectionData, uint info, uint time)
            {
                x = this.x;
                y = this.y;

                object[] data = DragObjects ?? new object[] { selectionData };
                bool canDrop = tv.CheckAndDrop(x, y, dropping, context, data);
                if (dropping)
                {
                    dropping = false;
                    SetDragDestRow(null, 0);
                    Drag.Finish(context, canDrop, true, time);
                    return;
                }

                //let default handler handle hover-to-expand, autoscrolling, etc
                base.OnDragMotion(context, x, y, time);

                //if we can't handle it, flag as not droppable and remove the drop marker
                if (!canDrop)
                {
                    Gdk.Drag.Status(context, 0, time);
                    SetDragDestRow(null, 0);
                }
            }

            protected override bool OnDragDrop(DragContext context, int x, int y, uint time)
            {
                dropping = true;
                return base.OnDragDrop(context, x, y, time);
            }

            protected override void OnDragDataGet(DragContext context, SelectionData selectionData, uint info, uint time)
            {
                if (DragObjects == null || nodeToUri == null)
                    return;

                uint uriListTarget = TargetTable[0].Info;
                if (info == uriListTarget)
                {
                    var sb = new StringBuilder();
                    foreach (var dobj in DragObjects)
                    {
                        var val = nodeToUri(dobj);
                        if (val != null)
                        {
                            sb.AppendLine(val);
                        }
                    }
                    selectionData.Set(selectionData.Target, selectionData.Format, Encoding.UTF8.GetBytes(sb.ToString()));
                }
            }
        }

        private class CustomCellRendererText : CellRendererText
        {
            private double zoom;
            private Layout layout;
            private FontDescription scaledFont, customFont;

            private readonly ExtensibleTreeView parent;
            private Rectangle buttonScreenRect;
            private string markup;
            private string secondarymarkup;

            private const int StatusIconSpacing = 4;

            //using this instead of FontDesc property, FontDesc seems to be broken
            public FontDescription CustomFont
            {
                get
                {
                    return customFont;
                }
                set
                {
                    if (scaledFont != null)
                    {
                        scaledFont.Dispose();
                        scaledFont = null;
                    }
                    customFont = value;
                }
            }

            static CustomCellRendererText()
            {
            }

            [Property("text-markup")]
            public string TextMarkup
            {
                get { return markup; }
                set
                {
                    markup = value;
                    if (!string.IsNullOrEmpty(secondarymarkup))
                        Markup = markup + " " + secondarymarkup;
                    else
                        Markup = markup;
                }
            }

            [Property("secondary-text-markup")]
            public string SecondaryTextMarkup
            {
                get { return secondarymarkup; }
                set
                {
                    secondarymarkup = value;
                    if (!string.IsNullOrEmpty(secondarymarkup))
                        Markup = markup + " " + secondarymarkup;
                    else
                        Markup = markup;
                }
            }

            public bool DisabledStyle { get; set; }

            [Property("status-icon")]
            public Image StatusIcon { get; set; }

            public CustomCellRendererText(ExtensibleTreeView parent)
            {
                this.parent = parent;
            }

            private static readonly Size DefaultIconSize = IconSize.Menu.GetSize();

            private static Size GetZoomedIconSize(Image icon, double zoom)
            {
                if (icon == null || icon == CellRendererImage.NullImage)
                    return DefaultIconSize;

                var size = icon.HasFixedSize ? icon.Size : DefaultIconSize;

                if (zoom == 1)
                    return size;

                int w = (int)(zoom * size.Width);
                int h = (int)(zoom * size.Height);
                if (w == 0) w = 1;
                if (h == 0) h = 1;
                return new Size(w, h);
            }

            private static Image GetResized(Image icon, double zoom)
            {
                var size = GetZoomedIconSize(icon, zoom);
                return icon.WithSize(size);
            }

            private void SetupLayout(Widget widget, CellRendererState flags = 0)
            {
                if (scaledFont == null)
                {
                    scaledFont?.Dispose();
                    scaledFont = (customFont ?? parent.widget.Style.FontDesc).Copy();
                    scaledFont.Size = (int)(customFont?.Size * Zoom ?? 0);
                    if (layout != null)
                        layout.FontDescription = scaledFont;
                }

                if (layout == null || layout.Context != widget.PangoContext)
                {
                    layout?.Dispose();
                    layout = new Layout (widget.PangoContext) {FontDescription = scaledFont};
                }

                string newmarkup = TextMarkup;
                if (DisabledStyle)
                {
                    Color fgColor;
                    if (Platform.IsMac && flags.HasFlag(CellRendererState.Selected))
                        fgColor = widget.Style.Text(IdeTheme.UserInterfaceTheme == Theme.Light ? StateType.Selected : StateType.Normal);
                    else
                        fgColor = widget.Style.Text(StateType.Insensitive);
                    newmarkup = "<span foreground='" + fgColor.GetHex() + "'>" + TextMarkup + "</span>";
                }

                if (!string.IsNullOrEmpty(SecondaryTextMarkup))
                {
                    if (Platform.IsMac && flags.HasFlag(CellRendererState.Selected))
                        newmarkup += " <span foreground='" + Styles.SecondarySelectionTextColor.ToHexString(false) + "'>" + SecondaryTextMarkup + "</span>";
                    else
                        newmarkup += " <span foreground='" + Styles.SecondaryTextColor.ToHexString(false) + "'>" + SecondaryTextMarkup + "</span>";
                }

                layout.SetMarkup(newmarkup);
            }

            protected override void Render(Drawable window, Widget widget, Rectangle backgroundArea, Rectangle cellArea, Rectangle exposeArea, CellRendererState flags)
            {
                StateType st = StateType.Normal;
                if ((flags & CellRendererState.Prelit) != 0)
                    st = StateType.Prelight;
                if ((flags & CellRendererState.Focused) != 0)
                    st = StateType.Normal;
                if ((flags & CellRendererState.Insensitive) != 0)
                    st = StateType.Insensitive;
                if ((flags & CellRendererState.Selected) != 0)
                    st = widget.HasFocus ? StateType.Selected : StateType.Active;

                SetupLayout(widget, flags);

                int w, h;
                layout.GetPixelSize(out w, out h);

                int tx = cellArea.X + (int)Xpad;
                int ty = cellArea.Y + (cellArea.Height - h) / 2;

                bool hasStatusIcon = StatusIcon != CellRendererImage.NullImage && StatusIcon != null;

                if (hasStatusIcon)
                {
                    var img = GetResized(StatusIcon, zoom);
                    if (st == StateType.Selected)
                        img = img.WithStyles("sel");
                    var x = tx + w + StatusIconSpacing;
                    using (var ctx = CairoHelper.Create(window))
                    {
                        ctx.DrawImage(widget, img, x, cellArea.Y + (cellArea.Height - img.Height) / 2);
                    }
                }

                window.DrawLayout(widget.Style.TextGC(st), tx, ty, layout);
            }

            public Rectangle GetStatusIconArea(Widget widget, Rectangle cellArea)
            {
                SetupLayout(widget);

                int w, h;
                layout.GetPixelSize(out w, out h);

                var iconSize = GetZoomedIconSize(StatusIcon, zoom);
                int tx = cellArea.X + (int)Xpad;
                var x = tx + w + StatusIconSpacing;
                return new Rectangle(x, cellArea.Y, (int)iconSize.Width, cellArea.Height);
            }

            public override void GetSize(Widget widget, ref Rectangle cellArea, out int xOffset, out int yOffset, out int width, out int height)
            {
                SetupLayout(widget);

                xOffset = yOffset = 0;

                layout.GetPixelSize(out width, out height);
                width += (int)Xpad * 2;

                if (StatusIcon != CellRendererImage.NullImage && StatusIcon != null)
                {
                    var iconSize = GetZoomedIconSize(StatusIcon, zoom);
                    width += (int)iconSize.Width + StatusIconSpacing;
                }
            }

            protected override void OnEditingStarted(CellEditable editable, string path)
            {
                var entry = editable as Entry;
                if (entry != null && scaledFont != null)
                    entry.ModifyFont(scaledFont);
                base.OnEditingStarted(editable, path);
            }

            public double Zoom
            {
                get
                {
                    return zoom;
                }
                set
                {
                    if (scaledFont != null)
                    {
                        scaledFont.Dispose();
                        scaledFont = null;
                    }
                    zoom = value;
                }
            }

            protected override void OnDestroyed()
            {
                base.OnDestroyed();
                scaledFont?.Dispose();
                layout?.Dispose();
            }
        }
    }

    internal class NodeCommandTargetChain : ICommandDelegatorRouter
    {
        private readonly NodeCommandHandler target;
        private readonly ITreeNavigator[] nodes;
        internal NodeCommandTargetChain Next;

        public NodeCommandTargetChain(NodeCommandHandler target, ITreeNavigator[] nodes)
        {
            this.nodes = nodes;
            this.target = target;
        }

        public object GetNextCommandTarget()
        {
            target.SetCurrentNodes(null);
            return Next;
        }

        public object GetDelegatedCommandTarget()
        {
            target.SetCurrentNodes(nodes);
            return target;
        }
    }

    internal class IterComparer : IEqualityComparer<TreeIter>
    {
        private readonly TreeStore store;

        public IterComparer(TreeStore store)
        {
            this.store = store;
        }
        public bool Equals(TreeIter x, TreeIter y)
        {
            if (!store.IterIsValid(x) || !store.IterIsValid(y))
                return false;
            TreePath px = store.GetPath(x);
            TreePath py = store.GetPath(y);
            if (px == null || py == null)
                return false;
            return px.Equals(py);
        }

        public int GetHashCode(TreeIter obj)
        {
            if (!store.IterIsValid(obj))
                return 0;
            TreePath p = store.GetPath(obj);
            if (p == null)
                return 0;
            return p.ToString().GetHashCode();
        }
    }

    internal class ZoomableCellRendererPixbuf : CellRendererImage
    {
        private double zoom = 1f;

        private readonly Dictionary<Image, Image> resizedCache = new Dictionary<Image, Image>();

        private Image overlayBottomLeft;
        private Image overlayBottomRight;
        private Image overlayTopLeft;
        private Image overlayTopRight;

        public double Zoom
        {
            get { return zoom; }
            set
            {
                if (zoom != value)
                {
                    zoom = value;
                    resizedCache.Clear();
                    Notify("image");
                }
            }
        }

        public override Image Image
        {
            get
            {
                return base.Image;
            }
            set
            {
                base.Image = GetResized(value);
            }
        }

        public override Image ImageExpanderOpen
        {
            get
            {
                return base.ImageExpanderOpen;
            }
            set
            {
                base.ImageExpanderOpen = GetResized(value);
            }
        }

        public override Image ImageExpanderClosed
        {
            get
            {
                return base.ImageExpanderClosed;
            }
            set
            {
                base.ImageExpanderClosed = GetResized(value);
            }
        }

        [Property("overlay-image-top-left")]
        public Image OverlayTopLeft
        {
            get
            {
                return overlayTopLeft;
            }
            set
            {
                overlayTopLeft = GetResized(value);
            }
        }

        [Property("overlay-image-top-right")]
        public Image OverlayTopRight
        {
            get
            {
                return overlayTopRight;
            }
            set
            {
                overlayTopRight = GetResized(value);
            }
        }

        [Property("overlay-image-bottom-left")]
        public Image OverlayBottomLeft
        {
            get
            {
                return overlayBottomLeft;
            }
            set
            {
                overlayBottomLeft = GetResized(value);
            }
        }

        [Property("overlay-image-bottom-right")]
        public Image OverlayBottomRight
        {
            get
            {
                return overlayBottomRight;
            }
            set
            {
                overlayBottomRight = GetResized(value);
            }
        }

        private Image GetResized(Image value)
        {
            //this can happen during solution deserialization if the project is unrecognized
            //because a line is added into the treeview with no icon
            if (value == null || value == NullImage)
                return null;

            var img = value.HasFixedSize ? value : value.WithSize(IconSize.Menu);

            if (zoom == 1)
                return img;

            Image resized;
            if (resizedCache.TryGetValue(img, out resized))
                return resized;

            int w = (int)(zoom * img.Width);
            int h = (int)(zoom * img.Height);
            if (w == 0) w = 1;
            if (h == 0) h = 1;
            resized = img.WithSize(w, h);
            resizedCache[img] = resized;
            return resized;
        }

        public override void GetSize(Widget widget, ref Rectangle cellArea, out int xOffset, out int yOffset, out int width, out int height)
        {
            base.GetSize(widget, ref cellArea, out xOffset, out yOffset, out width, out height);
            /*			if (overlayBottomLeft != null || overlayBottomRight != null)
				height += overlayOverflow;
			if (overlayTopLeft != null || overlayTopRight != null)
				height += overlayOverflow;
			if (overlayBottomRight != null || overlayTopRight != null)
				width += overlayOverflow;*/
        }

        private const int OverlayOverflow = 2;

        protected override void Render(Drawable window, Widget widget, Rectangle backgroundArea, Rectangle cellArea, Rectangle exposeArea, CellRendererState flags)
        {
            base.Render(window, widget, backgroundArea, cellArea, exposeArea, flags);

            if (overlayBottomLeft != null || overlayBottomRight != null || overlayTopLeft != null || overlayTopRight != null)
            {
                int x, y;
                Image image;
                GetImageInfo(cellArea, out image, out x, out y);

                if (image == null)
                    return;

                bool selected = (flags & CellRendererState.Selected) != 0;

                using (var ctx = CairoHelper.Create(window))
                {
                    if (overlayBottomLeft != null && overlayBottomLeft != NullImage)
                    {
                        var img = selected ? overlayBottomLeft.WithStyles("sel") : overlayBottomLeft;
                        ctx.DrawImage(widget, img, x - OverlayOverflow, y + image.Height - img.Height + OverlayOverflow);
                    }
                    if (overlayBottomRight != null && overlayBottomRight != NullImage)
                    {
                        var img = selected ? overlayBottomRight.WithStyles("sel") : overlayBottomRight;
                        ctx.DrawImage(widget, img, x + image.Width - img.Width + OverlayOverflow, y + image.Height - img.Height + OverlayOverflow);
                    }
                    if (overlayTopLeft != null && overlayTopLeft != NullImage)
                    {
                        var img = selected ? overlayTopLeft.WithStyles("sel") : overlayTopLeft;
                        ctx.DrawImage(widget, img, x - OverlayOverflow, y - OverlayOverflow);
                    }
                    if (overlayTopRight != null && overlayTopRight != NullImage)
                    {
                        var img = selected ? overlayTopRight.WithStyles("sel") : overlayTopRight;
                        ctx.DrawImage(widget, img, x + image.Width - img.Width + OverlayOverflow, y - OverlayOverflow);
                    }
                }
            }
        }
    }

    internal class NodeHashtable : Dictionary<object, object>
    {
        // This dictionary can be configured to use object reference equality
        // instead of regular object equality for a specific set of types

        private readonly NodeComparer nodeComparer;

        public NodeHashtable() : base(new NodeComparer())
        {
            nodeComparer = (NodeComparer)Comparer;
        }

        /// <summary>
        /// Sets that the objects of the specified type have to be compared
        /// using object reference equality
        /// </summary>
        public void RegisterByRefType(Type type)
        {
            nodeComparer.ByRefTypes.Add(type);
        }

        private class NodeComparer : IEqualityComparer<object>
        {
            public readonly HashSet<Type> ByRefTypes = new HashSet<Type>();
            public readonly Dictionary<Type, bool> TypeData = new Dictionary<Type, bool>();

            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                if (CompareByRef(x.GetType()))
                    return x == y;
                return x.Equals(y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                if (CompareByRef(obj.GetType()))
                    return RuntimeHelpers.GetHashCode(obj);
                return obj.GetHashCode();
            }

            private bool CompareByRef(Type type)
            {
                if (ByRefTypes.Count == 0)
                    return false;

                bool compareRef;
                if (!TypeData.TryGetValue(type, out compareRef))
                {
                    compareRef = false;
                    var t = type;
                    while (t != null)
                    {
                        if (ByRefTypes.Contains(t))
                        {
                            compareRef = true;
                            break;
                        }
                        t = t.BaseType;
                    }
                    TypeData[type] = compareRef;
                }
                return compareRef;
            }
        }
    }
}
