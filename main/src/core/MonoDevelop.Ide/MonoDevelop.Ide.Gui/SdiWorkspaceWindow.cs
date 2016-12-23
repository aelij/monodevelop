//  SdiWorkspaceWindow.cs
//
// Author:
//   Mike Krüger
//   Lluis Sanchez Gual
//
//  This file was derived from a file from #Develop 2.0
//
//  Copyright (C) 2001-2007 Mike Krüger <mkrueger@novell.com>
//  Copyright (C) 2006 Novell, Inc (http://www.novell.com)
// 
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
// 
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

using System;
using System.Collections.Generic;
using System.Linq;
using Gdk;
using GLib;
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Components.DockNotebook;
using MonoDevelop.Core;
using MonoDevelop.Ide.Gui.Content;
using Window = Gtk.Window;

namespace MonoDevelop.Ide.Gui
{
    internal class SdiWorkspaceWindow : EventBox, IWorkbenchWindow, ICommandDelegatorRouter
    {
        private readonly DefaultWorkbench workbench;
        private ViewContent content;

        private List<BaseViewContent> viewContents = new List<BaseViewContent>();
        private Notebook subViewNotebook;
        private Tabstrip subViewToolbar;
        private PathBar pathBar;
        private HBox toolbarBox;
        private readonly Dictionary<BaseViewContent, DocumentToolbar> documentToolbars = new Dictionary<BaseViewContent, DocumentToolbar>();

        private readonly VBox box;
        private DockNotebookTab tab;
        private Widget tabPage;
        private DockNotebook tabControl;

        private string myUntitledTitle;
        private string titleHolder = "";

        private IPathedDocument pathDoc;

        private bool showNotification;

        private readonly uint presentTimeout = 0;

        private ViewCommandHandlers commandHandler;

        public event EventHandler ViewsChanged;

        public DockNotebook TabControl => tabControl;

        internal void SetDockNotebook(DockNotebook tc, DockNotebookTab tabLabel)
        {
            tabControl = tc;
            tab = tabLabel;
            SetTitleEvent(null, null);
            SetDockNotebookTabTitle();
        }

        public SdiWorkspaceWindow(DefaultWorkbench workbench, ViewContent content, DockNotebook tabControl, DockNotebookTab tabLabel)
        {
            this.workbench = workbench;
            this.tabControl = tabControl;
            this.content = content;
            tab = tabLabel;

            box = new VBox();

            viewContents.Add(content);

            //this fires an event that the content uses to access this object's ExtensionContext
            content.WorkbenchWindow = this;

            // The previous WorkbenchWindow property assignement may end with a call to AttachViewContent,
            // which will add the content control to the subview notebook. In that case, we don't need to add it to box
            content.ContentNameChanged += SetTitleEvent;
            content.DirtyChanged += HandleDirtyChanged;
            box.Show();
            Add(box);

            SetTitleEvent(null, null);
        }

        internal void CreateCommandHandler()
        {
            commandHandler = new ViewCommandHandlers(this);
        }

        private void HandleDirtyChanged(object sender, EventArgs e)
        {
            OnTitleChanged(null);
        }

        public Widget TabPage
        {
            get
            {
                if (tabPage == null)
                {
                    var control = content.Control;
                    var type = control.GetType().FullName;
                    tabPage = control;
                }

                return tabPage;
            }
            set { tabPage = value; }
        }

        internal DockNotebookTab TabLabel => tab;

        protected override void OnRealized()
        {
            base.OnRealized();
            if (tabPage == null && subViewNotebook == null)
            {
                box.PackStart(TabPage);
            }
        }

        private Document document;
        public Document Document
        {
            get
            {
                return document;
            }
            set
            {
                document = value;
                OnDocumentChanged(EventArgs.Empty);
            }
        }

        protected override bool OnWidgetEvent(Event evt)
        {
            if (evt.Type == EventType.ButtonRelease)
                DockNotebook.ActiveNotebook = (SdiDragNotebook)Parent.Parent;

            return base.OnWidgetEvent(evt);
        }

        protected virtual void OnDocumentChanged(EventArgs e)
        {
            DocumentChanged?.Invoke(this, e);
        }
        public event EventHandler DocumentChanged;

        public bool ShowNotification
        {
            get
            {
                return showNotification;
            }
            set
            {
                if (showNotification != value)
                {
                    showNotification = value;
                    OnTitleChanged(null);
                }
            }
        }

