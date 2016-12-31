//
// Workbench.cs
//
// Author:
//   Lluis Sanchez Gual
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
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


using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gtk;
using MonoDevelop.Components.Docking;
using MonoDevelop.Components.DockNotebook;
using MonoDevelop.Core;
using MonoDevelop.Core.Annotations;
using MonoDevelop.Core.Instrumentation;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Projects;
using MonoDevelop.Projects.Text;

namespace MonoDevelop.Core
{
    public static class GettextCatalog
    {
        public static string GetString(string s) => s;
        [StringFormatMethod("s")]
        public static string GetString(string s, params object[] args) => string.Format(s, args);
    }
}

namespace MonoDevelop.Ide.Gui
{
    /// <summary>
    /// This is the basic interface to the workspace.
    /// </summary>
    public sealed class Workbench
    {
        private PadCollection pads;
        private IDefaultWorkbenchWindow window;

        public event EventHandler ActiveDocumentChanged;
        public event EventHandler LayoutChanged;
        public event EventHandler GuiLocked;
        public event EventHandler GuiUnlocked;

        internal void Initialize(ProgressMonitor monitor)
        {
            monitor.BeginTask(GettextCatalog.GetString("Initializing Main Window"), 4);
            try
            {
                Counters.Initialization.Trace("Creating DefaultWorkbench");
                window = new DefaultWorkbenchWindow();
                monitor.Step();

                Counters.Initialization.Trace("Initializing Workspace");
                window.InitializeWorkspace();
                monitor.Step();

                Counters.Initialization.Trace("Initializing Layout");
                window.InitializeLayout();
                monitor.Step();

                window.Visible = false;
                window.ActiveWorkbenchWindowChanged += OnDocumentChanged;

                IdeApp.FocusOut += delegate
                {
                    SaveFileStatus();
                };
                IdeApp.FocusIn += delegate
                {
                    CheckFileStatus();
                };

                pads = null;    // Make sure we get an up to date pad list.
                monitor.Step();
            }
            finally
            {
                monitor.EndTask();
            }
        }

        internal void Show()
        {
            Counters.Initialization.Trace("Realizing Root Window");
            RootWindow.Realize();
            Counters.Initialization.Trace("Making Visible");
            RootWindow.Visible = true;
            window.CurrentLayout = "Solution";

            // now we have an layout set notify it
            Counters.Initialization.Trace("Setting layout");
            LayoutChanged?.Invoke(this, EventArgs.Empty);

            Present();
        }

        internal bool Close()
        {
            return window.Close();
        }

        public ImmutableList<Document> Documents { get; private set; } = ImmutableList<Document>.Empty;

        /// <summary>
        /// This is a wrapper for use with AutoTest
        /// </summary>
        internal bool DocumentsDirty
        {
            get { return Documents.Any(d => d.IsDirty); }
        }

        public Document ActiveDocument
        {
            get
            {
                if (window.ActiveWorkbenchWindow == null)
                    return null;
                return WrapDocument(window.ActiveWorkbenchWindow);
            }
        }

        public Document GetDocument(string name)
        {
            var fullPath = (FilePath)FileService.GetFullPath(name);

            foreach (Document doc in Documents)
            {
                var fullDocPath = (FilePath)FileService.GetFullPath(doc.Name);

                if (fullDocPath == fullPath)
                    return doc;
            }
            return null;
        }

        internal TextReader[] GetDocumentReaders(List<string> filenames)
        {
            TextReader[] results = new TextReader[filenames.Count];

            int idx = 0;
            foreach (var f in filenames)
            {
                var fullPath = (FilePath)FileService.GetFullPath(f);

                Document doc = Documents.Find(d => d.Editor != null && (fullPath == FileService.GetFullPath(d.Name)));
                if (doc != null)
                {
                    results[idx] = doc.Editor.CreateReader();
                }
                else
                {
                    results[idx] = null;
                }

                idx++;
            }

            return results;
        }

        public PadCollection Pads
        {
            get
            {
                if (pads == null)
                {
                    pads = new PadCollection();
                    foreach (PadDefinition pc in window.PadContentCollection)
                        WrapPad(pc);
                }
                return pads;
            }
        }

        public Window RootWindow => (Window)window;

        /// <summary>
        /// When set to <c>true</c>, opened documents will automatically be reloaded when a change in the underlying
        /// file is detected (unless the document has unsaved changes)
        /// </summary>
        public bool AutoReloadDocuments { get; set; }

