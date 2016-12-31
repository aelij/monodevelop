// ExtendibleTextEditor.cs
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
using System.Linq;
using Cairo;
using Gdk;
using GLib;
using Gtk;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using Mono.TextEditor.Vi;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.CodeCompletion;
using MonoDevelop.Ide.CodeTemplates;
using MonoDevelop.Ide.Commands;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;
using MonoDevelop.Ide.Editor.Highlighting;
using MonoDevelop.Projects;
using MonoDevelop.SourceEditor.Wrappers;
using EditMode = Mono.TextEditor.EditMode;
using Image = Xwt.Drawing.Image;
using ITextEditorOptions = Mono.TextEditor.ITextEditorOptions;
using Key = Gdk.Key;
using SyntaxModeService = MonoDevelop.Ide.Editor.Highlighting.SyntaxModeService;
using TextLink = Mono.TextEditor.TextLink;
using Timeout = GLib.Timeout;

namespace MonoDevelop.SourceEditor
{
    internal class ExtensibleTextEditor : MonoTextEditor
    {
        internal object MemoryProbe = Counters.EditorsInMemory.CreateMemoryProbe();

        private Adjustment cachedHAdjustment, cachedVAdjustment;

        private TextEditorExtension editorExtension;
        private bool needToAddLastExtension;

        public TextEditorExtension EditorExtension
        {
            get
            {
                return editorExtension;
            }
            set
            {
                editorExtension = value;
                needToAddLastExtension = true;
            }
        }

        private SemanticHighlighting semanticHighlighting;
        public SemanticHighlighting SemanticHighlighting
        {
            get
            {
                return semanticHighlighting;
            }
            set
            {
                semanticHighlighting = value;
                UpdateSemanticHighlighting();
            }
        }

        private bool updatingSemanticHighlighting;

        private void UpdateSemanticHighlighting()
        {
            if (updatingSemanticHighlighting) return;

            updatingSemanticHighlighting = true;

            try
            {
                var oldSemanticHighighting = Document.SyntaxMode as SemanticHighlightingSyntaxMode;

                if (semanticHighlighting == null)
                {
                    if (oldSemanticHighighting != null)
                        Document.MimeType = Document.MimeType;
                }
                else
                {
                    var syntaxMode = semanticHighlighting as ISyntaxMode;
                    if (syntaxMode != null)
                    {
                        Document.SyntaxMode = syntaxMode;
                    }
                    else
                    {
                        if (oldSemanticHighighting == null)
                        {
                            Document.SyntaxMode = new SemanticHighlightingSyntaxMode(this, Document.SyntaxMode, semanticHighlighting);
                        }
                        else
                        {
                            oldSemanticHighighting.UpdateSemanticHighlighting(semanticHighlighting);
                        }
                    }
                }
            }
            finally
            {
                updatingSemanticHighlighting = false;
            }
        }

        private class LastEditorExtension : TextEditorExtension
        {
            private readonly ExtensibleTextEditor ext;
            public LastEditorExtension(ExtensibleTextEditor ext)
            {
                if (ext == null)
                    throw new ArgumentNullException(nameof(ext));
                this.ext = ext;
            }

            public override bool KeyPress(KeyDescriptor descriptor)
            {
                var native = (Tuple<Key, ModifierType>)descriptor.NativeKeyChar;
                ext.SimulateKeyPress(native.Item1, descriptor.KeyChar, native.Item2);
                if (descriptor.SpecialKey == SpecialKey.Escape)
                    return true;
                return false;
            }
        }

        static ExtensibleTextEditor()
        {
            var icon = Image.FromResource("gutter-bookmark-15.png");

            BookmarkMarker.DrawBookmarkFunc = delegate (MonoTextEditor editor, Context cr, DocumentLine lineSegment, double x, double y, double width, double height)
            {
                if (!lineSegment.IsBookmarked)
                    return;
                cr.DrawImage(
                    editor,
                    icon,
                    Math.Floor(x + (width - icon.Width) / 2),
                    Math.Floor(y + (height - icon.Height) / 2)
                );
            };

        }