        public string Title
        {
            get
            {
                //FIXME: This breaks, Why? --Todd
                //_titleHolder = tabControl.GetTabLabelText (tabPage);
                return titleHolder;
            }
            set
            {
                titleHolder = value;
                OnTitleChanged(null);
            }
        }

        public IEnumerable<BaseViewContent> SubViewContents => viewContents;

        // caution use activeView with care !!
        private BaseViewContent activeView;
        public BaseViewContent ActiveViewContent
        {
            get
            {
                if (activeView != null)
                    return activeView;
                if (subViewToolbar != null)
                    return viewContents[subViewToolbar.ActiveTab];
                return content;
            }
            set
            {
                activeView = value;
                OnActiveViewContentChanged(new ActiveViewContentEventArgs(value));
            }
        }

        public void SwitchView(int viewNumber)
        {
            if (subViewNotebook != null)
                ShowPage(viewNumber);
        }

        public void SwitchView(BaseViewContent view)
        {
            if (subViewNotebook != null)
                ShowPage(viewContents.IndexOf(view));
        }

        public int FindView<T>()
        {
            for (int i = 0; i < viewContents.Count; i++)
            {
                if (viewContents[i] is T)
                    return i;
            }

            return -1;
        }

        public void OnDeactivated()
        {
            pathBar?.HideMenu();
        }

        public void OnActivated()
        {
            subViewToolbar?.Tabs[subViewToolbar.ActiveTab].Activate();
        }

        public void SelectWindow()
        {
            var window = tabControl.Toplevel as Window;
            if (window is DockWindow)
                DesktopService.GrabDesktopFocus(window);

#if MAC
				AppKit.NSWindow nswindow = MonoDevelop.Components.Mac.GtkMacInterop.GetNSWindow (window);
				if (nswindow != null)
					nswindow.MakeFirstResponder (nswindow.ContentView);
#endif

            // The tab change must be done now to ensure that the content is created
            // before exiting this method.
            tabControl.CurrentTabIndex = tab.Index;

            // Focus the tab in the next iteration since presenting the window may take some time
            Application.Invoke(delegate
            {
                DockNotebook.ActiveNotebook = tabControl;
                DeepGrabFocus(ActiveViewContent.Control);
            });
        }

        public bool CanMoveToNextNotebook()
        {
            return TabControl.GetNextNotebook() != null || (TabControl.Container.AllowRightInsert && TabControl.TabCount > 1);
        }

        public bool CanMoveToPreviousNotebook()
        {
            return TabControl.GetPreviousNotebook() != null || (TabControl.Container.AllowLeftInsert && TabControl.TabCount > 1);
        }

        public void MoveToNextNotebook()
        {
            var nextNotebook = TabControl.GetNextNotebook();
            if (nextNotebook == null)
            {
                if (TabControl.Container.AllowRightInsert)
                {
                    TabControl.RemoveTab(tab.Index, true);
                    TabControl.Container.InsertRight(this);
                    SelectWindow();
                }
                return;
            }

            TabControl.RemoveTab(tab.Index, true);
            var newTab = nextNotebook.AddTab();
            newTab.Content = this;
            SetDockNotebook(nextNotebook, newTab);
            SelectWindow();
        }

        public void MoveToPreviousNotebook()
        {
            var nextNotebook = TabControl.GetPreviousNotebook();
            if (nextNotebook == null)
            {
                if (TabControl.Container.AllowLeftInsert)
                {
                    TabControl.RemoveTab(tab.Index, true);
                    TabControl.Container.InsertLeft(this);
                    SelectWindow();
                }
                return;
            }

            TabControl.RemoveTab(tab.Index, true);
            var newTab = nextNotebook.AddTab();
            newTab.Content = this;
            SetDockNotebook(nextNotebook, newTab);
            SelectWindow();
        }

        private static void DeepGrabFocus(Widget widget)
        {
            Widget first = null;
            foreach (var f in GetFocussableWidgets(widget))
            {
                if (f.HasFocus)
                    return;
                if (first == null)
                    first = f;
            }
            first?.GrabFocus();
        }

        private static IEnumerable<Widget> GetFocussableWidgets(Widget widget)
        {
            var c = widget as Container;
            if (widget.CanFocus)
                yield return widget;
            if (c != null)
            {
                foreach (var f in c.FocusChain.SelectMany(GetFocussableWidgets).Where(y => y != null))
                    yield return f;
            }
        }

