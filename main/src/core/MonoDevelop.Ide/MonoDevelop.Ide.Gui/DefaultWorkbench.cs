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
using MonoDevelop.Ide.Gui.Dialogs;
using Image = Xwt.Drawing.Image;
using Key = Gdk.Key;
using Rectangle = System.Drawing.Rectangle;

namespace MonoDevelop.Ide.Gui
{
    /// <summary>
    /// This is the a Workspace with a multiple document interface.
    /// </summary>
    internal class DefaultWorkbench : WorkbenchWindow, ICommandRouter
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
        private readonly Dictionary<PadCodon, IPadWindow> padWindows = new Dictionary<PadCodon, IPadWindow>();
        private readonly Dictionary<IPadWindow, PadCodon> padCodons = new Dictionary<IPadWindow, PadCodon>();

        private IWorkbenchWindow lastActive;

        private bool closeAll;

        private readonly Rectangle normalBounds = new Rectangle(0, 0, MinimumWidth, MinimumHeight);

        internal Container rootWidget;
        private CommandFrame toolbarFrame;
        private SdiDragNotebook tabControl;
        internal VBox fullViewVBox;
        private DockItem documentDockItem;

#if DUMMY_STRINGS_FOR_TRANSLATION_DO_NOT_COMPILE
		private void DoNotCompile ()
		{
			//The default layout, translated indirectly because it's used as an ID
			GettextCatalog.GetString ("Default");
		}
#endif

        public event EventHandler ActiveWorkbenchWindowChanged;
        public event EventHandler WorkbenchTabsChanged;

        public StatusBar StatusBar => Toolbar.StatusBar;

        public MainToolbarController Toolbar { get; private set; }

        public MenuBar TopMenu { get; private set; }

        public MonoDevelopStatusBar BottomBar { get; private set; }

        internal SdiDragNotebook TabControl
        {
            get
            {
                return tabControl;
            }
            set
            {
                tabControl = value;
                tabControl.NavigationButtonsVisible = true;
            }
        }

        internal IWorkbenchWindow ActiveWorkbenchWindow
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

        public DockFrame DockFrame { get; internal set; }

        public bool FullScreen
        {
            get
            {
                return DesktopService.GetIsFullscreen(this);
            }
            set
            {
                DesktopService.SetIsFullscreen(this, value);
            }
        }

        public IList<string> Layouts => layouts;

