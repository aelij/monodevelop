//
// Document.cs
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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MonoDevelop.Components;
using MonoDevelop.Core;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.Ide.Gui.Dialogs;
using MonoDevelop.Projects;

namespace MonoDevelop.Ide.Gui
{
    public class Document : DocumentContext
    {
        internal object MemoryProbe = Counters.DocumentsInMemory.CreateMemoryProbe();

        private IWorkbenchWindow window;

        public IWorkbenchWindow Window => window;

        internal DateTime LastTimeActive
        {
            get;
            set;
        }

        public override T GetContent<T>()
        {
            if (window == null)
                return null;
            //check whether the ViewContent can return the type directly
            T ret = Window.ActiveViewContent.GetContent(typeof(T)) as T;
            if (ret != null)
                return ret;

            //check the primary viewcontent
            //not sure if this is the right thing to do, but things depend on this behaviour
            if (Window.ViewContent != Window.ActiveViewContent)
            {
                ret = Window.ViewContent.GetContent(typeof(T)) as T;
                if (ret != null)
                    return ret;
            }

            //If we didn't find in ActiveView or ViewContent... Try in SubViews
            foreach (var subView in window.SubViewContents)
            {
                foreach (var cnt in subView.GetContents<T>())
                {
                    return cnt;
                }
            }

            return null;
        }

        internal ProjectReloadCapability ProjectReloadCapability => Window.ViewContent.ProjectReloadCapability;

        public override IEnumerable<T> GetContents<T>()
        {
            foreach (var cnt in window.ViewContent.GetContents<T>())
            {
                yield return cnt;
            }

            foreach (var subView in window.SubViewContents)
            {
                foreach (var cnt in subView.GetContents<T>())
                {
                    yield return cnt;
                }
            }
        }

        public Document(IWorkbenchWindow window)
        {
            Counters.OpenDocuments++;
            LastTimeActive = DateTime.Now;
            this.window = window;
            window.Closed += OnClosed;
            window.ActiveViewContentChanged += OnActiveViewContentChanged;
            window.ViewsChanged += HandleViewsChanged;
        }

        public FilePath FileName
        {
            get
            {
                if (Window == null || !Window.ViewContent.IsFile)
                    return null;
                return Window.ViewContent.IsUntitled ? Window.ViewContent.UntitledName : Window.ViewContent.ContentName;
            }
        }

        public bool IsFile => Window.ViewContent.IsFile;

        public bool IsDirty
        {
            get { return !Window.ViewContent.IsViewOnly && (Window.ViewContent.ContentName == null || Window.ViewContent.IsDirty); }
            set { Window.ViewContent.IsDirty = value; }
        }

        public object GetDocumentObject()
        {
            return Window?.ViewContent?.GetDocumentObject();
        }

        public override Project Project => Window?.ViewContent.Project;
        
        public string PathRelativeToProject => Window.ViewContent.PathRelativeToProject;

        public void Select()
        {
            window.SelectWindow();
        }

        public DocumentView ActiveView
        {
            get
            {
                LoadViews(true);
                return WrapView(window.ActiveViewContent);
            }
        }

        public DocumentView PrimaryView
        {
            get
            {
                LoadViews(true);
                return WrapView(window.ViewContent);
            }
        }

        public ReadOnlyCollection<DocumentView> Views
        {
            get
            {
                LoadViews(true);
                return viewsRo ?? (viewsRo = new ReadOnlyCollection<DocumentView> (views));
            }
        }

        private ReadOnlyCollection<DocumentView> viewsRo;
        private List<DocumentView> views = new List<DocumentView>();

        private void HandleViewsChanged(object sender, EventArgs e)
        {
            LoadViews(false);
        }

        private void LoadViews(bool force)
        {
            if (!force && views == null)
                return;
            var newList = new List<DocumentView> {WrapView (window.ViewContent)};
            foreach (var v in window.SubViewContents)
                newList.Add(WrapView(v));
            views = newList;
            viewsRo = null;
        }

        private DocumentView WrapView(BaseViewContent content)
        {
            if (content == null)
                return null;
            if (views != null)
                return views.FirstOrDefault(v => v.BaseContent == content) ?? new DocumentView(this, content);
            return new DocumentView(this, content);
        }

        public override string Name
        {
            get
            {
                ViewContent view = Window.ViewContent;
                return view.IsUntitled ? view.UntitledName : view.ContentName;
            }
        }

        public TextEditor Editor => GetContent<TextEditor>();

