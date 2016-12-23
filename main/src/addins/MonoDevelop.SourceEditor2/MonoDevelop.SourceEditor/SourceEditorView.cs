// SourceEditorView.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cairo;
using Gtk;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using Mono.TextEditor.PopupWindow;
using Mono.TextEditor.Utils;
using Mono.TextEditor.Vi;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide;
using MonoDevelop.Ide.CodeCompletion;
using MonoDevelop.Ide.CodeTemplates;
using MonoDevelop.Ide.Commands;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;
using MonoDevelop.Ide.Editor.Highlighting;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.Ide.TextEditing;
using MonoDevelop.Projects.Text;
using MonoDevelop.SourceEditor.Wrappers;
using Xwt;
using Action = System.Action;
using Application = Gtk.Application;
using ColoredSegment = Mono.TextEditor.Utils.ColoredSegment;
using DocumentLocation = Mono.TextEditor.DocumentLocation;
using DocumentRegion = MonoDevelop.Ide.Editor.DocumentRegion;
using EditMode = MonoDevelop.Ide.Editor.EditMode;
using FileSettingsStore = MonoDevelop.Ide.Editor.FileSettingsStore;
using FoldingType = Mono.TextEditor.FoldingType;
using InsertionCursorEventArgs = Mono.TextEditor.InsertionCursorEventArgs;
using InsertionPoint = Mono.TextEditor.InsertionPoint;
using ITextEditorOptions = MonoDevelop.Ide.Editor.ITextEditorOptions;
using LineEventArgs = Mono.TextEditor.LineEventArgs;
using NewLineInsertion = Mono.TextEditor.NewLineInsertion;
using Path = System.IO.Path;
using Point = Gdk.Point;
using Rectangle = Xwt.Rectangle;
using Scale = Pango.Scale;
using Selection = Mono.TextEditor.Selection;
using SelectionMode = MonoDevelop.Ide.Editor.SelectionMode;
using SyntaxModeService = Mono.TextEditor.Highlighting.SyntaxModeService;
using TextFileUtility = MonoDevelop.Core.Text.TextFileUtility;
using TextLink = Mono.TextEditor.TextLink;
using TextSegment = Mono.TextEditor.TextSegment;
using TooltipProvider = MonoDevelop.Ide.Editor.TooltipProvider;
using UrlType = MonoDevelop.Ide.Editor.UrlType;
using Widget = Gtk.Widget;
using Window = Gtk.Window;

