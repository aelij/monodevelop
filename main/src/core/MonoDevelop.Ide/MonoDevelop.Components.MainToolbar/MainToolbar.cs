// 
// MainToolbar.cs
//  
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
// 
// Copyright (c) 2012 Xamarin Inc. (http://xamarin.com)
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
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using Pango;
using Xwt;
using Alignment = Gtk.Alignment;
using CairoHelper = Gdk.CairoHelper;
using ComboBox = Gtk.ComboBox;
using EllipsizeMode = Pango.EllipsizeMode;
using HBox = Gtk.HBox;
using Key = Xwt.Key;
using Label = Gtk.Label;
using TreeStore = Gtk.TreeStore;
using VBox = Gtk.VBox;
using Widget = Gtk.Widget;

namespace MonoDevelop.Components.MainToolbar
{
    internal class MainToolbar : EventBox, IMainToolbarView
    {
        private readonly HBox contentBox = new HBox(false, 0);

        private readonly HBox configurationCombosBox;

        private readonly ComboBox configurationCombo;
        private readonly TreeStore configurationStore = new TreeStore(typeof(string), typeof(IConfigurationModel));

        private readonly ComboBox runConfigurationCombo;
        private readonly TreeStore runConfigurationStore = new TreeStore(typeof(string), typeof(IRunConfigurationModel));

        private readonly ComboBox runtimeCombo;
        private readonly TreeStore runtimeStore = new TreeStore(typeof(IRuntimeModel));

        private readonly StatusArea statusArea;

        private readonly SearchEntry matchEntry;
        private static WeakReference lastCommandTarget;

        private readonly ButtonBar buttonBar = new ButtonBar();
        private readonly RoundButton button = new RoundButton();
        private readonly Alignment buttonBarBox;

        // attributes for the runtime combo (bold / normal)
        private readonly AttrList boldAttributes = new AttrList();
        private readonly AttrList normalAttributes = new AttrList();

        public ImageSurface Background { get; set; }

        public int TitleBarHeight { get; set; }

        public IStatusBar StatusBar => statusArea;

        internal static object LastCommandTarget => lastCommandTarget?.Target;

        private static bool RuntimeIsSeparator(TreeModel model, TreeIter iter)
        {
            var runtime = (IRuntimeModel)model.GetValue(iter, 0);
            return runtime == null || runtime.IsSeparator;
        }

        private void RuntimeRenderCell(CellLayout layout, CellRenderer cell, TreeModel model, TreeIter iter)
        {
            var runtime = (IRuntimeModel)model.GetValue(iter, 0);
            var renderer = (CellRendererText)cell;

            if (runtime == null || runtime.IsSeparator)
            {
                renderer.Xpad = 0;
                return;
            }

            using (var mutableModel = runtime.GetMutableModel())
            {
                renderer.Visible = mutableModel.Visible;
                renderer.Sensitive = mutableModel.Enabled;
                renderer.Xpad = (uint)(runtime.IsIndented ? 18 : 3);

                if (!runtimeCombo.PopupShown)
                {
                    // no need to ident text when the combo dropdown is not showing
                    if (Platform.IsWindows)
                        renderer.Xpad = 3;
                    renderer.Text = mutableModel.FullDisplayString;
                    renderer.Attributes = normalAttributes;
                }
                else
                {
                    renderer.Text = mutableModel.DisplayString;
                    renderer.Attributes = runtime.Notable ? boldAttributes : normalAttributes;
                }

            }
        }