        /// <summary>
        /// Whether the root window or any undocked part of it has toplevel focus. 
        /// </summary>
        public bool HasToplevelFocus
        {
            get
            {
                if (DesktopService.IsModalDialogRunning())
                    return false;
                var windows = Window.ListToplevels();
                var toplevel = windows.FirstOrDefault(x => x.HasToplevelFocus);
                if (toplevel == null)
                    return false;
                if (toplevel == RootWindow)
                    return true;
#if WIN32
				var app = System.Windows.Application.Current;
				if (app != null) {
					var wpfWindow = app.Windows.OfType<System.Windows.Window>().SingleOrDefault (x => x.IsActive);
					if (wpfWindow != null)
						return true;
				}
#endif
                var dock = toplevel as DockFloatingWindow;
                return dock != null && dock.DockParent == RootWindow;
            }
        }

        public void Present()
        {
            //HACK: window resets its size on Win32 on Present if it was maximized by snapping to top edge of screen
            //partially work around this by avoiding the present call if it's already toplevel
            if (Platform.IsWindows && RootWindow.HasToplevelFocus)
                return;

            //FIXME: this should do a "request for attention" dock bounce on MacOS but only in some cases.
            //Doing it for all Present calls is excessive and annoying. Maybe we have too many Present calls...
            //Mono.TextEditor.GtkWorkarounds.PresentWindowWithNotification (RootWindow);
            RootWindow.Present();
        }

        public void GrabDesktopFocus()
        {
            DesktopService.GrabDesktopFocus(RootWindow);
        }

        public bool FullScreen
        {
            get { return window.FullScreen; }
            set { window.FullScreen = value; }
        }