        public ExtensibleTextEditor(SourceEditorView view, ITextEditorOptions options, TextDocument doc) : base(doc, options)
        {
            Initialize(view);
        }

        public ExtensibleTextEditor(SourceEditorView view)
        {
            Options = new StyledSourceEditorOptions(DefaultSourceEditorOptions.Instance);
            Initialize(view);
        }

        internal SourceEditorView View { get; private set; }

        private void Initialize(SourceEditorView view)
        {
            View = view;

            Document.SyntaxModeChanged += delegate
            {
                UpdateSemanticHighlighting();
            };

            UpdateEditMode();
            DoPopupMenu = ShowPopup;
        }

        private void UpdateEditMode()
        {
            //		if (!(CurrentMode is SimpleEditMode)){
            SimpleEditMode simpleMode = new SimpleEditMode();
            simpleMode.KeyBindings[EditMode.GetKeyCode(Key.Tab)] = new TabAction(this).Action;
            simpleMode.KeyBindings[EditMode.GetKeyCode(Key.BackSpace)] = EditActions.AdvancedBackspace;
            CurrentMode = simpleMode;
            //		}
        }

        private void UnregisterAdjustments()
        {
            if (cachedHAdjustment != null)
                cachedHAdjustment.ValueChanged -= HAdjustment_ValueChanged;
            if (cachedVAdjustment != null)
                cachedVAdjustment.ValueChanged -= VAdjustment_ValueChanged;
            cachedHAdjustment = null;
            cachedVAdjustment = null;
        }

        internal bool IsDestroyed { get; private set; }

        protected override void OnDestroyed()
        {
            IsDestroyed = true;
            UnregisterAdjustments();
            View = null;
            var disposableSyntaxMode = Document.SyntaxMode as IDisposable;
            if (disposableSyntaxMode != null)
            {
                disposableSyntaxMode.Dispose();
                Document.SyntaxMode = null;
            }
            base.OnDestroyed();
            if (Options != null)
            {
                Options.Dispose();
                Options = null;
            }
        }

        public void FireOptionsChange()
        {
            OptionsChanged(null, null);
        }

        protected override void OptionsChanged(object sender, EventArgs args)
        {
            if (View?.Control != null)
            {
                if (!Options.ShowFoldMargin)
                    Document.ClearFoldSegments();
            }
            UpdateEditMode();
            base.OptionsChanged(sender, args);
        }

        protected override string GetIdeColorStyleName()
        {
            var scheme = SyntaxModeService.GetColorStyle(IdeApp.Preferences.ColorScheme);
            if (!scheme.FitsIdeTheme(IdeApp.Preferences.UserInterfaceTheme))
                scheme = IdeApp.Preferences.UserInterfaceTheme.GetDefaultColorStyle();
            return scheme.Name;
        }

        private bool isInKeyStroke;
        protected override bool OnKeyPressEvent(EventKey evnt)
        {
            isInKeyStroke = true;
            try
            {
                // Handle keyboard toolip popup
                /*			if ((evnt.Key == Gdk.Key.F1 && (evnt.State & Gdk.ModifierType.ControlMask) == Gdk.ModifierType.ControlMask)) {
                                Gdk.Point p = this.TextViewMargin.LocationToDisplayCoordinates (this.Caret.Location);
                                this.mx = p.X;
                                this.my = p.Y;
                                this.ShowTooltip ();
                                return true;
                            }
                */
                return base.OnKeyPressEvent(evnt);
            }
            finally
            {
                isInKeyStroke = false;
            }
        }

        private bool ExtensionKeyPress(Key key, uint ch, ModifierType state)
        {
            isInKeyStroke = true;
            try
            {
                if (needToAddLastExtension)
                {
                    var ext = EditorExtension;
                    while (ext.Next != null)
                        ext = ext.Next;
                    ext.Next = new LastEditorExtension(this);
                    needToAddLastExtension = false;
                }
                return EditorExtension.KeyPress(KeyDescriptor.FromGtk(key, (char)ch, state));
            }
            catch (Exception ex)
            {
                ReportExtensionError(ex);
            }
            finally
            {
                isInKeyStroke = false;
            }
            return false;
        }