        private TreeIter lastSelection = TreeIter.Zero;
        public MainToolbar()
        {
            WidgetFlags |= WidgetFlags.AppPaintable;

            AddWidget(button);
            AddSpace(8);

            configurationCombosBox = new HBox(false, 8);

            var ctx = new CellRendererText();

            runConfigurationCombo = new ComboBox { Model = runConfigurationStore };
            runConfigurationCombo.PackStart(ctx, true);
            runConfigurationCombo.AddAttribute(ctx, "text", 0);

            var runConfigurationComboVBox = new VBox();
            runConfigurationComboVBox.PackStart(runConfigurationCombo, true, false, 0);
            configurationCombosBox.PackStart(runConfigurationComboVBox, false, false, 0);

            configurationCombo = new ComboBox { Model = configurationStore };
            configurationCombo.PackStart(ctx, true);
            configurationCombo.AddAttribute(ctx, "text", 0);

            var configurationComboVBox = new VBox();
            configurationComboVBox.PackStart(configurationCombo, true, false, 0);
            configurationCombosBox.PackStart(configurationComboVBox, false, false, 0);

            // bold attributes for running runtime targets / (emulators)
            boldAttributes.Insert(new AttrWeight(Weight.Bold));

            runtimeCombo = new ComboBox { Model = runtimeStore };
            ctx = new CellRendererText();
            if (Platform.IsWindows)
                ctx.Ellipsize = EllipsizeMode.Middle;
            runtimeCombo.PackStart(ctx, true);
            runtimeCombo.SetCellDataFunc(ctx, RuntimeRenderCell);
            runtimeCombo.RowSeparatorFunc = RuntimeIsSeparator;

            var runtimeComboVBox = new VBox();
            runtimeComboVBox.PackStart(runtimeCombo, true, false, 0);
            configurationCombosBox.PackStart(runtimeComboVBox, false, false, 0);
            AddWidget(configurationCombosBox);

            buttonBarBox = new Alignment(0.5f, 0.5f, 0, 0) { LeftPadding = 7 };
            buttonBarBox.Add(buttonBar);
            buttonBarBox.NoShowAll = true;
            AddWidget(buttonBarBox);
            AddSpace(24);

            statusArea = new StatusArea();
            statusArea.ShowMessage(BrandingService.ApplicationName);

            var statusAreaAlign = new Alignment(0, 0, 1, 1) { statusArea };
            contentBox.PackStart(statusAreaAlign, true, true, 0);
            AddSpace(24);

            statusAreaAlign.SizeAllocated += (o, args) =>
            {
                Widget toplevel = Toplevel;
                if (toplevel == null)
                    return;

                int windowWidth = toplevel.Allocation.Width;
                int center = windowWidth / 2;
                int left = Math.Max(center - 300, args.Allocation.Left);
                int right = Math.Min(left + 600, args.Allocation.Right);
                uint leftPadding = (uint)(left - args.Allocation.Left);
                uint rightPadding = (uint)(args.Allocation.Right - right);

                if (leftPadding != statusAreaAlign.LeftPadding || rightPadding != statusAreaAlign.RightPadding)
                    statusAreaAlign.SetPadding(0, 0, leftPadding, rightPadding);
            };

            matchEntry = new SearchEntry { ForceFilterButtonVisible = true };

            matchEntry.Entry.FocusOutEvent += (o, e) => SearchEntryLostFocus?.Invoke(o, e);

            matchEntry.Ready = true;
            matchEntry.Visible = true;
            matchEntry.IsCheckMenu = true;
            matchEntry.WidthRequest = 240;
            if (!Platform.IsMac && !Platform.IsWindows)
                matchEntry.Entry.ModifyFont(FontDescription.FromString("Sans 9")); // TODO: VV: "Segoe UI 9"
            matchEntry.RoundedShape = true;
            matchEntry.Entry.Changed += HandleSearchEntryChanged;
            matchEntry.Activated += HandleSearchEntryActivated;
            matchEntry.Entry.KeyPressEvent += HandleSearchEntryKeyPressed;
            SizeAllocated += (o, e) => SearchEntryResized?.Invoke(o, e);

            contentBox.PackStart(matchEntry, false, false, 0);

            var align = new Alignment(0, 0, 1f, 1f);
            align.Show();
            align.TopPadding = 5;
            align.LeftPadding = 9;
            align.RightPadding = 18;
            align.BottomPadding = 10;
            align.Add(contentBox);

            Add(align);
            SetDefaultSizes(-1, 21);

            configurationCombo.Changed += (o, e) => ConfigurationChanged?.Invoke(o, e);
            runConfigurationCombo.Changed += (o, e) => RunConfigurationChanged?.Invoke(o, e);
            runtimeCombo.Changed += (o, e) =>
            {
                var ea = new HandledEventArgs();
                RuntimeChanged?.Invoke(o, ea);

                TreeIter it;
                if (runtimeCombo.GetActiveIter(out it))
                {
                    if (ea.Handled)
                    {
                        runtimeCombo.SetActiveIter(lastSelection);
                        return;
                    }
                    lastSelection = it;
                }
            };

            button.Clicked += HandleStartButtonClicked;

            IdeApp.CommandService.ActiveWidgetChanged += (sender, e) =>
            {
                lastCommandTarget = new WeakReference(e.OldActiveWidget);
            };

            ShowAll();
            statusArea.StatusIconBox.HideAll();
        }

