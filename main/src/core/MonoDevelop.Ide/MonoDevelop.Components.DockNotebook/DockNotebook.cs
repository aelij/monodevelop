// DragNotebook.cs
//
// Author:
//   Todd Berman  <tberman@off.net>
//
// Copyright (c) 2004 Todd Berman
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Gdk;
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using Drag = Gtk.Drag;

namespace MonoDevelop.Components.DockNotebook
{
    internal delegate void TabsReorderedHandler(Widget widget, int oldPlacement, int newPlacement);

    internal class DockNotebook : VBox
    {
        private readonly List<DockNotebookTab> pages = new List<DockNotebookTab>();
        private readonly List<DockNotebookTab> pagesHistory = new List<DockNotebookTab>();
        private readonly TabStrip tabStrip;
        private readonly EventBox contentBox;
        private const int MAX_LASTACTIVEWINDOWS = 10;

        private DockNotebookTab currentTab;

        private static DockNotebook activeNotebook;
        private static readonly List<DockNotebook> AllNotebooksList = new List<DockNotebook>();

        public static event EventHandler ActiveNotebookChanged;
        public static event EventHandler NotebookChanged;

        private enum TargetList
        {
            UriList = 100
        }

        private static readonly TargetEntry[] TargetEntryTypes = {
            new TargetEntry ("text/uri-list", 0, (uint)TargetList.UriList)
        };

        public DockNotebook()
        {
            Tabs = new ReadOnlyCollection<DockNotebookTab>(pages);
            AddEvents((int)EventMask.AllEventsMask);

            tabStrip = new TabStrip(this);

            PackStart(tabStrip, false, false, 0);

            contentBox = new EventBox();
            PackStart(contentBox, true, true, 0);

            ShowAll();

            contentBox.NoShowAll = true;

            tabStrip.DropDownButton.Sensitive = false;

            tabStrip.DropDownButton.ContextMenuRequested = delegate
            {
                ContextMenu menu = new ContextMenu();
                foreach (var tab in pages)
                {
                    var item = new ContextMenuItem(tab.Markup ?? tab.Text);
                    var locTab = tab;
                    item.Clicked += (sender, e) =>
                    {
                        CurrentTab = locTab;
                    };
                    menu.Items.Add(item);
                }
                return menu;
            };

            Drag.DestSet(this, DestDefaults.Motion | DestDefaults.Highlight | DestDefaults.Drop, TargetEntryTypes, DragAction.Copy);
            DragDataReceived += OnDragDataReceived;

            DragMotion += delegate
            {
                // Bring this window to the front. Otherwise, the drop may end being done in another window that overlaps this one
                if (!Platform.IsWindows)
                {
                    var window = ((Gtk.Window)Toplevel);
                    if (window is DockWindow)
                        window.Present();
                }
            };

            AllNotebooksList.Add(this);
        }

