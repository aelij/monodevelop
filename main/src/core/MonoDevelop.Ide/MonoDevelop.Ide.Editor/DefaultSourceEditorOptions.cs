﻿//
// DefaultSourceEditorOptions.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2014 Xamarin Inc. (http://xamarin.com)
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
using MonoDevelop.Core;
using MonoDevelop.Ide.Fonts;
using MonoDevelop.Ide.Gui.Content;

namespace MonoDevelop.Ide.Editor
{
    public enum WordNavigationStyle
    {
        Unix,
        Windows
    }

    public enum LineEndingConversion
    {
        Ask,
        LeaveAsIs,
        ConvertAlways
    }

    /// <summary>
    /// This class contains all text editor options from ITextEditorOptions and additional options
    /// the text editor frontend may use.  
    /// </summary>
    public sealed class DefaultSourceEditorOptions : ITextEditorOptions
    {
        private static DefaultSourceEditorOptions instance;
        //static TextStylePolicy defaultPolicy;
        private static bool inited;

        public static DefaultSourceEditorOptions Instance => instance;

        public static ITextEditorOptions PlainEditor
        {
            get;
            private set;
        }

        static DefaultSourceEditorOptions()
        {
            Init();
        }

        public static void Init()
        {
            if (inited)
                return;
            inited = true;

            var policy = new TextStylePolicy();
            instance = new DefaultSourceEditorOptions(policy);
            PlainEditor = new PlainEditorOptions();
        }

        internal void FireChange()
        {
            OnChanged(EventArgs.Empty);
        }

        private class PlainEditorOptions : ITextEditorOptions
        {
            #region IDisposable implementation

            void IDisposable.Dispose()
            {
                // nothing
            }

            #endregion

            #region ITextEditorOptions implementation

            WordFindStrategy ITextEditorOptions.WordFindStrategy => Instance.WordFindStrategy;

            bool ITextEditorOptions.TabsToSpaces => Instance.TabsToSpaces;

            int ITextEditorOptions.IndentationSize => Instance.IndentationSize;

            int ITextEditorOptions.TabSize => Instance.TabSize;

            bool ITextEditorOptions.ShowIconMargin => false;

            bool ITextEditorOptions.ShowLineNumberMargin => false;

            bool ITextEditorOptions.ShowFoldMargin => false;

            bool ITextEditorOptions.HighlightCaretLine => Instance.HighlightCaretLine;

            int ITextEditorOptions.RulerColumn => Instance.RulerColumn;

            bool ITextEditorOptions.ShowRuler => false;

            IndentStyle ITextEditorOptions.IndentStyle => Instance.IndentStyle;

            bool ITextEditorOptions.OverrideDocumentEolMarker => false;

            bool ITextEditorOptions.EnableSyntaxHighlighting => Instance.EnableSyntaxHighlighting;

            bool ITextEditorOptions.RemoveTrailingWhitespaces => Instance.RemoveTrailingWhitespaces;

            bool ITextEditorOptions.WrapLines => Instance.WrapLines;

            string ITextEditorOptions.FontName => Instance.FontName;

            string ITextEditorOptions.GutterFontName => Instance.GutterFontName;

            string ITextEditorOptions.ColorScheme => Instance.ColorScheme;

            string ITextEditorOptions.DefaultEolMarker => Instance.DefaultEolMarker;

            bool ITextEditorOptions.GenerateFormattingUndoStep => Instance.GenerateFormattingUndoStep;

            bool ITextEditorOptions.EnableSelectionWrappingKeys => Instance.EnableSelectionWrappingKeys;

            ShowWhitespaces ITextEditorOptions.ShowWhitespaces => ShowWhitespaces.Never;

            IncludeWhitespaces ITextEditorOptions.IncludeWhitespaces => Instance.IncludeWhitespaces;

            bool ITextEditorOptions.SmartBackspace => Instance.SmartBackspace;

            #endregion


        }

        private DefaultSourceEditorOptions(TextStylePolicy currentPolicy)
        {
            WordNavigationStyle defaultWordNavigation = WordNavigationStyle.Unix;
            if (Platform.IsWindows)
            {
                defaultWordNavigation = WordNavigationStyle.Windows;
            }
            wordNavigationStyle = ConfigurationProperty.Create("WordNavigationStyle", defaultWordNavigation);

            UpdateStylePolicy(currentPolicy);
            FontService.RegisterFontChangedCallback("Editor", UpdateFont);
            FontService.RegisterFontChangedCallback("MessageBubbles", UpdateFont);

            IdeApp.Preferences.ColorScheme.Changed += OnColorSchemeChanged;
        }

