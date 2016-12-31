// 
// QuickFixEditorExtension.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2011 Novell, Inc (http://www.novell.com)
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
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GLib;
using Gtk;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using MonoDevelop.AnalysisCore;
using MonoDevelop.CodeIssues;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;
using MonoDevelop.Refactoring;
using Action = System.Action;
using Global = Gtk.Global;
using Pointer = Gdk.Pointer;
using Rectangle = Xwt.Rectangle;
using TextChangeEventArgs = MonoDevelop.Core.Text.TextChangeEventArgs;
using Timeout = GLib.Timeout;

namespace MonoDevelop.CodeActions
{
    public class CodeActionEditorExtension : TextEditorExtension
    {
        private const int MenuTimeout = 250;
        private uint smartTagPopupTimeoutId;
        private uint menuCloseTimeoutId;

        static CodeActionEditorExtension()
        {
            var usages = PropertyService.Get("CodeActionUsages", new Properties());
            foreach (var key in usages.Keys)
            {
                CodeActionUsages[key] = usages.Get<int>(key);
            }
        }

        public Document RoslynDocument { get; set; }

        private void CancelSmartTagPopupTimeout()
        {

            if (smartTagPopupTimeoutId != 0)
            {
                Source.Remove(smartTagPopupTimeoutId);
                smartTagPopupTimeoutId = 0;
            }
        }

        private void CancelMenuCloseTimer()
        {
            if (menuCloseTimeoutId != 0)
            {
                Source.Remove(menuCloseTimeoutId);
                menuCloseTimeoutId = 0;
            }
        }

        private void RemoveWidget()
        {
            if (currentSmartTag != null)
            {
                Editor.RemoveMarker(currentSmartTag);
                currentSmartTag.CancelPopup -= CurrentSmartTag_CancelPopup;
                currentSmartTag.ShowPopup -= CurrentSmartTag_ShowPopup;

                currentSmartTag = null;
                currentSmartTagBegin = -1;
            }
            CancelSmartTagPopupTimeout();
        }

        public override void Dispose()
        {
            CancelMenuCloseTimer();
            CancelQuickFixTimer();
            HidePreviewTooltip();
            Editor.CaretPositionChanged -= HandleCaretPositionChanged;
            Editor.SelectionChanged -= HandleSelectionChanged;
            DocumentContext.DocumentParsed -= HandleDocumentDocumentParsed;
            Editor.MouseMoved -= HandleBeginHover;
            Editor.TextChanged -= Editor_TextChanged;
            Editor.EndAtomicUndoOperation -= Editor_EndAtomicUndoOperation;
            RemoveWidget();
            base.Dispose();
        }

        private static readonly Dictionary<string, int> CodeActionUsages = new Dictionary<string, int>();

        private static void ConfirmUsage(string id)
        {
            if (id == null)
                return;
            if (!CodeActionUsages.ContainsKey(id))
            {
                CodeActionUsages[id] = 1;
            }
            else
            {
                CodeActionUsages[id]++;
            }
            var usages = PropertyService.Get("CodeActionUsages", new Properties());
            usages.Set(id, CodeActionUsages[id]);
        }

        internal static int GetUsage(string id)
        {
            int result;
            if (id == null || !CodeActionUsages.TryGetValue(id, out result))
                return 0;
            return result;
        }

        public void CancelQuickFixTimer()
        {
            quickFixCancellationTokenSource.Cancel();
            quickFixCancellationTokenSource = new CancellationTokenSource();
            smartTagTask = null;
        }

        private Task<CodeActionContainer> smartTagTask;
        private CancellationTokenSource quickFixCancellationTokenSource = new CancellationTokenSource();
        private List<CodeDiagnosticFixDescriptor> codeFixes;