        public DocumentToolbar GetToolbar(BaseViewContent targetView)
        {
            DocumentToolbar toolbar;
            if (!documentToolbars.TryGetValue(targetView, out toolbar))
            {
                toolbar = new DocumentToolbar();
                documentToolbars[targetView] = toolbar;
                box.PackStart(toolbar.Container, false, false, 0);
                box.ReorderChild(toolbar.Container, 0);
                toolbar.Visible = (targetView == ActiveViewContent);
                PathWidgetEnabled = !toolbar.Visible;
            }
            return toolbar;
        }

        public ViewContent ViewContent
        {
            get
            {
                return content;
            }
            set
            {
                content = value;
            }
        }

        public ViewCommandHandlers CommandHandler => commandHandler;

        public string DocumentType { get; set; }

        public void SetTitleEvent(object sender, EventArgs e)
        {
            if (content == null)
                return;

            string newTitle;
            if (content.ContentName == null)
            {
                if (myUntitledTitle == null)
                {
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(content.UntitledName);
                    int number = 1;
                    bool found = true;
                    myUntitledTitle = baseName + System.IO.Path.GetExtension(content.UntitledName);
                    while (found)
                    {
                        found = false;
                        foreach (ViewContent windowContent in workbench.InternalViewContentCollection)
                        {
                            string title = windowContent.WorkbenchWindow.Title;
                            if (title.EndsWith("+"))
                            {
                                title = title.Substring(0, title.Length - 1);
                            }
                            if (title == myUntitledTitle)
                            {
                                myUntitledTitle = baseName + number + System.IO.Path.GetExtension(content.UntitledName);
                                found = true;
                                ++number;
                                break;
                            }
                        }
                    }
                }
                newTitle = myUntitledTitle;
            }
            else
            {
                newTitle = System.IO.Path.GetFileName(content.ContentName);
            }

            if (content.IsDirty)
            {
                if (!String.IsNullOrEmpty(content.ContentName))
                {
                    // TODO-AELIJ
                    //IdeApp.ProjectOperations.MarkFileDirty(content.ContentName);
                }
            }
            else if (content.IsReadOnly)
            {
                newTitle += "+";
            }
            if (newTitle != Title)
            {
                Title = newTitle;
            }
        }

        public bool CloseWindow(bool force)
        {
            return CloseWindow(force, false);
        }

        public bool CloseWindow(bool force, bool animate)
        {
            bool wasActive = workbench.ActiveWorkbenchWindow == this;
            WorkbenchWindowEventArgs args = new WorkbenchWindowEventArgs(force, wasActive);
            args.Cancel = false;
            OnClosing(args);
            if (args.Cancel)
                return false;

            workbench.RemoveTab(tabControl, tab.Index, animate);

            OnClosed(args);

            // This may happen if the document contains an attached view that is shown by
            // default. In that case the main view is not added to the notebook and won't
            // be destroyed.
            bool destroyMainPage = tabPage != null && tabPage.Parent == null;

            Destroy();

            // Destroy after the document is destroyed, since attached views may have references to the main view
            if (destroyMainPage)
                tabPage.Destroy();

            return true;
        }

        protected override void OnDestroyed()
        {
            if (presentTimeout != 0)
            {
                Source.Remove(presentTimeout);
            }

            if (viewContents != null)
            {
                foreach (BaseViewContent sv in SubViewContents)
                {
                    sv.Dispose();
                }
                viewContents = null;
            }

            if (content != null)
            {
                content.ContentNameChanged -= SetTitleEvent;
                content.DirtyChanged -= HandleDirtyChanged;
                content.WorkbenchWindow = null;
                content.Dispose();
                content = null;
            }

            if (subViewToolbar != null)
            {
                subViewToolbar.Dispose();
                subViewToolbar = null;
            }

            DetachFromPathedDocument();
            commandHandler = null;
            document = null;
            base.OnDestroyed();
        }

        #region lazy UI element creation

        private void CheckCreateSubViewToolbar()
        {
            if (subViewToolbar != null)
                return;

            subViewToolbar = new Tabstrip();
            subViewToolbar.Show();

            CheckCreateToolbarBox();
            toolbarBox.PackStart(subViewToolbar, true, true, 0);
        }