        private void UpdateFont()
        {
            OnChanged(EventArgs.Empty);
        }

        private void UpdateStylePolicy(TextStylePolicy currentPolicy)
        {
            defaultEolMarker = TextStylePolicy.GetEolMarker(currentPolicy.EolMarker);
            tabsToSpaces = currentPolicy.TabsToSpaces; // PropertyService.Get ("TabsToSpaces", false);
            indentationSize = currentPolicy.TabWidth; //PropertyService.Get ("TabIndent", 4);
            rulerColumn = currentPolicy.FileWidth; //PropertyService.Get ("RulerColumn", 80);
            allowTabsAfterNonTabs = !currentPolicy.NoTabsAfterNonTabs; //PropertyService.Get ("AllowTabsAfterNonTabs", true);
            removeTrailingWhitespaces = currentPolicy.RemoveTrailingWhitespace; //PropertyService.Get ("RemoveTrailingWhitespaces", true);
        }

        public ITextEditorOptions WithTextStyle(TextStylePolicy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            var result = (DefaultSourceEditorOptions)MemberwiseClone();
            result.UpdateStylePolicy(policy);
            result.Changed = null;
            return result;
        }

        #region new options

        public bool EnableAutoCodeCompletion
        {
            get { return IdeApp.Preferences.EnableAutoCodeCompletion; }
            set { IdeApp.Preferences.EnableAutoCodeCompletion.Set(value); }
        }

