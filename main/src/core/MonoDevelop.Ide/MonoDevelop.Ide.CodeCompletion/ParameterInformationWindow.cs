// ParameterInformationWindow.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2007 Novell, Inc (http://www.novell.com)
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
//
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Gdk;
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Core;
using MonoDevelop.Ide.Editor.Extension;
using MonoDevelop.Ide.Editor.Highlighting;
using MonoDevelop.Ide.Fonts;
using MonoDevelop.Ide.Gui;
using Color = Cairo.Color;
using WrapMode = Pango.WrapMode;

namespace MonoDevelop.Ide.CodeCompletion
{
    internal class ParameterInformationWindow : PopoverWindow
    {
        public CompletionTextEditorExtension Ext { get; set; }

        public ICompletionWidget Widget { get; set; }

        private readonly VBox descriptionBox = new VBox(false, 0);
        private readonly VBox vb2 = new VBox(false, 0);
        private Color foreColor;
        private readonly FixedWidthWrapLabel headlabel;

        public ParameterInformationWindow()
        {
            TypeHint = WindowTypeHint.Tooltip;
            SkipTaskbarHint = true;
            SkipPagerHint = true;
            AllowShrink = false;
            AllowGrow = false;
            CanFocus = false;
            CanDefault = false;
            WindowTransparencyDecorator.Attach(this);

            headlabel = new FixedWidthWrapLabel
            {
                Indent = -20,
                Wrap = WrapMode.WordChar,
                BreakOnCamelCasing = false,
                BreakOnPunctuation = false
            };

            descriptionBox.Spacing = 4;
            VBox vb = new VBox(false, 0);
            vb.PackStart(headlabel, true, true, 0);
            vb.PackStart(descriptionBox, true, true, 0);

            HBox hb = new HBox(false, 0);
            hb.PackStart(vb, true, true, 0);

            vb2.Spacing = 4;
            vb2.PackStart(hb, true, true, 0);
            ContentBox.Add(vb2);

            UpdateStyle();
            Styles.Changed += HandleThemeChanged;
            IdeApp.Preferences.ColorScheme.Changed += HandleThemeChanged;

            ShowAll();
            DesktopService.RemoveWindowShadow(this);
        }

        private void UpdateStyle()
        {
            var scheme = SyntaxModeService.GetColorStyle(IdeApp.Preferences.ColorScheme);
            if (!scheme.FitsIdeTheme(IdeApp.Preferences.UserInterfaceTheme))
                scheme = IdeApp.Preferences.UserInterfaceTheme.GetDefaultColorStyle();

            Theme.SetSchemeColors(scheme);
            Theme.Font = FontService.SansFont.CopyModified(Styles.FontScale11);
            Theme.ShadowColor = Styles.PopoverWindow.ShadowColor.ToCairoColor();
            foreColor = Styles.PopoverWindow.DefaultTextColor.ToCairoColor();

            headlabel.ModifyFg(StateType.Normal, foreColor.ToGdkColor());
            headlabel.FontDescription = FontService.GetFontDescription("Editor").CopyModified(Styles.FontScale11);

            if (Visible)
                QueueDraw();
        }

        private void HandleThemeChanged(object sender, EventArgs e)
        {
            UpdateStyle();
        }

        protected override void OnDestroyed()
        {
            base.OnDestroyed();
            Styles.Changed -= HandleThemeChanged;
            IdeApp.Preferences.ColorScheme.Changed -= HandleThemeChanged;
        }

        private int lastParam = -2;
        private TooltipInformation currentTooltipInformation;
        private ParameterHintingResult lastProvider;
        private CancellationTokenSource cancellationTokenSource;

        public async void ShowParameterInfo(ParameterHintingResult provider, int overload, int param, int maxSize)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            int numParams = Math.Max(0, provider[overload].ParameterCount);
            var currentParam = Math.Min(param, numParams - 1);
            if (numParams > 0 && currentParam < 0)
                currentParam = 0;
            if (lastParam == currentParam && currentTooltipInformation != null && ReferenceEquals (lastProvider, provider))
            {
                return;
            }
            lastProvider = provider;

            lastParam = currentParam;
            var parameterHintingData = provider[overload];

            ResetTooltipInformation();
            ClearDescriptions();
            if (Ext == null)
            {
                // ext == null means HideParameterInfo was called aka. we are not in valid context to display tooltip anymore
                lastParam = -2;
                return;
            }
            var ct = new CancellationTokenSource();
            try
            {
                cancellationTokenSource = ct;
                currentTooltipInformation = await parameterHintingData.CreateTooltipInformation(Ext.Editor, Ext.DocumentContext, currentParam, false, ct.Token);
            }
            catch (Exception ex)
            {
                if (!(ex is TaskCanceledException))
                    LoggingService.LogError("Error while getting tooltip information", ex);
                return;
            }

            if (ct.IsCancellationRequested)
                return;

            cancellationTokenSource = null;

            Theme.NumPages = provider.Count;
            Theme.CurrentPage = overload;

            if (provider.Count > 1)
            {
                Theme.DrawPager = true;
                Theme.PagerVertical = true;
            }

            ShowTooltipInfo();
        }

        private void ShowTooltipInfo()
        {
            ClearDescriptions();
            headlabel.Markup = currentTooltipInformation.SignatureMarkup;
            headlabel.Visible = true;
            if (Theme.DrawPager)
                headlabel.WidthRequest = headlabel.RealWidth + 70;

            foreach (var cat in currentTooltipInformation.Categories)
            {
                descriptionBox.PackStart(CreateCategory(TooltipInformationWindow.GetHeaderMarkup(cat.Item1), cat.Item2), true, true, 4);
            }

            if (!string.IsNullOrEmpty(currentTooltipInformation.SummaryMarkup))
            {
                descriptionBox.PackStart(CreateCategory(TooltipInformationWindow.GetHeaderMarkup("Summary"), currentTooltipInformation.SummaryMarkup), true, true, 4);
            }
            descriptionBox.ShowAll();
            QueueResize();
            Show();
        }

        private void ClearDescriptions()
        {
            while (descriptionBox.Children.Length > 0)
            {
                var child = descriptionBox.Children[0];
                descriptionBox.Remove(child);
                child.Destroy();
            }
        }

        private void ResetTooltipInformation()
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            }
            currentTooltipInformation = null;
        }

        private VBox CreateCategory(string categoryName, string categoryContentMarkup)
        {
            return TooltipInformationWindow.CreateCategory(categoryName, categoryContentMarkup, foreColor, Theme.Font);
        }

        public void ChangeOverload()
        {
            lastParam = -2;
            ResetTooltipInformation();
        }

        protected override void OnPagerLeftClicked()
        {
            if (Ext != null && Widget != null)
                ParameterInformationWindowManager.OverloadUp(Ext, Widget);
            base.OnPagerRightClicked();
        }

        protected override void OnPagerRightClicked()
        {
            if (Ext != null && Widget != null)
                ParameterInformationWindowManager.OverloadDown(Ext, Widget);
            base.OnPagerRightClicked();
        }

        public void HideParameterInfo()
        {
            ChangeOverload();
            Hide();
            Ext = null;
            Widget = null;
        }
    }
}