        public bool IsViewOnly => Window.ViewContent.IsViewOnly;

        public Task Reload()
        {
            return ReloadTask();
        }

        private async Task ReloadTask()
        {
            ICustomXmlSerializer memento = null;
            IMementoCapable mc = GetContent<IMementoCapable>();
            if (mc != null)
            {
                memento = mc.Memento;
            }
            window.ViewContent.DiscardChanges();
            await window.ViewContent.Load(new FileOpenInformation(window.ViewContent.ContentName) { IsReloadOperation = true });
            if (memento != null)
            {
                mc.Memento = memento;
            }
        }

        public Task Save()
        {
            return SaveTask();
        }

        private async Task SaveTask()
        {
            // suspend type service "check all file loop" since we have already a parsed document.
            // Or at least one that updates "soon".
            try
            {
                // Freeze the file change events. There can be several such events, and sending them all together
                // is more efficient
                FileService.FreezeEvents();

                if (Window.ViewContent.IsViewOnly || !Window.ViewContent.IsDirty)
                    return;

                if (!Window.ViewContent.IsFile)
                {
                    await Window.ViewContent.Save();
                    return;
                }

                if (Window.ViewContent.ContentName == null)
                {
                    await SaveAs();
                }
                else
                {
                    try
                    {
                        FileService.RequestFileEdit(Window.ViewContent.ContentName);
                    }
                    catch (Exception ex)
                    {
                        MessageService.ShowError(("The file could not be saved."), ex.Message, ex);
                    }

                    FileAttributes attr = FileAttributes.ReadOnly | FileAttributes.Directory | FileAttributes.Offline | FileAttributes.System;

                    if (!File.Exists(Window.ViewContent.ContentName) || (File.GetAttributes(window.ViewContent.ContentName) & attr) != 0)
                    {
                        await SaveAs();
                    }
                    else
                    {
                        string fileName = Window.ViewContent.ContentName;
                        // save backup first						
                        if (IdeApp.Preferences.CreateFileBackupCopies)
                        {
                            await Window.ViewContent.Save(fileName + "~");
                            FileService.NotifyFileChanged(fileName + "~");
                        }
                        await Window.ViewContent.Save(fileName);
                        FileService.NotifyFileChanged(fileName);
                        OnSaved(EventArgs.Empty);
                    }
                }
            }
            finally
            {
                // Send all file change notifications
                FileService.ThawEvents();
            }
        }

        public Task SaveAs()
        {
            return SaveAs(null);
        }

        public Task SaveAs(string filename)
        {
            return SaveAsTask(filename);
        }

        private async Task SaveAsTask(string filename)
        {
            if (Window.ViewContent.IsViewOnly || !Window.ViewContent.IsFile)
                return;

            Encoding encoding = null;

            var tbuffer = GetContent<ITextSource>();
            if (tbuffer != null)
            {
                encoding = tbuffer.Encoding ?? Encoding.UTF8;
            }

            if (filename == null)
            {
                var dlg = new OpenFileDialog(("Save as..."), FileChooserAction.Save)
                {
                    TransientFor = IdeApp.Workbench.RootWindow,
                    Encoding = encoding,
                    ShowEncodingSelector = (tbuffer != null)
                };
                if (Window.ViewContent.IsUntitled)
                    dlg.InitialFileName = Window.ViewContent.UntitledName;
                else
                {
                    dlg.CurrentFolder = Path.GetDirectoryName(Window.ViewContent.ContentName);
                    dlg.InitialFileName = Path.GetFileName(Window.ViewContent.ContentName);
                }

                if (!dlg.Run())
                    return;

                filename = dlg.SelectedFile;
                encoding = dlg.Encoding;
            }

            if (!FileService.IsValidPath(filename))
            {
                MessageService.ShowMessage($"File name {filename} is invalid");
                return;
            }
            // detect preexisting file
            if (File.Exists(filename))
            {
                if (!MessageService.Confirm($"File {filename} already exists. Overwrite?", AlertButton.OverwriteFile))
                    return;
            }

            // save backup first
            if (IdeApp.Preferences.CreateFileBackupCopies)
            {
                if (tbuffer != null && encoding != null)
                    TextFileUtility.WriteText(filename + "~", tbuffer.Text, encoding, tbuffer.UseBOM);
                else
                    await Window.ViewContent.Save(new FileSaveInformation(filename + "~", encoding));
            }
            // do actual save
            await Window.ViewContent.Save(new FileSaveInformation(filename, encoding));
            DesktopService.RecentFiles.AddFile(filename, (Project)null);

            OnSaved(EventArgs.Empty);
        }