        protected override bool OnButtonPressEvent(EventButton evnt)
        {
            if (evnt.Button == 1 && evnt.Window == GdkWindow)
            {
                var window = (Gtk.Window)Toplevel;
                if (!DesktopService.GetIsFullscreen(window))
                {
                    window.BeginMoveDrag(1, (int)evnt.XRoot, (int)evnt.YRoot, evnt.Time);
                    return true;
                }
            }
            return base.OnButtonPressEvent(evnt);
        }

        private void SetDefaultSizes(int comboHeight, int height)
        {
            configurationCombo.SetSizeRequest(150, comboHeight);
            runConfigurationCombo.SetSizeRequest(150, comboHeight);
            // make the windows runtime slightly wider to accomodate select devices text
            runtimeCombo.SetSizeRequest(Platform.IsWindows ? 175 : 150, comboHeight);
            statusArea.SetSizeRequest(32, 32);
            matchEntry.HeightRequest = height + 4;
            buttonBar.HeightRequest = height + 2;
        }

        private void AddSpace(int w)
        {
            Label la = new Label("") { WidthRequest = w };
            la.Show();
            contentBox.PackStart(la, false, false, 0);
        }

        private void HandleSearchEntryChanged(object sender, EventArgs e)
        {
            SearchEntryChanged?.Invoke(sender, e);
        }

        private void HandleSearchEntryActivated(object sender, EventArgs e)
        {
            SearchEntryActivated?.Invoke(sender, e);
        }

        private void HandleSearchEntryKeyPressed(object sender, KeyPressEventArgs e)
        {
            if (SearchEntryKeyPressed != null)
            {
                // TODO: Refactor this in Xwt as an extension method.
                var k = (Key)e.Event.KeyValue;
                var m = ModifierKeys.None;
                if ((e.Event.State & ModifierType.ShiftMask) != 0)
                    m |= ModifierKeys.Shift;
                if ((e.Event.State & ModifierType.ControlMask) != 0)
                    m |= ModifierKeys.Control;
                if ((e.Event.State & ModifierType.Mod1Mask) != 0)
                    m |= ModifierKeys.Alt;
                // TODO: Backport this one.
                if ((e.Event.State & ModifierType.Mod2Mask) != 0)
                    m |= ModifierKeys.Command;
                var kargs = new KeyEventArgs(k, m, false, e.Event.Time);
                SearchEntryKeyPressed(sender, kargs);
                e.RetVal = kargs.Handled;
            }
        }

        public void AddWidget(Widget widget)
        {
            contentBox.PackStart(widget, false, false, 0);
        }