namespace MonoDevelop.SourceEditor
{
    internal class SourceEditorView : ViewContent, IBookmarkBuffer, IClipboardHandler, ITextFile,
        ICompletionWidget, ISplittable, IFoldable,
        IZoomable, ITextEditorDataProvider,
        ICodeTemplateHandler, ICodeTemplateContextProvider, IPrintable,
        ITextEditorImpl, IEditorActionHost, ITextMarkerFactory, IUndoHandler
    {
        private readonly SourceEditorWidget widget;
        private bool isDisposed;
        private DateTime lastSaveTimeUtc;
        internal object MemoryProbe = Counters.SourceViewsInMemory.CreateMemoryProbe();
        private bool writeAllowed;
        private bool writeAccessChecked;

        public ViewContent ViewContent => this;

        public TextDocument Document
        {
            get
            {
                return widget?.TextEditor?.Document;
            }
            set
            {
                widget.TextEditor.Document = value;
            }
        }

        public DateTime LastSaveTimeUtc
        {
            get
            {
                return lastSaveTimeUtc;
            }
            internal set
            {
                lastSaveTimeUtc = value;
            }
        }

        internal ExtensibleTextEditor TextEditor => widget.TextEditor;

        internal SourceEditorWidget SourceEditorWidget => widget;

        public override Control Control => widget?.Vbox;

        public int LineCount => Document.LineCount;


        string ITextEditorImpl.ContextMenuPath
        {
            get { return TextEditor.ContextMenuPath; }
            set { TextEditor.ContextMenuPath = value; }
        }

        public override string TabPageLabel => ("Source");

        public SourceEditorView(IReadonlyTextDocument document = null)
        {
            Counters.LoadedEditors++;

            widget = new SourceEditorWidget(this);
            if (document != null)
            {
                var textDocument = document as TextDocument;
                if (textDocument != null)
                {
                    widget.TextEditor.Document = textDocument;
                }
                else
                {
                    widget.TextEditor.Document.Text = document.Text;
                }
            }

            widget.TextEditor.Document.LineChanged += HandleLineChanged;
            widget.TextEditor.Document.LineInserted += HandleLineChanged;
            widget.TextEditor.Document.LineRemoved += HandleLineChanged;

            widget.TextEditor.Document.BeginUndo += HandleBeginUndo;
            widget.TextEditor.Document.EndUndo += HandleEndUndo;

            widget.TextEditor.Document.TextReplacing += OnTextReplacing;
            widget.TextEditor.Document.TextReplaced += OnTextReplaced;
            widget.TextEditor.Document.ReadOnlyCheckDelegate = CheckReadOnly;
            widget.TextEditor.Document.TextSet += HandleDocumentTextSet;


            widget.TextEditor.TextViewMargin.LineShown += TextViewMargin_LineShown;
            //			widget.TextEditor.Document.DocumentUpdated += delegate {
            //				this.IsDirty = Document.IsDirty;
            //			};

            widget.TextEditor.Caret.PositionChanged += HandlePositionChanged;
            widget.TextEditor.IconMargin.ButtonPressed += OnIconButtonPress;
            widget.TextEditor.TextArea.FocusOutEvent += TextArea_FocusOutEvent;

            TextEditorService.FileExtensionAdded += HandleFileExtensionAdded;
            TextEditorService.FileExtensionRemoved += HandleFileExtensionRemoved;

            Document.AddAnnotation(this);
            widget.TextEditor.Document.MimeTypeChanged += Document_MimeTypeChanged;
            if (document != null)
            {
                Document.MimeType = document.MimeType;
                Document.FileName = document.FileName;
            }
            FileRegistry.Add(this);
        }

        private void Document_MimeTypeChanged(object sender, EventArgs e)
        {
            //if the mimetype doesn't have a syntax mode, try to load one for its base mimetypes
            var sm = Document.SyntaxMode as SyntaxMode;
            if (sm != null && sm.MimeType == null)
            {
                foreach (string mt in DesktopService.GetMimeTypeInheritanceChain(Document.MimeType))
                {
                    var syntaxMode = SyntaxModeService.GetSyntaxMode(null, mt);
                    if (syntaxMode != null)
                    {
                        Document.SyntaxMode = syntaxMode;
                        break;
                    }
                }
            }
        }

        private void HandleDocumentTextSet(object sender, EventArgs e)
        {
            while (EditSessions.Count > 0)
            {
                EndSession();
            }
        }

        protected override void OnContentNameChanged()
        {
            Document.FileName = ContentName;
            if (!String.IsNullOrEmpty(ContentName) && File.Exists(ContentName))
                lastSaveTimeUtc = File.GetLastWriteTimeUtc(ContentName);
            base.OnContentNameChanged();
        }

        private void HandleLineChanged(object sender, LineEventArgs e)
        {
            UpdateWidgetPositions();
            LineChanged?.Invoke(this, new Ide.Editor.LineEventArgs(new DocumentLineWrapper(e.Line)));
        }

        private void HandleEndUndo(object sender, TextDocument.UndoOperationEventArgs e)
        {
            OnEndUndo(EventArgs.Empty);
        }

        private void HandleBeginUndo(object sender, EventArgs e)
        {
            OnBeginUndo(EventArgs.Empty);
        }


        private void HandlePositionChanged(object sender, DocumentLocationEventArgs e)
        {
            OnCaretPositionSet(EventArgs.Empty);
            FireCompletionContextChanged();
            OnCaretPositionChanged(EventArgs.Empty);
        }

        private void HandleFileExtensionRemoved(object sender, FileExtensionEventArgs args)
        {
            if (ContentName == null || args.Extension.File.FullPath != Path.GetFullPath(ContentName))
                return;
            RemoveFileExtension(args.Extension);
        }

        private void HandleFileExtensionAdded(object sender, FileExtensionEventArgs args)
        {
            if (ContentName == null || args.Extension.File.FullPath != Path.GetFullPath(ContentName))
                return;
            AddFileExtension(args.Extension);
        }

        private readonly Dictionary<TopLevelWidgetExtension, Widget> widgetExtensions = new Dictionary<TopLevelWidgetExtension, Widget>();
        private readonly Dictionary<FileExtension, Tuple<TextLineMarker, DocumentLine>> markerExtensions = new Dictionary<FileExtension, Tuple<TextLineMarker, DocumentLine>>();

        private void LoadExtensions()
        {
            if (ContentName == null)
                return;

            foreach (var ext in TextEditorService.GetFileExtensions(ContentName))
                AddFileExtension(ext);
        }

        private void AddFileExtension(FileExtension extension)
        {
            var levelWidgetExtension = extension as TopLevelWidgetExtension;
            if (levelWidgetExtension != null)
            {
                var widgetExtension = levelWidgetExtension;
                Widget w = widgetExtension.CreateWidget();
                int x, y;
                if (!CalcWidgetPosition(widgetExtension, w, out x, out y))
                {
                    w.Destroy();
                    return;
                }

                widgetExtensions[widgetExtension] = w;
                widget.TextEditor.TextArea.AddTopLevelWidget(w, x, y);
                widgetExtension.ScrollToViewRequested += HandleScrollToViewRequested;
            }
            else if (extension is TextLineMarkerExtension)
            {
                var lineExt = (TextLineMarkerExtension)extension;

                DocumentLine line = widget.TextEditor.Document.GetLine(lineExt.Line);
                if (line == null)
                    return;

                var marker = (TextLineMarker)lineExt.CreateMarker();
                widget.TextEditor.Document.AddMarker(line, marker);
                widget.TextEditor.QueueDraw();
                markerExtensions[extension] = new Tuple<TextLineMarker, DocumentLine>(marker, line);
            }
        }

        private void HandleScrollToViewRequested(object sender, EventArgs e)
        {
            var widgetExtension = (TopLevelWidgetExtension)sender;
            Widget w;
            if (widgetExtensions.TryGetValue(widgetExtension, out w))
            {
                int x, y;
                widget.TextEditor.TextArea.GetTopLevelWidgetPosition(w, out x, out y);
                var size = w.SizeRequest();
                Application.Invoke(delegate
                {
                    widget.TextEditor.ScrollTo(new Gdk.Rectangle(x, y, size.Width, size.Height));
                });
            }
        }

        private void RemoveFileExtension(FileExtension extension)
        {
            var levelWidgetExtension = extension as TopLevelWidgetExtension;
            if (levelWidgetExtension != null)
            {
                var widgetExtension = levelWidgetExtension;
                Widget w;
                if (!widgetExtensions.TryGetValue(widgetExtension, out w))
                    return;
                widgetExtensions.Remove(widgetExtension);
                widget.TextEditor.TextArea.Remove(w);
                w.Destroy();
                widgetExtension.ScrollToViewRequested -= HandleScrollToViewRequested;
            }
            else if (extension is TextLineMarkerExtension)
            {
                Tuple<TextLineMarker, DocumentLine> data;
                if (markerExtensions.TryGetValue(extension, out data))
                    widget.TextEditor.Document.RemoveMarker(data.Item1);
            }
        }

        private void ClearExtensions()
        {
            foreach (var ex in widgetExtensions.Keys)
                ex.ScrollToViewRequested -= HandleScrollToViewRequested;
        }

        private void UpdateWidgetPositions()
        {
            foreach (var e in widgetExtensions)
            {
                int x, y;
                if (CalcWidgetPosition(e.Key, e.Value, out x, out y))
                    widget.TextEditor.TextArea.MoveTopLevelWidget(e.Value, x, y);
                else
                    e.Value.Hide();
            }
        }

        private bool CalcWidgetPosition(TopLevelWidgetExtension widgetExtension, Widget w, out int x, out int y)
        {
            DocumentLine line = widget.TextEditor.Document.GetLine(widgetExtension.Line);
            if (line == null)
            {
                x = y = 0;
                return false;
            }

            int lw, lh;
            var tmpWrapper = widget.TextEditor.TextViewMargin.GetLayout(line);
            tmpWrapper.Layout.GetPixelSize(out lw, out lh);
            if (tmpWrapper.IsUncached)
                tmpWrapper.Dispose();
            lh = (int)TextEditor.TextViewMargin.GetLineHeight(widgetExtension.Line);
            x = (int)widget.TextEditor.TextViewMargin.XOffset + lw + 4;
            y = (int)widget.TextEditor.LineToY(widgetExtension.Line);
            int lineStart = (int)widget.TextEditor.TextViewMargin.XOffset;
            var size = w.SizeRequest();

            switch (widgetExtension.HorizontalAlignment)
            {
                case HorizontalAlignment.LineLeft:
                    x = (int)widget.TextEditor.TextViewMargin.XOffset;
                    break;
                case HorizontalAlignment.LineRight:
                    x = lineStart + lw + 4;
                    break;
                case HorizontalAlignment.LineCenter:
                    x = lineStart + (lw - size.Width) / 2;
                    if (x < lineStart)
                        x = lineStart;
                    break;
                case HorizontalAlignment.Left:
                    x = 0;
                    break;
                case HorizontalAlignment.Right:
                    break;
                case HorizontalAlignment.Center:
                    break;
                case HorizontalAlignment.ViewLeft:
                    break;
                case HorizontalAlignment.ViewRight:
                    break;
                case HorizontalAlignment.ViewCenter:
                    break;
            }

            switch (widgetExtension.VerticalAlignment)
            {
                case VerticalAlignment.LineTop:
                    break; // the default
                case VerticalAlignment.LineBottom:
                    y += lh - size.Height;
                    break;
                case VerticalAlignment.LineCenter:
                    y = y + (lh - size.Height) / 2;
                    break;
                case VerticalAlignment.AboveLine:
                    y -= size.Height;
                    break;
                case VerticalAlignment.BelowLine:
                    y += lh;
                    break;
            }
            x += widgetExtension.OffsetX;
            y += widgetExtension.OffsetY;

            //We don't want Widget to appear outside TextArea(cut off)...
            x = Math.Max(0, x);
            y = Math.Max(0, y);
            return true;
        }

        private CancellationTokenSource messageBubbleUpdateSource = new CancellationTokenSource();

        private void CancelMessageBubbleUpdate()
        {
            messageBubbleUpdateSource.Cancel();
            messageBubbleUpdateSource = new CancellationTokenSource();
        }

        protected virtual string ProcessSaveText(string text)
        {
            return text;
        }

        public override Task Save(FileSaveInformation fileSaveInformation)
        {
            return Save(fileSaveInformation.FileName, fileSaveInformation.Encoding ?? encoding);
        }

        public async Task Save(string fileName, Encoding fileEncoding)
        {
            if (widget.HasMessageBar)
                return;
            if (fileEncoding != null)
            {
                encoding = fileEncoding;
                UpdateTextDocumentEncoding();
            }
            if (ContentName != fileName)
            {
                FileService.RequestFileEdit(fileName);
                writeAllowed = true;
                writeAccessChecked = true;
            }

            if (warnOverwrite)
            {
                if (string.Equals(fileName, ContentName, FilePath.PathComparison))
                {
                    string question =
                        $"This file {fileName} has been changed outside of {BrandingService.ApplicationName}. Are you sure you want to overwrite the file?";
                    if (MessageService.AskQuestion(question, AlertButton.Cancel, AlertButton.OverwriteFile) != AlertButton.OverwriteFile)
                        return;
                }
                warnOverwrite = false;
                widget.RemoveMessageBar();
                WorkbenchWindow.ShowNotification = false;
            }

            FileRegistry.SkipNextChange(fileName);
            try
            {
                object attributes = null;
                if (File.Exists(fileName))
                {
                    try
                    {
                        attributes = DesktopService.GetFileAttributes(fileName);
                        var fileAttributes = File.GetAttributes(fileName);
                        if (fileAttributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            var result = MessageService.AskQuestion(
                                ("Can't save file"),
                                ("The file was marked as read only. Should the file be overwritten?"),
                                AlertButton.Yes,
                                AlertButton.No);
                            if (result == AlertButton.Yes)
                            {
                                try
                                {
                                    File.SetAttributes(fileName, fileAttributes & ~FileAttributes.ReadOnly);
                                }
                                catch (Exception)
                                {
                                    MessageService.ShowError(("Error"),
                                                              ("Operation failed."));
                                    return;
                                }
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        LoggingService.LogWarning("Can't get file attributes", e);
                    }
                }
                try
                {
                    var writeEncoding = fileEncoding;
                    var writeBom = hadBom;
                    var writeText = ProcessSaveText(Document.Text);
                    if (writeEncoding == null)
                    {
                        if (encoding != null)
                        {
                            writeEncoding = encoding;
                        }
                        else
                        {
                            writeEncoding = Encoding.UTF8;
                            // Disabled. Shows up in the source control as diff, it's atm confusing for the users to see a change without
                            // changed files.
                            writeBom = false;
                            //						writeBom =!Mono.TextEditor.Utils.TextFileUtility.IsASCII (writeText);
                        }
                    }
                    await TextFileUtility.WriteTextAsync(fileName, writeText, writeEncoding, writeBom);
                    encoding = writeEncoding;
                }
                catch (InvalidEncodingException)
                {
                    var result = MessageService.AskQuestion(("Can't save file with current codepage."),
                        ("Some unicode characters in this file could not be saved with the current encoding.\nDo you want to resave this file as Unicode ?\nYou can choose another encoding in the 'save as' dialog."),
                        1,
                        AlertButton.Cancel,
                        new AlertButton(("Save as Unicode")));
                    if (result != AlertButton.Cancel)
                    {
                        hadBom = true;
                        encoding = Encoding.UTF8;
                        TextFileUtility.WriteText(fileName, Document.Text, fileEncoding, hadBom);
                    }
                    else
                    {
                        return;
                    }
                }
                lastSaveTimeUtc = File.GetLastWriteTimeUtc(fileName);
                try
                {
                    if (attributes != null)
                        DesktopService.SetFileAttributes(fileName, attributes);
                }
                catch (Exception e)
                {
                    LoggingService.LogError("Can't set file attributes", e);
                }
            }
            catch (UnauthorizedAccessException e)
            {
                LoggingService.LogError("Error while saving file", e);
                MessageService.ShowError(("Can't save file - access denied"), e.Message);
            }

            //			if (encoding != null)
            //				se.Buffer.SourceEncoding = encoding;
            //			TextFileService.FireCommitCountChanges (this);
            await Runtime.RunInMainThread(delegate
            {
                Document.FileName = ContentName = fileName;
                if (Document != null)
                {
                    UpdateMimeType(fileName);
                    Document.SetNotDirtyState();
                }
                IsDirty = false;
            });
        }

        public void InformLoadComplete()
        {
            /*
                Document.MimeType = mimeType;
                string text = null;
                if (content != null) {
                    text = Mono.TextEditor.Utils.TextFileUtility.GetText (content, out encoding, out hadBom);
                    text = ProcessLoadText (text);
                    Document.Text = text;
                }
                this.CreateDocumentParsedHandler ();
                RunFirstTimeFoldUpdate (text);
                */
            Document.InformLoadComplete();
        }

        public override Task LoadNew(Stream content, string mimeType)
        {
            throw new NotSupportedException("Moved to TextEditorViewContent.LoadNew.");
        }

        public override Task Load(FileOpenInformation fileOpenInformation)
        {
            return Load(fileOpenInformation.FileName, fileOpenInformation.Encoding, fileOpenInformation.IsReloadOperation);
        }

        protected virtual string ProcessLoadText(string text)
        {
            return text;
        }

        private void UpdateTextDocumentEncoding()
        {
            if (wrapper != null)
            {
                wrapper.Document.Encoding = encoding;
                wrapper.Document.UseBom = hadBom;
            }
        }

        public Task Load(string fileName, Encoding loadEncoding, bool reload = false)
        {
            var document = Document;
            if (document == null)
                return TaskUtil.Default<object>();
            document.TextReplaced -= OnTextReplaced;

            if (warnOverwrite)
            {
                warnOverwrite = false;
                widget.RemoveMessageBar();
                WorkbenchWindow.ShowNotification = false;
            }
            // Look for a mime type for which there is a syntax mode
            UpdateMimeType(fileName);
            string text;
            if (loadEncoding == null)
            {
                text = TextFileUtility.ReadAllText(fileName, out hadBom, out encoding);
            }
            else
            {
                encoding = loadEncoding;
                text = TextFileUtility.ReadAllText(fileName, loadEncoding, out hadBom);
            }
            text = ProcessLoadText(text);
            if (reload)
            {
                document.Replace(0, Document.TextLength, text);
                document.DiffTracker.Reset();
            }
            else
            {
                document.Text = text;
                document.DiffTracker.SetBaseDocument(Document.CreateDocumentSnapshot());
            }
            // TODO: Would be much easier if the view would be created after the containers.
            ContentName = fileName;
            lastSaveTimeUtc = File.GetLastWriteTimeUtc(ContentName);
            widget.TextEditor.Caret.Offset = 0;
            LoadExtensions();
            IsDirty = false;
            widget.TextEditor.TextArea.SizeAllocated += HandleTextEditorVAdjustmentChanged;
            widget.EnsureCorrectEolMarker(fileName);
            UpdateTextDocumentEncoding();
            document.TextReplaced += OnTextReplaced;
            return TaskUtil.Default<object>();
        }

        private void HandleTextEditorVAdjustmentChanged(object sender, EventArgs e)
        {
            widget.TextEditor.TextArea.SizeAllocated -= HandleTextEditorVAdjustmentChanged;
            LoadSettings();
        }

        internal void LoadSettings()
        {
            FileSettingsStore.Settings settings;
            if (widget == null || string.IsNullOrEmpty(ContentName) || !FileSettingsStore.TryGetValue(ContentName, out settings))
                return;

            widget.TextEditor.Caret.Offset = settings.CaretOffset;
            widget.TextEditor.VAdjustment.Value = settings.vAdjustment;
            widget.TextEditor.HAdjustment.Value = settings.hAdjustment;

            //			foreach (var f in widget.TextEditor.Document.FoldSegments) {
            //				bool isFolded;
            //				if (settings.FoldingStates.TryGetValue (f.Offset, out isFolded))
            //					f.IsFolded = isFolded;
            //			}
        }

        internal void StoreSettings()
        {
            //			var foldingStates = new Dictionary<int, bool> ();
            //			foreach (var f in widget.TextEditor.Document.FoldSegments) {
            //				foldingStates [f.Offset] = f.IsFolded;
            //			}
            if (string.IsNullOrEmpty(ContentName))
                return;
            FileSettingsStore.Store(ContentName, new FileSettingsStore.Settings
            {
                CaretOffset = widget.TextEditor.Caret.Offset,
                vAdjustment = widget.TextEditor.VAdjustment.Value,
                hAdjustment = widget.TextEditor.HAdjustment.Value//,
                                                                 //				FoldingStates = foldingStates
            });
        }

        private bool warnOverwrite;
        private Encoding encoding;
        private bool hadBom;

        internal void ReplaceContent(string fileName, string content, Encoding enc)
        {
            if (warnOverwrite)
            {
                warnOverwrite = false;
                widget.RemoveMessageBar();
                WorkbenchWindow.ShowNotification = false;
            }
            UpdateMimeType(fileName);

            Document.Replace(0, Document.TextLength, content);
            Document.DiffTracker.Reset();
            encoding = enc;
            ContentName = fileName;
            LoadExtensions();
            IsDirty = false;
            UpdateTextDocumentEncoding();
            InformLoadComplete();
        }

        private void UpdateMimeType(string fileName)
        {
            Document.MimeType = DesktopService.GetMimeTypeForUri(fileName);
        }

        public Encoding SourceEncoding => encoding;

        public override void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;

            CancelMessageBubbleUpdate();
            ClearExtensions();
            FileRegistry.Remove(this);

            StoreSettings();

            Counters.LoadedEditors--;

            /*	if (messageBubbleHighlightPopupWindow != null)
                    messageBubbleHighlightPopupWindow.Destroy ();*/

            widget.TextEditor.Document.LineChanged -= HandleLineChanged;
            widget.TextEditor.Document.BeginUndo -= HandleBeginUndo;
            widget.TextEditor.Document.EndUndo -= HandleEndUndo;
            widget.TextEditor.Caret.PositionChanged -= HandlePositionChanged;
            widget.TextEditor.IconMargin.ButtonPressed -= OnIconButtonPress;
            widget.TextEditor.Document.TextReplacing -= OnTextReplacing;
            widget.TextEditor.Document.TextReplaced -= OnTextReplaced;
            widget.TextEditor.Document.ReadOnlyCheckDelegate = null;
            widget.TextEditor.TextViewMargin.LineShown -= TextViewMargin_LineShown;
            widget.TextEditor.TextArea.FocusOutEvent -= TextArea_FocusOutEvent;
            widget.TextEditor.Document.TextSet -= HandleDocumentTextSet;
            widget.TextEditor.Document.MimeTypeChanged -= Document_MimeTypeChanged;

            TextEditorService.FileExtensionAdded -= HandleFileExtensionAdded;
            TextEditorService.FileExtensionRemoved -= HandleFileExtensionRemoved;

            // This is not necessary but helps when tracking down memory leaks

            widget.Dispose();
            if (wrapper != null)
            {
                wrapper.Dispose();
                wrapper = null;
            }
            Project = null;
        }

        private bool CheckReadOnly(int line)
        {
            if (!writeAccessChecked && !IsUntitled)
            {
                writeAccessChecked = true;
                try
                {
                    writeAllowed = FileService.RequestFileEdit(ContentName);
                }
                catch (Exception e)
                {
                    IdeApp.Workbench.StatusBar.ShowError(e.Message);
                    writeAllowed = false;
                }
            }
            return IsUntitled || writeAllowed;
        }

        private string oldReplaceText;

        private void OnTextReplacing(object s, DocumentChangeEventArgs a)
        {
            oldReplaceText = a.RemovedText.Text;
        }

        private void OnTextReplaced(object s, DocumentChangeEventArgs a)
        {
            IsDirty = Document.IsDirty;

            var location = Document.OffsetToLocation(a.Offset);

            int i = 0, lines = 0;
            while (i != -1 && i < oldReplaceText.Length)
            {
                i = oldReplaceText.IndexOf('\n', i);
                if (i != -1)
                {
                    lines--;
                    i++;
                }
            }

            if (a.InsertedText != null)
            {
                i = 0;
                string sb = a.InsertedText.Text;
                while (i < sb.Length)
                {
                    if (sb[i] == '\n')
                        lines++;
                    i++;
                }
            }
            if (lines != 0)
                TextEditorService.NotifyLineCountChanged(this, location.Line, lines, location.Column);
        }

        private void OnIconButtonPress(object s, MarginMouseEventArgs args)
        {
            if (args.LineNumber < DocumentLocation.MinLine)
                return;

            if (args.TriggersContextMenu())
            {
                if (TextEditor.Caret.Line != args.LineNumber)
                {
                    TextEditor.Caret.Line = args.LineNumber;
                    TextEditor.Caret.Column = 1;
                }

                // TODO-AELIJ
                //IdeApp.CommandService.ShowContextMenu(
                //    TextEditor,
                //    args.RawEvent as Gdk.EventButton,
                //    WorkbenchWindow.ExtensionContext ?? AddinManager.AddinEngine,
                //    "/MonoDevelop/SourceEditor2/IconContextMenu/Editor");
            }
            //else if (args.Button == 1)
            //{
            //    if (!string.IsNullOrEmpty(Document.FileName))
            //    {
            //        if (args.LineSegment != null)
            //        {
            //            int column = TextEditor.Caret.Line == args.LineNumber ? TextEditor.Caret.Column : 1;

            //            lock (breakpoints)
            //                breakpoints.Toggle(Document.FileName, args.LineNumber, column);
            //        }
            //    }
            //}
        }

        #region IEditableTextBuffer
        public bool EnableUndo
        {
            get
            {
                if (widget == null)
                    return false;
                return /*this.TextEditor.PreeditOffset < 0 &&*/ Document.CanUndo && widget.EditorHasFocus;
            }
        }

        public void Undo()
        {
            // TODO: Maybe make this feature optional ?
            /*			if (this.Document.GetCurrentUndoDepth () > 0 && !this.Document.IsDirty) {
                            var buttonCancel = new AlertButton (GettextCatalog.GetString ("Don't Undo")); 
                            var buttonOk = new AlertButton (GettextCatalog.GetString ("Undo")); 
                            var question = GettextCatalog.GetString ("You are about to undo past the last point this file was saved. Do you want to do this?");
                            var result = MessageService.GenericAlert (Gtk.Stock.DialogWarning, GettextCatalog.GetString ("Warning"),
                                                                      question, 1, buttonCancel, buttonOk);
                            if (result != buttonOk)
                                return;
                        }*/
            if (MiscActions.CancelPreEditMode(TextEditor.GetTextEditorData()))
                return;
            MiscActions.Undo(TextEditor.GetTextEditorData());
        }

        public bool EnableRedo
        {
            get
            {
                if (widget == null)
                    return false;
                return /*this.TextEditor.PreeditOffset < 0 && */ Document.CanRedo && widget.EditorHasFocus;
            }
        }

        public void SetCaretTo(int line, int column)
        {
            Document.RunWhenLoaded(() =>
            {
                PrepareToSetCaret(line, column);
                widget.TextEditor.SetCaretTo(line, column, true);
            });
        }

        public void SetCaretTo(int line, int column, bool highlight)
        {
            Document.RunWhenLoaded(() =>
            {
                PrepareToSetCaret(line, column);
                widget.TextEditor.SetCaretTo(line, column, highlight);
            });
        }

        public void SetCaretTo(int line, int column, bool highlight, bool centerCaret)
        {
            Document.RunWhenLoaded(() =>
            {
                PrepareToSetCaret(line, column);
                widget.TextEditor.SetCaretTo(line, column, highlight, centerCaret);
            });
        }

        protected virtual void PrepareToSetCaret(int line, int column)
        {

        }

        public void Redo()
        {
            if (MiscActions.CancelPreEditMode(TextEditor.GetTextEditorData()))
                return;
            MiscActions.Redo(TextEditor.GetTextEditorData());
        }

        public IDisposable OpenUndoGroup()
        {
            return Document.OpenUndoGroup();
        }

        public string SelectedText
        {
            get
            {
                return TextEditor.IsSomethingSelected ? Document.GetTextAt(TextEditor.SelectionRange) : "";
            }
            set
            {
                TextEditor.DeleteSelectedText();
                var offset = TextEditor.Caret.Offset;
                int length = TextEditor.Insert(offset, value);
                TextEditor.SelectionRange = new TextSegment(offset, length);
            }
        }

        protected virtual void OnCaretPositionSet(EventArgs args)
        {
            CaretPositionSet?.Invoke(this, args);
        }

        public event EventHandler CaretPositionSet;

        public bool HasInputFocus => TextEditor.HasFocus;

        public void RunWhenLoaded(Action action)
        {
            Document.RunWhenLoaded(action);
        }

        public void RunWhenRealized(Action action)
        {
            Document.RunWhenRealized(action);
        }
        #endregion

        public int CursorPosition
        {
            get
            {
                return TextEditor.Caret.Offset;
            }
            set
            {
                TextEditor.Caret.Offset = value;
            }
        }

        #region ITextFile
        public FilePath Name => ContentName ?? UntitledName;

        public string Text
        {
            get
            {
                return widget.TextEditor.Document.Text;
            }
            set
            {
                IsDirty = true;
                var document = widget.TextEditor.Document;
                document.Replace(0, document.TextLength, value);
            }
        }

        public int Length => widget.TextEditor.Document.TextLength;

        public bool WarnOverwrite
        {
            get
            {
                return warnOverwrite;
            }
            set
            {
                warnOverwrite = value;
            }
        }

        public string GetText(int startPosition, int endPosition)
        {
            var doc = widget.TextEditor.Document;
            if (startPosition < 0 || endPosition < 0 || startPosition > endPosition || startPosition >= doc.TextLength)
                return "";
            var length = Math.Min(endPosition - startPosition, doc.TextLength - startPosition);
            return doc.GetTextAt(startPosition, length);
        }

        public char GetCharAt(int position)
        {
            return widget.TextEditor.Document.GetCharAt(position);
        }

        public int GetPositionFromLineColumn(int line, int column)
        {
            return widget.TextEditor.Document.LocationToOffset(new DocumentLocation(line, column));
        }

        public void GetLineColumnFromPosition(int position, out int line, out int column)
        {
            var location = widget.TextEditor.Document.OffsetToLocation(position);
            line = location.Line;
            column = location.Column;
        }
        #endregion

        #region IEditableTextFile
        public int InsertText(int position, string text)
        {
            return widget.TextEditor.Insert(position, text);
        }

        public void DeleteText(int position, int length)
        {
            widget.TextEditor.TextArea.Remove(position, length);
        }
        #endregion

        #region IBookmarkBuffer

        private DocumentLine GetLine(int position)
        {
            var location = Document.OffsetToLocation(position);
            return Document.GetLine(location.Line);
        }

        public void SetBookmarked(int position, bool mark)
        {
            var line = GetLine(position);
            if (line != null && line.IsBookmarked != mark)
            {
                int lineNumber = widget.TextEditor.Document.OffsetToLineNumber(line.Offset);
                line.IsBookmarked = mark;
                widget.TextEditor.Document.RequestUpdate(new LineUpdate(lineNumber));
                widget.TextEditor.Document.CommitDocumentUpdate();
            }
        }

        public bool IsBookmarked(int position)
        {
            var line = GetLine(position);
            return line != null && line.IsBookmarked;
        }

        public void PrevBookmark()
        {
            TextEditor.RunAction(BookmarkActions.GotoPrevious);
        }

        public void NextBookmark()
        {
            TextEditor.RunAction(BookmarkActions.GotoNext);
        }

        public void ClearBookmarks()
        {
            TextEditor.RunAction(BookmarkActions.ClearAll);
        }
        #endregion

        #region IClipboardHandler
        public bool EnableCut => !widget.SearchWidgetHasFocus;

        public bool EnableCopy => EnableCut;

        public bool EnablePaste => EnableCut;

        public bool EnableDelete => EnableCut;

        public bool EnableSelectAll => EnableCut;

        public void Cut()
        {
            TextEditor.RunAction(ClipboardActions.Cut);
        }

        public void Copy()
        {
            TextEditor.RunAction(ClipboardActions.Copy);
        }

        public void Paste()
        {
            TextEditor.RunAction(ClipboardActions.Paste);
        }

        public void Delete()
        {
            if (TextEditor.IsSomethingSelected)
            {
                TextEditor.DeleteSelectedText();
            }
            else
            {
                TextEditor.RunAction(DeleteActions.Delete);
            }
        }

        public void SelectAll()
        {
            TextEditor.RunAction(SelectionActions.SelectAll);
        }
        #endregion

        #region ICompletionWidget

        public CodeCompletionContext CurrentCodeCompletionContext => CreateCodeCompletionContext(TextEditor.Caret.Offset);

        public int TextLength => Document.TextLength;

        public int SelectedLength
        {
            get
            {
                if (TextEditor.IsSomethingSelected)
                {
                    if (TextEditor.MainSelection.SelectionMode == Mono.TextEditor.SelectionMode.Block)
                        return Math.Abs(TextEditor.MainSelection.Anchor.Column - TextEditor.MainSelection.Lead.Column);
                    return TextEditor.SelectionRange.Length;
                }
                return 0;
            }
        }
        //		public string GetText (int startOffset, int endOffset)
        //		{
        //			return this.widget.TextEditor.Document.Buffer.GetTextAt (startOffset, endOffset - startOffset);
        //		}
        public char GetChar(int offset)
        {
            return Document.GetCharAt(offset);
        }

        public int CaretOffset
        {
            get
            {
                return TextEditor.Caret.Offset;
            }
            set
            {
                TextEditor.Caret.Offset = value;
                TextEditor.ScrollToCaret();
            }
        }

        public Style GtkStyle => widget.Vbox.Style;

        public void Replace(int offset, int count, string text)
        {
            widget.TextEditor.GetTextEditorData().Replace(offset, count, text);
        }

        public CodeCompletionContext CreateCodeCompletionContext(int triggerOffset)
        {
            var result = new CodeCompletionContext();
            if (widget == null)
                return result;
            var editor = widget.TextEditor;
            if (editor == null)
                return result;
            result.TriggerOffset = triggerOffset;
            var loc = editor.Caret.Location;
            result.TriggerLine = loc.Line;
            result.TriggerLineOffset = loc.Column - 1;
            var p = widget.TextEditor.LocationToPoint(loc);
            int tx, ty;
            var parentWindow = editor.ParentWindow;
            if (parentWindow != null)
            {
                parentWindow.GetOrigin(out tx, out ty);
            }
            else
            {
                tx = ty = 0;
            }
            tx += editor.Allocation.X + p.X;
            ty += editor.Allocation.Y + p.Y + (int)editor.LineHeight;

            result.TriggerXCoord = tx;
            result.TriggerYCoord = ty;
            result.TriggerTextHeight = (int)TextEditor.GetLineHeight(loc.Line);
            return result;
        }

        public Point DocumentToScreenLocation(DocumentLocation location)
        {
            var p = widget.TextEditor.LocationToPoint(location);
            int tx, ty;
            widget.Vbox.ParentWindow.GetOrigin(out tx, out ty);
            tx += widget.TextEditor.Allocation.X + p.X;
            ty += widget.TextEditor.Allocation.Y + p.Y + (int)TextEditor.LineHeight;
            return new Point(tx, ty);
        }

        public CodeTemplateContext GetCodeTemplateContext()
        {
            return TextEditor.GetTemplateContext();
        }

        public string GetCompletionText(CodeCompletionContext ctx)
        {
            if (ctx == null)
                return null;
            int min = Math.Min(ctx.TriggerOffset, TextEditor.Caret.Offset);
            int max = Math.Max(ctx.TriggerOffset, TextEditor.Caret.Offset);
            return Document.GetTextBetween(min, max);
        }

        public void SetCompletionText(CodeCompletionContext ctx, string partialWord, string completeWord)
        {
            if (ctx == null)
                throw new ArgumentNullException(nameof(ctx));
            if (completeWord == null)
                throw new ArgumentNullException(nameof(completeWord));
            SetCompletionText(ctx, partialWord, completeWord, completeWord.Length);
        }

        public static void SetCompletionText(TextEditorData data, CodeCompletionContext ctx, string partialWord, string completeWord, int wordOffset)
        {
            if (data?.Document == null)
                return;

            int triggerOffset = ctx.TriggerOffset;
            int length = String.IsNullOrEmpty(partialWord) ? 0 : partialWord.Length;

            // for named arguments invoke(arg:<Expr>);
            if (completeWord.EndsWith(":", StringComparison.Ordinal))
            {
                if (data.GetCharAt(triggerOffset + length) == ':')
                    length++;
            }

            bool blockMode = false;
            if (data.IsSomethingSelected)
            {
                blockMode = data.MainSelection.SelectionMode == Mono.TextEditor.SelectionMode.Block;
                if (blockMode)
                {
                    data.Caret.PreserveSelection = true;
                    triggerOffset = data.Caret.Offset - length;
                }
                else
                {
                    if (data.SelectionRange.Offset < ctx.TriggerOffset)
                        triggerOffset = ctx.TriggerOffset - data.SelectionRange.Length;
                    data.DeleteSelectedText();
                }
                length = 0;
            }

            // | in the completion text now marks the caret position
            int idx = completeWord.IndexOf('|');
            if (idx >= 0)
            {
                completeWord = completeWord.Remove(idx, 1);
            }

            triggerOffset += data.EnsureCaretIsNotVirtual();
            if (blockMode)
            {
                using (data.OpenUndoGroup())
                {

                    int minLine = data.MainSelection.MinLine;
                    int maxLine = data.MainSelection.MaxLine;
                    int column = triggerOffset - data.Document.GetLineByOffset(triggerOffset).Offset;
                    for (int lineNumber = minLine; lineNumber <= maxLine; lineNumber++)
                    {
                        DocumentLine lineSegment = data.Document.GetLine(lineNumber);
                        if (lineSegment == null)
                            continue;
                        int offset = lineSegment.Offset + column;
                        data.Replace(offset, length, completeWord);
                    }
                    int minColumn = Math.Min(data.MainSelection.Anchor.Column, data.MainSelection.Lead.Column);
                    data.MainSelection = data.MainSelection.WithRange(
                        new DocumentLocation(data.Caret.Line == minLine ? maxLine : minLine, minColumn),
                        data.Caret.Location
                    );

                    data.Document.CommitMultipleLineUpdate(data.MainSelection.MinLine, data.MainSelection.MaxLine);
                    data.Caret.PreserveSelection = false;
                }
            }
            else
            {
                data.Replace(triggerOffset, length, completeWord);
            }

            data.Document.CommitLineUpdate(data.Caret.Line);
            if (idx >= 0)
                data.Caret.Offset = triggerOffset + idx;

        }

        public void SetCompletionText(CodeCompletionContext ctx, string partialWord, string completeWord, int wordOffset)
        {
            var data = GetTextEditorData();
            if (data == null)
                return;
            using (data.OpenUndoGroup())
            {
                SetCompletionText(data, ctx, partialWord, completeWord, wordOffset);
            }
        }

        internal void FireCompletionContextChanged()
        {
            CompletionContextChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler CompletionContextChanged;
        #endregion

        #region ISplittable
        public bool EnableSplitHorizontally => !EnableUnsplit;

        public bool EnableSplitVertically => !EnableUnsplit;

        public bool EnableUnsplit => widget.IsSplitted;

        public void SplitHorizontally()
        {
            widget.Split(false);
        }

        public void SplitVertically()
        {
            widget.Split(true);
        }

        public void Unsplit()
        {
            widget.Unsplit();
        }

        public void SwitchWindow()
        {
            widget.SwitchWindow();
        }

        #endregion

        #region IFoldable
        public void ToggleAllFoldings()
        {
            FoldActions.ToggleAllFolds(TextEditor.GetTextEditorData());
            widget.TextEditor.ScrollToCaret();
        }

        public void FoldDefinitions()
        {
            bool toggle = true;

            foreach (var segment in Document.FoldSegments)
            {
                if (segment.FoldingType == FoldingType.TypeMember || segment.FoldingType == FoldingType.Comment)
                    if (segment.IsFolded)
                        toggle = false;
            }


            foreach (var segment in Document.FoldSegments)
            {
                if (segment.FoldingType == FoldingType.TypeDefinition)
                {
                    segment.IsFolded = false;
                }
                if (segment.FoldingType == FoldingType.TypeMember || segment.FoldingType == FoldingType.Comment)
                    segment.IsFolded = toggle;
            }

            widget.TextEditor.Caret.MoveCaretBeforeFoldings();
            Document.RequestUpdate(new UpdateAll());
            Document.CommitDocumentUpdate();
            widget.TextEditor.GetTextEditorData().RaiseUpdateAdjustmentsRequested();
            widget.TextEditor.ScrollToCaret();
        }

        public void ToggleFolding()
        {
            FoldActions.ToggleFold(TextEditor.GetTextEditorData());
            widget.TextEditor.ScrollToCaret();
        }
        #endregion

        #region IPrintable

        public bool CanPrint => true;

        public void PrintDocument(PrintingSettings settings)
        {
            RunPrintOperation(PrintOperationAction.PrintDialog, settings);
        }

        public void PrintPreviewDocument(PrintingSettings settings)
        {
            RunPrintOperation(PrintOperationAction.Preview, settings);
        }

        private void RunPrintOperation(PrintOperationAction action, PrintingSettings settings)
        {
            var op = new SourceEditorPrintOperation(TextEditor.Document, Name);

            if (settings.PrintSettings != null)
                op.PrintSettings = settings.PrintSettings;
            if (settings.PageSetup != null)
                op.DefaultPageSetup = settings.PageSetup;

            //FIXME: implement in-place preview
            //op.Preview += HandleOpPreview;

            //FIXME: implement async on platforms that support it
            var result = op.Run(action, IdeApp.Workbench.RootWindow);

            if (result == PrintOperationResult.Apply)
                settings.PrintSettings = op.PrintSettings;
            else if (result == PrintOperationResult.Error)
                //FIXME: can't show more details, GTK# GetError binding is bad
                MessageService.ShowError(("Print operation failed."));
        }

        #endregion

        #region IZoomable
        bool IZoomable.EnableZoomIn => TextEditor.Options.CanZoomIn;

        bool IZoomable.EnableZoomOut => TextEditor.Options.CanZoomOut;

        bool IZoomable.EnableZoomReset => TextEditor.Options.CanResetZoom;

        void IZoomable.ZoomIn()
        {
            TextEditor.Options.ZoomIn();
        }

        void IZoomable.ZoomOut()
        {
            TextEditor.Options.ZoomOut();
        }

        void IZoomable.ZoomReset()
        {
            TextEditor.Options.ZoomReset();
        }

        #region ISupportsProjectReload implementaion

        public override ProjectReloadCapability ProjectReloadCapability => ProjectReloadCapability.Full;

        #endregion

        #endregion

        public TextEditorData GetTextEditorData()
        {
            var editor = TextEditor;
            return editor?.GetTextEditorData();
        }

        public void InsertTemplate(CodeTemplate template, TextEditor editor, DocumentContext context)
        {
            TextEditor.InsertTemplate(template, editor, context);
        }

        protected override object OnGetContent(Type type)
        {
            if (type == typeof(TextEditorData))
                return TextEditor.GetTextEditorData();
            return base.OnGetContent(type);
        }

        #region widget command handlers
        [CommandHandler(SearchCommands.EmacsFindNext)]
        public void EmacsFindNext()
        {
            widget.EmacsFindNext();
        }

        [CommandHandler(SearchCommands.EmacsFindPrevious)]
        public void EmacsFindPrevious()
        {
            widget.EmacsFindPrevious();
        }

        [CommandHandler(SearchCommands.Find)]
        public void ShowSearchWidget()
        {
            widget.ShowSearchWidget();
        }

        [CommandHandler(SearchCommands.Replace)]
        public void ShowReplaceWidget()
        {
            widget.ShowReplaceWidget();
        }

        [CommandUpdateHandler(SearchCommands.UseSelectionForFind)]
        protected void OnUpdateUseSelectionForFind(CommandInfo info)
        {
            widget.OnUpdateUseSelectionForFind(info);
        }

        [CommandHandler(SearchCommands.UseSelectionForFind)]
        public void UseSelectionForFind()
        {
            widget.UseSelectionForFind();
        }

        [CommandUpdateHandler(SearchCommands.UseSelectionForReplace)]
        protected void OnUpdateUseSelectionForReplace(CommandInfo info)
        {
            widget.OnUpdateUseSelectionForReplace(info);
        }

        [CommandHandler(SearchCommands.UseSelectionForReplace)]
        public void UseSelectionForReplace()
        {
            widget.UseSelectionForReplace();
        }

        [CommandHandler(SearchCommands.GotoLineNumber)]
        public void ShowGotoLineNumberWidget()
        {
            widget.ShowGotoLineNumberWidget();
        }

        [CommandHandler(SearchCommands.FindNext)]
        public SearchResult FindNext()
        {
            return widget.FindNext();
        }

        [CommandUpdateHandler(SearchCommands.FindNext)]
        [CommandUpdateHandler(SearchCommands.FindPrevious)]
        private void UpdateFindNextAndPrev(CommandInfo cinfo)
        {
            cinfo.Enabled = !string.IsNullOrEmpty(SearchAndReplaceOptions.SearchPattern);
        }

        [CommandHandler(SearchCommands.FindPrevious)]
        public SearchResult FindPrevious()
        {
            return widget.FindPrevious();
        }

        [CommandHandler(SearchCommands.FindNextSelection)]
        public SearchResult FindNextSelection()
        {
            return widget.FindNextSelection();
        }

        [CommandHandler(SearchCommands.FindPreviousSelection)]
        public SearchResult FindPreviousSelection()
        {
            return widget.FindPreviousSelection();
        }
        
        #endregion

        private TextDocumentWrapper wrapper;
        IReadonlyTextDocument ITextEditorImpl.Document
        {
            get
            {
                if (wrapper == null)
                {
                    wrapper = new TextDocumentWrapper(widget.TextEditor.Document);
                    if (encoding != null)
                    {
                        wrapper.Document.Encoding = encoding;
                        wrapper.Document.UseBom = hadBom;
                    }
                    else
                    {
                        wrapper.Document.Encoding = Encoding.UTF8;
                        wrapper.Document.UseBom = true;
                    }
                }
                return wrapper;
            }
        }

        event EventHandler ITextEditorImpl.SelectionChanged
        {
            add
            {
                TextEditor.SelectionChanged += value;
            }
            remove
            {
                TextEditor.SelectionChanged -= value;
            }
        }

        event EventHandler<MouseMovedEventArgs> ITextEditorImpl.MouseMoved
        {
            add
            {
                TextEditor.BeginHover += value;
            }
            remove
            {
                TextEditor.BeginHover -= value;
            }
        }

        event EventHandler ITextEditorImpl.VAdjustmentChanged
        {
            add
            {
                TextEditor.VAdjustment.ValueChanged += value;
            }
            remove
            {
                TextEditor.VAdjustment.ValueChanged -= value;
            }
        }

        event EventHandler ITextEditorImpl.HAdjustmentChanged
        {
            add
            {
                TextEditor.HAdjustment.ValueChanged += value;
            }
            remove
            {
                TextEditor.HAdjustment.ValueChanged -= value;
            }
        }

        public event EventHandler CaretPositionChanged;
        private bool hasCaretPositionChanged;
        protected virtual void OnCaretPositionChanged(EventArgs e)
        {
            if (widget.TextEditor.Document.IsInAtomicUndo)
            {
                hasCaretPositionChanged = true;
                return;
            }
            CaretPositionChanged?.Invoke(this, e);
        }

        public event EventHandler BeginAtomicUndoOperation;

        protected virtual void OnBeginUndo(EventArgs e)
        {
            hasCaretPositionChanged = false;
            BeginAtomicUndoOperation?.Invoke(this, e);
        }

        public event EventHandler EndAtomicUndoOperation;

        protected virtual void OnEndUndo(EventArgs e)
        {
            EndAtomicUndoOperation?.Invoke(this, e);
            if (hasCaretPositionChanged)
            {
                OnCaretPositionChanged(e);
                hasCaretPositionChanged = false;
            }
        }

        void ITextEditorImpl.SetSelection(int anchorOffset, int leadOffset)
        {
            TextEditor.SetSelection(anchorOffset, leadOffset);
        }

        void ITextEditorImpl.ClearSelection()
        {
            TextEditor.ClearSelection();
        }

        void ITextEditorImpl.CenterToCaret()
        {
            TextEditor.CenterToCaret();
        }

        void ITextEditorImpl.StartCaretPulseAnimation()
        {
            TextEditor.StartCaretPulseAnimation();
        }

        int ITextEditorImpl.EnsureCaretIsNotVirtual()
        {
            return TextEditor.GetTextEditorData().EnsureCaretIsNotVirtual();
        }

        void ITextEditorImpl.FixVirtualIndentation()
        {
            TextEditor.GetTextEditorData().FixVirtualIndentation();
        }

        private bool viewContentCreated;
        object ITextEditorImpl.CreateNativeControl()
        {
            if (!viewContentCreated)
            {
                viewContentCreated = true;
                Document.InformRealizedComplete();
            }
            return widget?.Vbox;
        }

        string ITextEditorImpl.FormatString(int offset, string code)
        {
            return TextEditor.GetTextEditorData().FormatString(offset, code);
        }

        void ITextEditorImpl.StartInsertionMode(InsertionModeOptions insertionModeOptions)
        {
            var mode = new InsertionCursorEditMode(TextEditor, insertionModeOptions.InsertionPoints.Select(ip => new InsertionPoint(
              new DocumentLocation(ip.Location.Line, ip.Location.Column),
              (NewLineInsertion)ip.LineBefore,
              (NewLineInsertion)ip.LineAfter
          )).ToList());
            if (mode.InsertionPoints.Count == 0)
            {
                return;
            }
            var helpWindow = new InsertionCursorLayoutModeHelpWindow { TitleText = insertionModeOptions.Operation };
            mode.HelpWindow = helpWindow;
            mode.CurIndex = insertionModeOptions.FirstSelectedInsertionPoint;
            mode.StartMode();
            mode.Exited += delegate (object s, InsertionCursorEventArgs iCArgs)
            {
                insertionModeOptions.ModeExitedAction?.Invoke(new Ide.Editor.InsertionCursorEventArgs(iCArgs.Success,
                    iCArgs.Success ?
                        new Ide.Editor.InsertionPoint(
                            new Ide.Editor.DocumentLocation(iCArgs.InsertionPoint.Location.Line, iCArgs.InsertionPoint.Location.Column),
                            (Ide.Editor.NewLineInsertion)iCArgs.InsertionPoint.LineBefore,
                            (Ide.Editor.NewLineInsertion)iCArgs.InsertionPoint.LineAfter)
                        : null
                ));
            };
        }

        void ITextEditorImpl.StartTextLinkMode(TextLinkModeOptions textLinkModeOptions)
        {
            var convertedLinks = new List<TextLink>();
            foreach (var link in textLinkModeOptions.Links)
            {
                var convertedLink = new TextLink(link.Name)
                {
                    IsEditable = link.IsEditable,
                    IsIdentifier = link.IsIdentifier
                };
                var func = link.GetStringFunc;
                if (func != null)
                {
                    convertedLink.GetStringFunc = arg => new ListDataProviderWrapper(func(arg));
                }
                foreach (var segment in link.Links)
                {
                    convertedLink.AddLink(new TextSegment(segment.Offset, segment.Length));
                }
                convertedLinks.Add(convertedLink);
            }

            var tle = new TextLinkEditMode(TextEditor, 0, convertedLinks) { SetCaretPosition = false };
            if (tle.ShouldStartTextLinkMode)
            {
                tle.OldMode = TextEditor.CurrentMode;
                if (textLinkModeOptions.ModeExitedAction != null)
                {
                    tle.Cancel += (sender, e) => textLinkModeOptions.ModeExitedAction(new TextLinkModeEventArgs(false));
                    tle.Exited += (sender, e) =>
                    {
                        for (int i = 0; i < convertedLinks.Count; i++)
                        {
                            textLinkModeOptions.Links[i].CurrentText = convertedLinks[i].CurrentText;
                        }
                        textLinkModeOptions.ModeExitedAction(new TextLinkModeEventArgs(true));

                    };
                }
                var undoOperation = TextEditor.OpenUndoGroup();
                tle.Exited += (sender, e) => undoOperation.Dispose();
                tle.StartMode();
                TextEditor.CurrentMode = tle;
            }
        }

        Ide.Editor.DocumentLocation ITextEditorImpl.PointToLocation(double xp, double yp, bool endAtEol)
        {
            var pt = TextEditor.PointToLocation(xp, yp);
            return new Ide.Editor.DocumentLocation(pt.Line, pt.Column);
        }

        Xwt.Point ITextEditorImpl.LocationToPoint(int line, int column)
        {
            var p = TextEditor.LocationToPoint(line, column);
            return new Xwt.Point(p.X, p.Y);
        }

        void ITextEditorImpl.AddMarker(IDocumentLine line, ITextLineMarker lineMarker)
        {
            var textLineMarker = lineMarker as TextLineMarker;
            if (textLineMarker == null)
                throw new InvalidOperationException("Tried to add an incompatible text marker. Use the MarkerHost to create compatible ones.");

            if (lineMarker is IUnitTestMarker)
            {
                var actionMargin = TextEditor.ActionMargin;
                if (actionMargin != null)
                {
                    actionMargin.IsVisible = true;
                }
            }

            TextEditor.Document.AddMarker(((DocumentLineWrapper)line).Line, textLineMarker);
        }

        void ITextEditorImpl.RemoveMarker(ITextLineMarker lineMarker)
        {
            var textLineMarker = lineMarker as TextLineMarker;
            if (textLineMarker == null)
                throw new InvalidOperationException("Tried to add an incompatible text marker.");
            TextEditor.Document.RemoveMarker(textLineMarker);
        }

        IEnumerable<ITextLineMarker> ITextEditorImpl.GetLineMarkers(IDocumentLine line)
        {
            return ((DocumentLineWrapper)line).Line.Markers.OfType<ITextLineMarker>();
        }

        IEnumerable<ITextSegmentMarker> ITextEditorImpl.GetTextSegmentMarkersAt(ISegment segment)
        {
            return TextEditor.Document.GetTextSegmentMarkersAt(new TextSegment(segment.Offset, segment.Length)).OfType<ITextSegmentMarker>();
        }

        IEnumerable<ITextSegmentMarker> ITextEditorImpl.GetTextSegmentMarkersAt(int offset)
        {
            return TextEditor.Document.GetTextSegmentMarkersAt(offset).OfType<ITextSegmentMarker>();
        }

        void ITextEditorImpl.AddMarker(ITextSegmentMarker marker)
        {
            var textSegmentMarker = marker as TextSegmentMarker;
            if (textSegmentMarker == null)
                throw new InvalidOperationException("Tried to add an incompatible text marker. Use the MarkerHost to create compatible ones.");
            TextEditor.Document.AddMarker(textSegmentMarker);
        }

        bool ITextEditorImpl.RemoveMarker(ITextSegmentMarker marker)
        {
            var textSegmentMarker = marker as TextSegmentMarker;
            if (textSegmentMarker == null)
                throw new InvalidOperationException("Tried to remove an incompatible text marker.");
            return TextEditor.Document.RemoveMarker(textSegmentMarker);
        }

        IFoldSegment ITextEditorImpl.CreateFoldSegment(int offset, int length, bool isFolded)
        {
            return new FoldSegmentWrapper(TextEditor.Document, "...", offset, length, FoldingType.None) { IsFolded = isFolded };
        }

        void ITextEditorImpl.SetFoldings(IEnumerable<IFoldSegment> foldings)
        {
            if (isDisposed || !TextEditor.Options.ShowFoldMargin)
                return;
            TextEditor.Document.UpdateFoldSegments(foldings.Cast<FoldSegment>().ToList(), true);
        }

        IEnumerable<IFoldSegment> ITextEditorImpl.GetFoldingsContaining(int offset)
        {
            return TextEditor.Document.GetFoldingsFromOffset(offset).Cast<IFoldSegment>();
        }

        IEnumerable<IFoldSegment> ITextEditorImpl.GetFoldingsIn(int offset, int length)
        {
            return TextEditor.Document.GetFoldingContaining(offset, length).Cast<IFoldSegment>();
        }

        ITextEditorOptions ITextEditorImpl.Options
        {
            get
            {
                return ((StyledSourceEditorOptions)TextEditor.Options).OptionsCore;
            }
            set
            {
                ((StyledSourceEditorOptions)TextEditor.Options).OptionsCore = value;
            }
        }

        Ide.Editor.DocumentLocation ITextEditorImpl.CaretLocation
        {
            get
            {
                var loc = TextEditor.Caret.Location;
                return new Ide.Editor.DocumentLocation(loc.Line, loc.Column);
            }
            set
            {
                TextEditor.Caret.Location = new DocumentLocation(value.Line, value.Column);
                TextEditor.ScrollToCaret();
            }
        }

        bool ITextEditorImpl.IsSomethingSelected => TextEditor.IsSomethingSelected;

        SelectionMode ITextEditorImpl.SelectionMode => (SelectionMode)TextEditor.SelectionMode;

        ISegment ITextEditorImpl.SelectionRange
        {
            get
            {
                var range = TextEditor.SelectionRange;
                return Core.Text.TextSegment.FromBounds(range.Offset, range.EndOffset);
            }
            set
            {
                TextEditor.SelectionRange = new TextSegment(value.Offset, value.Length);
            }
        }

        int ITextEditorImpl.SelectionAnchorOffset
        {
            get
            {
                return TextEditor.SelectionAnchor;
            }
            set
            {
                TextEditor.SelectionAnchor = value;
            }
        }

        int ITextEditorImpl.SelectionLeadOffset
        {
            get
            {
                return TextEditor.SelectionLead;
            }
            set
            {
                TextEditor.SelectionLead = value;
            }
        }

        bool ITextEditorImpl.SuppressTooltips
        {
            get
            {
                return TextEditor.GetTextEditorData().SuppressTooltips;
            }
            set
            {
                if (value)
                    TextEditor.HideTooltip();
                TextEditor.GetTextEditorData().SuppressTooltips = value;
            }
        }

        DocumentRegion ITextEditorImpl.SelectionRegion
        {
            get
            {
                return new DocumentRegion(
                    TextEditor.MainSelection.Start.Line,
                    TextEditor.MainSelection.Start.Column,
                    TextEditor.MainSelection.End.Line,
                    TextEditor.MainSelection.End.Column
                );
            }
            set
            {
                TextEditor.MainSelection = new Selection(
                    value.BeginLine,
                    value.BeginColumn,
                    value.EndLine,
                    value.EndColumn
                );
            }
        }

        IEditorActionHost ITextEditorImpl.Actions => this;

        double ITextEditorImpl.LineHeight => TextEditor.GetTextEditorData().LineHeight;

        ITextMarkerFactory ITextEditorImpl.TextMarkerFactory => this;

        EditMode ITextEditorImpl.EditMode
        {
            get
            {
                if (TextEditor.CurrentMode is TextLinkEditMode)
                    return EditMode.TextLink;
                if (TextEditor.CurrentMode is InsertionCursorEditMode)
                    return EditMode.CursorInsertion;
                return EditMode.Edit;
            }
        }

        string ITextEditorImpl.GetVirtualIndentationString(int lineNumber)
        {
            if (!TextEditor.GetTextEditorData().HasIndentationTracker)
                return TextEditor.GetLineIndent(lineNumber);
            return TextEditor.GetTextEditorData().IndentationTracker.GetIndentationString(lineNumber, 1);
        }

        void ITextEditorImpl.SetIndentationTracker(IndentationTracker indentationTracker)
        {
            TextEditor.GetTextEditorData().IndentationTracker = indentationTracker != null ? new IndentationTrackerWrapper(TextEditor.GetTextEditorData(), wrapper, indentationTracker) : null;
        }

        void ITextEditorImpl.SetSelectionSurroundingProvider(SelectionSurroundingProvider surroundingProvider)
        {
            TextEditor.GetTextEditorData().SelectionSurroundingProvider = surroundingProvider != null ? new SelectionSurroundingProviderWrapper(surroundingProvider) : null;
        }

        void ITextEditorImpl.SetTextPasteHandler(TextPasteHandler textPasteHandler)
        {
            var data = TextEditor.GetTextEditorData();
            ((TextPasteHandlerWrapper)data.TextPasteHandler)?.Dispose();
            if (textPasteHandler == null)
            {
                data.TextPasteHandler = null;
                return;
            }
            data.TextPasteHandler = new TextPasteHandlerWrapper(data, textPasteHandler);
        }

        internal Stack<EditSession> EditSessions = new Stack<EditSession>();

        public EditSession CurrentSession => EditSessions.Any() ? EditSessions.Peek() : null;

        public void StartSession(EditSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            EditSessions.Push(session);
            session.SessionStarted();
        }

        public void EndSession()
        {
            if (EditSessions.Count == 0)
                throw new InvalidOperationException("No edit session was started.");
            var session = EditSessions.Pop();
            session.Dispose();
        }

        void ITextEditorImpl.ScrollTo(int offset)
        {
            TextEditor.ScrollTo(offset);
        }

        void ITextEditorImpl.CenterTo(int offset)
        {
            TextEditor.CenterTo(offset);
        }

        void ITextEditorImpl.ClearTooltipProviders()
        {
            TextEditor.ClearTooltipProviders();
        }

        IEnumerable<TooltipProvider> ITextEditorImpl.TooltipProvider
        {
            get
            {
                foreach (var p in GetTextEditorData().TooltipProviders)
                {
                    var w = p as TooltipProviderWrapper;
                    if (w == null)
                        continue;
                    yield return w.OriginalProvider;
                }
            }
        }

        void ITextEditorImpl.AddTooltipProvider(TooltipProvider provider)
        {
            TextEditor.AddTooltipProvider(new TooltipProviderWrapper(provider));
        }

        void ITextEditorImpl.RemoveTooltipProvider(TooltipProvider provider)
        {
            foreach (var p in GetTextEditorData().TooltipProviders)
            {
                var w = p as TooltipProviderWrapper;
                if (w == null)
                    continue;
                if (w.OriginalProvider == provider)
                {
                    TextEditor.RemoveTooltipProvider(p);
                    return;
                }
            }
        }

        Xwt.Point ITextEditorImpl.GetEditorWindowOrigin()
        {
            int ox, oy;
            TextEditor.GdkWindow.GetOrigin(out ox, out oy);
            return new Xwt.Point(ox, oy);
        }

        Rectangle ITextEditorImpl.GetEditorAllocation()
        {
            var alloc = TextEditor.Allocation;
            return new Rectangle(alloc.X, alloc.Y, alloc.Width, alloc.Height);
        }


        TextEditorExtension ITextEditorImpl.EditorExtension
        {
            get
            {
                return TextEditor.EditorExtension;
            }
            set
            {
                TextEditor.EditorExtension = value;
            }
        }

        SemanticHighlighting ITextEditorImpl.SemanticHighlighting
        {
            get
            {
                return TextEditor.SemanticHighlighting;
            }
            set
            {
                TextEditor.SemanticHighlighting = value;
            }
        }

        string ITextEditorImpl.GetMarkup(int offset, int length, MarkupOptions options)
        {
            var data = TextEditor.GetTextEditorData();
            switch (options.MarkupFormat)
            {
                case MarkupFormat.Pango:
                    return data.GetMarkup(offset, length, false, replaceTabs: false, fitIdeStyle: options.FitIdeStyle);
                case MarkupFormat.Html:
                    return HtmlWriter.GenerateHtml(ColoredSegment.GetChunks(data, new TextSegment(offset, length)), data.ColorStyle, data.Options, false);
                case MarkupFormat.RichText:
                    return RtfWriter.GenerateRtf(ColoredSegment.GetChunks(data, new TextSegment(offset, length)), data.ColorStyle, data.Options);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void ITextEditorImpl.SetQuickTaskProviders(IEnumerable<IQuickTaskProvider> providers)
        {
        }

        private class BracketMatcherTextMarker : TextSegmentMarker
        {
            public BracketMatcherTextMarker(int offset, int length) : base(offset, length)
            {
            }

            public override void DrawBackground(MonoTextEditor editor, Context cr, LineMetrics metrics, int startOffset, int endOffset)
            {
                try
                {
                    double fromX, toX;
                    GetLineDrawingPosition(metrics, startOffset, out fromX, out toX);

                    fromX = Math.Max(fromX, editor.TextViewMargin.XOffset);
                    toX = Math.Max(toX, editor.TextViewMargin.XOffset);
                    if (fromX < toX)
                    {
                        var bracketMatch = new Cairo.Rectangle(fromX + 0.5, metrics.LineYRenderStartPosition + 0.5, toX - fromX - 1, editor.LineHeight - 2);
                        if (editor.TextViewMargin.BackgroundRenderer == null)
                        {
                            cr.SetSourceColor(editor.ColorStyle.BraceMatchingRectangle.Color);
                            cr.Rectangle(bracketMatch);
                            cr.FillPreserve();
                            cr.SetSourceColor(editor.ColorStyle.BraceMatchingRectangle.SecondColor);
                            cr.Stroke();
                        }
                    }
                }
                catch (Exception e)
                {
                    LoggingService.LogError($"Error while drawing bracket matcher ({this}) startOffset={startOffset} lineCharLength={metrics.Layout.LineChars.Length}", e);
                }
            }

            private void GetLineDrawingPosition(LineMetrics metrics, int startOffset, out double fromX, out double toX)
            {
                var startXPos = metrics.TextRenderStartPosition;
                int start = Offset;
                int end = EndOffset;

                uint curIndex = 0, byteIndex = 0;
                TextViewMargin.TranslateToUTF8Index(metrics.Layout.LineChars, (uint)Math.Min(start - startOffset, metrics.Layout.LineChars.Length), ref curIndex, ref byteIndex);

                int xPos = metrics.Layout.Layout.IndexToPos((int)byteIndex).X;

                fromX = startXPos + (int)(xPos / Scale.PangoScale);

                TextViewMargin.TranslateToUTF8Index(metrics.Layout.LineChars, (uint)Math.Min(end - startOffset, metrics.Layout.LineChars.Length), ref curIndex, ref byteIndex);
                xPos = metrics.Layout.Layout.IndexToPos((int)byteIndex).X;

                toX = startXPos + (int)(xPos / Scale.PangoScale);
            }
        }

        private readonly List<BracketMatcherTextMarker> bracketMarkers = new List<BracketMatcherTextMarker>();

        void ITextEditorImpl.UpdateBraceMatchingResult(BraceMatchingResult? result)
        {
            if (result.HasValue)
            {
                if (bracketMarkers.Count > 0 && result.Value.LeftSegment.Offset == bracketMarkers[0].Offset)
                    return;
                ClearBracketMarkers();
                bracketMarkers.Add(new BracketMatcherTextMarker(result.Value.LeftSegment.Offset, result.Value.LeftSegment.Length));
                bracketMarkers.Add(new BracketMatcherTextMarker(result.Value.RightSegment.Offset, result.Value.RightSegment.Length));
                bracketMarkers.ForEach(marker => widget.TextEditor.Document.AddMarker(marker));
            }
            else
            {
                ClearBracketMarkers();
            }
        }

        private void ClearBracketMarkers()
        {
            bracketMarkers.ForEach(marker => widget.TextEditor.Document.RemoveMarker(marker));
            bracketMarkers.Clear();
        }

        public event EventHandler<Ide.Editor.LineEventArgs> LineChanged;

        public event EventHandler<Ide.Editor.LineEventArgs> LineInserted;

        public event EventHandler<Ide.Editor.LineEventArgs> LineRemoved;

        public double ZoomLevel
        {
            get { return TextEditor?.Options?.Zoom ?? 1d; }
            set { if (TextEditor?.Options != null) TextEditor.Options.Zoom = value; }
        }
        event EventHandler ITextEditorImpl.ZoomLevelChanged
        {
            add
            {
                TextEditor.Options.ZoomChanged += value;
            }
            remove
            {
                TextEditor.Options.ZoomChanged += value;
            }
        }

        public void AddOverlay(Control messageOverlayContent, Func<int> sizeFunc)
        {
            widget.AddOverlay(messageOverlayContent.GetNativeWidget<Widget>(), sizeFunc);
        }

        public void RemoveOverlay(Control messageOverlayContent)
        {
            widget.RemoveOverlay(messageOverlayContent.GetNativeWidget<Widget>());
        }

        private void TextViewMargin_LineShown(object sender, LineEventArgs e)
        {
            LineShown?.Invoke(this, new Ide.Editor.LineEventArgs(new DocumentLineWrapper(e.Line)));
        }

        public IEnumerable<IDocumentLine> VisibleLines
        {
            get
            {
                foreach (var v in TextEditor.TextViewMargin.CachedLine)
                {
                    yield return new DocumentLineWrapper(v);
                }
            }
        }

        public event EventHandler<Ide.Editor.LineEventArgs> LineShown;




        #region IEditorActionHost implementation

        void IEditorActionHost.MoveCaretDown()
        {
            CaretMoveActions.Down(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretUp()
        {
            CaretMoveActions.Up(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretRight()
        {
            CaretMoveActions.Right(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretLeft()
        {
            CaretMoveActions.Left(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretToLineEnd()
        {
            CaretMoveActions.LineEnd(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretToLineStart()
        {
            CaretMoveActions.LineHome(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretToDocumentStart()
        {
            CaretMoveActions.ToDocumentStart(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.MoveCaretToDocumentEnd()
        {
            CaretMoveActions.ToDocumentEnd(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.Backspace()
        {
            DeleteActions.Backspace(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.ClipboardCopy()
        {
            ClipboardActions.Copy(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.ClipboardCut()
        {
            ClipboardActions.Cut(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.ClipboardPaste()
        {
            ClipboardActions.Paste(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.NewLine()
        {
            MiscActions.InsertNewLine(TextEditor.GetTextEditorData());
        }

        void IEditorActionHost.SwitchCaretMode()
        {
            TextEditor.RunAction(MiscActions.SwitchCaretMode);
        }

        void IEditorActionHost.InsertTab()
        {
            TextEditor.RunAction(MiscActions.InsertTab);
        }

        void IEditorActionHost.RemoveTab()
        {
            TextEditor.RunAction(MiscActions.RemoveTab);
        }

        void IEditorActionHost.InsertNewLine()
        {
            TextEditor.RunAction(MiscActions.InsertNewLine);
        }

        void IEditorActionHost.DeletePreviousWord()
        {
            TextEditor.RunAction(DeleteActions.PreviousWord);
        }

        void IEditorActionHost.DeleteNextWord()
        {
            TextEditor.RunAction(DeleteActions.NextWord);
        }

        void IEditorActionHost.DeletePreviousSubword()
        {
            TextEditor.RunAction(DeleteActions.PreviousSubword);
        }

        void IEditorActionHost.DeleteNextSubword()
        {
            TextEditor.RunAction(DeleteActions.NextSubword);
        }

        void IEditorActionHost.StartCaretPulseAnimation()
        {
            TextEditor.StartCaretPulseAnimation();
        }

        void IEditorActionHost.RecenterEditor()
        {
            TextEditor.RunAction(MiscActions.RecenterEditor);
        }

        void IEditorActionHost.JoinLines()
        {
            using (Document.OpenUndoGroup())
            {
                TextEditor.RunAction(ViActions.Join);
            }
        }

        void IEditorActionHost.MoveNextSubWord()
        {
            TextEditor.RunAction(SelectionActions.MoveNextSubword);
        }

        void IEditorActionHost.MovePrevSubWord()
        {
            TextEditor.RunAction(SelectionActions.MovePreviousSubword);
        }

        void IEditorActionHost.MoveNextWord()
        {
            TextEditor.RunAction(CaretMoveActions.NextWord);
        }

        void IEditorActionHost.MovePrevWord()
        {
            TextEditor.RunAction(CaretMoveActions.PreviousWord);
        }

        void IEditorActionHost.PageUp()
        {
            TextEditor.RunAction(CaretMoveActions.PageUp);
        }

        void IEditorActionHost.PageDown()
        {
            TextEditor.RunAction(CaretMoveActions.PageDown);
        }

        void IEditorActionHost.DeleteCurrentLine()
        {
            TextEditor.RunAction(DeleteActions.CaretLine);
        }

        void IEditorActionHost.DeleteCurrentLineToEnd()
        {
            TextEditor.RunAction(DeleteActions.CaretLineToEnd);
        }

        void IEditorActionHost.ScrollLineUp()
        {
            TextEditor.RunAction(ScrollActions.Up);
        }

        void IEditorActionHost.ScrollLineDown()
        {
            TextEditor.RunAction(ScrollActions.Down);
        }

        void IEditorActionHost.ScrollPageUp()
        {
            TextEditor.RunAction(ScrollActions.PageUp);
        }

        void IEditorActionHost.ScrollPageDown()
        {
            TextEditor.RunAction(ScrollActions.PageDown);
        }

        void IEditorActionHost.MoveBlockUp()
        {
            using (TextEditor.OpenUndoGroup())
            {
                TextEditor.RunAction(MiscActions.MoveBlockUp);
            }
        }

        void IEditorActionHost.MoveBlockDown()
        {
            using (TextEditor.OpenUndoGroup())
            {
                TextEditor.RunAction(MiscActions.MoveBlockDown);
            }
        }

        void IEditorActionHost.ToggleBlockSelectionMode()
        {
            TextEditor.SelectionMode = TextEditor.SelectionMode == Mono.TextEditor.SelectionMode.Normal ? Mono.TextEditor.SelectionMode.Block : Mono.TextEditor.SelectionMode.Normal;
            TextEditor.QueueDraw();
        }

        void IEditorActionHost.IndentSelection()
        {
            if (widget.TextEditor.IsSomethingSelected)
            {
                MiscActions.IndentSelection(widget.TextEditor.GetTextEditorData());
            }
            else
            {
                int offset = widget.TextEditor.LocationToOffset(widget.TextEditor.Caret.Line, 1);
                widget.TextEditor.Insert(offset, widget.TextEditor.Options.IndentationString);
            }
        }

        void IEditorActionHost.UnIndentSelection()
        {
            MiscActions.RemoveTab(widget.TextEditor.GetTextEditorData());
        }

        #endregion


        #region ISegmentMarkerHost implementation

        IUrlTextLineMarker ITextMarkerFactory.CreateUrlTextMarker(TextEditor editor, IDocumentLine line, string value, UrlType url, string syntax, int startCol, int endCol)
        {
            return new UrlTextLineMarker(TextEditor.Document, line, value, (Mono.TextEditor.UrlType)url, syntax, startCol, endCol);
        }

        ICurrentDebugLineTextMarker ITextMarkerFactory.CreateCurrentDebugLineTextMarker(TextEditor editor, int offset, int length)
        {
            return null;
        }

        IGenericTextSegmentMarker ITextMarkerFactory.CreateGenericTextSegmentMarker(TextEditor editor, TextSegmentMarkerEffect effect, int offset, int length)
        {
            switch (effect)
            {
                case TextSegmentMarkerEffect.DottedLine:
                case TextSegmentMarkerEffect.WavedLine:
                    return new GenericUnderlineMarker(new TextSegment(offset, length), effect);
                case TextSegmentMarkerEffect.GrayOut:
                    return new GrayOutMarker(new TextSegment(offset, length));
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public ILinkTextMarker CreateLinkMarker(TextEditor editor, int offset, int length, Action<LinkRequest> activateLink)
        {
            return new LinkMarker(offset, length, activateLink);
        }

        ISmartTagMarker ITextMarkerFactory.CreateSmartTagMarker(TextEditor editor, int offset, Ide.Editor.DocumentLocation realLocation)
        {
            return new SmartTagMarker(offset, realLocation);
        }

        IErrorMarker ITextMarkerFactory.CreateErrorMarker(TextEditor editor, Error info, int offset, int length)
        {
            return new ErrorMarker(info, offset, length);
        }
        
        #endregion

        public event EventHandler FocusLost;

        private void TextArea_FocusOutEvent(object o, FocusOutEventArgs args)
        {
            FocusLost?.Invoke(this, EventArgs.Empty);
        }

        void ITextEditorImpl.GrabFocus()
        {
            var topLevelWindow = TextEditor.Toplevel as Window;
            topLevelWindow?.Present();
            TextEditor.GrabFocus();
        }
    }
}