        private readonly ConfigurationProperty<bool> defaultRegionsFolding = ConfigurationProperty.Create("DefaultRegionsFolding", false);
        public bool DefaultRegionsFolding
        {
            get
            {
                return defaultRegionsFolding;
            }
            set
            {
                if (defaultRegionsFolding.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> defaultCommentFolding = ConfigurationProperty.Create("DefaultCommentFolding", true);
        public bool DefaultCommentFolding
        {
            get
            {
                return defaultCommentFolding;
            }
            set
            {
                if (defaultCommentFolding.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> enableSemanticHighlighting = ConfigurationProperty.Create("EnableSemanticHighlighting", true);
        public bool EnableSemanticHighlighting
        {
            get
            {
                return enableSemanticHighlighting;
            }
            set
            {
                if (enableSemanticHighlighting.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> tabIsReindent = ConfigurationProperty.Create("TabIsReindent", false);
        public bool TabIsReindent
        {
            get { return tabIsReindent; }
            set
            {
                if (tabIsReindent.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> autoInsertMatchingBracket = ConfigurationProperty.Create("AutoInsertMatchingBracket", false);
        public bool AutoInsertMatchingBracket
        {
            get
            {
                return autoInsertMatchingBracket;
            }
            set
            {
                if (autoInsertMatchingBracket.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> smartSemicolonPlacement = ConfigurationProperty.Create("SmartSemicolonPlacement", false);
        public bool SmartSemicolonPlacement
        {
            get
            {
                return smartSemicolonPlacement;
            }
            set
            {
                if (smartSemicolonPlacement.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<IndentStyle> indentStyle = ConfigurationProperty.Create("IndentStyle", IndentStyle.Smart);
        public IndentStyle IndentStyle
        {
            get
            {
                return indentStyle;
            }
            set
            {
                if (indentStyle.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> enableHighlightUsages = ConfigurationProperty.Create("EnableHighlightUsages", true);
        public bool EnableHighlightUsages
        {
            get
            {
                return enableHighlightUsages;
            }
            set
            {
                if (enableHighlightUsages.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<LineEndingConversion> lineEndingConversion = ConfigurationProperty.Create("LineEndingConversion", LineEndingConversion.LeaveAsIs);
        public LineEndingConversion LineEndingConversion
        {
            get
            {
                return lineEndingConversion;
            }
            set
            {
                if (lineEndingConversion.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        #endregion

        private readonly ConfigurationProperty<bool> onTheFlyFormatting = ConfigurationProperty.Create("OnTheFlyFormatting", true);
        public bool OnTheFlyFormatting
        {
            get
            {
                return onTheFlyFormatting;
            }
            set
            {
                if (onTheFlyFormatting.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        #region ITextEditorOptions

        private string defaultEolMarker = Environment.NewLine;
        public string DefaultEolMarker
        {
            get
            {
                return defaultEolMarker;
            }
            set
            {
                if (defaultEolMarker != value)
                {
                    defaultEolMarker = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }

        private readonly ConfigurationProperty<WordNavigationStyle> wordNavigationStyle;
        public WordNavigationStyle WordNavigationStyle
        {
            get
            {
                return wordNavigationStyle;
            }
            set
            {
                if (wordNavigationStyle.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        public WordFindStrategy WordFindStrategy
        {
            get
            {
                switch (WordNavigationStyle)
                {
                    case WordNavigationStyle.Windows:
                        return WordFindStrategy.SharpDevelop;
                    default:
                        return WordFindStrategy.Emacs;
                }
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        private bool allowTabsAfterNonTabs = true;
        public bool AllowTabsAfterNonTabs
        {
            get
            {
                return allowTabsAfterNonTabs;
            }
            set
            {
                if (allowTabsAfterNonTabs != value)
                {
                    PropertyService.Set("AllowTabsAfterNonTabs", value);
                    allowTabsAfterNonTabs = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }

        private bool tabsToSpaces;
        public bool TabsToSpaces
        {
            get
            {
                return tabsToSpaces;
            }
            set
            {
                if (tabsToSpaces != value)
                {
                    PropertyService.Set("TabsToSpaces", value);
                    tabsToSpaces = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }

        private int indentationSize = 4;
        public int IndentationSize
        {
            get
            {
                return indentationSize;
            }
            set
            {
                if (indentationSize != value)
                {
                    PropertyService.Set("TabIndent", value);
                    indentationSize = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }


        public string IndentationString => TabsToSpaces ? new string(' ', TabSize) : "\t";

        public int TabSize
        {
            get
            {
                return IndentationSize;
            }
            set
            {
                IndentationSize = value;
            }
        }


        private bool removeTrailingWhitespaces = true;
        public bool RemoveTrailingWhitespaces
        {
            get
            {
                return removeTrailingWhitespaces;
            }
            set
            {
                if (removeTrailingWhitespaces != value)
                {
                    PropertyService.Set("RemoveTrailingWhitespaces", value);
                    OnChanged(EventArgs.Empty);
                    removeTrailingWhitespaces = value;
                }
            }
        }

        private readonly ConfigurationProperty<bool> showLineNumberMargin = ConfigurationProperty.Create("ShowLineNumberMargin", true);
        public bool ShowLineNumberMargin
        {
            get
            {
                return showLineNumberMargin;
            }
            set
            {
                if (showLineNumberMargin.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> showFoldMargin = ConfigurationProperty.Create("ShowFoldMargin", false);
        public bool ShowFoldMargin
        {
            get
            {
                return showFoldMargin;
            }
            set
            {
                if (showFoldMargin.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private bool showIconMargin = true;
        public bool ShowIconMargin
        {
            get
            {
                return showIconMargin;
            }
            set
            {
                if (showIconMargin != value)
                {
                    PropertyService.Set("ShowIconMargin", value);
                    showIconMargin = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }

        private readonly ConfigurationProperty<bool> highlightCaretLine = ConfigurationProperty.Create("HighlightCaretLine", false);
        public bool HighlightCaretLine
        {
            get
            {
                return highlightCaretLine;
            }
            set
            {
                if (highlightCaretLine.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> enableSyntaxHighlighting = ConfigurationProperty.Create("EnableSyntaxHighlighting", true);
        public bool EnableSyntaxHighlighting
        {
            get
            {
                return enableSyntaxHighlighting;
            }
            set
            {
                if (enableSyntaxHighlighting.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        internal ConfigurationProperty<bool> highlightMatchingBracket = ConfigurationProperty.Create("HighlightMatchingBracket", true);
        public bool HighlightMatchingBracket
        {
            get
            {
                return highlightMatchingBracket;
            }
            set
            {
                if (highlightMatchingBracket.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private int rulerColumn = 80;

        public int RulerColumn
        {
            get
            {
                return rulerColumn;
            }
            set
            {
                if (rulerColumn != value)
                {
                    PropertyService.Set("RulerColumn", value);
                    rulerColumn = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }

        private readonly ConfigurationProperty<bool> showRuler = ConfigurationProperty.Create("ShowRuler", true);
        public bool ShowRuler
        {
            get
            {
                return showRuler;
            }
            set
            {
                if (showRuler.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> enableAnimations = ConfigurationProperty.Create("EnableAnimations", true);
        public bool EnableAnimations
        {
            get
            {
                return enableAnimations;
            }
            set
            {
                if (enableAnimations.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> drawIndentationMarkers = ConfigurationProperty.Create("DrawIndentationMarkers", false);
        public bool DrawIndentationMarkers
        {
            get
            {
                return drawIndentationMarkers;
            }
            set
            {
                if (drawIndentationMarkers.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> wrapLines = ConfigurationProperty.Create("WrapLines", false);
        public bool WrapLines
        {
            get
            {
                return wrapLines;
            }
            set
            {
                if (wrapLines.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> enableQuickDiff = ConfigurationProperty.Create("EnableQuickDiff", false);
        public bool EnableQuickDiff
        {
            get
            {
                return enableQuickDiff;
            }
            set
            {
                if (enableQuickDiff.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        public string FontName
        {
            get
            {
                return FontService.FilterFontName(FontService.GetUnderlyingFontName("Editor"));
            }
            set
            {
                throw new InvalidOperationException("Set font through font service");
            }
        }

        public string GutterFontName
        {
            get
            {
                return FontService.FilterFontName(FontService.GetUnderlyingFontName("Editor"));
            }
            set
            {
                throw new InvalidOperationException("Set font through font service");
            }
        }

        private readonly ConfigurationProperty<string> colorScheme = IdeApp.Preferences.ColorScheme;
        public string ColorScheme
        {
            get
            {
                return colorScheme;
            }
            set
            {
                colorScheme.Set(value);
            }
        }

        private void OnColorSchemeChanged(object sender, EventArgs e)
        {
            OnChanged(EventArgs.Empty);
        }

        private readonly ConfigurationProperty<bool> generateFormattingUndoStep = ConfigurationProperty.Create("GenerateFormattingUndoStep", false);
        public bool GenerateFormattingUndoStep
        {
            get
            {
                return generateFormattingUndoStep;
            }
            set
            {
                if (generateFormattingUndoStep.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> enableSelectionWrappingKeys = ConfigurationProperty.Create("EnableSelectionWrappingKeys", false);
        public bool EnableSelectionWrappingKeys
        {
            get
            {
                return enableSelectionWrappingKeys;
            }
            set
            {
                if (enableSelectionWrappingKeys.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private bool overrideDocumentEolMarker;
        public bool OverrideDocumentEolMarker
        {
            get
            {
                return overrideDocumentEolMarker;
            }
            set
            {
                if (overrideDocumentEolMarker != value)
                {
                    overrideDocumentEolMarker = value;
                    OnChanged(EventArgs.Empty);
                }
            }
        }

        private readonly ConfigurationProperty<ShowWhitespaces> showWhitespaces = ConfigurationProperty.Create("ShowWhitespaces", ShowWhitespaces.Never);
        public ShowWhitespaces ShowWhitespaces
        {
            get
            {
                return showWhitespaces;
            }
            set
            {
                if (showWhitespaces.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<IncludeWhitespaces> includeWhitespaces = ConfigurationProperty.Create("IncludeWhitespaces", IncludeWhitespaces.All);
        public IncludeWhitespaces IncludeWhitespaces
        {
            get
            {
                return includeWhitespaces;
            }
            set
            {
                if (includeWhitespaces.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }

        private readonly ConfigurationProperty<bool> smartBackspace = ConfigurationProperty.Create("SmartBackspace", true);
        public bool SmartBackspace
        {
            get
            {
                return smartBackspace;
            }
            set
            {
                if (smartBackspace.Set(value))
                    OnChanged(EventArgs.Empty);
            }
        }
        #endregion

        public void Dispose()
        {
            FontService.RemoveCallback(UpdateFont);
            IdeApp.Preferences.ColorScheme.Changed -= OnColorSchemeChanged;
        }

        protected void OnChanged(EventArgs args)
        {
            if (Changed != null)
                Changed(null, args);
        }

        public event EventHandler Changed;
    }
}