        private void ReportExtensionError(Exception ex)
        {
            LoggingService.LogInternalError("Error in text editor extension chain", ex);
        }

        internal static IEnumerable<char> GetTextWithoutCommentsAndStrings(TextDocument doc, int start, int end)
        {
            bool isInString = false, isInChar = false;
            bool isInLineComment = false, isInBlockComment = false;
            int escaping = 0;

            for (int pos = start; pos < end; pos++)
            {
                char ch = doc.GetCharAt(pos);
                switch (ch)
                {
                    case '\r':
                    case '\n':
                        isInLineComment = false;
                        break;
                    case '/':
                        if (isInBlockComment)
                        {
                            if (pos > 0 && doc.GetCharAt(pos - 1) == '*')
                                isInBlockComment = false;
                        }
                        else if (!isInString && !isInChar && pos + 1 < doc.TextLength)
                        {
                            char nextChar = doc.GetCharAt(pos + 1);
                            if (nextChar == '/')
                                isInLineComment = true;
                            if (!isInLineComment && nextChar == '*')
                                isInBlockComment = true;
                        }
                        break;
                    case '"':
                        if (!(isInChar || isInLineComment || isInBlockComment))
                            if (!isInString || escaping != 1)
                                isInString = !isInString;
                        break;
                    case '\'':
                        if (!(isInString || isInLineComment || isInBlockComment))
                            if (!isInChar || escaping != 1)
                                isInChar = !isInChar;
                        break;
                    case '\\':
                        if (escaping != 1)
                            escaping = 2;
                        break;
                    default:
                        if (!(isInString || isInChar || isInLineComment || isInBlockComment))
                            yield return ch;
                        break;
                }
                escaping--;
            }
        }


        protected override bool OnIMProcessedKeyPressEvent(Key key, uint ch, ModifierType state)
        {
            bool result = true;
            if (key == Key.Escape)
            {
                bool b = EditorExtension != null ? ExtensionKeyPress(key, ch, state) : base.OnIMProcessedKeyPressEvent(key, ch, state);
                if (b)
                {
                    View.SourceEditorWidget.RemoveSearchWidget();
                    return true;
                }
                return false;
            }

            if (Document == null)
                return true;


            bool wasHandled = false;
            var currentSession = View.CurrentSession;
            if (currentSession != null)
            {
                switch (key)
                {
                    case Key.Return:
                        currentSession.BeforeReturn(out wasHandled);
                        break;
                    case Key.BackSpace:
                        currentSession.BeforeBackspace(out wasHandled);
                        break;
                    case Key.Delete:
                    case Key.KP_Delete:
                        currentSession.BeforeDelete(out wasHandled);
                        break;
                    default:
                        currentSession.BeforeType((char)ch, out wasHandled);
                        break;
                }
            }

            if (!wasHandled)
            {
                if (EditorExtension != null)
                {
                    if (!DefaultSourceEditorOptions.Instance.GenerateFormattingUndoStep)
                    {
                        using (Document.OpenUndoGroup())
                        {
                            if (ExtensionKeyPress(key, ch, state))
                                result = base.OnIMProcessedKeyPressEvent(key, ch, state);
                        }
                    }
                    else
                    {
                        if (ExtensionKeyPress(key, ch, state))
                            result = base.OnIMProcessedKeyPressEvent(key, ch, state);
                    }
                }
                else
                {
                    result = base.OnIMProcessedKeyPressEvent(key, ch, state);
                }

                if (currentSession != null)
                {
                    switch (key)
                    {
                        case Key.Return:
                            currentSession.AfterReturn();
                            break;
                        case Key.BackSpace:
                            currentSession.AfterBackspace();
                            break;
                        case Key.Delete:
                        case Key.KP_Delete:
                            currentSession.AfterDelete();
                            break;
                        default:
                            currentSession.AfterType((char)ch);
                            break;
                    }
                }
            }
            return result;
        }