        public string CurrentLayout
        {
            get
            {
                if (DockFrame?.CurrentLayout != null)
                {
                    string s = DockFrame.CurrentLayout;
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
            string layout = name;
            layouts.Remove(name);
            DockFrame.DeleteLayout(layout);
        }

        public List<PadCodon> PadContentCollection { get; } = new List<PadCodon>();

        internal List<ViewContent> InternalViewContentCollection
        {
            get
            {
                Debug.Assert(viewContentCollection != null);
                return viewContentCollection;
            }
        }

        public DefaultWorkbench()
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
                List<ViewContent> fullList = new List<ViewContent>(viewContentCollection);
                foreach (ViewContent content in fullList)
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

        public virtual void ShowView(ViewContent content, bool bringToFront, IViewDisplayBinding binding = null, DockNotebook notebook = null)
        {
            bool isFile = content.IsFile;
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

            SdiWorkspaceWindow sdiWorkspaceWindow = new SdiWorkspaceWindow(this, content, addToControl, tab);
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

        public void ShowPad(PadCodon content)
        {
            AddPad(content, true);
        }

        public void AddPad(PadCodon content)
        {
            AddPad(content, false);
        }

        private void RegisterPad(PadCodon content)
        {
            string lab = content.Label.Length > 0 ? (content.Label) : "";
            ActionCommand cmd = new ActionCommand("Pad|" + content.PadId, lab, null)
            {
                DefaultHandler = new PadActivationHandler(this, content),
                Category = ("View (Pads)")
            };
            cmd.Description = $"Show {cmd.Text}";
            IdeApp.CommandService.RegisterCommand(cmd);
            PadContentCollection.Add(content);
        }

        private void AddPad(PadCodon content, bool show)
        {
            DockItem item = GetDockItem(content);
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

        public void RemovePad(PadCodon codon)
        {
            Command cmd = IdeApp.CommandService.GetCommand(codon.Id);
            if (cmd != null)
                IdeApp.CommandService.UnregisterCommand(cmd);
            DockItem item = GetDockItem(codon);
            PadContentCollection.Remove(codon);
            PadWindow win = (PadWindow)GetPadWindow(codon);
            if (win != null)
            {
                win.NotifyDestroyed();
                Counters.PadsLoaded--;
                padCodons.Remove(win);
            }
            if (item != null)
                DockFrame.RemoveItem(item);
            padWindows.Remove(codon);
        }

        public void BringToFront(PadCodon content)
        {
            BringToFront(content, false);
        }

        public virtual void BringToFront(PadCodon content, bool giveFocus)
        {
            if (!IsVisible(content))
                ShowPad(content);

            ActivatePad(content, giveFocus);
        }

        internal static string GetTitle(IWorkbenchWindow window)
        {
            if (window.ViewContent.IsUntitled)
                return GetDefaultTitle();
            string post = String.Empty;
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
                IWorkbenchWindow window = ActiveWorkbenchWindow;
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
                string fileName = content.ContentName.Substring(3).Replace('/', '.').Replace('\\', '.').Replace(System.IO.Path.DirectorySeparatorChar, '.');
                string fullFileName = directory + System.IO.Path.DirectorySeparatorChar + fileName;
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
            foreach (FileCopyEventInfo e in args)
            {
                if (e.IsDirectory)
                {
                    foreach (ViewContent content in viewContentCollection)
                    {
                        if (content.ContentName != null && ((FilePath)content.ContentName).IsChildPathOf(e.SourceFile))
                        {
                            content.ContentName = e.TargetFile.Combine(((FilePath)content.ContentName).FileName);
                        }
                    }
                }
                else
                {
                    foreach (ViewContent content in viewContentCollection)
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

        protected /*override*/ void OnClosing(object o, DeleteEventArgs e)
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

            foreach (PadCodon content in PadContentCollection)
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

            bool showDirtyDialog = false;

            foreach (ViewContent content in viewContentCollection)
            {
                if (content.IsDirty)
                {
                    showDirtyDialog = true;
                    break;
                }
            }

            if (showDirtyDialog)
            {
                using (DirtyFilesDialog dlg = new DirtyFilesDialog())
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

        public PadCodon GetPad(Type type)
        {
            foreach (PadCodon pad in PadContentCollection)
            {
                if (pad.ClassName == type.FullName)
                {
                    return pad;
                }
            }
            return null;
        }

        public PadCodon GetPad(string id)
        {
            foreach (PadCodon pad in PadContentCollection)
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
            layouts.Add ("Solution");

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
            Toolbar = DesktopService.CreateMainToolbar(this);
            DesktopService.SetMainWindowDecorations(this);
            DesktopService.AttachMainToolbar(fullViewVBox, Toolbar);
            toolbarFrame = new CommandFrame(IdeApp.CommandService);

            fullViewVBox.PackStart(toolbarFrame, true, true, 0);

            // Create the docking widget and add it to the window.
            DockFrame = new DockFrame();
            DockFrame.LayoutChanged += LayoutChanged;

            DockFrame.CompactGuiLevel = ((int)IdeApp.Preferences.WorkbenchCompactness.Value) + 1;
            IdeApp.Preferences.WorkbenchCompactness.Changed += delegate
            {
                DockFrame.CompactGuiLevel = ((int)IdeApp.Preferences.WorkbenchCompactness.Value) + 1;
            };

            /* Side bar is experimental. Disabled for now
			HBox hbox = new HBox ();
			VBox sideBox = new VBox ();
			sideBox.PackStart (new SideBar (workbench, Orientation.Vertical), false, false, 0);
			hbox.PackStart (sideBox, false, false, 0);
			hbox.ShowAll ();
			sideBox.NoShowAll = true;
			hbox.PackStart (dock, true, true, 0);
			DockBar bar = dock.ExtractDockBar (PositionType.Left);
			bar.AlwaysVisible = true;
			sideBox.PackStart (bar, true, true, 0);
			toolbarFrame.AddContent (hbox);
			*/

            toolbarFrame.Add(DockFrame);

            // Create the notebook for the various documents.
            tabControl = new SdiDragNotebook(this);

            DockNotebook.ActiveNotebookChanged += delegate
            {
                OnActiveWindowChanged(null, null);
            };

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
            documentDockItem.Label = ("Documents");
            documentDockItem.Content = new DockNotebookContainer(tabControl, true);

            LoadDockStyles();
            Styles.Changed += (sender, e) => LoadDockStyles();

            // Add some hiden items to be used as position reference
            DockItem dit = DockFrame.AddItem("__left");
            dit.DefaultLocation = "Documents/Left";
            dit.Behavior = DockItemBehavior.Locked;
            dit.DefaultVisible = false;

            dit = DockFrame.AddItem("__right");
            dit.DefaultLocation = "Documents/Right";
            dit.Behavior = DockItemBehavior.Locked;
            dit.DefaultVisible = false;

            dit = DockFrame.AddItem("__top");
            dit.DefaultLocation = "Documents/Top";
            dit.Behavior = DockItemBehavior.Locked;
            dit.DefaultVisible = false;

            dit = DockFrame.AddItem("__bottom");
            dit.DefaultLocation = "Documents/Bottom";
            dit.Behavior = DockItemBehavior.Locked;
            dit.DefaultVisible = false;

            if (Platform.IsMac)
                BottomBar.HasResizeGrip = true;
            else
            {
                if (GdkWindow != null && GdkWindow.State == WindowState.Maximized)
                    BottomBar.HasResizeGrip = false;
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
                    foreach (string layout in DockFrame.Layouts)
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

        private void LoadDockStyles()
        {
            var barHeight = tabControl.BarHeight;

            DockVisualStyle style = new DockVisualStyle
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
                string fullViewLayout = CurrentLayout + FullViewModeTag;
                if (!DockFrame.HasLayout(fullViewLayout))
                    DockFrame.CreateLayout(fullViewLayout, true);
                DockFrame.CurrentLayout = fullViewLayout;
                foreach (DockItem it in DockFrame.GetItems())
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
            ModifierType winSwitchModifier = Platform.IsMac ? KeyBindingManager.SelectionModifierControl : KeyBindingManager.SelectionModifierAlt;
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
            SdiWorkspaceWindow f = (SdiWorkspaceWindow)sender;
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

        internal void ReorderTab(int oldPlacement, int newPlacement)
        {
            DockNotebookTab tab = tabControl.GetTab(oldPlacement);
            DockNotebookTab targetTab = tabControl.GetTab(newPlacement);
            tabControl.ReorderTab(tab, targetTab);
        }

        #endregion

        #region Dock Item management

        public IPadWindow GetPadWindow(PadCodon content)
        {
            IPadWindow w;
            padWindows.TryGetValue(content, out w);
            return w;
        }

        public bool IsVisible(PadCodon padContent)
        {
            DockItem item = GetDockItem(padContent);
            if (item != null)
                return item.Visible;
            return false;
        }

        public bool IsContentVisible(PadCodon padContent)
        {
            DockItem item = GetDockItem(padContent);
            if (item != null)
                return item.ContentVisible;
            return false;
        }

        public void HidePad(PadCodon padContent)
        {
            DockItem item = GetDockItem(padContent);
            if (item != null)
                item.Visible = false;
        }

        public void ActivatePad(PadCodon padContent, bool giveFocus)
        {
            DockItem item = GetDockItem(padContent);
            item?.Present(giveFocus);
        }

        public bool IsSticky(PadCodon padContent)
        {
            DockItem item = GetDockItem(padContent);
            return item != null && (item.Behavior & DockItemBehavior.Sticky) != 0;
        }

        public void SetSticky(PadCodon padContent, bool sticky)
        {
            DockItem item = GetDockItem(padContent);
            if (item != null)
            {
                if (sticky)
                    item.Behavior |= DockItemBehavior.Sticky;
                else
                    item.Behavior &= ~DockItemBehavior.Sticky;
            }
        }

        internal DockItem GetDockItem(PadCodon content)
        {
            if (PadContentCollection.Contains(content))
            {
                DockItem item = DockFrame.GetItem(content.PadId);
                return item;
            }
            return null;
        }

        private void CreatePadContent(bool force, PadCodon padCodon, PadWindow window, DockItem item)
        {
            if (force || item.Content == null)
            {
                PadContent newContent = padCodon.InitializePadContent(window);

                Widget crc = new PadCommandRouterContainer(newContent.Control, newContent, true);
                crc.Show();

                Widget router = new PadCommandRouterContainer(crc, toolbarFrame, false);
                router.Show();
                item.Content = router;
            }
        }

        private string ToDockLocation(string loc)
        {
            string location = "";
            foreach (string s in loc.Split(' '))
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

        private void AddPad(PadCodon padCodon, string placement, DockItemStatus defaultStatus)
        {
            RegisterPad(padCodon);

            PadWindow window = new PadWindow(this, padCodon) { Icon = padCodon.Icon };
            padWindows[padCodon] = window;
            padCodons[window] = padCodon;

            window.StatusChanged += UpdatePad;

            string location = ToDockLocation(placement);

            DockItem item = DockFrame.AddItem(padCodon.PadId);
            item.Label = (padCodon.Label);
            item.Icon = ImageService.GetIcon(padCodon.Icon).WithSize(IconSize.Menu);
            item.DefaultLocation = location;
            item.DefaultVisible = false;
            item.DefaultStatus = defaultStatus;
            window.Item = item;

            if (padCodon.Initialized)
            {
                CreatePadContent(true, padCodon, window, item);
            }
            else
            {
                item.ContentRequired += delegate
                {
                    CreatePadContent(false, padCodon, window, item);
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
            IPadWindow window = (IPadWindow)source;
            if (!padCodons.ContainsKey(window))
                return;
            PadCodon codon = padCodons[window];
            DockItem item = GetDockItem(codon);
            if (item != null)
            {
                string windowTitle = (window.Title);
                var windowIcon = ImageService.GetIcon(window.Icon).WithSize(IconSize.Menu);
                if (String.IsNullOrEmpty(windowTitle))
                    windowTitle = (codon.Label);
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
            return Toolbar;
        }

        #endregion
    }

    internal class PadActivationHandler : CommandHandler
    {
        private readonly PadCodon pad;
        private readonly DefaultWorkbench wb;

        public PadActivationHandler(DefaultWorkbench wb, PadCodon pad)
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
        public SdiDragNotebook(DefaultWorkbench window)
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