        protected override bool OnExposeEvent(EventExpose evnt)
        {
            using (var context = CairoHelper.Create(evnt.Window))
            {
                context.Rectangle(
                    evnt.Area.X,
                    evnt.Area.Y,
                    evnt.Area.Width,
                    evnt.Area.Height
                );
                context.Clip();
                context.LineWidth = 1;
                if (Background != null && Background.Width > 0)
                {
                    for (int x = 0; x < Allocation.Width; x += Background.Width)
                    {
                        Background.Show(context, x, -TitleBarHeight);
                    }
                }
                else
                {
                    context.Rectangle(0, 0, Allocation.Width, Allocation.Height);
                    using (var lg = new LinearGradient(0, 0, 0, Allocation.Height))
                    {
                        lg.AddColorStop(0, Style.Light(StateType.Normal).ToCairoColor());
                        lg.AddColorStop(1, Style.Mid(StateType.Normal).ToCairoColor());
                        context.SetSource(lg);
                    }
                    context.Fill();

                }
                context.MoveTo(0, Allocation.Height - 0.5);
                context.RelLineTo(Allocation.Width, 0);
                context.SetSourceColor(Styles.ToolbarBottomBorderColor.ToCairoColor());
                context.Stroke();
            }
            return base.OnExposeEvent(evnt);
        }

        private void HandleStartButtonClicked(object sender, EventArgs e)
        {
            RunButtonClicked?.Invoke(sender, e);
        }

        protected override void OnDestroyed()
        {
            base.OnDestroyed();

            if (button != null)
                button.Clicked -= HandleStartButtonClicked;

            if (Background != null)
            {
                ((IDisposable)Background).Dispose();
                Background = null;
            }
        }

        #region IMainToolbarView implementation
        public event EventHandler RunButtonClicked;

        public bool RunButtonSensitivity
        {
            get { return button.Sensitive; }
            set { button.Sensitive = value; }
        }

        public OperationIcon RunButtonIcon
        {
            set { button.Icon = value; }
        }

        public bool ConfigurationPlatformSensitivity
        {
            get { return configurationCombosBox.Sensitive; }
            set { configurationCombosBox.Sensitive = value; }
        }

        private static bool FindIter<T>(TreeStore store, Func<T, bool> match, out TreeIter iter)
        {
            if (store.GetIterFirst(out iter))
            {
                do
                {
                    if (match((T)store.GetValue(iter, 1)))
                        return true;
                } while (store.IterNext(ref iter));
            }
            return false;
        }

        public IConfigurationModel ActiveConfiguration
        {
            get
            {
                TreeIter iter;
                if (!configurationCombo.GetActiveIter(out iter))
                    return null;

                return (IConfigurationModel)configurationStore.GetValue(iter, 1);
            }
            set
            {
                TreeIter iter;
                if (FindIter<IConfigurationModel>(configurationStore, it => value.OriginalId == it.OriginalId, out iter))
                    configurationCombo.SetActiveIter(iter);
                else
                    configurationCombo.Active = 0;
            }
        }

        public IRunConfigurationModel ActiveRunConfiguration
        {
            get
            {
                TreeIter iter;
                if (!runConfigurationCombo.GetActiveIter(out iter))
                    return null;

                return (IRunConfigurationModel)runConfigurationStore.GetValue(iter, 1);
            }
            set
            {
                TreeIter iter;
                if (FindIter<IRunConfigurationModel>(runConfigurationStore, it => value.OriginalId == it.OriginalId, out iter))
                    runConfigurationCombo.SetActiveIter(iter);
                else
                    runConfigurationCombo.Active = 0;
            }
        }

        public IRuntimeModel ActiveRuntime
        {
            get
            {
                TreeIter iter;
                if (!runtimeCombo.GetActiveIter(out iter))
                    return null;

                return (IRuntimeModel)runtimeStore.GetValue(iter, 0);
            }
            set
            {
                TreeIter iter;
                bool found = false;
                if (runtimeStore.GetIterFirst(out iter))
                {
                    do
                    {
                        if (value == runtimeStore.GetValue(iter, 0))
                        {
                            found = true;
                            break;
                        }
                    } while (runtimeStore.IterNext(ref iter));
                }
                if (found)
                    runtimeCombo.SetActiveIter(iter);
                else
                    runtimeCombo.Active = 0;
            }
        }

