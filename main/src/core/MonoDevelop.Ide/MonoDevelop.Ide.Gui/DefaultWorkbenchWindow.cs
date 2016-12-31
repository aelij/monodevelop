//  DefaultWorkbench.cs
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gdk;
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Components.Docking;
using MonoDevelop.Components.DockNotebook;
using MonoDevelop.Components.MainToolbar;
using MonoDevelop.Core;
using MonoDevelop.Core.Annotations;
using MonoDevelop.Ide.Gui.Dialogs;
using Image = Xwt.Drawing.Image;
using Key = Gdk.Key;
using Rectangle = System.Drawing.Rectangle;

namespace MonoDevelop.Ide.Gui
{
    /// <summary>
    /// This is the a Workspace with a multiple document interface.
    /// </summary>
    internal class DefaultWorkbenchWindow : WorkbenchWindow, ICommandRouter, IDefaultWorkbenchWindow
    {
        private const string MainMenuPath = "/MonoDevelop/Ide/MainMenu";
        private const string AppMenuPath = "/MonoDevelop/Ide/AppMenu";

        private static readonly string ConfigFile = UserProfile.Current.ConfigDir.Combine("EditingLayout.xml");
        private const string FullViewModeTag = "[FullViewMode]";
        private const int MinimumWidth = 800;
        private const int MinimumHeight = 540;

        // list of layout names for the current context, without the context prefix
        private readonly List<string> layouts = new List<string>();

        private readonly List<ViewContent> viewContentCollection = new List<ViewContent>();
        private readonly Dictionary<PadDefinition, IPadWindow> padWindows = new Dictionary<PadDefinition, IPadWindow>();
        private readonly Dictionary<IPadWindow, PadDefinition> padCodons = new Dictionary<IPadWindow, PadDefinition>();

        private IWorkbenchWindow lastActive;

        private bool closeAll;

        private readonly Rectangle normalBounds = new Rectangle(0, 0, MinimumWidth, MinimumHeight);

        private Container rootWidget;
        [CanBeNull]
        private CommandFrame toolbarFrame;
        private SdiDragNotebook tabControl;
        private VBox fullViewVBox;
        private DockItem documentDockItem;
        [CanBeNull]
        private IMainToolbarController toolbar;

        public event EventHandler ActiveWorkbenchWindowChanged;
        public event EventHandler WorkbenchTabsChanged;

        public IStatusBar StatusBar => toolbar?.StatusBar;

        public MenuBar TopMenu { get; private set; }

        public MonoDevelopStatusBar BottomBar { get; private set; }

        internal SdiDragNotebook TabControl
        {
            get { return tabControl; }
            set
            {
                tabControl = value;
                tabControl.NavigationButtonsVisible = true;
            }
        }

        public IWorkbenchWindow ActiveWorkbenchWindow
        {
            get
            {
                if (DockNotebook.ActiveNotebook == null || DockNotebook.ActiveNotebook.CurrentTabIndex < 0 || DockNotebook.ActiveNotebook.CurrentTabIndex >= DockNotebook.ActiveNotebook.TabCount)
                {
                    return null;
                }
                return (IWorkbenchWindow)DockNotebook.ActiveNotebook.CurrentTab.Content;
            }
        }

        public void ShowCommandBar(string barId)
        {
            toolbar?.ShowCommandBar(barId);
        }

        public void HideCommandBar(string barId)
        {
            toolbar?.HideCommandBar(barId);
        }

        public DockFrame DockFrame { get; internal set; }

        public bool FullScreen
        {
            get { return DesktopService.GetIsFullscreen(this); }
            set { DesktopService.SetIsFullscreen(this, value); }
        }

        public IList<string> Layouts => layouts;

        public string CurrentLayout
        {
            get
            {
                if (DockFrame?.CurrentLayout != null)
                {
                    var s = DockFrame.CurrentLayout;
                    s = s.Substring(s.IndexOf(".", StringComparison.Ordinal) + 1);
                    if (s.EndsWith(FullViewModeTag))
                        return s.Substring(0, s.Length - FullViewModeTag.Length);
                    return s;
                }
                return "";
            }
            set
            {
                var oldLayout = DockFrame.CurrentLayout;

                InitializeLayout(value);
                DockFrame.CurrentLayout = value;

                DestroyFullViewLayouts(oldLayout);

                // persist the selected layout
                PropertyService.Set("MonoDevelop.Core.Gui.CurrentWorkbenchLayout", value);
            }
        }

        public void DeleteLayout(string name)
        {
            var layout = name;
            layouts.Remove(name);
            DockFrame.DeleteLayout(layout);
        }

        public List<PadDefinition> PadContentCollection { get; } = new List<PadDefinition>();

        public List<ViewContent> ViewContentCollection
        {
            get
            {
                Debug.Assert(viewContentCollection != null);
                return viewContentCollection;
            }
        }