        private void EnsureToolbarBoxSeparator()
        {
            /*			The path bar is now shown at the top

                        if (toolbarBox == null || subViewToolbar == null)
                            return;

                        if (separatorItem != null && pathBar == null) {
                            subViewToolbar.Remove (separatorItem);
                            separatorItem = null;
                        } else if (separatorItem == null && pathBar != null) {
                            separatorItem = new SeparatorToolItem ();
                            subViewToolbar.PackStart (separatorItem, false, false, 0);
                        } else if (separatorItem != null && pathBar != null) {
                            Widget[] buttons = subViewToolbar.Children;
                            if (separatorItem != buttons [buttons.Length - 1])
                                subViewToolbar.ReorderChild (separatorItem, buttons.Length - 1);
                        }
                        */
        }

        private void CheckCreateToolbarBox()
        {
            if (toolbarBox != null)
                return;
            toolbarBox = new HBox(false, 0);
            toolbarBox.Show();
            box.PackEnd(toolbarBox, false, false, 0);
        }

        private void CheckCreateSubViewContents()
        {
            if (subViewNotebook != null)
                return;

            // The view may call AttachViewContent when initialized, and this
            // may happen before the main content is added to 'box', so we
            // have to check if the content is already parented or not

            Widget viewWidget = ViewContent.Control;
            if (viewWidget.Parent != null)
                box.Remove(viewWidget);

            subViewNotebook = new Notebook();
            subViewNotebook.TabPos = PositionType.Bottom;
            subViewNotebook.ShowTabs = false;
            subViewNotebook.ShowBorder = false;
            subViewNotebook.Show();

            //add existing ViewContent
            AddButton(ViewContent.TabPageLabel, ViewContent);

            //pack them in a box
            subViewNotebook.Show();
            box.PackStart(subViewNotebook, true, true, 1);
            box.Show();
        }

        #endregion

        public void AttachViewContent(BaseViewContent subViewContent)
        {
            InsertViewContent(viewContents.Count, subViewContent);
        }

        public void InsertViewContent(int index, BaseViewContent subViewContent)
        {
            // need to create child Notebook when first IAttachableViewContent is added
            CheckCreateSubViewContents();

            viewContents.Insert(index, subViewContent);
            subViewContent.WorkbenchWindow = this;
            InsertButton(index, subViewContent.TabPageLabel, subViewContent);

            ViewsChanged?.Invoke(this, EventArgs.Empty);
        }

        protected Tab AddButton(string label, BaseViewContent viewContent)
        {
            return InsertButton(viewContents.Count, label, viewContent);
        }

        private bool updating;
        protected Tab InsertButton(int index, string label, BaseViewContent viewContent)
        {
            CheckCreateSubViewToolbar();
            updating = true;

            var addedContent = (index == 0 || subViewToolbar.TabCount == 0) && IdeApp.Workbench.ActiveDocument == Document;
            var widgetBox = new VBox();
            var tab = new Tab(subViewToolbar, label)
            {
                Tag = viewContent
            };

            // If this is the current displayed document we need to add the control immediately as the tab is already active.
            if (addedContent)
            {
                widgetBox.Add(viewContent.Control);
                widgetBox.Show();
            }

            subViewToolbar.InsertTab(index, tab);
            subViewNotebook.InsertPage(widgetBox, new Label(), index);
            tab.Activated += (sender, e) =>
            {
                if (!addedContent)
                {
                    widgetBox.Add(viewContent.Control);
                    widgetBox.Show();
                    addedContent = true;
                }

                int page = viewContents.IndexOf((BaseViewContent)tab.Tag);
                SetCurrentView(page);
                QueueDraw();
            };

            EnsureToolbarBoxSeparator();
            updating = false;

            if (index == 0)
                ShowPage(0);

            return tab;
        }

        #region Track and display document's "path"

        internal void AttachToPathedDocument(IPathedDocument pathDoc)
        {
            if (this.pathDoc != pathDoc)
                DetachFromPathedDocument();
            if (pathDoc == null)
                return;
            pathDoc.PathChanged += HandlePathChange;
            this.pathDoc = pathDoc;

            // If a toolbar is already being shown, we don't show the pathbar yet
            DocumentToolbar toolbar;
            if (documentToolbars.TryGetValue(ActiveViewContent, out toolbar) && toolbar.Visible)
                return;

            PathWidgetEnabled = true;
            pathBar.SetPath(pathDoc.CurrentPath);
        }

        internal void DetachFromPathedDocument()
        {
            if (pathDoc == null)
                return;
            PathWidgetEnabled = false;
            pathDoc.PathChanged -= HandlePathChange;
            pathDoc = null;
        }