        internal string GetErrorInformationAt(int offset)
        {
            var location = Document.OffsetToLocation(offset);
            DocumentLine line = Document.GetLine(location.Line);
            if (line == null)
                return null;

            var error = Document.GetTextSegmentMarkersAt(offset).OfType<ErrorMarker>().FirstOrDefault();

            if (error != null)
            {
                if (error.Error.ErrorType == ErrorType.Warning)
                    return GettextCatalog.GetString("<b>Warning</b>: {0}",
                        Markup.EscapeText(error.Error.Message));
                return GettextCatalog.GetString("<b>Error</b>: {0}",
                    Markup.EscapeText(error.Error.Message));
            }
            return null;
        }

        public Project Project
        {
            get
            {
                var doc = IdeApp.Workbench.ActiveDocument;
                return doc?.Project;
            }
        }

        public CodeTemplateContext GetTemplateContext()
        {
            // TODO-AELIJ
            //if (IsSomethingSelected) {
            //	var result = GetLanguageItem (Caret.Offset, Document.GetTextAt (SelectionRange));
            //	if (result != null)
            //		return CodeTemplateContext.InExpression;
            //}
            return CodeTemplateContext.Standard;
        }

        protected override bool OnFocusOutEvent(EventFocus evnt)
        {
            CompletionWindowManager.HideWindow();
            ParameterInformationWindowManager.HideWindow(null, View);
            return base.OnFocusOutEvent(evnt);
        }

        internal string ContextMenuPath { get; set; }

        private void ShowPopup(EventButton evt)
        {
            View.FireCompletionContextChanged();
            CompletionWindowManager.HideWindow();
            ParameterInformationWindowManager.HideWindow(null, View);
            HideTooltip();
            if (string.IsNullOrEmpty(ContextMenuPath))
                return;
            CommandEntrySet cset = IdeApp.CommandService.CreateCommandEntrySet(ContextMenuPath);

            if (Platform.IsMac)
            {
                if (evt == null)
                {
                    int x, y;
                    var pt = LocationToPoint(Caret.Location);
                    TranslateCoordinates(Toplevel, pt.X, pt.Y, out x, out y);

                    IdeApp.CommandService.ShowContextMenu(this, x, y, cset, this);
                }
                else
                {
                    IdeApp.CommandService.ShowContextMenu(this, evt, cset, this);
                }
            }
            else
            {
                Menu menu = IdeApp.CommandService.CreateMenu(cset);
                var imMenu = CreateInputMethodMenuItem("_Input Methods");
                if (imMenu != null)
                {
                    menu.Append(new SeparatorMenuItem());
                    menu.Append(imMenu);
                }

                menu.Hidden += HandleMenuHidden;
                if (evt != null)
                {
                    GtkWorkarounds.ShowContextMenu(menu, this, evt);
                }
                else
                {
                    var pt = LocationToPoint(Caret.Location);

                    GtkWorkarounds.ShowContextMenu(menu, this, pt.X, pt.Y);
                }
            }
        }

        private void HandleMenuHidden(object sender, EventArgs e)
        {
            var menu = (Menu)sender;
            menu.Hidden -= HandleMenuHidden;
            Timeout.Add(10, delegate
            {
                menu.Destroy();
                return false;
            });
        }

        #region Templates

        public bool IsTemplateKnown()
        {
            string shortcut = CodeTemplate.GetTemplateShortcutBeforeCaret(EditorExtension.Editor);
            bool result = false;
            foreach (CodeTemplate template in CodeTemplateService.GetCodeTemplates(Document.MimeType))
            {
                if (template.Shortcut == shortcut)
                {
                    result = true;
                }
                else if (template.Shortcut.StartsWith(shortcut))
                {
                    result = false;
                    break;
                }
            }
            return result;
        }

        public bool DoInsertTemplate()
        {
            string shortcut = CodeTemplate.GetTemplateShortcutBeforeCaret(EditorExtension.Editor);
            foreach (CodeTemplate template in CodeTemplateService.GetCodeTemplates(Document.MimeType))
            {
                if (template.Shortcut == shortcut)
                {
                    InsertTemplate(template, View.WorkbenchWindow.Document.Editor, View.WorkbenchWindow.Document);
                    return true;
                }
            }
            return false;
        }