        public bool PlatformSensitivity
        {
            set { runtimeCombo.Sensitive = value; }
        }

        private IEnumerable<IConfigurationModel> configurationModel;
        public IEnumerable<IConfigurationModel> ConfigurationModel
        {
            get { return configurationModel; }
            set
            {
                configurationModel = value;
                configurationStore.Clear();
                foreach (var item in value)
                {
                    configurationStore.AppendValues(item.DisplayString, item);
                }
            }
        }

        private IEnumerable<IRunConfigurationModel> runConfigurationModel;
        public IEnumerable<IRunConfigurationModel> RunConfigurationModel
        {
            get { return runConfigurationModel; }
            set
            {
                runConfigurationModel = value;
                runConfigurationStore.Clear();
                foreach (var item in value)
                {
                    runConfigurationStore.AppendValues(item.DisplayString, item);
                }
            }
        }

        private IEnumerable<IRuntimeModel> runtimeModel;
        public IEnumerable<IRuntimeModel> RuntimeModel
        {
            get { return runtimeModel; }
            set
            {
                runtimeModel = value;
                runtimeStore.Clear();
                foreach (var item in value)
                {
                    if (item.HasParent)
                        continue;

                    var iter = runtimeStore.AppendValues(item);
                    foreach (var subitem in item.Children)
                        runtimeStore.AppendValues(iter, subitem);
                }
            }
        }

        public bool RunConfigurationVisible
        {
            get { return runConfigurationCombo.Visible; }
            set { runConfigurationCombo.Visible = value; }
        }

        public bool SearchSensivitity
        {
            set { matchEntry.Sensitive = value; }
        }

        public IEnumerable<ISearchMenuModel> SearchMenuItems
        {
            set
            {
                foreach (var item in value)
                {
                    var menuItem = matchEntry.AddMenuItem(item.DisplayString);
                    menuItem.Activated += delegate
                    {
                        item.NotifyActivated();
                    };
                }
            }
        }

        public string SearchCategory
        {
            set
            {
                matchEntry.Entry.Text = value;
                matchEntry.Entry.GrabFocus();
                var pos = matchEntry.Entry.Text.Length;
                matchEntry.Entry.SelectRegion(pos, pos);
            }
        }

        public void FocusSearchBar()
        {
            matchEntry.Entry.GrabFocus();
        }

        public event EventHandler SearchEntryChanged;
        public event EventHandler SearchEntryActivated;
        public event EventHandler<KeyEventArgs> SearchEntryKeyPressed;
        public event EventHandler SearchEntryResized;
        public event EventHandler SearchEntryLostFocus;

        public Widget PopupAnchor => matchEntry;

        public string SearchText
        {
            get { return matchEntry.Entry.Text; }
            set { matchEntry.Entry.Text = value; }
        }

        public string SearchPlaceholderMessage
        {
            set { matchEntry.EmptyMessage = value; }
        }

        public void RebuildToolbar(IEnumerable<IButtonBarButton> buttons)
        {
            var barButtons = buttons as IButtonBarButton[] ?? buttons.ToArray();
            if (!barButtons.Any())
            {
                buttonBarBox.Hide();
                return;
            }

            buttonBarBox.Show();
            buttonBar.ShowAll();
            buttonBar.Buttons = barButtons;
        }

        public bool ButtonBarSensitivity
        {
            set { buttonBar.Sensitive = value; }
        }

        public event EventHandler ConfigurationChanged;
        public event EventHandler RunConfigurationChanged;
        public event EventHandler<HandledEventArgs> RuntimeChanged;

        #endregion
    }
}