        public static DockNotebook ActiveNotebook
        {
            get { return activeNotebook; }
            set
            {
                if (activeNotebook != value)
                {
                    if (activeNotebook != null)
                        activeNotebook.tabStrip.IsActiveNotebook = false;
                    activeNotebook = value;
                    if (activeNotebook != null)
                        activeNotebook.tabStrip.IsActiveNotebook = true;
                    ActiveNotebookChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static IEnumerable<DockNotebook> AllNotebooks => AllNotebooksList;

        private Cursor fleurCursor = new Cursor(CursorType.Fleur);

        public event EventHandler<TabEventArgs> TabClosed;
        public event EventHandler<TabEventArgs> TabActivated;

        public event EventHandler PageAdded;
        public event EventHandler PageRemoved;
        public event EventHandler SwitchPage;

        public event EventHandler PreviousButtonClicked
        {
            add { tabStrip.PreviousButton.Clicked += value; }
            remove { tabStrip.PreviousButton.Clicked -= value; }
        }

        public event EventHandler NextButtonClicked
        {
            add { tabStrip.NextButton.Clicked += value; }
            remove { tabStrip.NextButton.Clicked -= value; }
        }

        public bool PreviousButtonEnabled
        {
            get { return tabStrip.PreviousButton.Sensitive; }
            set { tabStrip.PreviousButton.Sensitive = value; }
        }

        public bool NextButtonEnabled
        {
            get { return tabStrip.NextButton.Sensitive; }
            set { tabStrip.NextButton.Sensitive = value; }
        }

        public bool NavigationButtonsVisible
        {
            get { return tabStrip.NavigationButtonsVisible; }
            set { tabStrip.NavigationButtonsVisible = value; }
        }

        public ReadOnlyCollection<DockNotebookTab> Tabs { get; }

        public DockNotebookTab CurrentTab
        {
            get { return currentTab; }
            set
            {
                if (currentTab != value)
                {
                    currentTab = value;
                    if (contentBox.Child != null)
                        contentBox.Remove(contentBox.Child);

                    if (currentTab != null)
                    {
                        if (currentTab.Content != null)
                        {
                            contentBox.Add(currentTab.Content);
                            contentBox.ChildFocus(DirectionType.Down);
                        }
                        pagesHistory.Remove(currentTab);
                        pagesHistory.Insert(0, currentTab);
                        if (pagesHistory.Count > MAX_LASTACTIVEWINDOWS)
                            pagesHistory.RemoveAt(pagesHistory.Count - 1);
                    }

                    tabStrip.Update();

                    SwitchPage?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int CurrentTabIndex
        {
            get { return currentTab?.Index ?? -1; }
            set { CurrentTab = value > pages.Count - 1 ? null : pages[value]; }
        }

        private void SelectLastActiveTab(int lastClosed)
        {
            if (pages.Count == 0)
            {
                CurrentTab = null;
                return;
            }

            while (pagesHistory.Count > 0 && pagesHistory[0].Content == null)
                pagesHistory.RemoveAt(0);

            if (pagesHistory.Count > 0)
                CurrentTab = pagesHistory[0];
            else
            {
                CurrentTab = lastClosed + 1 < pages.Count ? pages[lastClosed + 1] : pages[lastClosed - 1];
            }
        }

        public int TabCount => pages.Count;

        public int BarHeight => tabStrip.BarHeight;

        internal void InitSize()
        {
            tabStrip.InitSize();
        }

        private void OnDragDataReceived(object o, DragDataReceivedArgs args)
        {
            if (args.Info != (uint)TargetList.UriList)
                return;
            string fullData = Encoding.UTF8.GetString(args.SelectionData.Data);

            foreach (string individualFile in fullData.Split('\n'))
            {
                string file = individualFile.Trim();
                if (file.StartsWith("file://"))
                {
                    var filePath = new FilePath(file);

                    try
                    {
                        IdeApp.Workbench.OpenDocument(filePath, null, -1, -1, OpenDocumentOptions.Default, null, null, this);
                    }
                    catch (Exception e)
                    {
                        LoggingService.LogError("unable to open file {0} exception was :\n{1}", file, e.ToString());
                    }
                }
            }
        }
        public DockNotebookContainer Container
        {
            get
            {
                var container = (DockNotebookContainer)Parent;
                return container.MotherContainer() ?? container;
            }
        }

        /// <summary>
        /// Returns the next notebook in the same window
        /// </summary>
        public DockNotebook GetNextNotebook()
        {
            return Container.GetNextNotebook(this);
        }

        /// <summary>
        /// Returns the previous notebook in the same window
        /// </summary>
        public DockNotebook GetPreviousNotebook()
        {
            return Container.GetPreviousNotebook(this);
        }

        public Action<DockNotebook, int, EventButton> DoPopupMenu { get; set; }

        public DockNotebookTab AddTab(Widget content = null)
        {
            var t = InsertTab(-1);
            if (content != null)
                t.Content = content;
            return t;
        }

        public DockNotebookTab InsertTab(int index)
        {
            var tab = new DockNotebookTab(this, tabStrip);
            if (index == -1)
            {
                pages.Add(tab);
                tab.Index = pages.Count - 1;
            }
            else
            {
                pages.Insert(index, tab);
                tab.Index = index;
                UpdateIndexes(index + 1);
            }

            pagesHistory.Add(tab);

            if (pages.Count == 1)
                CurrentTab = tab;

            tabStrip.StartOpenAnimation(tab);
            tabStrip.Update();
            tabStrip.DropDownButton.Sensitive = pages.Count > 0;

            PageAdded?.Invoke(this, EventArgs.Empty);

            NotebookChanged?.Invoke(this, EventArgs.Empty);

            return tab;
        }

        private void UpdateIndexes(int startIndex)
        {
            for (int n = startIndex; n < pages.Count; n++)
                pages[n].Index = n;
        }

        public DockNotebookTab GetTab(int n)
        {
            if (n < 0 || n >= pages.Count)
                return null;
            return pages[n];
        }

        public void RemoveTab(int page, bool animate)
        {
            var tab = pages[page];
            if (animate)
                tabStrip.StartCloseAnimation(tab);
            pagesHistory.Remove(tab);
            if (pages.Count == 1)
                CurrentTab = null;
            else if (page == CurrentTabIndex)
                SelectLastActiveTab(page);
            pages.RemoveAt(page);
            UpdateIndexes(page);
            tabStrip.Update();
            tabStrip.DropDownButton.Sensitive = pages.Count > 0;

            PageRemoved?.Invoke(this, EventArgs.Empty);

            NotebookChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void ReorderTab(DockNotebookTab tab, DockNotebookTab targetTab)
        {
            if (tab == targetTab)
                return;
            int targetPos = targetTab.Index;
            if (tab.Index > targetTab.Index)
            {
                pages.RemoveAt(tab.Index);
                pages.Insert(targetPos, tab);
            }
            else
            {
                pages.Insert(targetPos + 1, tab);
                pages.RemoveAt(tab.Index);
            }
            IdeApp.Workbench.ReorderDocuments(tab.Index, targetPos);
            UpdateIndexes(Math.Min(tab.Index, targetPos));
            tabStrip.Update();
        }

        internal void OnCloseTab(DockNotebookTab tab)
        {
            TabClosed?.Invoke(this, new TabEventArgs { Tab = tab });
        }

        internal void OnActivateTab(DockNotebookTab tab)
        {
            TabActivated?.Invoke(this, new TabEventArgs { Tab = tab });
        }

        internal void ShowContent(DockNotebookTab tab)
        {
            if (tab == currentTab)
                contentBox.Child = tab.Content;
        }

        protected override bool OnButtonPressEvent(EventButton evnt)
        {
            ActiveNotebook = this;
            return base.OnButtonPressEvent(evnt);
        }

        protected override void OnDestroyed()
        {
            AllNotebooksList.Remove(this);

            if (ActiveNotebook == this)
                ActiveNotebook = null;
            if (fleurCursor != null)
            {
                fleurCursor.Dispose();
                fleurCursor = null;
            }
            base.OnDestroyed();
        }
    }

    internal class DockNotebookChangedArgs : EventArgs
    {
        public DockNotebook Notebook { get; private set; }
        public DockNotebookChangedArgs(DockNotebook notebook)
        {
            Notebook = notebook;
        }
    }
}
