// SourceEditorWidget.cs
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
using Gdk;
using GLib;
using Gtk;
using Mono.TextEditor;
using Mono.TextEditor.Theatrics;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Editor;
using Pango;
using Timeout = GLib.Timeout;

namespace MonoDevelop.SourceEditor
{
    class SourceEditorWidget : IServiceProvider
    {
        DecoratedScrolledWindow mainsw;

        // We need a reference to TextEditorData to be able to access the
        // editor document without getting the TextEditor property. This
        // property runs some gtk code, so it can only be used from the GUI thread.
        // Other threads can use textEditorData to get the document.
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        TextEditorData textEditorData;

        const uint CHILD_PADDING = 0;

        // VV: I removed the animation since it was very slow especially on @2x
        // TODO: Maybe the AddAnimationWidget () shouldn't be used at all
        const uint ANIMATION_DURATION = 0; // 300

        bool isDisposed;

        ExtensibleTextEditor textEditor;
        ExtensibleTextEditor splittedTextEditor;
        ExtensibleTextEditor lastActiveEditor;

        public ExtensibleTextEditor TextEditor => lastActiveEditor ?? textEditor;

        public bool HasMessageBar => messageBar != null;

        public VBox Vbox { get; } = new VBox();

        public bool SearchWidgetHasFocus
        {
            get
            {
                if (HasAnyFocusedChild(searchAndReplaceWidget) || HasAnyFocusedChild(gotoLineNumberWidget))
                    return true;
                return false;
            }
        }