        private void HandleCaretPositionChanged(object sender, EventArgs e)
        {
            if (Editor.IsInAtomicUndo)
                return;
            CancelQuickFixTimer();
            if (AnalysisOptions.EnableFancyFeatures)
            {
                var token = quickFixCancellationTokenSource.Token;
                var curOffset = Editor.CaretOffset;
                if (HasCurrentFixes)
                {
                    foreach (var fix in GetCurrentFixes().AllValidCodeActions)
                    {
                        if (!fix.ValidSegment.Contains(curOffset))
                        {
                            RemoveWidget();
                            break;
                        }
                    }
                }

                var loc = Editor.CaretOffset;

                TextSpan span;

                if (Editor.IsSomethingSelected)
                {
                    var selectionRange = Editor.SelectionRange;
                    span = selectionRange.Offset >= 0 ? TextSpan.FromBounds(selectionRange.Offset, selectionRange.EndOffset) : TextSpan.FromBounds(loc, loc);
                }
                else
                {
                    span = TextSpan.FromBounds(loc, loc);
                }

                var diagnosticsAtCaret =
                    Editor.GetTextSegmentMarkersAt(Editor.CaretOffset)
                          .OfType<IGenericTextSegmentMarker>()
                          .Select(rm => rm.Tag)
                          .OfType<DiagnosticResult>()
                          .Select(dr => dr.Diagnostic)
                          .ToList();

                var errorList = Editor
                    .GetTextSegmentMarkersAt(Editor.CaretOffset)
                    .OfType<IErrorMarker>()
                    .Where(rm => !string.IsNullOrEmpty(rm.Error.Id)).ToList();

                smartTagTask = Task.Run(async delegate
                {
                    try
                    {
                        var codeIssueFixes = new List<ValidCodeDiagnosticAction>();
                        var diagnosticIds = diagnosticsAtCaret.Select(diagnostic => diagnostic.Id).Concat(errorList.Select(rm => rm.Error.Id)).ToList();
                        if (codeFixes == null)
                        {
                            codeFixes = (await CodeRefactoringService.GetCodeFixesAsync(DocumentContext, CodeRefactoringService.MimeTypeToLanguage(Editor.MimeType), token).ConfigureAwait(false)).ToList();
                        }
                        foreach (var cfp in codeFixes)
                        {
                            if (token.IsCancellationRequested)
                                return CodeActionContainer.Empty;
                            var provider = cfp.GetCodeFixProvider();
                            if (!provider.FixableDiagnosticIds.Any(diagnosticIds.Contains))
                                continue;
                            try
                            {
                                var groupedDiagnostics = diagnosticsAtCaret
                                    .Concat(errorList.Select(em => em.Error.Tag)
                                    .OfType<Diagnostic>())
                                    .GroupBy(d => d.Location.SourceSpan);
                                foreach (var g in groupedDiagnostics)
                                {
                                    if (token.IsCancellationRequested)
                                        return CodeActionContainer.Empty;
                                    var diagnosticSpan = g.Key;

                                    var validDiagnostics = g.Where(d => provider.FixableDiagnosticIds.Contains(d.Id)).ToImmutableArray();
                                    if (validDiagnostics.Length == 0)
                                        continue;
                                    await provider.RegisterCodeFixesAsync(new CodeFixContext(RoslynDocument, diagnosticSpan, validDiagnostics, (ca, d) => codeIssueFixes.Add(new ValidCodeDiagnosticAction(cfp, ca, validDiagnostics, diagnosticSpan)), token));

                                    // TODO: Is that right ? Currently it doesn't really make sense to run one code fix provider on several overlapping diagnostics at the same location
                                    //       However the generate constructor one has that case and if I run it twice the same code action is generated twice. So there is a dupe check problem there.
                                    // Work around for now is to only take the first diagnostic batch.
                                    break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                return CodeActionContainer.Empty;
                            }
                            catch (AggregateException ae)
                            {
                                ae.Flatten().Handle(aex => aex is OperationCanceledException);
                                return CodeActionContainer.Empty;
                            }
                            catch (Exception ex)
                            {
                                LoggingService.LogError("Error while getting refactorings from code fix provider " + cfp.Name, ex);
                            }
                        }
                        var codeActions = new List<ValidCodeAction>();
                        foreach (var action in await CodeRefactoringService.GetValidActionsAsync(Editor, DocumentContext, span, token).ConfigureAwait(false))
                        {
                            codeActions.Add(action);
                        }
                        var codeActionContainer = new CodeActionContainer(codeIssueFixes, codeActions, diagnosticsAtCaret);
                        Application.Invoke(delegate
                        {
                            if (token.IsCancellationRequested)
                                return;
                            if (codeActionContainer.IsEmpty)
                            {
                                RemoveWidget();
                                return;
                            }
                            CreateSmartTag(codeActionContainer, loc);
                        });
                        return codeActionContainer;

                    }
                    catch (AggregateException ae)
                    {
                        ae.Flatten().Handle(aex => aex is OperationCanceledException);
                        return CodeActionContainer.Empty;
                    }
                    catch (OperationCanceledException)
                    {
                        return CodeActionContainer.Empty;
                    }
                    catch (TargetInvocationException ex)
                    {
                        if (ex.InnerException is OperationCanceledException)
                            return CodeActionContainer.Empty;
                        throw;
                    }

                }, token);
            }
            else
            {
                RemoveWidget();
            }
        }

        internal static bool IsAnalysisOrErrorFix(CodeAction act)
        {
            return false;
        }

        internal class FixMenuEntry
        {
            public static readonly FixMenuEntry Separator = new FixMenuEntry("-", null);
            public readonly string Label;

            public readonly Action Action;
            public Action<Rectangle> ShowPreviewTooltip;

            public FixMenuEntry(string label, Action action)
            {
                Label = label;
                Action = action;
            }
        }

        internal class FixMenuDescriptor : FixMenuEntry
        {
            private readonly List<FixMenuEntry> items = new List<FixMenuEntry>();

            public IReadOnlyList<FixMenuEntry> Items => items;

            public FixMenuDescriptor() : base(null, null)
            {
            }

            public FixMenuDescriptor(string label) : base(label, null)
            {
            }

            public void Add(FixMenuEntry entry)
            {
                items.Add(entry);
            }

            public object MotionNotifyEvent
            {
                get;
                set;
            }
        }

        private void PopupQuickFixMenu(Action<FixMenuDescriptor> menuAction)
        {
            FixMenuDescriptor menu = new FixMenuDescriptor();
            var fixMenu = menu;
            int items = 0;

            PopulateFixes(fixMenu, ref items);

            if (items == 0)
            {
                return;
            }
            Editor.SuppressTooltips = true;
            menuAction?.Invoke(menu);

            var p = Editor.LocationToPoint(Editor.OffsetToLocation(currentSmartTagBegin));
            Widget widget = Editor;
            var rect = new Gdk.Rectangle(
                (int)p.X + widget.Allocation.X,
                (int)p.Y + widget.Allocation.Y, 0, 0);

            ShowFixesMenu(widget, rect, menu);
        }

        private void ShowFixesMenu(Widget parent, Gdk.Rectangle evt, FixMenuDescriptor entrySet)
        {
            if (parent?.GdkWindow == null)
            {
                Editor.SuppressTooltips = false;
                return;
            }

            try
            {
                parent.GrabFocus();
                var x = evt.X;
                var y = evt.Y;

                // Explicitly release the grab because the menu is shown on the mouse position, and the widget doesn't get the mouse release event
                Pointer.Ungrab(Global.CurrentEventTime);
                var menu = CreateContextMenu(entrySet);
                HidePreviewTooltip();
                menu.Show(parent, x, y, () => { Editor.SuppressTooltips = false; HidePreviewTooltip(); }, true);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error while context menu popup.", ex);
            }
        }

        private ContextMenu CreateContextMenu(FixMenuDescriptor entrySet)
        {
            var menu = new ContextMenu();
            foreach (var item in entrySet.Items)
            {
                if (item == FixMenuEntry.Separator)
                {
                    menu.Items.Add(new SeparatorContextMenuItem());
                    continue;
                }

                var menuItem = new ContextMenuItem(item.Label) { Context = item.Action };
                var subMenu = item as FixMenuDescriptor;
                if (subMenu != null)
                {
                    menuItem.SubMenu = CreateContextMenu(subMenu);
                    menuItem.Selected += (sender, args) => HidePreviewTooltip();
                    menuItem.Deselected += (sender, args) => HidePreviewTooltip();
                }
                else
                {
                    menuItem.Clicked += (sender, e) => ((Action)((ContextMenuItem)sender).Context)();
                    menuItem.Selected += (sender, e) =>
                    {
                        HidePreviewTooltip();
                        item.ShowPreviewTooltip?.Invoke(e);
                    };
                    menuItem.Deselected += (sender, args) => HidePreviewTooltip();
                }
                menu.Items.Add(menuItem);
            }
            menu.Closed += delegate { HidePreviewTooltip(); };
            return menu;
        }

        private void HidePreviewTooltip()
        {
            if (currentPreviewWindow != null)
            {
                currentPreviewWindow.Destroy();
                currentPreviewWindow = null;
            }
        }

        private static string CreateLabel(string title, ref int mnemonic)
        {
            var escapedLabel = title.Replace("_", "__");
#if MAC
			return escapedLabel;
#else
            return (mnemonic <= 10) ? "_" + mnemonic++ % 10 + " \u2013 " + escapedLabel : "  " + escapedLabel;
#endif
        }

        private static RefactoringPreviewTooltipWindow currentPreviewWindow;

        private void PopulateFixes(FixMenuDescriptor menu, ref int items)
        {
            int mnemonic = 1;
            bool gotImportantFix = false, addedSeparator = false;
            foreach (var fix in GetCurrentFixes().CodeFixActions.OrderByDescending(i => Tuple.Create(IsAnalysisOrErrorFix(i.CodeAction), 0, GetUsage(i.CodeAction.EquivalenceKey))))
            {
                // filter out code actions that are already resolutions of a code issue
                if (IsAnalysisOrErrorFix(fix.CodeAction))
                    gotImportantFix = true;
                if (!addedSeparator && gotImportantFix && !IsAnalysisOrErrorFix(fix.CodeAction))
                {
                    menu.Add(FixMenuEntry.Separator);
                    addedSeparator = true;
                }

                var label = CreateLabel(fix.CodeAction.Title, ref mnemonic);
                var thisInstanceMenuItem = new FixMenuEntry(label, async delegate
                {
                    // TODO-AELIJ: execute action
                    await Task.CompletedTask;
                    //await new ContextActionRunner(fix.CodeAction, Editor, DocumentContext).Run();
                    ConfirmUsage(fix.CodeAction.EquivalenceKey);
                })
                {
                    ShowPreviewTooltip = delegate (Rectangle rect)
                    {
                        HidePreviewTooltip();
                        currentPreviewWindow = new RefactoringPreviewTooltipWindow(Editor, DocumentContext,
                            fix.CodeAction);
                        currentPreviewWindow.RequestPopup(rect);
                    }
                };


                menu.Add(thisInstanceMenuItem);
                items++;
            }

            bool first = true;
            foreach (var fix in GetCurrentFixes().CodeRefactoringActions)
            {
                if (first)
                {
                    if (items > 0)
                        menu.Add(FixMenuEntry.Separator);
                    first = false;
                }

                var label = CreateLabel(fix.CodeAction.Title, ref mnemonic);
                var thisInstanceMenuItem = new FixMenuEntry(label, async delegate
                {
                    // TODO-AELIJ: execute action
                    await Task.CompletedTask;
                    //await new ContextActionRunner(fix.CodeAction, Editor, DocumentContext).Run();
                    ConfirmUsage(fix.CodeAction.EquivalenceKey);
                })
                {
                    ShowPreviewTooltip = delegate (Rectangle rect)
                    {
                        HidePreviewTooltip();
                        currentPreviewWindow = new RefactoringPreviewTooltipWindow(Editor, DocumentContext,
                            fix.CodeAction);
                        currentPreviewWindow.RequestPopup(rect);
                    }
                };


                menu.Add(thisInstanceMenuItem);
                items++;
            }

            foreach (var fix in GetCurrentFixes().DiagnosticsAtCaret)
            {
                var label = GettextCatalog.GetString("_Options for \u2018{0}\u2019", fix.GetMessage());
                var subMenu = new FixMenuDescriptor(label);

                CodeDiagnosticDescriptor descriptor = BuiltInCodeDiagnosticProvider.GetCodeDiagnosticDescriptor(fix.Id);
                if (descriptor == null)
                    continue;

                foreach (var fix2 in GetCurrentFixes().CodeFixActions.OrderByDescending(i => Tuple.Create(IsAnalysisOrErrorFix(i.CodeAction), 0, GetUsage(i.CodeAction.EquivalenceKey))))
                {

                    var provider = fix2.Diagnostic.GetCodeFixProvider().GetFixAllProvider();
                    if (provider == null)
                        continue;
                    if (!provider.GetSupportedFixAllScopes().Contains(FixAllScope.Document))
                        continue;
                    var subMenu2 = new FixMenuDescriptor("Fix all");
                    var diagnosticAnalyzer = fix2.Diagnostic.GetCodeDiagnosticDescriptor(LanguageNames.CSharp).GetProvider();
                    if (!diagnosticAnalyzer.SupportedDiagnostics.Contains(fix.Descriptor))
                        continue;
                    
                    var menuItem = new FixMenuEntry(
                        "In _Document",
                        async delegate
                        {
                            var fixAllDiagnosticProvider = new FixAllState.FixAllDiagnosticProvider(diagnosticAnalyzer.SupportedDiagnostics.Select(d => d.Id).ToImmutableHashSet(), async (doc, diagnostics, token) =>
                            {
                                var model = await doc.GetSemanticModelAsync(token);
                                var compilationWithAnalyzer = model.Compilation.WithAnalyzers(new[] { diagnosticAnalyzer }.ToImmutableArray(), null, token);
                                return await compilationWithAnalyzer.GetAnalyzerSemanticDiagnosticsAsync(model, null, token);
                            }, (arg1, arg2, arg3, arg4) => Task.FromResult((IEnumerable<Diagnostic>)new Diagnostic[] { }));
                            var ctx = new FixAllContext(
                                RoslynDocument,
                                fix2.Diagnostic.GetCodeFixProvider(),
                                FixAllScope.Document,
                                fix2.CodeAction.EquivalenceKey,
                                diagnosticAnalyzer.SupportedDiagnostics.Select(d => d.Id),
                                fixAllDiagnosticProvider,
                                default(CancellationToken)
                            );
                            var fixAll = await provider.GetFixAsync(ctx);
                            using (Editor.OpenUndoGroup())
                            {
                                CodeDiagnosticDescriptor.RunAction(DocumentContext, fixAll, default(CancellationToken));
                            }
                        });
                    subMenu2.Add(menuItem);
                    subMenu.Add(FixMenuEntry.Separator);
                    subMenu.Add(subMenu2);
                }

                menu.Add(subMenu);
                items++;
            }
        }

        private ISmartTagMarker currentSmartTag;
        private int currentSmartTagBegin;

        private void CreateSmartTag(CodeActionContainer fixes, int offset)
        {
            if (!AnalysisOptions.EnableFancyFeatures || fixes.IsEmpty)
            {
                RemoveWidget();
                return;
            }
            var editor = Editor;
            if (editor == null)
            {
                RemoveWidget();
                return;
            }

            bool first = true;
            var smartTagLocBegin = offset;
            foreach (var fix in fixes.CodeFixActions.Concat(fixes.CodeRefactoringActions))
            {
                var textSpan = fix.ValidSegment;
                if (textSpan.IsEmpty)
                    continue;
                if (first || offset < textSpan.Start)
                {
                    smartTagLocBegin = textSpan.Start;
                }
                first = false;
            }

            if (currentSmartTag != null && currentSmartTagBegin == smartTagLocBegin)
            {
                return;
            }
            RemoveWidget();
            currentSmartTagBegin = smartTagLocBegin;
            var realLoc = Editor.OffsetToLocation(smartTagLocBegin);

            currentSmartTag = TextMarkerFactory.CreateSmartTagMarker(Editor, smartTagLocBegin, realLoc);
            currentSmartTag.CancelPopup += CurrentSmartTag_CancelPopup;
            currentSmartTag.ShowPopup += CurrentSmartTag_ShowPopup;
            currentSmartTag.Tag = fixes;
            currentSmartTag.IsVisible = fixes.CodeFixActions.Count > 0;
            editor.AddMarker(currentSmartTag);
        }

        private void CurrentSmartTag_ShowPopup(object sender, EventArgs e)
        {
            CurrentSmartTagPopup();
        }

        private void CurrentSmartTag_CancelPopup(object sender, EventArgs e)
        {
            CancelSmartTagPopupTimeout();
        }

        protected override void Initialize()
        {
            base.Initialize();
            DocumentContext.DocumentParsed += HandleDocumentDocumentParsed;
            Editor.SelectionChanged += HandleSelectionChanged;
            Editor.MouseMoved += HandleBeginHover;
            Editor.CaretPositionChanged += HandleCaretPositionChanged;
            Editor.TextChanged += Editor_TextChanged;
            Editor.EndAtomicUndoOperation += Editor_EndAtomicUndoOperation;
        }

        private void Editor_EndAtomicUndoOperation(object sender, EventArgs e)
        {
            RemoveWidget();
            HandleCaretPositionChanged(null, EventArgs.Empty);
        }

        private void Editor_TextChanged(object sender, TextChangeEventArgs e)
        {
            if (Editor.IsInAtomicUndo)
                return;
            RemoveWidget();
            HandleCaretPositionChanged(null, EventArgs.Empty);
        }

        private void HandleBeginHover(object sender, EventArgs e)
        {
            CancelSmartTagPopupTimeout();
            CancelMenuCloseTimer();
        }

        private void HandleSelectionChanged(object sender, EventArgs e)
        {
            HandleCaretPositionChanged(null, EventArgs.Empty);
        }

        private void HandleDocumentDocumentParsed(object sender, EventArgs e)
        {
            HandleCaretPositionChanged(null, EventArgs.Empty);
        }

        private void CurrentSmartTagPopup()
        {
            CancelSmartTagPopupTimeout();
            smartTagPopupTimeoutId = Timeout.Add(MenuTimeout, delegate
            {
                PopupQuickFixMenu(menu => { });
                smartTagPopupTimeoutId = 0;
                return false;
            });
        }

        [CommandHandler(RefactoryCommands.QuickFix)]
        private void OnQuickFixCommand()
        {
            if (!AnalysisOptions.EnableFancyFeatures || currentSmartTag == null)
            {
                currentSmartTagBegin = Editor.CaretOffset;
                PopupQuickFixMenu(null);
                return;
            }

            CancelSmartTagPopupTimeout();
            PopupQuickFixMenu(menu => { });
        }

        internal bool HasCurrentFixes => smartTagTask != null && smartTagTask.IsCompleted;

        internal CodeActionContainer GetCurrentFixes()
        {
            return smartTagTask == null ? CodeActionContainer.Empty : smartTagTask.Result;
        }
    }
}