        internal void InsertTemplate(CodeTemplate template, TextEditor editor, DocumentContext context)
        {
            using (editor.OpenUndoGroup())
            {
                var result = template.InsertTemplateContents(editor, context);

                var links = result.TextLinks.Select(l => new TextLink(l.Name)
                {
                    Links = l.Links.Select(s => new TextSegment(s.Offset, s.Length)).ToList(),
                    IsEditable = l.IsEditable,
                    IsIdentifier = l.IsIdentifier,
                    GetStringFunc = l.GetStringFunc != null ? (Func<Func<string, string>, Mono.TextEditor.PopupWindow.IListDataProvider<string>>)(arg => new ListDataProviderWrapper(l.GetStringFunc(arg))) : null
                }).ToList();
                var tle = new TextLinkEditMode(this, result.InsertPosition, links)
                {
                    TextLinkMode = TextLinkMode.General
                };
                if (tle.ShouldStartTextLinkMode)
                {
                    tle.OldMode = CurrentMode;
                    tle.StartMode();
                    CurrentMode = tle;
                    Timeout.Add(10, delegate
                    {
                        tle.UpdateTextLinks();
                        return false;
                    });
                }
            }
        }

        protected override void OnScrollAdjustmentsSet()
        {
            UnregisterAdjustments();
            if (HAdjustment != null)
            {
                cachedHAdjustment = HAdjustment;
                HAdjustment.ValueChanged += HAdjustment_ValueChanged;
            }
            if (VAdjustment != null)
            {
                cachedVAdjustment = VAdjustment;
                VAdjustment.ValueChanged += VAdjustment_ValueChanged;
            }
        }

        private void VAdjustment_ValueChanged(object sender, EventArgs e)
        {
            CompletionWindowManager.HideWindow();
            ParameterInformationWindowManager.HideWindow(null, View);
        }

        private void HAdjustment_ValueChanged(object sender, EventArgs e)
        {
            if (!isInKeyStroke)
            {
                CompletionWindowManager.HideWindow();
                ParameterInformationWindowManager.HideWindow(null, View);
            }
            else
            {
                CompletionWindowManager.RepositionWindow();
                ParameterInformationWindowManager.RepositionWindow(null, View);
            }
        }

        #endregion

        #region Key bindings

        [CommandHandler(TextEditorCommands.LineEnd)]
        internal void OnLineEnd()
        {
            RunAction(CaretMoveActions.LineEnd);
        }

        [CommandHandler(TextEditorCommands.LineStart)]
        internal void OnLineStart()
        {
            RunAction(CaretMoveActions.LineHome);
        }

        [CommandHandler(TextEditorCommands.DeleteLeftChar)]
        internal void OnDeleteLeftChar()
        {
            RunAction(DeleteActions.Backspace);
        }

        [CommandHandler(TextEditorCommands.DeleteRightChar)]
        internal void OnDeleteRightChar()
        {
            RunAction(DeleteActions.Delete);
        }

        [CommandHandler(TextEditorCommands.CharLeft)]
        internal void OnCharLeft()
        {
            RunAction(CaretMoveActions.Left);
        }

        [CommandHandler(TextEditorCommands.CharRight)]
        internal void OnCharRight()
        {
            RunAction(CaretMoveActions.Right);
        }

        [CommandHandler(TextEditorCommands.LineUp)]
        internal void OnLineUp()
        {
            RunAction(CaretMoveActions.Up);
        }

        [CommandHandler(TextEditorCommands.LineDown)]
        internal void OnLineDown()
        {
            RunAction(CaretMoveActions.Down);
        }

        [CommandHandler(TextEditorCommands.DocumentStart)]
        internal void OnDocumentStart()
        {
            RunAction(CaretMoveActions.ToDocumentStart);
        }

        [CommandHandler(TextEditorCommands.DocumentEnd)]
        internal void OnDocumentEnd()
        {
            RunAction(CaretMoveActions.ToDocumentEnd);
        }

        [CommandHandler(TextEditorCommands.PageUp)]
        internal void OnPageUp()
        {
            RunAction(CaretMoveActions.PageUp);
        }