        public bool Close()
        {
            return ((SdiWorkspaceWindow)Window).CloseWindow(false, true);
        }

        protected override void OnSaved(EventArgs e)
        {
            IdeApp.Workbench.SaveFileStatus();
            base.OnSaved(e);
        }

        private void OnClosed(object s, EventArgs a)
        {
            try
            {
                OnClosed(a);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Exception while calling OnClosed.", ex);
            }

            Counters.OpenDocuments--;
        }

        internal void DisposeDocument()
        {
            (window as SdiWorkspaceWindow)?.DetachFromPathedDocument();
            window.Closed -= OnClosed;
            window.ActiveViewContentChanged -= OnActiveViewContentChanged;

            // Unsubscribe project events
            window.ViewsChanged += HandleViewsChanged;

            window = null;

            views = null;
            viewsRo = null;
        }

        #region document tasks

        #endregion

        private void OnActiveViewContentChanged(object s, EventArgs args)
        {
            OnViewChanged(args);
        }

        private void OnClosed(EventArgs args)
        {
            Closed?.Invoke(this, args);
        }

        private void OnViewChanged(EventArgs args)
        {
            ViewChanged?.Invoke(this, args);
        }

        private void InitializeExtensionChain()
        {
            Editor.InitializeExtensionChain(this);

            (window as SdiWorkspaceWindow)?.AttachToPathedDocument(GetContent<IPathedDocument>());
        }

        private void InitializeEditor()
        {
            InitializeExtensionChain();
        }

        internal void OnDocumentAttached()
        {
            if (Editor != null)
            {
                InitializeEditor();
            }

            window.Document = this;
        }

        /// <summary>
        /// Performs an action when the content is loaded.
        /// </summary>
        /// <param name='action'>
        /// The action to run.
        /// </param>
        public void RunWhenLoaded(Action action)
        {
            var e = Editor;
            if (e == null)
            {
                action();
                return;
            }
            e.RunWhenLoaded(action);
        }

        public void RunWhenRealized(Action action)
        {
            var e = Editor;
            if (e == null)
            {
                action();
                return;
            }
            e.RunWhenRealized(action);
        }

        internal void SetProject(Project project)
        {
            if (Window?.ViewContent == null || Window.ViewContent.Project == project)
                return;
            // Unsubscribe project events
            Window.ViewContent.Project = project;
            InitializeExtensionChain();
        }

        public override void AttachToProject(Project project)
        {
            SetProject(project);
        }

        public override void ReparseDocument()
        {
        }

        public event EventHandler Closed;
        public event EventHandler ViewChanged;


        public string[] CommentTags
        {
            get
            {
                if (IsFile)
                    return GetCommentTags(FileName);
                return null;
            }
        }

        public static string[] GetCommentTags(string fileName)
        {
            //Document doc = IdeApp.Workbench.ActiveDocument;
            string loadedMimeType = DesktopService.GetMimeTypeForUri(fileName);

            var result = TextEditorFactory.GetSyntaxProperties(loadedMimeType, "LineComment");
            if (result != null)
                return result;

            var start = TextEditorFactory.GetSyntaxProperties(loadedMimeType, "BlockCommentStart");
            var end = TextEditorFactory.GetSyntaxProperties(loadedMimeType, "BlockCommentEnd");
            if (start != null && end != null)
                return new[] { start[0], end[0] };
            return null;
        }

        //		public MonoDevelop.Projects.CodeGeneration.CodeGenerator CreateCodeGenerator ()
        //		{
        //			return MonoDevelop.Projects.CodeGeneration.CodeGenerator.CreateGenerator (Editor.Document.MimeType, 
        //				Editor.Options.TabsToSpaces, Editor.Options.TabSize, Editor.EolMarker);
        //		}

        /// <summary>
        /// If the document shouldn't restore the settings after the load it can be disabled with this method.
        /// That is useful when opening a document and programmatically scrolling to a specified location.
        /// </summary>
        public void DisableAutoScroll()
        {
            if (IsFile)
                FileSettingsStore.Remove(FileName);
        }
    }

    [Serializable]
    public sealed class DocumentEventArgs : EventArgs
    {
        public Document Document
        {
            get;
            set;
        }
        public DocumentEventArgs(Document document)
        {
            Document = document;
        }
    }
}