        public string CurrentLayout
        {
            get { return window.CurrentLayout; }
            set
            {
                if (value != window.CurrentLayout)
                {
                    window.CurrentLayout = value;
                    LayoutChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public IList<string> Layouts => window.Layouts;

        public IStatusBar StatusBar => window.StatusBar;

        public void ShowCommandBar(string barId)
        {
            window.ShowCommandBar(barId);
        }

        public void HideCommandBar(string barId)
        {
            window.HideCommandBar(barId);
        }

        //internal MainToolbarController Toolbar => window.Toolbar;

        public Pad GetPad<T>()
        {
            foreach (Pad pad in Pads)
            {
                if (typeof(T).FullName == pad.InternalContent.ClassName)
                    return pad;
            }
            return null;
        }

        public void DeleteLayout(string name)
        {
            window.DeleteLayout(name);
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LockGui()
        {
            if (IdeApp.CommandService.LockAll())
            {
                GuiLocked?.Invoke(this, EventArgs.Empty);
            }
        }

        public void UnlockGui()
        {
            if (IdeApp.CommandService.UnlockAll())
            {
                GuiUnlocked?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SaveAll()
        {
            ITimeTracker tt = Counters.SaveAllTimer.BeginTiming();
            try
            {
                // Make a copy of the list, since it may change during save
                Document[] docs = new Document[Documents.Count];
                Documents.CopyTo(docs, 0);

                foreach (Document doc in docs)
                    doc.Save();
            }
            finally
            {
                tt.End();
            }
        }

        internal bool SaveAllDirtyFiles()
        {
            Document[] docs = Documents.Where(doc => doc.IsDirty && doc.Window.ViewContent != null).ToArray();
            if (!docs.Any())
                return true;

            foreach (Document doc in docs)
            {
                AlertButton result = PromptToSaveChanges(doc);
                if (result == AlertButton.Cancel)
                    return false;

                doc.Save();
                if (doc.IsDirty)
                {
                    doc.Select();
                    return false;
                }
            }
            return true;
        }

        private static AlertButton PromptToSaveChanges(Document doc)
        {
            return MessageService.GenericAlert(Stock.Warning,
                GettextCatalog.GetString("Save the changes to document '{0}' before creating a new solution?",
                (object)(doc.Window.ViewContent.IsUntitled
                    ? doc.Window.ViewContent.UntitledName
                    : Path.GetFileName(doc.Window.ViewContent.ContentName))),
                "",
                  AlertButton.Cancel, doc.Window.ViewContent.IsUntitled ? AlertButton.SaveAs : AlertButton.Save);
        }

        public void CloseAllDocuments(bool leaveActiveDocumentOpen)
        {
            Document[] docs = new Document[Documents.Count];
            Documents.CopyTo(docs, 0);

            // The active document is the last one to close.
            // It avoids firing too many ActiveDocumentChanged events.

            foreach (Document doc in docs)
            {
                if (doc != ActiveDocument)
                    doc.Close();
            }
            if (!leaveActiveDocumentOpen)
                ActiveDocument?.Close();
        }

        internal Pad ShowPad(PadDefinition content)
        {
            window.ShowPad(content);
            return WrapPad(content);
        }

        internal Pad AddPad(PadDefinition content)
        {
            window.AddPad(content);
            return WrapPad(content);
        }

        public Pad AddPad(PadContent padContent, string id, string label, string defaultPlacement, IconId icon)
        {
            return AddPad(new PadDefinition(padContent, id, label, defaultPlacement, DockItemStatus.Dockable, icon));
        }

        public Pad AddPad(PadContent padContent, string id, string label, string defaultPlacement, DockItemStatus defaultStatus, IconId icon)
        {
            return AddPad(new PadDefinition(padContent, id, label, defaultPlacement, defaultStatus, icon));
        }

        public Pad ShowPad(PadContent padContent, string id, string label, string defaultPlacement, IconId icon)
        {
            return ShowPad(new PadDefinition(padContent, id, label, defaultPlacement, DockItemStatus.Dockable, icon));
        }

        public Pad ShowPad(PadContent padContent, string id, string label, string defaultPlacement, DockItemStatus defaultStatus, IconId icon)
        {
            return ShowPad(new PadDefinition(padContent, id, label, defaultPlacement, defaultStatus, icon));
        }

        public Task<Document> OpenDocument(FilePath fileName, Project project, bool bringToFront)
        {
            return OpenDocument(fileName, project, bringToFront ? OpenDocumentOptions.Default : OpenDocumentOptions.Default & ~OpenDocumentOptions.BringToFront);
        }

        public Task<Document> OpenDocument(FilePath fileName, Project project, OpenDocumentOptions options = OpenDocumentOptions.Default)
        {
            return OpenDocument(fileName, project, -1, -1, options, null, null);
        }

        public Task<Document> OpenDocument(FilePath fileName, Project project, Encoding encoding, OpenDocumentOptions options = OpenDocumentOptions.Default)
        {
            return OpenDocument(fileName, project, -1, -1, options, encoding, null);
        }

        public Task<Document> OpenDocument(FilePath fileName, Project project, int line, int column, OpenDocumentOptions options = OpenDocumentOptions.Default)
        {
            return OpenDocument(fileName, project, line, column, options, null, null);
        }

        public Task<Document> OpenDocument(FilePath fileName, Project project, int line, int column, Encoding encoding, OpenDocumentOptions options = OpenDocumentOptions.Default)
        {
            return OpenDocument(fileName, project, line, column, options, encoding, null);
        }

        internal Task<Document> OpenDocument(FilePath fileName, Project project, int line, int column, OpenDocumentOptions options, Encoding encoding, IViewDisplayBinding binding)
        {
            var openFileInfo = new FileOpenInformation(fileName, project)
            {
                Options = options,
                Line = line,
                Column = column,
                DisplayBinding = binding,
                Encoding = encoding
            };
            return OpenDocument(openFileInfo);
        }

        private static void ScrollToRequestedCaretLocation(Document doc, FileOpenInformation info)
        {
            var ipos = doc.Editor;
            if ((info.Line >= 1 || info.Offset >= 0) && ipos != null)
            {
                doc.DisableAutoScroll();
                doc.RunWhenLoaded(() =>
                {
                    var loc = new DocumentLocation(info.Line, info.Column >= 1 ? info.Column : 1);
                    if (info.Offset >= 0)
                    {
                        loc = ipos.OffsetToLocation(info.Offset);
                    }
                    if (loc.IsEmpty)
                        return;
                    ipos.SetCaretLocation(loc, info.Options.HasFlag(OpenDocumentOptions.HighlightCaretLine), info.Options.HasFlag(OpenDocumentOptions.CenterCaretLine));
                });
            }
        }

        internal Task<Document> OpenDocument(FilePath fileName, Project project, int line, int column, OpenDocumentOptions options, Encoding encoding, IViewDisplayBinding binding, DockNotebook dockNotebook)
        {
            var openFileInfo = new FileOpenInformation(fileName, project)
            {
                Options = options,
                Line = line,
                Column = column,
                DisplayBinding = binding,
                Encoding = encoding,
                DockNotebook = dockNotebook
            };

            return OpenDocument(openFileInfo);
        }

        public async Task<Document> OpenDocument(FileOpenInformation info)
        {
            if (string.IsNullOrEmpty(info.FileName))
                return null;
            // Ensure that paths like /a/./a.cs are equalized 
            using (Counters.OpenDocumentTimer.BeginTiming("Opening file " + info.FileName))
            {
                Counters.OpenDocumentTimer.Trace("Look for open document");
                foreach (Document doc in Documents)
                {
                    BaseViewContent vcFound = null;

                    //search all ViewContents to see if they can "re-use" this filename
                    if (doc.Window.ViewContent.CanReuseView(info.FileName))
                        vcFound = doc.Window.ViewContent;

                    //old method as fallback
                    if ((vcFound == null) && (doc.FileName.CanonicalPath == info.FileName)) // info.FileName is already Canonical
                        vcFound = doc.Window.ViewContent;
                    //if found, try to reuse or close the old view
                    if (vcFound != null)
                    {
                        // reuse the view if the binidng didn't change
                        if (info.Options.HasFlag(OpenDocumentOptions.TryToReuseViewer) || vcFound.Binding == info.DisplayBinding)
                        {
                            if (info.Project != null && doc.Project != info.Project)
                            {
                                doc.SetProject(info.Project);
                            }

                            ScrollToRequestedCaretLocation(doc, info);

                            if (info.Options.HasFlag(OpenDocumentOptions.BringToFront))
                            {
                                doc.Select();
                                doc.Window.SelectWindow();
                            }
                            return doc;
                        }
                        if (!doc.Close())
                            return doc;
                        break;
                    }
                }

                await RealOpenFile(new ProgressMonitor(), info);

                if (info.NewContent != null)
                {
                    Counters.OpenDocumentTimer.Trace("Wrapping document");
                    Document doc = WrapDocument(info.NewContent.WorkbenchWindow);

                    ScrollToRequestedCaretLocation(doc, info);

                    if (doc != null && info.Options.HasFlag(OpenDocumentOptions.BringToFront))
                    {
                        doc.RunWhenLoaded(() =>
                        {
                            doc.Window?.SelectWindow();
                        });
                    }
                    return doc;
                }
                return null;
            }
        }

        public Document OpenDocument(ViewContent content, bool bringToFront)
        {
            window.ShowView(content, bringToFront);
            if (bringToFront)
                Present();
            return WrapDocument(content.WorkbenchWindow);
        }

        public void ToggleMaximize()
        {
            window.ToggleFullViewMode();
        }

        public Document NewDocument(string defaultName, string mimeType, string content)
        {
            MemoryStream ms = new MemoryStream();
            byte[] data = Encoding.UTF8.GetBytes(content);
            ms.Write(data, 0, data.Length);
            ms.Position = 0;
            return NewDocument(defaultName, mimeType, ms);
        }

        public Document NewDocument(string defaultName, string mimeType, Stream content)
        {
            IViewDisplayBinding binding = DisplayBindingService.GetDefaultViewBinding(null, mimeType, null);
            if (binding == null)
                throw new ApplicationException("Can't create display binding for mime type: " + mimeType);

            ViewContent newContent = binding.CreateContent(defaultName, mimeType, null);
            using (content)
            {
                newContent.LoadNew(content, mimeType);
            }

            if (newContent == null)
                throw new ApplicationException(String.Format("Created view content was null{3}DefaultName:{0}{3}MimeType:{1}{3}Content:{2}",
                    defaultName, mimeType, content, Environment.NewLine));

            newContent.UntitledName = defaultName;
            newContent.IsDirty = true;
            newContent.Binding = binding;
            window.ShowView(newContent, true, binding);

            var document = WrapDocument(newContent.WorkbenchWindow);
            return document;
        }

        internal void ShowNext()
        {
            // Shows the next item in a pad that implements ILocationListPad.
        }

        internal void ShowPrevious()
        {
            // Shows the previous item in a pad that implements ILocationListPad.
        }

        private void OnDocumentChanged(object s, EventArgs a)
        {
            ActiveDocumentChanged?.Invoke(s, a);
            if (ActiveDocument != null)
                ActiveDocument.LastTimeActive = DateTime.Now;
        }

        internal Document WrapDocument(IWorkbenchWindow workbenchWindow)
        {
            if (workbenchWindow == null) return null;
            Document doc = FindDocument(workbenchWindow);
            if (doc != null) return doc;
            doc = new Document(workbenchWindow);
            workbenchWindow.Closing += OnWindowClosing;
            workbenchWindow.Closed += OnWindowClosed;
            Documents = Documents.Add(doc);

            doc.OnDocumentAttached();
            OnDocumentOpened(new DocumentEventArgs(doc));

            return doc;
        }

        private Pad WrapPad(PadDefinition padContent)
        {
            if (pads == null)
            {
                foreach (Pad p in Pads)
                {
                    if (p.InternalContent == padContent)
                        return p;
                }
            }
            Pad pad = new Pad(window, padContent);
            Pads.Add(pad);
            pad.Window.PadDestroyed += delegate
            {
                Pads.Remove(pad);
            };
            return pad;
        }

        private async void OnWindowClosing(object sender, WorkbenchWindowEventArgs args)
        {
            var workbenchWindow = (IWorkbenchWindow)sender;
            var viewContent = workbenchWindow.ViewContent;
            if (!args.Forced && viewContent != null && viewContent.IsDirty)
            {
                AlertButton result = MessageService.GenericAlert(Stock.Warning,
                    GettextCatalog.GetString("Save the changes to document '{0}' before closing?",
                        viewContent.IsUntitled
                            ? viewContent.UntitledName
                            : Path.GetFileName(viewContent.ContentName)),
                    GettextCatalog.GetString("If you don't save, all changes will be permanently lost."),
                    AlertButton.CloseWithoutSave, AlertButton.Cancel, viewContent.IsUntitled ? AlertButton.SaveAs : AlertButton.Save);
                if (result == AlertButton.Save || result == AlertButton.SaveAs)
                {
                    await FindDocument(workbenchWindow).Save();
                    args.Cancel = viewContent.IsDirty;
                    if (args.Cancel)
                        FindDocument(workbenchWindow).Select();
                }
                else
                {
                    args.Cancel |= result != AlertButton.CloseWithoutSave;
                    if (!args.Cancel)
                        viewContent.DiscardChanges();
                }
            }
            OnDocumentClosing(FindDocument(workbenchWindow));
        }

        private void OnWindowClosed(object sender, WorkbenchWindowEventArgs args)
        {
            IWorkbenchWindow workbenchWindow = (IWorkbenchWindow)sender;
            var doc = FindDocument(workbenchWindow);
            workbenchWindow.Closing -= OnWindowClosing;
            workbenchWindow.Closed -= OnWindowClosed;
            Documents = Documents.Remove(doc);

            OnDocumentClosed(doc);
            doc.DisposeDocument();
        }

        private async Task<bool> RealOpenFile(ProgressMonitor monitor, FileOpenInformation openFileInfo)
        {
            FilePath fileName;

            Counters.OpenDocumentTimer.Trace("Checking file");

            string origName = openFileInfo.FileName;

            if (origName == null)
            {
                monitor.ReportError(GettextCatalog.GetString("Invalid file name"));
                return false;
            }

            fileName = openFileInfo.FileName;
            if (!origName.StartsWith("http://", StringComparison.Ordinal))
                fileName = fileName.FullPath;

            //Debug.Assert(FileService.IsValidPath(fileName));
            if (FileService.IsDirectory(fileName))
            {
                monitor.ReportError(GettextCatalog.GetString("{0} is a directory", fileName));
                return false;
            }

            // test, if file fileName exists
            if (!origName.StartsWith("http://", StringComparison.Ordinal))
            {
                // test, if an untitled file should be opened
                if (!Path.IsPathRooted(origName))
                {
                    foreach (Document doc in Documents)
                    {
                        if (doc.Window.ViewContent.IsUntitled && doc.Window.ViewContent.UntitledName == origName)
                        {
                            doc.Select();
                            openFileInfo.NewContent = doc.Window.ViewContent;
                            return true;
                        }
                    }
                }

                if (!File.Exists(fileName))
                {
                    monitor.ReportError(GettextCatalog.GetString("File not found: {0}", fileName));
                    return false;
                }
            }

            Counters.OpenDocumentTimer.Trace("Looking for binding");

            IDisplayBinding binding;
            IViewDisplayBinding viewBinding;
            Project project = openFileInfo.Project;

            if (openFileInfo.DisplayBinding != null)
            {
                binding = viewBinding = openFileInfo.DisplayBinding;
            }
            else
            {
                var bindings = DisplayBindingService.GetDisplayBindings(fileName, null, project).ToList();
                if (openFileInfo.Options.HasFlag(OpenDocumentOptions.OnlyInternalViewer))
                {
                    binding = bindings.OfType<IViewDisplayBinding>().FirstOrDefault(d => d.CanUseAsDefault)
                        ?? bindings.OfType<IViewDisplayBinding>().FirstOrDefault();
                    viewBinding = (IViewDisplayBinding)binding;
                }
                else if (openFileInfo.Options.HasFlag(OpenDocumentOptions.OnlyExternalViewer))
                {
                    binding = bindings.OfType<IExternalDisplayBinding>().FirstOrDefault(d => d.CanUseAsDefault);
                    viewBinding = null;
                }
                else
                {
                    binding = bindings.FirstOrDefault(d => d.CanUseAsDefault);
                    viewBinding = binding as IViewDisplayBinding;
                }
            }

            try
            {
                if (binding != null)
                {
                    if (viewBinding != null)
                    {
                        var fw = new LoadFileWrapper(monitor, window, viewBinding, project, openFileInfo);
                        await fw.Invoke(fileName);
                    }
                    else
                    {
                        var extBinding = (IExternalDisplayBinding)binding;
                        var app = extBinding.GetApplication(fileName, null, project);
                        app.Launch(fileName);
                    }

                    Counters.OpenDocumentTimer.Trace("Adding to recent files");
                    DesktopService.RecentFiles.AddFile(fileName, project);
                }
                else if (!openFileInfo.Options.HasFlag(OpenDocumentOptions.OnlyInternalViewer))
                {
                    try
                    {
                        Counters.OpenDocumentTimer.Trace("Showing in browser");
                        DesktopService.OpenFile(fileName);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("Error opening file: " + fileName, ex);
                        MessageService.ShowError(GettextCatalog.GetString("File '{0}' could not be opened", fileName));
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                monitor.ReportError("", ex);
                return false;
            }
            return true;
        }

        internal Document FindDocument(IWorkbenchWindow workbenchWindow)
        {
            foreach (Document doc in Documents)
                if (doc.Window == workbenchWindow)
                    return doc;
            return null;
        }

        internal Pad FindPad(PadContent padContent)
        {
            foreach (Pad pad in Pads)
                if (pad.Content == padContent)
                    return pad;
            return null;
        }

        internal void ReorderTab(int oldPlacement, int newPlacement)
        {
            window.ReorderTab(oldPlacement, newPlacement);
        }

        internal void ReorderDocuments(int oldPlacement, int newPlacement)
        {
            ViewContent content = window.ViewContentCollection[oldPlacement];
            window.ViewContentCollection.RemoveAt(oldPlacement);
            window.ViewContentCollection.Insert(newPlacement, content);

            Document doc = Documents[oldPlacement];
            Documents = Documents.RemoveAt(oldPlacement).Insert(newPlacement, doc);
        }

        internal void LockActiveWindowChangeEvent()
        {
            window.LockActiveWindowChangeEvent();
        }

        internal void UnlockActiveWindowChangeEvent()
        {
            window.UnlockActiveWindowChangeEvent();
        }

        private List<FileData> fileStatus;
        private readonly SemaphoreSlim fileStatusLock = new SemaphoreSlim(1, 1);
        // http://msdn.microsoft.com/en-us/library/system.io.file.getlastwritetimeutc(v=vs.110).aspx
        private static readonly DateTime NonExistentFile = new DateTime(1601, 1, 1);
        internal void SaveFileStatus()
        {
            //			DateTime t = DateTime.Now;
            List<FilePath> files = new List<FilePath>(GetKnownFiles());
            fileStatus = new List<FileData>(files.Count);
            //			Console.WriteLine ("SaveFileStatus(0) " + (DateTime.Now - t).TotalMilliseconds + "ms " + files.Count);

            Task.Run(async delegate
            {
                //				t = DateTime.Now;
                try
                {
                    await fileStatusLock.WaitAsync().ConfigureAwait(false);
                    if (fileStatus == null)
                        return;
                    foreach (FilePath file in files)
                    {
                        try
                        {
                            DateTime ft = File.GetLastWriteTimeUtc(file);
                            FileData fd = new FileData(file, ft != NonExistentFile ? ft : DateTime.MinValue);
                            fileStatus.Add(fd);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                }
                finally
                {
                    fileStatusLock.Release();
                }
                //				Console.WriteLine ("SaveFileStatus " + (DateTime.Now - t).TotalMilliseconds + "ms " + fileStatus.Count);
            });
        }

        internal void CheckFileStatus()
        {
            if (fileStatus == null)
                return;

            Task.Run(async delegate
            {
                try
                {
                    await fileStatusLock.WaitAsync();
                    if (fileStatus == null)
                        return;
                    List<FilePath> modified = new List<FilePath>(fileStatus.Count);
                    foreach (FileData fd in fileStatus)
                    {
                        try
                        {
                            DateTime ft = File.GetLastWriteTimeUtc(fd.File);
                            if (ft != NonExistentFile)
                            {
                                if (ft != fd.TimeUtc)
                                    modified.Add(fd.File);
                            }
                            else if (fd.TimeUtc != DateTime.MinValue)
                            {
                                FileService.NotifyFileRemoved(fd.File);
                            }
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                    if (modified.Count > 0)
                        FileService.NotifyFilesChanged(modified);

                    fileStatus = null;
                }
                finally
                {
                    fileStatusLock.Release();
                }
            });
        }

        private IEnumerable<FilePath> GetKnownFiles()
        {
            foreach (Document doc in Documents)
            {
                if (!doc.HasProject && doc.IsFile)
                    yield return doc.FileName;
            }
        }

        private struct FileData
        {
            public FileData(FilePath file, DateTime timeUtc)
            {
                File = file;
                TimeUtc = timeUtc;
            }

            public readonly FilePath File;
            public readonly DateTime TimeUtc;
        }

        private void OnDocumentOpened(DocumentEventArgs e)
        {
            try
            {
                DocumentOpened?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Exception while opening documents", ex);
            }
        }

        private void OnDocumentClosed(Document doc)
        {
            try
            {
                var e = new DocumentEventArgs(doc);
                DocumentClosed?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Exception while closing documents", ex);
            }
        }

        private void OnDocumentClosing(Document doc)
        {
            try
            {
                var e = new DocumentEventArgs(doc);
                DocumentClosing?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Exception before closing documents", ex);
            }
        }

        public event EventHandler<DocumentEventArgs> DocumentOpened;
        public event EventHandler<DocumentEventArgs> DocumentClosed;
        public event EventHandler<DocumentEventArgs> DocumentClosing;
    }

    public class FileSaveInformation
    {
        private FilePath fileName;
        public FilePath FileName
        {
            get
            {
                return fileName;
            }
            set
            {
                fileName = value.CanonicalPath;
                if (fileName.IsNullOrEmpty)
                    LoggingService.LogError("FileName == null\n" + Environment.StackTrace);
            }
        }

        public Encoding Encoding { get; set; }

        public FileSaveInformation(FilePath fileName, Encoding encoding = null)
        {
            FileName = fileName;
            Encoding = encoding;
        }
    }

    public class FileOpenInformation
    {
        private FilePath fileName;
        public FilePath FileName
        {
            get
            {
                return fileName;
            }
            set
            {
                fileName = value.CanonicalPath.ResolveLinks();
                if (fileName.IsNullOrEmpty)
                    LoggingService.LogError("FileName == null\n" + Environment.StackTrace);
            }
        }

        public OpenDocumentOptions Options { get; set; }

        public int Offset { get; set; } = -1;

        public int Line { get; set; }
        public int Column { get; set; }
        public IViewDisplayBinding DisplayBinding { get; set; }
        public ViewContent NewContent { get; set; }
        public Encoding Encoding { get; set; }
        public Project Project { get; set; }

        /// <summary>
        /// Is true when the file is already open and reload is requested.
        /// </summary>
        public bool IsReloadOperation { get; set; }

        internal DockNotebook DockNotebook { get; set; }

        [Obsolete("Use FileOpenInformation (FilePath filePath, Project project, int line, int column, OpenDocumentOptions options)")]
        public FileOpenInformation(string fileName, int line, int column, OpenDocumentOptions options)
        {
            FileName = fileName;
            Line = line;
            Column = column;
            Options = options;

        }

        public FileOpenInformation(FilePath filePath, Project project = null)
        {
            FileName = filePath;
            Project = project;
            Options = OpenDocumentOptions.Default;
        }

        public FileOpenInformation(FilePath filePath, Project project, int line, int column, OpenDocumentOptions options)
        {
            FileName = filePath;
            Project = project;
            Line = line;
            Column = column;
            Options = options;
        }

        public FileOpenInformation(FilePath filePath, Project project, bool bringToFront)
        {
            FileName = filePath;
            Project = project;
            Options = OpenDocumentOptions.Default;
            if (bringToFront)
            {
                Options |= OpenDocumentOptions.BringToFront;
            }
            else
            {
                Options &= ~OpenDocumentOptions.BringToFront;
            }
        }
    }

    internal class LoadFileWrapper
    {
        private readonly IViewDisplayBinding binding;
        private readonly Project project;
        private readonly FileOpenInformation fileInfo;
        private readonly IDefaultWorkbenchWindow workbenchWindow;
        private readonly ProgressMonitor monitor;
        private ViewContent newContent;

        public LoadFileWrapper(ProgressMonitor monitor, IDefaultWorkbenchWindow workbenchWindow, IViewDisplayBinding binding, FileOpenInformation fileInfo)
        {
            this.monitor = monitor;
            this.workbenchWindow = workbenchWindow;
            this.fileInfo = fileInfo;
            this.binding = binding;
        }

        public LoadFileWrapper(ProgressMonitor monitor, IDefaultWorkbenchWindow workbenchWindow, IViewDisplayBinding binding, Project project, FileOpenInformation fileInfo)
            : this(monitor, workbenchWindow, binding, fileInfo)
        {
            this.project = project;
        }

        public async Task<bool> Invoke(string fileName)
        {
            try
            {
                Counters.OpenDocumentTimer.Trace("Creating content");
                string mimeType = DesktopService.GetMimeTypeForUri(fileName);
                if (binding.CanHandle(fileName, mimeType, project))
                {
                    newContent = binding.CreateContent(fileName, mimeType, project);
                }
                else
                {
                    monitor.ReportError(GettextCatalog.GetString("The file '{0}' could not be opened.", fileName));
                }
                if (newContent == null)
                {
                    monitor.ReportError(GettextCatalog.GetString("The file '{0}' could not be opened.", fileName));
                    return false;
                }

                newContent.Binding = binding;
                if (project != null)
                    newContent.Project = project;

                Counters.OpenDocumentTimer.Trace("Loading file");

                try
                {
                    await newContent.Load(fileInfo);
                }
                catch (InvalidEncodingException iex)
                {
                    monitor.ReportError(GettextCatalog.GetString("The file '{0}' could not opened. {1}", fileName, iex.Message));
                    return false;
                }
                catch (OverflowException)
                {
                    monitor.ReportError(GettextCatalog.GetString("The file '{0}' could not opened. File too large.", fileName));
                    return false;
                }
            }
            catch (Exception ex)
            {
                monitor.ReportError(GettextCatalog.GetString("The file '{0}' could not be opened.", fileName), ex);
                return false;
            }

            // content got re-used
            if (newContent.WorkbenchWindow != null)
            {
                newContent.WorkbenchWindow.SelectWindow();
                fileInfo.NewContent = newContent;
                return true;
            }

            Counters.OpenDocumentTimer.Trace("Showing view");

            workbenchWindow.ShowView(newContent, fileInfo, binding);

            // ReSharper disable once PossibleNullReferenceException
            newContent.WorkbenchWindow.DocumentType = binding.Name;


            var ipos = (TextEditor)newContent.GetContent(typeof(TextEditor));
            if (fileInfo.Line > 0 && ipos != null)
            {
                FileSettingsStore.Remove(fileName);
                ipos.RunWhenLoaded(JumpToLine);
            }

            fileInfo.NewContent = newContent;
            return true;
        }

        private void JumpToLine()
        {
            var ipos = (TextEditor)newContent.GetContent(typeof(TextEditor));
            var loc = new DocumentLocation(Math.Max(1, fileInfo.Line), Math.Max(1, fileInfo.Column));
            if (fileInfo.Offset >= 0)
            {
                loc = ipos.OffsetToLocation(fileInfo.Offset);
            }
            ipos.SetCaretLocation(loc, fileInfo.Options.HasFlag(OpenDocumentOptions.HighlightCaretLine));
        }
    }

    [Flags]
    public enum OpenDocumentOptions
    {
        None = 0,
        BringToFront = 1,
        CenterCaretLine = 1 << 1,
        HighlightCaretLine = 1 << 2,
        OnlyInternalViewer = 1 << 3,
        OnlyExternalViewer = 1 << 4,
        TryToReuseViewer = 1 << 5,

        Default = BringToFront | CenterCaretLine | HighlightCaretLine | TryToReuseViewer,
        Debugger = BringToFront | CenterCaretLine | TryToReuseViewer,
        DefaultInternal = Default | OnlyInternalViewer
    }
}