        [CommandHandler(TextEditorCommands.PageDown)]
        internal void OnPageDown()
        {
            RunAction(CaretMoveActions.PageDown);
        }

        [CommandHandler(TextEditorCommands.DeleteLine)]
        internal void OnDeleteLine()
        {
            RunAction(DeleteActions.CaretLine);
        }

        [CommandHandler(TextEditorCommands.DeleteToLineStart)]
        internal void OnDeleteToLineStart()
        {
            RunAction(DeleteActions.CaretLineToStart);
        }

        [CommandHandler(TextEditorCommands.DeleteToLineEnd)]
        internal void OnDeleteToLineEnd()
        {
            RunAction(DeleteActions.CaretLineToEnd);
        }

        [CommandHandler(TextEditorCommands.ScrollLineUp)]
        internal void OnScrollLineUp()
        {
            RunAction(ScrollActions.Up);
        }

        [CommandHandler(TextEditorCommands.ScrollLineDown)]
        internal void OnScrollLineDown()
        {
            RunAction(ScrollActions.Down);
        }

        [CommandHandler(TextEditorCommands.ScrollPageUp)]
        internal void OnScrollPageUp()
        {
            RunAction(ScrollActions.PageUp);
        }

        [CommandHandler(TextEditorCommands.ScrollPageDown)]
        internal void OnScrollPageDown()
        {
            RunAction(ScrollActions.PageDown);
        }

        [CommandHandler(TextEditorCommands.ScrollTop)]
        internal void OnScrollTop()
        {
            RunAction(ScrollActions.Top);
        }

        [CommandHandler(TextEditorCommands.ScrollBottom)]
        internal void OnScrollBottom()
        {
            RunAction(ScrollActions.Bottom);
        }