        private void HandlePathChange(object sender, DocumentPathChangedEventArgs args)
        {
            var pathDoc = (IPathedDocument)sender;
            pathBar?.SetPath(pathDoc.CurrentPath);
        }

        private bool PathWidgetEnabled
        {
            get { return (pathBar != null); }
            set
            {
                if (PathWidgetEnabled == value)
                    return;
                if (value)
                {
                    pathBar = new PathBar(pathDoc.CreatePathWidget);
                    box.PackStart(pathBar, false, true, 0);
                    box.ReorderChild(pathBar, 0);
                    pathBar.Show();
                }
                else
                {
                    box.Remove(pathBar);
                    pathBar.Destroy();
                    pathBar = null;
                }
            }
        }

        #endregion

        protected void ShowPage(int npage)
        {
            if (updating || npage < 0) return;
            updating = true;
            subViewToolbar.ActiveTab = npage;
            updating = false;
        }

        private void SetCurrentView(int newIndex)
        {
            BaseViewContent subViewContent;

            int oldIndex = subViewNotebook.CurrentPage;
            subViewNotebook.CurrentPage = newIndex;

            if (oldIndex != -1)
            {
                subViewContent = viewContents[oldIndex];
                subViewContent?.OnDeselected();
            }

            subViewContent = viewContents[newIndex];

            DetachFromPathedDocument();

            IPathedDocument pathedDocument;
            if (newIndex < 0 || newIndex == viewContents.IndexOf(ViewContent))
            {
                pathedDocument = Document != null ? Document.GetContent<IPathedDocument>() : (IPathedDocument)ViewContent.GetContent(typeof(IPathedDocument));
            }
            else
            {
                pathedDocument = (IPathedDocument)viewContents[newIndex].GetContent(typeof(IPathedDocument));
            }

            var toolbarVisible = false;
            foreach (var t in documentToolbars)
            {
                toolbarVisible = ActiveViewContent == t.Key;
                t.Value.Container.GetNativeWidget<Widget>().Visible = toolbarVisible;
            }

            if (pathedDocument != null && !toolbarVisible)
                AttachToPathedDocument(pathedDocument);

            subViewContent?.OnSelected();

            OnActiveViewContentChanged(new ActiveViewContentEventArgs(ActiveViewContent));
        }

        object ICommandDelegatorRouter.GetNextCommandTarget()
        {
            return Parent;
        }

        object ICommandDelegatorRouter.GetDelegatedCommandTarget()
        {
            // If command checks are flowing through this view, it means the view's notebook
            // is the active notebook.
            if (!(Toplevel is Window))
                return null;

            if (((Window)Toplevel).HasToplevelFocus)
                DockNotebook.ActiveNotebook = (SdiDragNotebook)Parent.Parent;

            // Route commands to the view
            return ActiveViewContent;
        }

        private void SetDockNotebookTabTitle()
        {
            tab.Text = Title;
            tab.Notify = showNotification;
            tab.Dirty = content.IsDirty;
            if (!string.IsNullOrEmpty(content.ContentName))
            {
                tab.Tooltip = content.ContentName;
            }
            try
            {
                if (content.StockIconId != null)
                {
                    tab.Icon = ImageService.GetIcon(content.StockIconId, IconSize.Menu);
                }
                else
                    if (content.ContentName != null && content.ContentName.IndexOfAny(new[] {
                        '*',
                        '+'
                    }) == -1)
                {
                    tab.Icon = DesktopService.GetIconForFile(content.ContentName, IconSize.Menu);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError(ex.ToString());
                tab.Icon = DesktopService.GetIconForType("gnome-fs-regular", IconSize.Menu);
            }
        }

        protected virtual void OnTitleChanged(EventArgs e)
        {
            SetDockNotebookTabTitle();
            TitleChanged?.Invoke(this, e);
        }

        protected virtual void OnClosing(WorkbenchWindowEventArgs e)
        {
            Closing?.Invoke(this, e);
        }

        protected virtual void OnClosed(WorkbenchWindowEventArgs e)
        {
            Closed?.Invoke(this, e);
        }

        protected virtual void OnActiveViewContentChanged(ActiveViewContentEventArgs e)
        {
            ActiveViewContentChanged?.Invoke(this, e);
        }

        public event EventHandler TitleChanged;
        public event WorkbenchWindowEventHandler Closed;
        public event WorkbenchWindowEventHandler Closing;
        public event ActiveViewContentEventHandler ActiveViewContentChanged;
    }
}