        static bool HasAnyFocusedChild(Widget widget)
        {
            // Seems that this is the only reliable way doing it on os x and linux :/
            if (widget == null)
                return false;
            var stack = new Stack<Widget>();
            stack.Push(widget);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur.HasFocus)
                {
                    return true;
                }
                var c = cur as Container;
                if (c != null)
                {
                    foreach (var child in c.Children)
                    {
                        stack.Push(child);
                    }
                }
            }
            return false;
        }

        class DecoratedScrolledWindow : HBox
        {
            ScrolledWindow scrolledWindow;
            readonly EventBox scrolledBackground;

            public Adjustment Hadjustment => scrolledWindow.Hadjustment;

            public Adjustment Vadjustment => scrolledWindow.Vadjustment;

            public DecoratedScrolledWindow()
            {
                scrolledBackground = new EventBox();
                scrolledWindow = new CompactScrolledWindow();
                scrolledWindow.ButtonPressEvent += PrepareEvent;
                scrolledBackground.Add(scrolledWindow);
                PackStart(scrolledBackground, true, true, 0);
                //strip.VAdjustment = scrolledWindow.Vadjustment;
                //PackEnd(strip, false, true, 0);

                FancyFeaturesChanged();
            }

            void FancyFeaturesChanged()
            {
                GtkWorkarounds.SetOverlayScrollbarPolicy(scrolledWindow, PolicyType.Automatic, PolicyType.Never);
                SetSuppressScrollbar(true);
                QueueResize();
            }

            bool suppressScrollbar;

            void SetSuppressScrollbar(bool value)
            {
                if (suppressScrollbar == value)
                    return;
                suppressScrollbar = value;

                if (suppressScrollbar)
                {
                    scrolledWindow.VScrollbar.SizeRequested += SuppressSize;
                    scrolledWindow.VScrollbar.ExposeEvent += SuppressExpose;
                }
                else
                {
                    scrolledWindow.VScrollbar.SizeRequested -= SuppressSize;
                    scrolledWindow.VScrollbar.ExposeEvent -= SuppressExpose;
                }
            }

            [ConnectBefore]
            static void SuppressExpose(object o, ExposeEventArgs args)
            {
                args.RetVal = true;
            }

            [ConnectBefore]
            static void SuppressSize(object o, SizeRequestedArgs args)
            {
                args.Requisition = Requisition.Zero;
                args.RetVal = true;
            }

            protected override void OnDestroyed()
            {
                if (scrolledWindow == null)
                    return;
                if (scrolledWindow.Child != null)
                    RemoveEvents();
                SetSuppressScrollbar(false);
                scrolledWindow.ButtonPressEvent -= PrepareEvent;
                scrolledWindow.Vadjustment.Destroy();
                scrolledWindow.Hadjustment.Destroy();
                scrolledWindow.Destroy();
                scrolledWindow = null;
                base.OnDestroyed();
            }

            void PrepareEvent(object sender, ButtonPressEventArgs args)
            {
                args.RetVal = true;
            }

            public void SetTextEditor(MonoTextEditor container)
            {
                scrolledWindow.Child = container;
                container.EditorOptionsChanged += OptionsChanged;
            }

            void OptionsChanged(object sender, EventArgs e)
            {
                var editor = (MonoTextEditor)scrolledWindow.Child;
                scrolledBackground.ModifyBg(StateType.Normal, (HslColor)editor.ColorStyle.PlainText.Background);
            }

            void RemoveEvents()
            {
                var container = scrolledWindow.Child as MonoTextEditor;
                if (container == null)
                {

                    LoggingService.LogError("can't remove events from text editor container.");
                    return;
                }
                container.EditorOptionsChanged -= OptionsChanged;
            }

            public MonoTextEditor RemoveTextEditor()
            {
                var child = scrolledWindow.Child as MonoTextEditor;
                if (child == null)
                    return null;
                RemoveEvents();
                scrolledWindow.Remove(child);
                child.Unparent();
                return child;
            }
        }

        public SourceEditorWidget(SourceEditorView view)
        {
            View = view;
            Vbox.SetSizeRequest(32, 32);
            lastActiveEditor = textEditor = new ExtensibleTextEditor(view);
            textEditor.TextArea.FocusInEvent += (o, s) =>
            {
                lastActiveEditor = (ExtensibleTextEditor)((TextArea)o).GetTextEditorData().Parent;
                view.FireCompletionContextChanged();
            };
            textEditor.TextArea.FocusOutEvent += delegate
            {
                if (splittedTextEditor == null || !splittedTextEditor.TextArea.HasFocus)
                    OnLostFocus();
            };
            if (IdeApp.CommandService != null)
                IdeApp.FocusOut += IdeApp_FocusOut;
            mainsw = new DecoratedScrolledWindow();
            mainsw.SetTextEditor(textEditor);

            Vbox.PackStart(mainsw, true, true, 0);

            textEditorData = textEditor.GetTextEditorData();
            textEditorData.EditModeChanged += TextEditorData_EditModeChanged;

            ResetFocusChain();

            Vbox.BorderWidth = 0;
            Vbox.Spacing = 0;
            Vbox.Destroyed += delegate
            {
                if (isDisposed)
                    return;
                isDisposed = true;
                KillWidgets();

                if (textEditor != null && !textEditor.IsDestroyed)
                    textEditor.Destroy();

                if (splittedTextEditor != null && !splittedTextEditor.IsDestroyed)
                    splittedTextEditor.Destroy();

                lastActiveEditor = null;
                splittedTextEditor = null;
                textEditor = null;
                textEditorData.EditModeChanged -= TextEditorData_EditModeChanged;
                textEditorData = null;
                view = null;
            };
            Vbox.ShowAll();

        }

        void TextEditorData_EditModeChanged(object sender, EditModeChangedEventArgs e)
        {
            KillWidgets();
        }

        void IdeApp_FocusOut(object sender, EventArgs e)
        {
            textEditor.TextArea.HideTooltip(false);
        }

        void OnLostFocus()
        {
        }

        void ResetFocusChain()
        {
            List<Widget> focusChain = new List<Widget> { textEditor.TextArea };
            if (searchAndReplaceWidget != null)
            {
                focusChain.Add(searchAndReplaceWidget);
            }
            if (gotoLineNumberWidget != null)
            {
                focusChain.Add(gotoLineNumberWidget);
            }
            Vbox.FocusChain = focusChain.ToArray();
        }

        public void Dispose()
        {
            if (IdeApp.CommandService != null)
                IdeApp.FocusOut -= IdeApp_FocusOut;

            if (!isDisposed)
            {
                Vbox.Destroy();
                isDisposed = true;
            }
        }

        internal void SetLastActiveEditor(ExtensibleTextEditor editor)
        {
            lastActiveEditor = editor;
        }

        Paned splitContainer;

        public bool IsSplitted => splitContainer != null;

        public bool EditorHasFocus => TextEditor.TextArea.HasFocus;

        public SourceEditorView View { get; set; }

        public void Unsplit()
        {
            if (splitContainer == null)
                return;
            double vadjustment = mainsw.Vadjustment.Value;
            double hadjustment = mainsw.Hadjustment.Value;

            splitContainer.Remove(mainsw);
            secondsw.Destroy();
            secondsw = null;
            splittedTextEditor = null;

            Vbox.Remove(splitContainer);
            splitContainer.Destroy();
            splitContainer = null;

            RecreateMainSw();
            Vbox.PackStart(mainsw, true, true, 0);
            Vbox.ShowAll();
            mainsw.Vadjustment.Value = vadjustment;
            mainsw.Hadjustment.Value = hadjustment;
        }

        public void SwitchWindow()
        {
            if (splittedTextEditor.HasFocus)
            {
                textEditor.GrabFocus();
            }
            else
            {
                splittedTextEditor.GrabFocus();
            }
        }
        DecoratedScrolledWindow secondsw;

        public void Split(bool vSplit)
        {
            double vadjustment = mainsw.Vadjustment.Value;
            double hadjustment = mainsw.Hadjustment.Value;

            if (splitContainer != null)
                Unsplit();
            Vbox.Remove(mainsw);

            RecreateMainSw();

            splitContainer = vSplit ? new VPaned() : (Paned)new HPaned();

            splitContainer.Add1(mainsw);

            splitContainer.ButtonPressEvent += delegate (object sender, ButtonPressEventArgs args)
            {
                if (args.Event.Type == EventType.TwoButtonPress && args.RetVal == null)
                {
                    Unsplit();
                }
            };
            secondsw = new DecoratedScrolledWindow();
            splittedTextEditor = new ExtensibleTextEditor(View, textEditor.Options, textEditor.Document);
            splittedTextEditor.TextArea.FocusInEvent += (o, s) =>
            {
                lastActiveEditor = (ExtensibleTextEditor)((TextArea)o).GetTextEditorData().Parent;
                View.FireCompletionContextChanged();
            };
            splittedTextEditor.TextArea.FocusOutEvent += delegate
            {
                if (!textEditor.TextArea.HasFocus)
                    OnLostFocus();
            };
            splittedTextEditor.EditorExtension = textEditor.EditorExtension;
            if (textEditor.GetTextEditorData().HasIndentationTracker)
                splittedTextEditor.GetTextEditorData().IndentationTracker = textEditor.GetTextEditorData().IndentationTracker;
            splittedTextEditor.Document.BracketMatcher = textEditor.Document.BracketMatcher;

            secondsw.SetTextEditor(splittedTextEditor);
            splitContainer.Add2(secondsw);

            Vbox.PackStart(splitContainer, true, true, 0);
            splitContainer.Position = (vSplit ? Vbox.Allocation.Height : Vbox.Allocation.Width) / 2 - 1;

            Vbox.ShowAll();
            secondsw.Vadjustment.Value = mainsw.Vadjustment.Value = vadjustment;
            secondsw.Hadjustment.Value = mainsw.Hadjustment.Value = hadjustment;
        }

        void RecreateMainSw()
        {
            // destroy old scrolled window to work around bug 526721 - When splitting window vertically, 
            // the slider under left split is not shown unitl window is resized
            double vadjustment = mainsw.Vadjustment.Value;
            double hadjustment = mainsw.Hadjustment.Value;

            var removedTextEditor = mainsw.RemoveTextEditor();
            mainsw.Destroy();

            mainsw = new DecoratedScrolledWindow();
            mainsw.SetTextEditor(removedTextEditor);
            mainsw.Vadjustment.Value = vadjustment;
            mainsw.Hadjustment.Value = hadjustment;
            lastActiveEditor = textEditor;
        }


        //		void SplitContainerSizeRequested (object sender, SizeRequestedArgs args)
        //		{
        //			this.splitContainer.SizeRequested -= SplitContainerSizeRequested;
        //			this.splitContainer.Position = args.Requisition.Width / 2;
        //			this.splitContainer.SizeRequested += SplitContainerSizeRequested;
        //		}
        //		
        InfoBar messageBar;

        internal static string EllipsizeMiddle(string str, int truncLen)
        {
            if (str == null)
                return "";
            if (str.Length <= truncLen)
                return str;

            string delimiter = "...";
            int leftOffset = (truncLen - delimiter.Length) / 2;
            int rightOffset = str.Length - truncLen + leftOffset + delimiter.Length;
            return str.Substring(0, leftOffset) + delimiter + str.Substring(rightOffset);
        }

        public void ShowFileChangedWarning(bool multiple)
        {
            RemoveMessageBar();

            if (messageBar == null)
            {
                messageBar = new InfoBar(MessageType.Warning);
                messageBar.SetMessageLabel(GettextCatalog.GetString(
                    "<b>The file \"{0}\" has been changed outside of {1}.</b>\n" +
                    "Do you want to keep your changes, or reload the file from disk?",
                    EllipsizeMiddle(Document.FileName, 50), BrandingService.ApplicationName));

                var b1 = new Button ("_Reload from disk") {
                    Image = new ImageView (Stock.Refresh, IconSize.Button)
                };
                b1.Clicked += delegate
                {
                    Reload();
                    View.TextEditor.GrabFocus();
                };
                messageBar.ActionArea.Add(b1);

                var b2 = new Button ("_Keep changes") {
                    Image = new ImageView (Stock.Cancel, IconSize.Button)
                };
                b2.Clicked += delegate
                {
                    RemoveMessageBar();
                    View.LastSaveTimeUtc = File.GetLastWriteTimeUtc(View.ContentName);
                    View.WorkbenchWindow.ShowNotification = false;
                };
                messageBar.ActionArea.Add(b2);

                if (multiple)
                {
                    var b3 = new Button ("_Reload all") {
                        Image = new ImageView (Stock.Cancel, IconSize.Button)
                    };
                    b3.Clicked += delegate
                    {
                        FileRegistry.ReloadAllChangedFiles();
                    };
                    messageBar.ActionArea.Add(b3);

                    var b4 = new Button ("_Ignore all") {
                        Image = new ImageView (Stock.Cancel, IconSize.Button)
                    };
                    b4.Clicked += delegate
                    {
                        FileRegistry.IgnoreAllChangedFiles();
                    };
                    messageBar.ActionArea.Add(b4);
                }
            }

            View.IsDirty = true;
            View.WarnOverwrite = true;
            Vbox.PackStart(messageBar, false, false, CHILD_PADDING);
            Vbox.ReorderChild(messageBar, 0);
            messageBar.ShowAll();

            messageBar.QueueDraw();

            View.WorkbenchWindow.ShowNotification = true;
        }

        #region Eol marker check
        internal bool UseIncorrectMarkers { get; set; }
        internal bool HasIncorrectEolMarker
        {
            get
            {
                var document = Document;
                if (document == null)
                    return false;
                if (document.HasLineEndingMismatchOnTextSet)
                    return true;
                string eol = DetectedEolMarker;
                if (eol == null)
                    return false;
                return eol != textEditor.Options.DefaultEolMarker;
            }
        }
        string DetectedEolMarker
        {
            get
            {
                if (Document.HasLineEndingMismatchOnTextSet)
                    return "?";
                if (textEditor.IsDisposed)
                {
                    LoggingService.LogWarning("SourceEditorWidget.cs: HasIncorrectEolMarker was called on disposed source editor widget." + Environment.NewLine + Environment.StackTrace);
                    return null;
                }
                var firstLine = Document.GetLine(1);
                if (firstLine != null && firstLine.DelimiterLength > 0)
                {
                    string firstDelimiter = Document.GetTextAt(firstLine.Length, firstLine.DelimiterLength);
                    return firstDelimiter;
                }
                return null;
            }
        }

        internal void UpdateEolMarkerMessage(bool multiple)
        {
            if (UseIncorrectMarkers || DefaultSourceEditorOptions.Instance.LineEndingConversion == LineEndingConversion.LeaveAsIs)
                return;
            ShowIncorrectEolMarkers(Document.FileName);
        }

        internal bool EnsureCorrectEolMarker(string fileName)
        {
            if (UseIncorrectMarkers || DefaultSourceEditorOptions.Instance.LineEndingConversion == LineEndingConversion.LeaveAsIs)
                return true;
            if (HasIncorrectEolMarker)
            {
                switch (DefaultSourceEditorOptions.Instance.LineEndingConversion)
                {
                    case LineEndingConversion.Ask:
                        var hasMultipleIncorrectEolMarkers = FileRegistry.HasMultipleIncorrectEolMarkers;
                        ShowIncorrectEolMarkers(fileName);
                        if (hasMultipleIncorrectEolMarkers)
                        {
                            FileRegistry.UpdateEolMessages();
                        }
                        return false;
                    case LineEndingConversion.ConvertAlways:
                        ConvertLineEndings();
                        return true;
                    default:
                        return true;
                }
            }
            return true;
        }

        internal void ConvertLineEndings()
        {
            string correctEol = TextEditor.Options.DefaultEolMarker;
            var newText = new StringBuilder();
            int offset = 0;
            foreach (var line in Document.Lines)
            {
                newText.Append(TextEditor.GetTextAt(offset, line.Length));
                offset += line.LengthIncludingDelimiter;
                if (line.DelimiterLength > 0)
                    newText.Append(correctEol);
            }
            View.StoreSettings();
            View.ReplaceContent(Document.FileName, newText.ToString(), View.SourceEncoding);
            Document.HasLineEndingMismatchOnTextSet = false;
            View.LoadSettings();
        }

        static string GetEolString(string detectedEol)
        {
            switch (detectedEol)
            {
                case "\n":
                    return "UNIX";
                case "\r\n":
                    return "Windows";
                case "\r":
                    return "Mac";
                case "?":
                    return "mixed";
            }
            return "Unknown";
        }

        //TODO: Support multiple Overlays at once to display above each other
        internal void AddOverlay(Widget messageOverlayContent, Func<int> sizeFunc = null)
        {
            var messageOverlayWindow = new OverlayMessageWindow {
                Child = messageOverlayContent,
                SizeFunc = sizeFunc
            };
            messageOverlayWindow.ShowOverlay(TextEditor);
            messageOverlayWindows.Add(messageOverlayWindow);
        }

        internal void RemoveOverlay(Widget messageOverlayContent)
        {
            var window = messageOverlayWindows.FirstOrDefault(w => w.Child == messageOverlayContent);
            if (window == null)
                return;
            messageOverlayWindows.Remove(window);
            window.Destroy();
        }

        readonly List<OverlayMessageWindow> messageOverlayWindows = new List<OverlayMessageWindow>();
        HBox incorrectEolMessage;

        void ShowIncorrectEolMarkers(string fileName)
        {
            RemoveMessageBar();
            var hbox = new HBox {Spacing = 8};

            var image = new HoverCloseButton();
            hbox.PackStart(image, false, false, 0);
            var label = new Label(GettextCatalog.GetString("This file has line endings ({0}) which differ from the policy settings ({1}).", GetEolString(DetectedEolMarker), GetEolString(textEditor.Options.DefaultEolMarker)));
            var color = (HslColor)textEditor.ColorStyle.NotificationText.Foreground;
            label.ModifyFg(StateType.Normal, color);

            int w, h;
            label.Layout.GetPixelSize(out w, out h);
            label.Ellipsize = EllipsizeMode.End;

            hbox.PackStart(label, true, true, 0);
            var okButton = new Button (Stock.Ok) {WidthRequest = 60};

            // Small amount of vertical padding for the OK button.
            const int verticalPadding = 2;
            var vbox = new VBox();
            vbox.PackEnd(okButton, true, true, verticalPadding);
            hbox.PackEnd(vbox, false, false, 0);

            var list = new[] {
                GettextCatalog.GetString ("Convert to {0} line endings", GetEolString (textEditor.Options.DefaultEolMarker)),
                GettextCatalog.GetString ("Convert all files to {0} line endings", GetEolString (textEditor.Options.DefaultEolMarker)),
                GettextCatalog.GetString ("Keep {0} line endings", GetEolString (DetectedEolMarker)),
                GettextCatalog.GetString ("Keep {0} line endings in all files", GetEolString (DetectedEolMarker))
            };
            var combo = new ComboBox (list) {Active = 0};
            hbox.PackEnd(combo, false, false, 0);
            incorrectEolMessage = new HBox();
            const int containerPadding = 8;
            incorrectEolMessage.PackStart(hbox, true, true, containerPadding);

            // This is hacky, but it will ensure that our combo appears with with the correct size.
            Timeout.Add(100, delegate
            {
                combo.QueueResize();
                return false;
            });

            AddOverlay(incorrectEolMessage, () => okButton.SizeRequest().Width +
                                                  combo.SizeRequest().Width +
                                                  image.SizeRequest().Width +
                                                  w +
                                                  hbox.Spacing * 4 +
                                                  containerPadding * 2);

            image.Clicked += delegate
            {
                UseIncorrectMarkers = true;
                View.WorkbenchWindow.ShowNotification = false;
                RemoveMessageBar();
            };
            okButton.Clicked += async delegate
            {
                switch (combo.Active)
                {
                    case 0:
                        ConvertLineEndings();
                        View.WorkbenchWindow.ShowNotification = false;
                        await View.Save(fileName, View.SourceEncoding);
                        break;
                    case 1:
                        FileRegistry.ConvertLineEndingsInAllFiles();
                        break;
                    case 2:
                        UseIncorrectMarkers = true;
                        View.WorkbenchWindow.ShowNotification = false;
                        break;
                    case 3:
                        FileRegistry.IgnoreLineEndingsInAllFiles();
                        break;
                }
                RemoveMessageBar();
            };
        }
        #endregion
        public void ShowAutoSaveWarning(string fileName)
        {
            RemoveMessageBar();
            TextEditor.Visible = false;
            if (messageBar == null)
            {
                messageBar = new InfoBar(MessageType.Warning);
                messageBar.SetMessageLabel(BrandingService.BrandApplicationName("<b>An autosave file has been found for this file.</b>\n" +
                                                                                "This could mean that another instance of MonoDevelop is editing this " +
                                                                                "file, or that MonoDevelop crashed with unsaved changes.\n\n" +
                                                                                "Do you want to use the original file, or load from the autosave file?"));

                Button b1 = new Button ("_Use original file") {
                    Image = new ImageView (Stock.Refresh, IconSize.Button)
                };
                b1.Clicked += delegate
                {
                    try
                    {
                        TextEditor.GrabFocus();
                        View.Load(fileName);
                        View.WorkbenchWindow.Document.ReparseDocument();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("Could not remove the autosave file.", ex);
                    }
                    finally
                    {
                        RemoveMessageBar();
                    }
                };
                messageBar.ActionArea.Add(b1);
            }

            View.IsDirty = true;
            View.WarnOverwrite = true;
            Vbox.PackStart(messageBar, false, false, CHILD_PADDING);
            Vbox.ReorderChild(messageBar, 0);
            messageBar.ShowAll();

            messageBar.QueueDraw();

            //			view.WorkbenchWindow.ShowNotification = true;
        }


        public void RemoveMessageBar()
        {
            if (messageBar != null)
            {
                if (messageBar.Parent == Vbox)
                    Vbox.Remove(messageBar);
                messageBar.Destroy();
                messageBar = null;
            }
            if (!TextEditor.Visible)
                TextEditor.Visible = true;
            if (incorrectEolMessage != null)
            {
                RemoveOverlay(incorrectEolMessage);
                incorrectEolMessage = null;
            }
        }

        public async void Reload()
        {
            try
            {
                if (!File.Exists(View.ContentName) || isDisposed)
                    return;

                View.StoreSettings();
                await View.Load(View.ContentName, View.SourceEncoding, true);
                View.WorkbenchWindow.ShowNotification = false;
            }
            catch (Exception ex)
            {
                MessageService.ShowError("Could not reload the file.", ex);
            }
            finally
            {
                RemoveMessageBar();
            }
        }

        #region Search and Replace
        RoundedFrame searchAndReplaceWidgetFrame;
        SearchAndReplaceWidget searchAndReplaceWidget;
        RoundedFrame gotoLineNumberWidgetFrame;
        GotoLineNumberWidget gotoLineNumberWidget;

        bool KillWidgets()
        {
            bool result = false;
            if (searchAndReplaceWidgetFrame != null)
            {
                searchAndReplaceWidgetFrame.Destroy();
                searchAndReplaceWidgetFrame = null;
                searchAndReplaceWidget = null;
                result = true;
                //clears any message it may have set
                IdeApp.Workbench.StatusBar.ShowReady();
            }

            if (gotoLineNumberWidgetFrame != null)
            {
                gotoLineNumberWidgetFrame.Destroy();
                gotoLineNumberWidgetFrame = null;
                gotoLineNumberWidget = null;
                result = true;
            }

            if (textEditor != null)
                textEditor.HighlightSearchPattern = false;
            if (splittedTextEditor != null)
                splittedTextEditor.HighlightSearchPattern = false;

            if (!isDisposed)
                ResetFocusChain();
            return result;
        }

        internal bool RemoveSearchWidget()
        {
            bool result = KillWidgets();
            if (!isDisposed)
                TextEditor.GrabFocus();
            return result;
        }

        public void EmacsFindNext()
        {
            if (searchAndReplaceWidget == null)
            {
                ShowSearchWidget();
            }
            else
            {
                FindNext();
            }
        }

        public void EmacsFindPrevious()
        {
            if (searchAndReplaceWidget == null)
            {
                ShowSearchWidget();
            }
            else
            {
                FindPrevious();
            }
        }

        public void ShowSearchWidget()
        {
            ShowSearchReplaceWidget(false);
        }

        public void ShowReplaceWidget()
        {
            ShowSearchReplaceWidget(true);
        }

        internal void OnUpdateUseSelectionForFind(CommandInfo info)
        {
            info.Enabled = searchAndReplaceWidget != null && TextEditor.IsSomethingSelected;
        }

        public void UseSelectionForFind()
        {
            SetSearchPatternToSelection();
        }

        internal void OnUpdateUseSelectionForReplace(CommandInfo info)
        {
            info.Enabled = searchAndReplaceWidget != null && TextEditor.IsSomethingSelected;
        }

        public void UseSelectionForReplace()
        {
            SetReplacePatternToSelection();
        }

        void ShowSearchReplaceWidget(bool replace, bool switchFocus = true)
        {
            if (searchAndReplaceWidget == null)
            {
                KillWidgets();
                searchAndReplaceWidgetFrame = new RoundedFrame();
                //searchAndReplaceWidgetFrame.SetFillColor (MonoDevelop.Components.CairoExtensions.GdkColorToCairoColor (widget.TextEditor.ColorStyle.Default.BackgroundColor));
                searchAndReplaceWidgetFrame.SetFillColor(CairoExtensions.GdkColorToCairoColor(Vbox.Style.Background(StateType.Normal)));

                searchAndReplaceWidgetFrame.Child = searchAndReplaceWidget = new SearchAndReplaceWidget(TextEditor, searchAndReplaceWidgetFrame);
                searchAndReplaceWidget.Destroyed += (sender, e) => RemoveSearchWidget();
                searchAndReplaceWidgetFrame.ShowAll();
                TextEditor.AddAnimatedWidget(searchAndReplaceWidgetFrame, ANIMATION_DURATION, Easing.ExponentialInOut, Blocking.Downstage, TextEditor.Allocation.Width - 400, -searchAndReplaceWidget.Allocation.Height);
                //				this.PackEnd (searchAndReplaceWidget);
                //				this.SetChildPacking (searchAndReplaceWidget, false, false, CHILD_PADDING, PackType.End);
                //		searchAndReplaceWidget.ShowAll ();
                if (splittedTextEditor != null)
                {
                    splittedTextEditor.HighlightSearchPattern = true;
                    splittedTextEditor.TextViewMargin.RefreshSearchMarker();
                }
                ResetFocusChain();

            }
            else
            {
                if (TextEditor.IsSomethingSelected)
                {
                    searchAndReplaceWidget.SetSearchPattern();
                }
            }
            searchAndReplaceWidget.UpdateSearchPattern();
            searchAndReplaceWidget.IsReplaceMode = replace;
            if (searchAndReplaceWidget.SearchFocused)
            {
                if (replace)
                {
                    searchAndReplaceWidget.Replace();
                }
                else
                {
                    FindNext();
                }
            }
            if (switchFocus)
                searchAndReplaceWidget.Focus();
        }

        public void ShowGotoLineNumberWidget()
        {
            if (gotoLineNumberWidget == null)
            {
                KillWidgets();


                gotoLineNumberWidgetFrame = new RoundedFrame();
                //searchAndReplaceWidgetFrame.SetFillColor (MonoDevelop.Components.CairoExtensions.GdkColorToCairoColor (widget.TextEditor.ColorStyle.Default.BackgroundColor));
                gotoLineNumberWidgetFrame.SetFillColor(CairoExtensions.GdkColorToCairoColor(Vbox.Style.Background(StateType.Normal)));

                gotoLineNumberWidgetFrame.Child = gotoLineNumberWidget = new GotoLineNumberWidget(textEditor, gotoLineNumberWidgetFrame);
                gotoLineNumberWidget.Destroyed += (sender, e) => RemoveSearchWidget();
                gotoLineNumberWidgetFrame.ShowAll();
                TextEditor.AddAnimatedWidget(gotoLineNumberWidgetFrame, ANIMATION_DURATION, Easing.ExponentialInOut, Blocking.Downstage, TextEditor.Allocation.Width - 400, -gotoLineNumberWidget.Allocation.Height);

                ResetFocusChain();
            }

            gotoLineNumberWidget.Focus();
        }


        public SearchResult FindNext()
        {
            return FindNext(true);
        }

        public SearchResult FindNext(bool focus)
        {
            if (searchAndReplaceWidget == null)
                ShowSearchReplaceWidget(false, false);
            return SearchAndReplaceWidget.FindNext(TextEditor);
        }

        public SearchResult FindPrevious()
        {
            return FindPrevious(true);
        }

        public SearchResult FindPrevious(bool focus)
        {
            if (searchAndReplaceWidget == null)
                ShowSearchReplaceWidget(false, false);
            return SearchAndReplaceWidget.FindPrevious(TextEditor);
        }

        internal static string FormatPatternToSelectionOption(string pattern)
        {
            // TODO-AELIJ
            return pattern;
        }

        void SetSearchPatternToSelection()
        {
            if (!TextEditor.IsSomethingSelected)
            {
                int start = textEditor.Caret.Offset;
                int end = start;
                while (start - 1 >= 0 && DynamicAbbrevHandler.IsIdentifierPart(textEditor.GetCharAt(start - 1)))
                    start--;

                while (end < textEditor.Length && DynamicAbbrevHandler.IsIdentifierPart(textEditor.GetCharAt(end)))
                    end++;
                textEditor.Caret.Offset = end;
                TextEditor.SetSelection(start, end);
            }

            if (TextEditor.IsSomethingSelected)
            {
                var pattern = FormatPatternToSelectionOption(TextEditor.SelectedText);
                SearchAndReplaceOptions.SearchPattern = pattern;
                SearchAndReplaceWidget.UpdateSearchHistory(TextEditor.SearchPattern);
            }
            searchAndReplaceWidget?.UpdateSearchPattern();
        }

        void SetReplacePatternToSelection()
        {
            if (searchAndReplaceWidget != null && TextEditor.IsSomethingSelected)
                searchAndReplaceWidget.ReplacePattern = TextEditor.SelectedText;
        }

        public SearchResult FindNextSelection()
        {
            SetSearchPatternToSelection();
            TextEditor.GrabFocus();
            return FindNext();
        }

        public SearchResult FindPreviousSelection()
        {
            SetSearchPatternToSelection();
            TextEditor.GrabFocus();
            return FindPrevious();
        }

        #endregion

        public TextDocument Document
        {
            get
            {
                var editor = TextEditor;
                return editor?.Document;
            }
        }

        #region commenting and indentation

        public void OnUpdateToggleErrorTextMarker(CommandInfo info)
        {
            DocumentLine line = TextEditor.Document.GetLine(TextEditor.Caret.Line);
            if (line == null)
            {
                info.Visible = false;
            }
            // TODO-AELIJ: visible marker?
        }

        public void OnToggleErrorTextMarker()
        {
            // TODO-AELIJ: hide marker?
            //DocumentLine line = TextEditor.Document.GetLine(TextEditor.Caret.Line);
            //if (line == null)
            //    return;
            //var marker = (MessageBubbleTextMarker)line.Markers.FirstOrDefault(m => m is MessageBubbleTextMarker);
            //if (marker != null)
            //{
            //    marker.IsVisible = !marker.IsVisible;
            //    TextEditor.QueueDraw();
            //}
        }

        #endregion


        #region IServiceProvider implementation
        object IServiceProvider.GetService(Type serviceType)
        {
            return View.GetContent(serviceType);
        }
        #endregion
    }

}