        [CommandHandler(TextEditorCommands.GotoMatchingBrace)]
        internal void OnGotoMatchingBrace()
        {
            RunAction(MiscActions.GotoMatchingBracket);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveLeft)]
        internal void OnSelectionMoveLeft()
        {
            RunAction(SelectionActions.MoveLeft);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveRight)]
        internal void OnSelectionMoveRight()
        {
            RunAction(SelectionActions.MoveRight);
        }

        [CommandHandler(TextEditorCommands.MovePrevWord)]
        internal void OnMovePrevWord()
        {
            RunAction(CaretMoveActions.PreviousWord);
        }

        [CommandHandler(TextEditorCommands.MoveNextWord)]
        internal void OnMoveNextWord()
        {
            RunAction(CaretMoveActions.NextWord);
        }

        [CommandHandler(TextEditorCommands.SelectionMovePrevWord)]
        internal void OnSelectionMovePrevWord()
        {
            RunAction(SelectionActions.MovePreviousWord);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveNextWord)]
        internal void OnSelectionMoveNextWord()
        {
            RunAction(SelectionActions.MoveNextWord);
        }

        [CommandHandler(TextEditorCommands.MovePrevSubword)]
        internal void OnMovePrevSubword()
        {
            RunAction(CaretMoveActions.PreviousSubword);
        }

        [CommandHandler(TextEditorCommands.MoveNextSubword)]
        internal void OnMoveNextSubword()
        {
            RunAction(CaretMoveActions.NextSubword);
        }

        [CommandHandler(TextEditorCommands.SelectionMovePrevSubword)]
        internal void OnSelectionMovePrevSubword()
        {
            RunAction(SelectionActions.MovePreviousSubword);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveNextSubword)]
        internal void OnSelectionMoveNextSubword()
        {
            RunAction(SelectionActions.MoveNextSubword);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveUp)]
        internal void OnSelectionMoveUp()
        {
            RunAction(SelectionActions.MoveUp);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveDown)]
        internal void OnSelectionMoveDown()
        {
            RunAction(SelectionActions.MoveDown);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveHome)]
        internal void OnSelectionMoveHome()
        {
            RunAction(SelectionActions.MoveLineHome);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveEnd)]
        internal void OnSelectionMoveEnd()
        {
            RunAction(SelectionActions.MoveLineEnd);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveToDocumentStart)]
        internal void OnSelectionMoveToDocumentStart()
        {
            RunAction(SelectionActions.MoveToDocumentStart);
        }

        [CommandHandler(TextEditorCommands.ExpandSelectionToLine)]
        internal void OnExpandSelectionToLine()
        {
            RunAction(SelectionActions.ExpandSelectionToLine);
        }

        [CommandHandler(TextEditorCommands.SelectionMoveToDocumentEnd)]
        internal void OnSelectionMoveToDocumentEnd()
        {
            RunAction(SelectionActions.MoveToDocumentEnd);
        }

        [CommandHandler(TextEditorCommands.SwitchCaretMode)]
        internal void OnSwitchCaretMode()
        {
            RunAction(MiscActions.SwitchCaretMode);
        }

        [CommandHandler(TextEditorCommands.InsertTab)]
        internal void OnInsertTab()
        {
            RunAction(MiscActions.InsertTab);
        }

        [CommandHandler(TextEditorCommands.RemoveTab)]
        internal void OnRemoveTab()
        {
            RunAction(MiscActions.RemoveTab);
        }

        [CommandHandler(TextEditorCommands.InsertNewLine)]
        internal void OnInsertNewLine()
        {
            RunAction(MiscActions.InsertNewLine);
        }

        [CommandHandler(TextEditorCommands.InsertNewLineAtEnd)]
        internal void OnInsertNewLineAtEnd()
        {
            RunAction(MiscActions.InsertNewLineAtEnd);
        }

        [CommandHandler(TextEditorCommands.InsertNewLinePreserveCaretPosition)]
        internal void OnInsertNewLinePreserveCaretPosition()
        {
            RunAction(MiscActions.InsertNewLinePreserveCaretPosition);
        }

        [CommandHandler(TextEditorCommands.CompleteStatement)]
        internal void OnCompleteStatement()
        {
            //TODO-AELIJ: complete statement
            //var doc = IdeApp.Workbench.ActiveDocument;
            //var generator = CodeGenerator.CreateGenerator(doc);
            //if (generator != null)
            //{
            //    generator.CompleteStatement(doc);
            //}
        }

        [CommandHandler(TextEditorCommands.DeletePrevWord)]
        internal void OnDeletePrevWord()
        {
            RunAction(DeleteActions.PreviousWord);
        }

        [CommandHandler(TextEditorCommands.DeleteNextWord)]
        internal void OnDeleteNextWord()
        {
            RunAction(DeleteActions.NextWord);
        }

        [CommandHandler(TextEditorCommands.DeletePrevSubword)]
        internal void OnDeletePrevSubword()
        {
            RunAction(DeleteActions.PreviousSubword);
        }

        [CommandHandler(TextEditorCommands.DeleteNextSubword)]
        internal void OnDeleteNextSubword()
        {
            RunAction(DeleteActions.NextSubword);
        }

        [CommandHandler(TextEditorCommands.SelectionPageDownAction)]
        internal void OnSelectionPageDownAction()
        {
            RunAction(SelectionActions.MovePageDown);
        }

        [CommandHandler(TextEditorCommands.SelectionPageUpAction)]
        internal void OnSelectionPageUpAction()
        {
            RunAction(SelectionActions.MovePageUp);
        }

        [CommandHandler(TextEditorCommands.PulseCaret)]
        internal void OnPulseCaretCommand()
        {
            StartCaretPulseAnimation();
        }

        [CommandHandler(TextEditorCommands.TransposeCharacters)]
        internal void TransposeCharacters()
        {
            RunAction(MiscActions.TransposeCharacters);
        }

        [CommandHandler(TextEditorCommands.DuplicateLine)]
        internal void DuplicateLine()
        {
            RunAction(MiscActions.DuplicateLine);
        }

        [CommandHandler(TextEditorCommands.RecenterEditor)]
        internal void RecenterEditor()
        {
            RunAction(MiscActions.RecenterEditor);
        }

        [CommandHandler(EditCommands.JoinWithNextLine)]
        internal void JoinLines()
        {
            using (Document.OpenUndoGroup())
            {
                RunAction(ViActions.Join);
            }
        }
        #endregion

    }
}