        public DefaultWorkbenchWindow()
        {
            Title = BrandingService.ApplicationLongName;
            LoggingService.LogInfo("Creating DefaultWorkbench");

            WidthRequest = normalBounds.Width;
            HeightRequest = normalBounds.Height;

            DeleteEvent += OnClosing;
            BrandingService.ApplicationNameChanged += ApplicationNameChanged;

            SetAppIcons();

            IdeApp.CommandService.SetRootWindow(this);
            DockNotebook.NotebookChanged += NotebookPagesChanged;
        }

        private void NotebookPagesChanged(object sender, EventArgs e)
        {
            WorkbenchTabsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetAppIcons()
        {
            //first try to get the icon from the GTK icon theme
            var appIconName = BrandingService.GetString("ApplicationIconId")
                ?? BrandingService.ApplicationName.ToLower();
            if (IconTheme.Default.HasIcon(appIconName))
            {
                DefaultIconName = appIconName;
                return;
            }

            if (!Platform.IsMac)
            {
                //branded icons
                var iconsEl = BrandingService.GetElement("ApplicationIcons");
                if (iconsEl != null)
                {
                    try
                    {
                        var icons = iconsEl.Elements("Icon")
                            .Select(el => new Pixbuf(BrandingService.GetFile((string)el))).ToArray();
                        DefaultIconList = icons;
                        foreach (var icon in icons)
                            icon.Dispose();
                        return;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("Could not load app icons", ex);
                    }
                }
            }

            //built-ins
            var appIcon = ImageService.GetIcon(Stock.MonoDevelop);
            DefaultIconList = new[] {
                appIcon.ToPixbuf (IconSize.Menu),
                appIcon.ToPixbuf (IconSize.Button),
                appIcon.ToPixbuf (IconSize.Dnd),
                appIcon.ToPixbuf (IconSize.Dialog)
            };
        }

        public void InitializeWorkspace()
        {
            FileService.FileRemoved += CheckRemovedFile;
            FileService.FileRenamed += CheckRenamedFile;

            //			TopMenu.Selected   += new CommandHandler(OnTopMenuSelected);
            //			TopMenu.Deselected += new CommandHandler(OnTopMenuDeselected);

            if (!DesktopService.SetGlobalMenu(IdeApp.CommandService, MainMenuPath, AppMenuPath))
            {
                CreateMenuBar();
            }
        }

        private void CreateMenuBar()
        {
            TopMenu = IdeApp.CommandService.CreateMenuBar(MainMenuPath);
            var appMenu = IdeApp.CommandService.CreateMenu(AppMenuPath);
            if (appMenu != null && appMenu.Children.Length > 0)
            {
                var item = new MenuItem(BrandingService.ApplicationName) { Submenu = appMenu };
                TopMenu.Insert(item, 0);
            }
        }

        private void InstallMenuBar()
        {
            if (TopMenu != null)
            {
                ((VBox)rootWidget).PackStart(TopMenu, false, false, 0);
                ((Box.BoxChild)rootWidget[TopMenu]).Position = 0;
                TopMenu.ShowAll();
            }
        }

        private void UninstallMenuBar()
        {
            if (TopMenu == null)
                return;

            rootWidget.Remove(TopMenu);
            TopMenu.Destroy();
            TopMenu = null;
        }

        public void CloseContent(ViewContent content)
        {
            if (viewContentCollection.Contains(content))
            {
                viewContentCollection.Remove(content);
            }
        }

        public void CloseAllViews()
        {
            try
            {
                closeAll = true;
                var fullList = new List<ViewContent>(viewContentCollection);
                foreach (var content in fullList)
                {
                    content.WorkbenchWindow?.CloseWindow(true);
                }
            }
            finally
            {
                closeAll = false;
                OnActiveWindowChanged(null, null);
            }
        }

        private Image PrepareShowView(ViewContent content)
        {
            viewContentCollection.Add(content);

            Image mimeimage;

            if (content.StockIconId != null)
            {
                mimeimage = ImageService.GetIcon(content.StockIconId, IconSize.Menu);
            }
            else if (content.IsUntitled && content.UntitledName == null)
            {
                mimeimage = DesktopService.GetIconForType("gnome-fs-regular", IconSize.Menu);
            }
            else
            {
                mimeimage = DesktopService.GetIconForFile(content.ContentName ?? content.UntitledName, IconSize.Menu);
            }

            return mimeimage;
        }

        public void ShowView(ViewContent content, FileOpenInformation fileInfo, IViewDisplayBinding binding)
        {
            ShowView(content, fileInfo.Options.HasFlag(OpenDocumentOptions.BringToFront), binding,
                fileInfo.DockNotebook);
        }

        public void ShowView(ViewContent content, bool bringToFront, IViewDisplayBinding binding)
        {
            ShowView(content, bringToFront, binding, notebook: null);
        }

        internal virtual void ShowView(ViewContent content, bool bringToFront, IViewDisplayBinding binding, DockNotebook notebook)
        {
            var isFile = content.IsFile;
            if (!isFile)
            {
                try
                {
                    isFile = File.Exists(content.ContentName);
                }
                catch { /*Ignore*/ }
            }

            string type;
            if (isFile)
            {
                type = System.IO.Path.GetExtension(content.ContentName);
                var mt = DesktopService.GetMimeTypeForUri(content.ContentName);
                if (!string.IsNullOrEmpty(mt))
                    type += " (" + mt + ")";
            }
            else
                type = "(not a file)";

            var metadata = new Dictionary<string, string> {
                { "FileType", type },
                { "DisplayBinding", content.GetType ().FullName }
            };

            if (isFile)
                metadata["DisplayBindingAndType"] = type + " | " + content.GetType().FullName;

            Counters.DocumentOpened.Inc(metadata);

            var mimeimage = PrepareShowView(content);
            var addToControl = notebook ?? DockNotebook.ActiveNotebook ?? tabControl;
            var tab = addToControl.AddTab();

            var sdiWorkspaceWindow = new SdiWorkspaceWindow(this, content, addToControl, tab);
            sdiWorkspaceWindow.TitleChanged += delegate { SetWorkbenchTitle(); };
            sdiWorkspaceWindow.Closed += CloseWindowEvent;
            sdiWorkspaceWindow.Show();
            if (binding != null)
                DisplayBindingService.AttachSubWindows(sdiWorkspaceWindow, binding);

            sdiWorkspaceWindow.CreateCommandHandler();

            tab.Content = sdiWorkspaceWindow;
            if (mimeimage != null)
                tab.Icon = mimeimage;

            if (bringToFront)
                content.WorkbenchWindow.SelectWindow();

            // The insertion of the tab may have changed the active view (or maybe not, this is checked in OnActiveWindowChanged)
            OnActiveWindowChanged(null, null);
        }

        public void ShowPad(PadDefinition content)
        {
            AddPad(content, true);
        }

        public void AddPad(PadDefinition content)
        {
            AddPad(content, false);
        }

        private void RegisterPad(PadDefinition content)
        {
            var lab = content.Label.Length > 0 ? content.Label : "";
            var cmd = new ActionCommand("Pad|" + content.PadId, lab, null)
            {
                DefaultHandler = new PadActivationHandler(this, content),
                Category = "View (Pads)"
            };
            cmd.Description = $"Show {cmd.Text}";
            IdeApp.CommandService.RegisterCommand(cmd);
            PadContentCollection.Add(content);
        }

        private void AddPad(PadDefinition content, bool show)
        {
            var item = GetDockItem(content);
            if (PadContentCollection.Contains(content))
            {
                if (show && item != null)
                    item.Visible = true;
                return;
            }

            if (item != null)
            {
                if (show)
                    item.Visible = true;
            }
            else
            {
                AddPad(content, content.DefaultPlacement, content.DefaultStatus);
            }
        }

        public void RemovePad(PadDefinition definition)
        {
            var cmd = IdeApp.CommandService.GetCommand(definition.Id);
            if (cmd != null)
                IdeApp.CommandService.UnregisterCommand(cmd);
            var item = GetDockItem(definition);
            PadContentCollection.Remove(definition);
            var win = (PadWindow)GetPadWindow(definition);
            if (win != null)
            {
                win.NotifyDestroyed();
                Counters.PadsLoaded--;
                padCodons.Remove(win);
            }
            if (item != null)
                DockFrame.RemoveItem(item);
            padWindows.Remove(definition);
        }

        public void BringToFront(PadDefinition content)
        {
            BringToFront(content, false);
        }

        public virtual void BringToFront(PadDefinition content, bool giveFocus)
        {
            if (!IsVisible(content))
                ShowPad(content);

            ActivatePad(content, giveFocus);
        }

        internal static string GetTitle(IWorkbenchWindow window)
        {
            if (window.ViewContent.IsUntitled)
                return GetDefaultTitle();
            var post = String.Empty;
            if (window.ViewContent.IsDirty)
            {
                post = "*";
            }
            if (window.ViewContent.Project != null)
            {
                return window.ViewContent.Project.Name + " – " + window.ViewContent.PathRelativeToProject + post + " – " + BrandingService.ApplicationLongName;
            }
            return window.ViewContent.ContentName + post + " – " + BrandingService.ApplicationLongName;
        }

        private void SetWorkbenchTitle()
        {
            try
            {
                var window = ActiveWorkbenchWindow;
                if (window != null)
                {
                    if (window.ActiveViewContent.Control.GetNativeWidget<Widget>().Toplevel == this)
                        Title = GetTitle(window);
                }
                else
                {
                    Title = GetDefaultTitle();
                    if (IsInFullViewMode)
                        ToggleFullViewMode();
                }
            }
            catch (Exception)
            {
                Title = GetDefaultTitle();
            }
        }

        private static string GetDefaultTitle()
        {
            return BrandingService.ApplicationLongName;
        }

        private void ApplicationNameChanged(object sender, EventArgs e)
        {
            SetWorkbenchTitle();
        }

        public Properties GetStoredMemento(ViewContent content)
        {
            if (content?.ContentName != null)
            {
                string directory = UserProfile.Current.CacheDir.Combine("temp");
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                var fileName = content.ContentName.Substring(3).Replace('/', '.').Replace('\\', '.').Replace(System.IO.Path.DirectorySeparatorChar, '.');
                var fullFileName = directory + System.IO.Path.DirectorySeparatorChar + fileName;
                // check the file name length because it could be more than the maximum length of a file name
                if (FileService.IsValidPath(fullFileName) && File.Exists(fullFileName))
                {
                    return Properties.Load(fullFileName);
                }
            }
            return null;
        }

        private void CheckRemovedFile(object sender, FileEventArgs args)
        {
            foreach (var e in args)
            {
                if (e.IsDirectory)
                {
                    var views = new ViewContent[viewContentCollection.Count];
                    viewContentCollection.CopyTo(views, 0);
                    foreach (var content in views)
                    {
                        if (content.ContentName.StartsWith(e.FileName, StringComparison.CurrentCulture))
                        {
                            if (content.IsDirty)
                            {
                                content.UntitledName = content.ContentName;
                                content.ContentName = null;
                            }
                            else
                            {
                                ((SdiWorkspaceWindow)content.WorkbenchWindow).CloseWindow(true, true);
                            }
                        }
                    }
                }
                else
                {
                    foreach (var content in viewContentCollection)
                    {
                        if (content.ContentName != null &&
                            content.ContentName == e.FileName)
                        {
                            if (content.IsDirty)
                            {
                                content.UntitledName = content.ContentName;
                                content.ContentName = null;
                            }
                            else
                            {
                                ((SdiWorkspaceWindow)content.WorkbenchWindow).CloseWindow(true, true);
                            }
                            return;
                        }
                    }
                }
            }
        }

        private void CheckRenamedFile(object sender, FileCopyEventArgs args)
        {
            foreach (var e in args)
            {
                if (e.IsDirectory)
                {
                    foreach (var content in viewContentCollection)
                    {
                        if (content.ContentName != null && ((FilePath)content.ContentName).IsChildPathOf(e.SourceFile))
                        {
                            content.ContentName = e.TargetFile.Combine(((FilePath)content.ContentName).FileName);
                        }
                    }
                }
                else
                {
                    foreach (var content in viewContentCollection)
                    {
                        if (content.ContentName != null &&
                            content.ContentName == e.SourceFile)
                        {
                            content.ContentName = e.TargetFile;
                            return;
                        }
                    }
                }
            }
        }

        protected void OnClosing(object o, DeleteEventArgs e)
        {
            if (Close())
            {
                Application.Quit();
            }
            else
            {
                e.RetVal = true;
            }
        }

        protected void OnClosed(EventArgs e)
        {
            //don't allow the "full view" layouts to persist - they are always derived from the "normal" layout
            foreach (var fv in DockFrame.Layouts)
                if (fv.EndsWith(FullViewModeTag))
                    DockFrame.DeleteLayout(fv);
            try
            {
                DockFrame.SaveLayouts(ConfigFile);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error while saving layout.", ex);
            }
            UninstallMenuBar();
            Remove(rootWidget);

            foreach (var content in PadContentCollection)
            {
                if (content.Initialized)
                    content.PadContent.Dispose();
            }

            rootWidget.Destroy();
            Destroy();
        }

        public bool Close()
        {
            if (!IdeApp.OnExit())
                return false;

            var showDirtyDialog = false;

            foreach (var content in viewContentCollection)
            {
                if (content.IsDirty)
                {
                    showDirtyDialog = true;
                    break;
                }
            }

            if (showDirtyDialog)
            {
                using (var dlg = new DirtyFilesDialog())
                {
                    dlg.Modal = true;
                    if (MessageService.ShowCustomDialog(dlg, this) != (int)ResponseType.Ok)
                        return false;
                }
            }

            CloseAllViews();

            BrandingService.ApplicationNameChanged -= ApplicationNameChanged;

            IdeApp.OnExited();
            OnClosed(null);

            IdeApp.CommandService.Dispose();

            return true;
        }

        private int activeWindowChangeLock;

        public void LockActiveWindowChangeEvent()
        {
            activeWindowChangeLock++;
        }

        public void UnlockActiveWindowChangeEvent()
        {
            activeWindowChangeLock--;
            OnActiveWindowChanged(null, null);
        }

        internal void OnActiveWindowChanged(object sender, EventArgs e)
        {
            if (activeWindowChangeLock > 0)
                return;
            if (lastActive == ActiveWorkbenchWindow)
                return;

            ((SdiWorkspaceWindow)lastActive)?.OnDeactivated();

            lastActive = ActiveWorkbenchWindow;
            SetWorkbenchTitle();

            if (!closeAll)
                ((SdiWorkspaceWindow)ActiveWorkbenchWindow)?.OnActivated();

            if (!closeAll)
            {
                ActiveWorkbenchWindowChanged?.Invoke(this, e);
            }
        }

        public PadDefinition GetPad(Type type)
        {
            foreach (var pad in PadContentCollection)
            {
                if (pad.ClassName == type.FullName)
                {
                    return pad;
                }
            }
            return null;
        }

        public PadDefinition GetPad(string id)
        {
            foreach (var pad in PadContentCollection)
            {
                if (pad.PadId == id)
                {
                    return pad;
                }
            }
            return null;
        }

        public void InitializeLayout()
        {
            layouts.Add("Solution");

            CreateComponents();

            // TODO-AELIJ: pads
        }

        private Task layoutChangedTask;

        private async void LayoutChanged(object o, EventArgs e)
        {
            if (layoutChangedTask != null)
            {
                return;
            }

            layoutChangedTask = Task.Delay(10000);
            await layoutChangedTask;
            layoutChangedTask = null;

            DockFrame.SaveLayouts(ConfigFile);
        }

        private void CreateComponents()
        {
            fullViewVBox = new VBox(false, 0);
            rootWidget = fullViewVBox;

            InstallMenuBar();
            Realize();
            toolbar = DesktopService.CreateMainToolbar(this);
            DesktopService.SetMainWindowDecorations(this);
            if (toolbar != null)
            {
                DesktopService.AttachMainToolbar(fullViewVBox, toolbar);
            }

            toolbarFrame = new CommandFrame(IdeApp.CommandService);
            fullViewVBox.PackStart(toolbarFrame, true, true, 0);

            // Create the docking widget and add it to the window.
            DockFrame = new DockFrame();
            DockFrame.LayoutChanged += LayoutChanged;

            DockFrame.CompactGuiLevel = (int)IdeApp.Preferences.WorkbenchCompactness.Value + 1;
            IdeApp.Preferences.WorkbenchCompactness.Changed += delegate
            {
                DockFrame.CompactGuiLevel = (int)IdeApp.Preferences.WorkbenchCompactness.Value + 1;
            };

            toolbarFrame?.Add(DockFrame);

            // Create the notebook for the various documents.
            tabControl = new SdiDragNotebook(this);

            DockNotebook.ActiveNotebookChanged += (sender, args) => OnActiveWindowChanged(null, null);

            Add(fullViewVBox);
            fullViewVBox.ShowAll();
            BottomBar = new MonoDevelopStatusBar();
            fullViewVBox.PackEnd(BottomBar, false, true, 0);
            BottomBar.ShowAll();

            // In order to get the correct bar height we need to calculate the tab size using the
            // correct style (the style of the window). At this point the widget is not yet a child
            // of the window, so its style is not yet the correct one.
            tabControl.InitSize();

            // The main document area
            documentDockItem = DockFrame.AddItem("Documents");
            documentDockItem.Behavior = DockItemBehavior.Locked;
            documentDockItem.Expand = true;
            documentDockItem.DrawFrame = false;
            documentDockItem.Label = "Documents";
            documentDockItem.Content = new DockNotebookContainer(tabControl, true);

            LoadDockStyles();
            Styles.Changed += (sender, e) => LoadDockStyles();

            // Add some hiden items to be used as position reference
            AddDock("__left", "Documents/Left");
            AddDock("__right", "Documents/Right");
            AddDock("__top", "Documents/Top");
            AddDock("__bottom", "Documents/Bottom");

            if (Platform.IsMac)
            {
                BottomBar.HasResizeGrip = true;
            }
            else
            {
                if (GdkWindow != null && GdkWindow.State == WindowState.Maximized)
                {
                    BottomBar.HasResizeGrip = false;
                }
                SizeAllocated += delegate
                {
                    if (GdkWindow != null)
                        BottomBar.HasResizeGrip = GdkWindow.State != WindowState.Maximized;
                };
            }

            // create DockItems for all the pads
            // TODO-AELIJ: dock pads

            try
            {
                if (File.Exists(ConfigFile))
                {
                    DockFrame.LoadLayouts(ConfigFile);
                    foreach (var layout in DockFrame.Layouts)
                    {
                        if (!layouts.Contains(layout) && !layout.EndsWith(FullViewModeTag))
                            layouts.Add(layout);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError(ex.ToString());
            }
        }

        private void AddDock(string id, string location)
        {
            var dit = DockFrame.AddItem(id);
            dit.DefaultLocation = location;
            dit.Behavior = DockItemBehavior.Locked;
            dit.DefaultVisible = false;
        }

        private void LoadDockStyles()
        {
            var barHeight = tabControl.BarHeight;

            var style = new DockVisualStyle
            {
                PadTitleLabelColor = Styles.PadLabelColor,
                InactivePadTitleLabelColor = Styles.InactivePadLabelColor,
                PadBackgroundColor = Styles.PadBackground,
                TreeBackgroundColor = Styles.BaseBackgroundColor,
                InactivePadBackgroundColor = Styles.InactivePadBackground,
                PadTitleHeight = barHeight
            };
            DockFrame.DefaultVisualStyle = style;

            style = new DockVisualStyle
            {
                PadTitleLabelColor = Styles.PadLabelColor,
                InactivePadTitleLabelColor = Styles.InactivePadLabelColor,
                PadTitleHeight = barHeight,
                UppercaseTitles = false,
                ExpandedTabs = true,
                PadBackgroundColor = Styles.BrowserPadBackground,
                InactivePadBackgroundColor = Styles.InactiveBrowserPadBackground,
                TreeBackgroundColor = Styles.BrowserPadBackground
            };
            // style.ShowPadTitleIcon = false; // VV: Now we want to have icons on all pads
            DockFrame.SetDockItemStyle("ProjectPad", style);
            DockFrame.SetDockItemStyle("ClassPad", style);

            DockFrame.UpdateStyles();
        }

        private void InitializeLayout(string name)
        {
            if (!layouts.Contains(name))
                layouts.Add(name);

            if (DockFrame.Layouts.Contains(name))
                return;

            DockFrame.CreateLayout(name, true);
            DockFrame.CurrentLayout = name;
            documentDockItem.Visible = true;

            // TODO-AELIJ: layout

            //LayoutExtensionNode stockLayout = null;
            //foreach (LayoutExtensionNode node in AddinManager.GetExtensionNodes(stockLayoutsPath))
            //{
            //    if (node.Name == name)
            //    {
            //        stockLayout = node;
            //        break;
            //    }
            //}

            //if (stockLayout == null)
            //    return;

            //HashSet<string> visible = new HashSet<string>();

            //foreach (LayoutPadExtensionNode pad in stockLayout.ChildNodes)
            //{
            //    DockItem it = dock.GetItem(pad.Id);
            //    if (it != null)
            //    {
            //        it.Visible = true;
            //        string loc = pad.Placement ?? it.DefaultLocation;
            //        if (!string.IsNullOrEmpty(loc))
            //            it.SetDockLocation(ToDockLocation(loc));
            //        DockItemStatus stat = pad.StatusSet ? pad.Status : it.DefaultStatus;
            //        it.Status = stat;
            //        visible.Add(pad.Id);
            //    }
            //}

            //foreach (PadCodon node in padContentCollection)
            //{
            //    if (!visible.Contains(node.Id) && node.DefaultLayouts != null && (node.DefaultLayouts.Contains(stockLayout.Id) || node.DefaultLayouts.Contains("*")))
            //    {
            //        DockItem it = dock.GetItem(node.Id);
            //        if (it != null)
            //        {
            //            it.Visible = true;
            //            if (!string.IsNullOrEmpty(node.DefaultPlacement))
            //                it.SetDockLocation(ToDockLocation(node.DefaultPlacement));
            //            it.Status = node.DefaultStatus;
            //            visible.Add(node.Id);
            //        }
            //    }
            //}

            //foreach (DockItem it in DockFrame.GetItems())
            //{
            //    if (!visible.Contains(it.Id) && ((it.Behavior & DockItemBehavior.Sticky) == 0) && it != documentDockItem)
            //        it.Visible = false;
            //}
        }

        internal void ShowPopup(DockNotebook notebook, int tabIndex, EventButton evt)
        {
            notebook.CurrentTabIndex = tabIndex;
            IdeApp.CommandService.ShowContextMenu(notebook, evt, "/MonoDevelop/Ide/ContextMenu/DocumentTab");
        }

        internal void OnTabsReordered(Widget widget, int oldPlacement, int newPlacement)
        {
            IdeApp.Workbench.ReorderDocuments(oldPlacement, newPlacement);
        }

        private bool IsInFullViewMode => DockFrame.CurrentLayout.EndsWith(FullViewModeTag);

        protected override void OnStyleSet(Style previousStyle)
        {
            base.OnStyleSet(previousStyle);
            IdeTheme.UpdateStyles();
        }

        protected override bool OnConfigureEvent(EventConfigure evnt)
        {
            SetActiveWidget(Focus);
            base.OnConfigureEvent(evnt);
            return false;
        }

        protected override bool OnFocusInEvent(EventFocus evnt)
        {
            SetActiveWidget(Focus);
            return base.OnFocusInEvent(evnt);
        }

        /// <summary>
        /// Sets the current active document widget.
        /// </summary>
        internal void SetActiveWidget(Widget child)
        {
            while (child != null)
            {
                var dragNotebook = child as SdiDragNotebook;
                if (dragNotebook != null)
                {
                    OnActiveWindowChanged(dragNotebook, EventArgs.Empty);
                    break;
                }
                child = child.Parent;
            }
        }

        //don't allow the "full view" layouts to persist - they are always derived from the "normal" layout
        //else they will diverge
        private void DestroyFullViewLayouts(string oldLayout)
        {
            if (oldLayout != null && oldLayout.EndsWith(FullViewModeTag))
            {
                DockFrame.DeleteLayout(oldLayout);
            }
        }

        public void ToggleFullViewMode()
        {
            if (IsInFullViewMode)
            {
                var oldLayout = DockFrame.CurrentLayout;
                DockFrame.CurrentLayout = CurrentLayout;
                DestroyFullViewLayouts(oldLayout);
            }
            else
            {
                var fullViewLayout = CurrentLayout + FullViewModeTag;
                if (!DockFrame.HasLayout(fullViewLayout))
                    DockFrame.CreateLayout(fullViewLayout, true);
                DockFrame.CurrentLayout = fullViewLayout;
                foreach (var it in DockFrame.GetItems())
                {
                    if (it.Behavior != DockItemBehavior.Locked && it.Visible)
                        it.Status = DockItemStatus.AutoHide;
                }
            }
        }

        protected override bool OnKeyPressEvent(EventKey evnt)
        {
            return FilterWindowKeypress(evnt) || base.OnKeyPressEvent(evnt);
        }

        internal bool FilterWindowKeypress(EventKey evnt)
        {
            // Handle Alt+1-0 keys
            var winSwitchModifier = Platform.IsMac ? KeyBindingManager.SelectionModifierControl : KeyBindingManager.SelectionModifierAlt;
            if ((evnt.State & winSwitchModifier) != 0 && (evnt.State & (ModifierType.ControlMask | ModifierType.Mod1Mask)) != (ModifierType.ControlMask | ModifierType.Mod1Mask))
            {
                switch (evnt.Key)
                {
                    case Key.KP_1:
                    case Key.Key_1:
                        SwitchToDocument(0);
                        return true;
                    case Key.KP_2:
                    case Key.Key_2:
                        SwitchToDocument(1);
                        return true;
                    case Key.KP_3:
                    case Key.Key_3:
                        SwitchToDocument(2);
                        return true;
                    case Key.KP_4:
                    case Key.Key_4:
                        SwitchToDocument(3);
                        return true;
                    case Key.KP_5:
                    case Key.Key_5:
                        SwitchToDocument(4);
                        return true;
                    case Key.KP_6:
                    case Key.Key_6:
                        SwitchToDocument(5);
                        return true;
                    case Key.KP_7:
                    case Key.Key_7:
                        SwitchToDocument(6);
                        return true;
                    case Key.KP_8:
                    case Key.Key_8:
                        SwitchToDocument(7);
                        return true;
                    case Key.KP_9:
                    case Key.Key_9:
                        SwitchToDocument(8);
                        return true;
                    case Key.KP_0:
                    case Key.Key_0:
                        SwitchToDocument(9);
                        return true;
                }
            }
            return false;
        }

        private void SwitchToDocument(int number)
        {
            if (DockNotebook.ActiveNotebook == null)
                return;

            if (number >= DockNotebook.ActiveNotebook.TabCount || number < 0)
                return;
            var window = DockNotebook.ActiveNotebook.Tabs[number].Content as IWorkbenchWindow;
            window?.SelectWindow();
        }

        #region View management

        private void CloseWindowEvent(object sender, WorkbenchWindowEventArgs e)
        {
            var f = (SdiWorkspaceWindow)sender;
            if (f.ViewContent != null)
                CloseContent(f.ViewContent);
        }

        internal void CloseClicked(object o, TabEventArgs e)
        {
            ((SdiWorkspaceWindow)e.Tab.Content).CloseWindow(false, true);
        }

        internal void RemoveTab(DockNotebook tabs, int pageNum, bool animate)
        {
            try
            {
                // Weird switch page events are fired when a tab is removed.
                // This flag avoids unneeded events.
                LockActiveWindowChangeEvent();
                // ReSharper disable once UnusedVariable
                var w = ActiveWorkbenchWindow;
                tabs.RemoveTab(pageNum, animate);
            }
            finally
            {
                UnlockActiveWindowChangeEvent();
            }
        }

        public void ReorderTab(int oldPlacement, int newPlacement)
        {
            var tab = tabControl.GetTab(oldPlacement);
            var targetTab = tabControl.GetTab(newPlacement);
            tabControl.ReorderTab(tab, targetTab);
        }

        #endregion

        #region Dock Item management

        public IPadWindow GetPadWindow(PadDefinition content)
        {
            IPadWindow w;
            padWindows.TryGetValue(content, out w);
            return w;
        }

        public bool IsVisible(PadDefinition padContent)
        {
            var item = GetDockItem(padContent);
            if (item != null)
                return item.Visible;
            return false;
        }

        public bool IsContentVisible(PadDefinition padContent)
        {
            var item = GetDockItem(padContent);
            if (item != null)
                return item.ContentVisible;
            return false;
        }

        public void HidePad(PadDefinition padContent)
        {
            var item = GetDockItem(padContent);
            if (item != null)
                item.Visible = false;
        }

        public void ActivatePad(PadDefinition padContent, bool giveFocus)
        {
            var item = GetDockItem(padContent);
            item?.Present(giveFocus);
        }

        public bool IsSticky(PadDefinition padContent)
        {
            var item = GetDockItem(padContent);
            return item != null && (item.Behavior & DockItemBehavior.Sticky) != 0;
        }

        public void SetSticky(PadDefinition padContent, bool sticky)
        {
            var item = GetDockItem(padContent);
            if (item != null)
            {
                if (sticky)
                    item.Behavior |= DockItemBehavior.Sticky;
                else
                    item.Behavior &= ~DockItemBehavior.Sticky;
            }
        }

        internal DockItem GetDockItem(PadDefinition content)
        {
            if (PadContentCollection.Contains(content))
            {
                var item = DockFrame.GetItem(content.PadId);
                return item;
            }
            return null;
        }

        private void CreatePadContent(bool force, PadDefinition padDefinition, PadWindow window, DockItem item)
        {
            if (force || item.Content == null)
            {
                var newContent = padDefinition.InitializePadContent(window);

                Widget crc = new PadCommandRouterContainer(newContent.Control, newContent, true);
                crc.Show();

                Widget router = new PadCommandRouterContainer(crc, toolbarFrame, false);
                router.Show();
                item.Content = router;
            }
        }

        private string ToDockLocation(string loc)
        {
            var location = "";
            foreach (var s in loc.Split(' '))
            {
                if (string.IsNullOrEmpty(s))
                    continue;
                if (location.Length > 0)
                    location += ";";
                if (s.IndexOf('/') == -1)
                    location += "__" + s.ToLower() + "/CenterBefore";
                else
                    location += s;
            }
            return location;
        }

        private void AddPad(PadDefinition padDefinition, string placement, DockItemStatus defaultStatus)
        {
            RegisterPad(padDefinition);

            var window = new PadWindow(this, padDefinition) { Icon = padDefinition.Icon };
            padWindows[padDefinition] = window;
            padCodons[window] = padDefinition;

            window.StatusChanged += UpdatePad;

            var location = ToDockLocation(placement);

            var item = DockFrame.AddItem(padDefinition.PadId);
            item.Label = padDefinition.Label;
            item.Icon = ImageService.GetIcon(padDefinition.Icon).WithSize(IconSize.Menu);
            item.DefaultLocation = location;
            item.DefaultVisible = false;
            item.DefaultStatus = defaultStatus;
            window.Item = item;

            if (padDefinition.Initialized)
            {
                CreatePadContent(true, padDefinition, window, item);
            }
            else
            {
                item.ContentRequired += delegate
                {
                    CreatePadContent(false, padDefinition, window, item);
                };
            }

            item.VisibleChanged += (s, a) =>
            {
                if (item.Visible)
                    window.NotifyShown(a);
                else
                    window.NotifyHidden(a);
            };

            item.ContentVisibleChanged += delegate
            {
                if (item.ContentVisible)
                    window.NotifyContentShown();
                else
                    window.NotifyContentHidden();
            };
        }

        private void UpdatePad(object source, EventArgs args)
        {
            var window = (IPadWindow)source;
            if (!padCodons.ContainsKey(window))
                return;
            var codon = padCodons[window];
            var item = GetDockItem(codon);
            if (item != null)
            {
                var windowTitle = window.Title;
                var windowIcon = ImageService.GetIcon(window.Icon).WithSize(IconSize.Menu);
                if (String.IsNullOrEmpty(windowTitle))
                    windowTitle = codon.Label;
                if (window.HasErrors && !window.ContentVisible)
                {
                    windowTitle = "<span foreground='" + Styles.ErrorForegroundColor.ToHexString(false) + "'>" + windowTitle + "</span>";
                    windowIcon = windowIcon.WithStyles("error");
                }
                else if (window.HasNewData && !window.ContentVisible)
                    windowTitle = "<b>" + windowTitle + "</b>";
                item.Label = windowTitle;
                item.Icon = windowIcon;
            }
        }

        #endregion

        #region ICommandRouter implementation

        object ICommandRouter.GetNextCommandTarget()
        {
            return toolbar;
        }

        #endregion
    }

    internal class PadActivationHandler : CommandHandler
    {
        private readonly PadDefinition pad;
        private readonly DefaultWorkbenchWindow wb;

        public PadActivationHandler(DefaultWorkbenchWindow wb, PadDefinition pad)
        {
            this.pad = pad;
            this.wb = wb;
        }

        protected override void Run()
        {
            wb.BringToFront(pad, true);
        }
    }

    internal class PadCommandRouterContainer : CommandRouterContainer
    {
        public PadCommandRouterContainer(Widget child, object target, bool continueToParent) : base(child, target, continueToParent)
        {
        }
    }

    // The SdiDragNotebook class allows redirecting the command route to the ViewCommandHandler
    // object of the selected document, which implement some default commands.

    internal class SdiDragNotebook : DockNotebook, ICommandDelegatorRouter, ICommandBar
    {
        public SdiDragNotebook(DefaultWorkbenchWindow window)
        {
            SwitchPage += window.OnActiveWindowChanged;
            PageRemoved += window.OnActiveWindowChanged;
            TabClosed += window.CloseClicked;
            TabActivated += delegate
            {
                window.ToggleFullViewMode();
            };
            CanFocus = true;

            DoPopupMenu = window.ShowPopup;
            Events |= EventMask.FocusChangeMask | EventMask.KeyPressMask;
            IdeApp.CommandService.RegisterCommandBar(this);
        }

        protected override void OnDestroyed()
        {
            IdeApp.CommandService.UnregisterCommandBar(this);
            base.OnDestroyed();
        }

        public object GetNextCommandTarget()
        {
            return Parent;
        }

        public object GetDelegatedCommandTarget()
        {
            return ((SdiWorkspaceWindow)CurrentTab?.Content)?.CommandHandler;
        }

        #region ICommandBar implementation

        void ICommandBar.Update(object activeTarget)
        {
        }

        void ICommandBar.SetEnabled(bool enabled)
        {
        }

        #endregion
    }
}