//
// TabStrip.cs
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
using System.Collections.Generic;
using System.Linq;
using Gdk;
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Ide.Fonts;
using MonoDevelop.Ide.Gui;
using Pango;
using Xwt;
using Xwt.Drawing;
using Xwt.Motion;
using Alignment = Gtk.Alignment;
using Button = Gtk.Button;
using CairoHelper = Gdk.CairoHelper;
using Context = Cairo.Context;
using EllipsizeMode = Pango.EllipsizeMode;
using Image = Xwt.Drawing.Image;
using Layout = Pango.Layout;
using LinearGradient = Cairo.LinearGradient;
using Rectangle = Gdk.Rectangle;
using Scale = Pango.Scale;
using Widget = Gtk.Widget;

namespace MonoDevelop.Components.DockNotebook
{
    internal class TabStrip : EventBox, IAnimatable
    {
        private static readonly Image TabbarPrevImage = Image.FromResource("tabbar-prev-12.png");
        private static readonly Image TabbarNextImage = Image.FromResource("tabbar-next-12.png");
        private static readonly Image TabActiveBackImage = Image.FromResource("tabbar-active.9.png");
        private static readonly Image TabBackImage = Image.FromResource("tabbar-inactive.9.png");
        private static readonly Image TabbarBackImage = Image.FromResource("tabbar-back.9.png");
        private static readonly Image TabCloseImage = Image.FromResource("tab-close-9.png");
        private static readonly Image TabDirtyImage = Image.FromResource("tab-dirty-9.png");

        private readonly List<Widget> children = new List<Widget>();
        private readonly DockNotebook notebook;
        private DockNotebookTab highlightedTab;
        private bool overCloseButton;
        private bool buttonPressedOnTab;
        private int tabStartX, tabEndX;
        private bool isActiveNotebook;

        private readonly MouseTracker tracker;

        private bool draggingTab;
        private int dragX;
        private int dragOffset;
        private double dragXProgress;

        private int renderOffset;
        private int targetOffset;
        private int animationTarget;

        private readonly Dictionary<int, DockNotebookTab> closingTabs;

        public Button PreviousButton;
        public Button NextButton;
        public MenuButton DropDownButton;

        private static readonly int TotalHeight = 32;
        private static readonly WidgetSpacing TabPadding;
        private static readonly WidgetSpacing TabActivePadding;
        private static readonly int LeftBarPadding = 44;
        private static readonly int RightBarPadding = 22;
        private static readonly int VerticalTextSize = 11;
        private const int TabSpacing = 0;
        private const int LeanWidth = 12;
        private const double CloseButtonMarginRight = 0;
        private const double CloseButtonMarginBottom = -1.0;

        private int TabWidth { get; set; }

        private int LastTabWidthAdjustment { get; set; }

        private int targetWidth;

        private int TargetWidth
        {
            get { return targetWidth; }
            set
            {
                targetWidth = value;
                if (TabWidth != value)
                {
                    this.Animate("TabWidth",
                        f => TabWidth = (int)f,
                        TabWidth,
                        value,
                        easing: Easing.CubicOut);
                }
            }
        }

        public bool NavigationButtonsVisible
        {
            get { return children.Contains(PreviousButton); }
            set
            {
                if (value == NavigationButtonsVisible)
                    return;
                if (value)
                {
                    children.Add(NextButton);
                    children.Add(PreviousButton);
                    OnSizeAllocated(Allocation);
                    PreviousButton.ShowAll();
                    NextButton.ShowAll();
                }
                else
                {
                    children.Remove(PreviousButton);
                    children.Remove(NextButton);
                    OnSizeAllocated(Allocation);
                }
            }
        }

        static TabStrip()
        {
            NinePatchImage tabBackImage9;
            if (TabBackImage is ThemedImage)
            {
                var img = ((ThemedImage)TabBackImage).GetImage(Xwt.Drawing.Context.GlobalStyles);
                tabBackImage9 = img as NinePatchImage;
            }
            else
                tabBackImage9 = TabBackImage as NinePatchImage;
            TabPadding = tabBackImage9.Padding;


            NinePatchImage tabActiveBackImage9;
            if (TabActiveBackImage is ThemedImage)
            {
                var img = ((ThemedImage)TabActiveBackImage).GetImage(Xwt.Drawing.Context.GlobalStyles);
                tabActiveBackImage9 = img as NinePatchImage;
            }
            else
                tabActiveBackImage9 = TabBackImage as NinePatchImage;
            TabActivePadding = tabActiveBackImage9.Padding;
        }

        public TabStrip(DockNotebook notebook)
        {
            if (notebook == null)
                throw new ArgumentNullException(nameof(notebook));
            TabWidth = 125;
            TargetWidth = 125;
            tracker = new MouseTracker(this);
            GtkWorkarounds.FixContainerLeak(this);

            this.notebook = notebook;
            WidgetFlags |= WidgetFlags.AppPaintable;
            Events |= EventMask.PointerMotionMask | EventMask.LeaveNotifyMask | EventMask.ButtonPressMask;

            var arr = new Xwt.ImageView(TabbarPrevImage);
            arr.HeightRequest = arr.WidthRequest = 10;

            var alignment = new Alignment(0.5f, 1, 0.0f, 0.0f);
            alignment.Add(arr.ToGtkWidget());
            PreviousButton = new Button(alignment);
            PreviousButton.TooltipText = "Switch to previous document";
            PreviousButton.Relief = ReliefStyle.None;
            PreviousButton.CanDefault = PreviousButton.CanFocus = false;

            arr = new Xwt.ImageView(TabbarNextImage);
            arr.HeightRequest = arr.WidthRequest = 10;

            alignment = new Alignment(0.5f, 1, 0.0f, 0.0f);
            alignment.Add(arr.ToGtkWidget());
            NextButton = new Button(alignment);
            NextButton.TooltipText = "Switch to next document";
            NextButton.Relief = ReliefStyle.None;
            NextButton.CanDefault = NextButton.CanFocus = false;

            DropDownButton = new MenuButton();
            DropDownButton.TooltipText = "Document List";
            DropDownButton.Relief = ReliefStyle.None;
            DropDownButton.CanDefault = DropDownButton.CanFocus = false;

            PreviousButton.ShowAll();
            NextButton.ShowAll();
            DropDownButton.ShowAll();

            PreviousButton.Name = "MonoDevelop.DockNotebook.BarButton";
            NextButton.Name = "MonoDevelop.DockNotebook.BarButton";
            DropDownButton.Name = "MonoDevelop.DockNotebook.BarButton";

            PreviousButton.Parent = this;
            NextButton.Parent = this;
            DropDownButton.Parent = this;

            children.Add(PreviousButton);
            children.Add(NextButton);
            children.Add(DropDownButton);

            tracker.HoveredChanged += (sender, e) =>
            {
                if (!tracker.Hovered)
                {
                    SetHighlightedTab(null);
                    UpdateTabWidth(tabEndX - tabStartX);
                    QueueDraw();
                }
            };

            notebook.PageAdded += (sender, e) => QueueResize();
            notebook.PageRemoved += (sender, e) => QueueResize();

            closingTabs = new Dictionary<int, DockNotebookTab>();
        }

        protected override void OnDestroyed()
        {
            this.AbortAnimation("TabWidth");
            this.AbortAnimation("EndDrag");
            this.AbortAnimation("ScrollTabs");
            base.OnDestroyed();
        }

        void IAnimatable.BatchBegin()
        {
        }

        void IAnimatable.BatchCommit()
        {
            QueueDraw();
        }

        public bool IsActiveNotebook
        {
            get
            {
                return isActiveNotebook;
            }
            set
            {
                isActiveNotebook = value;
                QueueDraw();
            }
        }

        public void StartOpenAnimation(DockNotebookTab tab)
        {
            tab.WidthModifier = 0;
            new Animation(f => tab.WidthModifier = f)
                .AddConcurrent(new Animation(f => tab.Opacity = f), 0.0d, 0.2d)
                .Commit(tab, "Open", easing: Easing.CubicInOut);
        }

        public void StartCloseAnimation(DockNotebookTab tab)
        {
            closingTabs[tab.Index] = tab;
            new Animation(f => tab.WidthModifier = f, tab.WidthModifier, 0)
                .AddConcurrent(new Animation(f => tab.Opacity = f, tab.Opacity, 0), 0.8d)
                .Commit(tab, "Closing",
                easing: Easing.CubicOut,
                finished: (f, a) =>
                {
                    if (!a)
                        closingTabs.Remove(tab.Index);
                });
        }

        protected override void ForAll(bool includeInternals, Callback callback)
        {
            base.ForAll(includeInternals, callback);
            for (int i = 0; i < children.Count; ++i)
                callback(children[i]);
        }

        protected override void OnRemoved(Widget widget)
        {
            children.Remove(widget);
        }

        protected override void OnSizeAllocated(Rectangle allocation)
        {
            if (NavigationButtonsVisible)
            {
                tabStartX = /*allocation.X +*/ LeftBarPadding + LeanWidth / 2;
            }
            else
            {
                tabStartX = LeanWidth / 2;
            }
            tabEndX = allocation.Width - RightBarPadding;
            var height = allocation.Height;

            PreviousButton.SizeAllocate(new Rectangle(
                0, // allocation.X,
                0, // allocation.Y,
                LeftBarPadding / 2,
                height
            )
            );
            NextButton.SizeAllocate(new Rectangle(
                LeftBarPadding / 2,
                0,
                LeftBarPadding / 2, height)
            );

            var image = PreviousButton.Child;
            int buttonWidth = LeftBarPadding / 2;
            image.SizeAllocate(new Rectangle(
                (buttonWidth - 12) / 2,
                (height - 12) / 2,
                12, 12)
            );

            image = NextButton.Child;
            image.SizeAllocate(new Rectangle(
                buttonWidth + (buttonWidth - 12) / 2,
                (height - 12) / 2,
                12, 12)
            );

            DropDownButton.SizeAllocate(new Rectangle(
                tabEndX,
                allocation.Y,
                DropDownButton.SizeRequest().Width,
                height));

            base.OnSizeAllocated(allocation);
            Update();
        }

        protected override void OnSizeRequested(ref Requisition requisition)
        {
            base.OnSizeRequested(ref requisition);
            requisition.Height = TotalHeight;
            requisition.Width = 0;
        }

        internal void InitSize()
        {
        }

        public int BarHeight => TotalHeight;

        private int lastDragX;

        private void SetHighlightedTab(DockNotebookTab tab)
        {
            if (highlightedTab == tab)
                return;

            if (highlightedTab != null)
            {
                var tmp = highlightedTab;
                tmp.Animate("Glow",
                    f => tmp.GlowStrength = f,
                    start: tmp.GlowStrength,
                    end: 0);
            }

            if (tab != null)
            {
                tab.Animate("Glow",
                    f => tab.GlowStrength = f,
                    start: tab.GlowStrength,
                    end: 1);
            }

            highlightedTab = tab;
            QueueDraw();
        }

        private PlaceholderWindow placeholderWindow;
        private bool mouseHasLeft;

        protected override bool OnLeaveNotifyEvent(EventCrossing evnt)
        {
            if (draggingTab && placeholderWindow == null && !mouseHasLeft)
                mouseHasLeft = true;
            return base.OnLeaveNotifyEvent(evnt);
        }

        private void CreatePlaceholderWindow()
        {
            var tab = notebook.CurrentTab;
            placeholderWindow = new PlaceholderWindow(tab);

            int x, y;
            Display.Default.GetPointer(out x, out y);
            placeholderWindow.MovePosition(x, y);
            placeholderWindow.Show();

            placeholderWindow.Destroyed += delegate
            {
                placeholderWindow = null;
                buttonPressedOnTab = false;
            };
        }

        private Rectangle GetScreenRect()
        {
            int ox, oy;
            ParentWindow.GetOrigin(out ox, out oy);
            var alloc = notebook.Allocation;
            alloc.X += ox;
            alloc.Y += oy;
            return alloc;
        }

        protected override bool OnMotionNotifyEvent(EventMotion evnt)
        {
            if (draggingTab && mouseHasLeft)
            {
                var sr = GetScreenRect();
                sr.Height = BarHeight;
                sr.Inflate(30, 30);

                int x, y;
                Display.Default.GetPointer(out x, out y);

                if (x < sr.Left || x > sr.Right || y < sr.Top || y > sr.Bottom)
                {
                    draggingTab = false;
                    mouseHasLeft = false;
                    CreatePlaceholderWindow();
                }
            }

            string newTooltip = null;
            if (placeholderWindow != null)
            {
                int x, y;
                Display.Default.GetPointer(out x, out y);
                placeholderWindow.MovePosition(x, y);
                return base.OnMotionNotifyEvent(evnt);
            }

            if (!draggingTab)
            {
                var t = FindTab((int)evnt.X, (int)evnt.Y);

                // If the user clicks and drags on the 'x' which closes the current
                // tab we can end up with a null tab here
                if (t == null)
                {
                    TooltipText = null;
                    return base.OnMotionNotifyEvent(evnt);
                }
                SetHighlightedTab(t);

                var newOver = IsOverCloseButton(t, (int)evnt.X, (int)evnt.Y);
                if (newOver != overCloseButton)
                {
                    overCloseButton = newOver;
                    QueueDraw();
                }
                if (!overCloseButton && !draggingTab && buttonPressedOnTab)
                {
                    draggingTab = true;
                    mouseHasLeft = false;
                    dragXProgress = 1.0f;
                    int x = (int)evnt.X;
                    dragOffset = x - t.Allocation.X;
                    dragX = x - dragOffset;
                    lastDragX = (int)evnt.X;
                }
                else if (t != null)
                    newTooltip = t.Tooltip;
            }
            else if (evnt.State.HasFlag(ModifierType.Button1Mask))
            {
                dragX = (int)evnt.X - dragOffset;
                QueueDraw();

                var t = FindTab((int)evnt.X, (int)TabPadding.Top + 3);
                if (t == null)
                {
                    var last = notebook.Tabs.Last();
                    if (dragX > last.Allocation.Right)
                        t = last;
                    if (dragX < 0)
                        t = notebook.Tabs.First();
                }
                if (t != null && t != notebook.CurrentTab && (
                        (int)evnt.X > lastDragX && t.Index > notebook.CurrentTab.Index ||
                        (int)evnt.X < lastDragX && t.Index < notebook.CurrentTab.Index))
                {
                    t.SaveAllocation();
                    t.SaveStrength = 1;
                    notebook.ReorderTab(notebook.CurrentTab, t);

                    t.Animate("TabMotion",
                        f => t.SaveStrength = f,
                        1.0f,
                        0.0f,
                        easing: Easing.CubicInOut);
                }
                lastDragX = (int)evnt.X;
            }

            if (newTooltip != null && TooltipText != null && TooltipText != newTooltip)
                TooltipText = null;
            else
                TooltipText = newTooltip;

            return base.OnMotionNotifyEvent(evnt);
        }

        private bool overCloseOnPress;
        private bool allowDoubleClick;

        protected override bool OnButtonPressEvent(EventButton evnt)
        {
            var t = FindTab((int)evnt.X, (int)evnt.Y);
            if (t != null)
            {
                if (evnt.IsContextMenuButton())
                {
                    DockNotebook.ActiveNotebook = notebook;
                    notebook.CurrentTab = t;
                    notebook.DoPopupMenu(notebook, t.Index, evnt);
                    return true;
                }
                // Don't select the tab if we are clicking the close button
                if (IsOverCloseButton(t, (int)evnt.X, (int)evnt.Y))
                {
                    overCloseOnPress = true;
                    return true;
                }
                overCloseOnPress = false;

                if (evnt.Type == EventType.TwoButtonPress)
                {
                    if (allowDoubleClick)
                    {
                        notebook.OnActivateTab(t);
                        buttonPressedOnTab = false;
                    }
                    return true;
                }
                if (evnt.Button == 2)
                {
                    notebook.OnCloseTab(t);
                    return true;
                }

                DockNotebook.ActiveNotebook = notebook;
                buttonPressedOnTab = true;
                notebook.CurrentTab = t;
                return true;
            }
            buttonPressedOnTab = true;
            QueueDraw();
            return base.OnButtonPressEvent(evnt);
        }

        protected override bool OnButtonReleaseEvent(EventButton evnt)
        {
            buttonPressedOnTab = false;

            if (placeholderWindow != null)
            {
                placeholderWindow.PlaceWindow(notebook);
                return base.OnButtonReleaseEvent(evnt);
            }

            if (!draggingTab && overCloseOnPress)
            {
                var t = FindTab((int)evnt.X, (int)evnt.Y);
                if (t != null && IsOverCloseButton(t, (int)evnt.X, (int)evnt.Y))
                {
                    notebook.OnCloseTab(t);
                    allowDoubleClick = false;
                    return true;
                }
            }
            overCloseOnPress = false;
            allowDoubleClick = true;
            if (dragX != 0)
                this.Animate("EndDrag",
                    f => dragXProgress = f,
                    1.0d,
                    0.0d,
                    easing: Easing.CubicOut,
                    finished: (f, a) => draggingTab = false);
            QueueDraw();
            return base.OnButtonReleaseEvent(evnt);
        }

        protected override void OnUnrealized()
        {
            // Cancel drag operations and animations
            buttonPressedOnTab = false;
            overCloseOnPress = false;
            allowDoubleClick = true;
            draggingTab = false;
            dragX = 0;
            this.AbortAnimation("EndDrag");
            base.OnUnrealized();
        }

        private DockNotebookTab FindTab(int x, int y)
        {
            var current = notebook.CurrentTab;
            if (current != null)
            {
                var allocWithLean = current.Allocation;
                allocWithLean.X -= LeanWidth / 2;
                allocWithLean.Width += LeanWidth;
                if (allocWithLean.Contains(x, y))
                    return current;
            }

            for (int n = 0; n < notebook.Tabs.Count; n++)
            {
                var tab = notebook.Tabs[n];
                if (tab.Allocation.Contains(x, y))
                    return tab;
            }
            return null;
        }

        private static bool IsOverCloseButton(DockNotebookTab tab, int x, int y)
        {
            return tab != null && tab.CloseButtonActiveArea.Contains(x, y);
        }

        public void Update()
        {
            if (!tracker.Hovered)
            {
                UpdateTabWidth(tabEndX - tabStartX);
            }
            else if (closingTabs.ContainsKey(notebook.Tabs.Count))
            {
                UpdateTabWidth(closingTabs[notebook.Tabs.Count].Allocation.Right - tabStartX, true);
            }
            QueueDraw();
        }

        private void UpdateTabWidth(int width, bool adjustLast = false)
        {
            if (notebook.Tabs.Any())
                TargetWidth = Clamp(width / notebook.Tabs.Count, 50, 200);

            if (adjustLast)
            {
                // adjust to align close buttons properly
                LastTabWidthAdjustment = width - TargetWidth * notebook.Tabs.Count + 1;
                LastTabWidthAdjustment = Math.Abs(LastTabWidthAdjustment) < 50 ? LastTabWidthAdjustment : 0;
            }
            else
            {
                LastTabWidthAdjustment = 0;
            }
            if (!IsRealized)
                TabWidth = TargetWidth;
        }

        private static int Clamp(int val, int min, int max)
        {
            return Math.Max(min, Math.Min(max, val));
        }

        private int GetRenderOffset()
        {
            int tabArea = tabEndX - tabStartX;
            if (notebook.CurrentTabIndex >= 0)
            {
                int normalizedArea = tabArea / TargetWidth * TargetWidth;
                int maxOffset = Math.Max(0, notebook.Tabs.Count * TargetWidth - normalizedArea);

                int distanceToTabEdge = TargetWidth * notebook.CurrentTabIndex;
                int window = normalizedArea - TargetWidth;
                targetOffset = Math.Min(maxOffset, Clamp(renderOffset, distanceToTabEdge - window, distanceToTabEdge));

                if (targetOffset != animationTarget)
                {
                    this.Animate("ScrollTabs",
                        easing: Easing.CubicOut,
                        start: renderOffset,
                        end: targetOffset,
                        callback: f => renderOffset = (int)f);
                    animationTarget = targetOffset;
                }
            }

            return tabStartX - renderOffset;
        }

        private Action<Context> DrawClosingTab(int index, Rectangle region, out int width)
        {
            width = 0;
            if (closingTabs.ContainsKey(index))
            {
                DockNotebookTab closingTab = closingTabs[index];
                width = (int)(closingTab.WidthModifier * TabWidth);
                int tmp = width;
                return c => DrawTab(c, closingTab, Allocation, new Rectangle(region.X, region.Y, tmp, region.Height), false, false, CreateTabLayout(closingTab));
            }
            return c =>
            {
            };
        }

        private void Draw(Context ctx)
        {
            int tabArea = tabEndX - tabStartX;
            int x = GetRenderOffset();
            const int y = 0;
            int n = 0;
            Action<Context> drawActive = null;
            var drawCommands = new List<Action<Context>>();
            for (; n < notebook.Tabs.Count; n++)
            {
                if (x + TabWidth < tabStartX)
                {
                    x += TabWidth;
                    continue;
                }

                if (x > tabEndX)
                    break;

                int closingWidth;
                var cmd = DrawClosingTab(n, new Rectangle(x, y, 0, Allocation.Height), out closingWidth);
                drawCommands.Add(cmd);
                x += closingWidth;

                var tab = notebook.Tabs[n];
                bool active = tab == notebook.CurrentTab;

                int width = Math.Min(TabWidth, Math.Max(50, tabEndX - x - 1));
                if (tab == notebook.Tabs.Last())
                    width += LastTabWidthAdjustment;
                width = (int)(width * tab.WidthModifier);

                if (active)
                {
                    int tmp = x;
                    drawActive = c => DrawTab(c, tab, Allocation, new Rectangle(tmp, y, width, Allocation.Height), true, draggingTab, CreateTabLayout(tab));
                    tab.Allocation = new Rectangle(tmp, Allocation.Y, width, Allocation.Height);
                }
                else
                {
                    int tmp = x;
                    bool highlighted = tab == highlightedTab;

                    if (tab.SaveStrength > 0.0f)
                    {
                        tmp = (int)(tab.SavedAllocation.X + (tmp - tab.SavedAllocation.X) * (1.0f - tab.SaveStrength));
                    }

                    drawCommands.Add(c => DrawTab(c, tab, Allocation, new Rectangle(tmp, y, width, Allocation.Height), false, false, CreateTabLayout(tab)));
                    tab.Allocation = new Rectangle(tmp, Allocation.Y, width, Allocation.Height);
                }

                x += width;
            }

            var allocation = Allocation;
            int tabWidth;
            drawCommands.Add(DrawClosingTab(n, new Rectangle(x, y, 0, allocation.Height), out tabWidth));
            drawCommands.Reverse();

            ctx.DrawImage(this, TabbarBackImage.WithSize(allocation.Width, allocation.Height), 0, 0);

            // Draw breadcrumb bar header
            //			if (notebook.Tabs.Count > 0) {
            //				ctx.Rectangle (0, allocation.Height - BottomBarPadding, allocation.Width, BottomBarPadding);
            //				ctx.SetSourceColor (Styles.BreadcrumbBackgroundColor);
            //				ctx.Fill ();
            //			}

            ctx.Rectangle(tabStartX - LeanWidth / 2, allocation.Y, tabArea + LeanWidth, allocation.Height);
            ctx.Clip();

            foreach (var cmd in drawCommands)
                cmd(ctx);

            ctx.ResetClip();

            // Redraw the dragging tab here to be sure its on top. We drew it before to get the sizing correct, this should be fixed.
            drawActive?.Invoke(ctx);
        }

        protected override bool OnExposeEvent(EventExpose evnt)
        {
            using (var context = CairoHelper.Create(evnt.Window))
            {
                Draw(context);
            }
            return base.OnExposeEvent(evnt);
        }

        private void DrawTab(Context ctx, DockNotebookTab tab, Rectangle allocation, Rectangle tabBounds, bool active, bool dragging, Layout la)
        {
            // This logic is stupid to have here, should be in the caller!
            if (dragging)
            {
                tabBounds.X = (int)(tabBounds.X + (dragX - tabBounds.X) * dragXProgress);
                tabBounds.X = Clamp(tabBounds.X, tabStartX, tabEndX - tabBounds.Width);
            }
            double rightPadding = (active ? TabActivePadding.Right : TabPadding.Right) - LeanWidth / 2;
            rightPadding = rightPadding * Math.Min(1.0, Math.Max(0.5, (tabBounds.Width - 30) / 70.0));
            double leftPadding = (active ? TabActivePadding.Left : TabPadding.Left) - LeanWidth / 2;
            leftPadding = leftPadding * Math.Min(1.0, Math.Max(0.5, (tabBounds.Width - 30) / 70.0));
            double bottomPadding = active ? TabActivePadding.Bottom : TabPadding.Bottom;

            DrawTabBackground(this, ctx, allocation, tabBounds.Width, tabBounds.X, active);

            ctx.LineWidth = 1;
            ctx.NewPath();

            // Render Close Button (do this first so we can tell how much text to render)

            var closeButtonAlloation = new Cairo.Rectangle(tabBounds.Right - rightPadding - TabCloseImage.Width / 2 - CloseButtonMarginRight,
                                             tabBounds.Height - bottomPadding - TabCloseImage.Height - CloseButtonMarginBottom,
                                             TabCloseImage.Width, TabCloseImage.Height);

            tab.CloseButtonActiveArea = closeButtonAlloation.Inflate(2, 2);

            bool closeButtonHovered = tracker.Hovered && tab.CloseButtonActiveArea.Contains(tracker.MousePosition);
            bool tabHovered = tracker.Hovered && tab.Allocation.Contains(tracker.MousePosition);
            bool drawCloseButton = active || tabHovered;

            if (!closeButtonHovered && tab.DirtyStrength > 0.5)
            {
                ctx.DrawImage(this, TabDirtyImage, closeButtonAlloation.X, closeButtonAlloation.Y);
                drawCloseButton = false;
            }

            if (drawCloseButton)
                ctx.DrawImage(this, TabCloseImage.WithAlpha((closeButtonHovered ? 1.0 : 0.5) * tab.Opacity), closeButtonAlloation.X, closeButtonAlloation.Y);

            // Render Text
            double tw = tabBounds.Width - (leftPadding + rightPadding);
            if (drawCloseButton || tab.DirtyStrength > 0.5)
                tw -= closeButtonAlloation.Width / 2;

            double tx = tabBounds.X + leftPadding;
            var baseline = la.GetLine(0).Layout.GetPixelBaseline();
            double ty = tabBounds.Height - bottomPadding - baseline;

            ctx.MoveTo(tx, ty);
            if (!Platform.IsMac && !Platform.IsWindows)
            {
                // This is a work around for a linux specific problem.
                // A bug in the proprietary ATI driver caused TAB text not to draw.
                // If that bug get's fixed remove this HACK asap.
                la.Ellipsize = EllipsizeMode.End;
                la.Width = (int)(tw * Scale.PangoScale);
                ctx.SetSourceColor((tab.Notify ? Styles.TabBarNotifyTextColor : (active ? Styles.TabBarActiveTextColor : Styles.TabBarInactiveTextColor)).ToCairoColor());
                Pango.CairoHelper.ShowLayout(ctx, la.GetLine(0).Layout);
            }
            else
            {
                // ellipses are for space wasting ..., we cant afford that
                using (var lg = new LinearGradient(tx + tw - 10, 0, tx + tw, 0))
                {
                    var color = (tab.Notify ? Styles.TabBarNotifyTextColor : (active ? Styles.TabBarActiveTextColor : Styles.TabBarInactiveTextColor)).ToCairoColor();
                    color = color.MultiplyAlpha(tab.Opacity);
                    lg.AddColorStop(0, color);
                    color.A = 0;
                    lg.AddColorStop(1, color);
                    ctx.SetSource(lg);
                    Pango.CairoHelper.ShowLayout(ctx, la.GetLine(0).Layout);
                }
            }
            la.Dispose();
        }

        private static void DrawTabBackground(Widget widget, Context ctx, Rectangle allocation, int contentWidth, int px, bool active = true)
        {
            int lean = Math.Min(LeanWidth, contentWidth / 2);
            int halfLean = lean / 2;

            double x = px + TabSpacing - halfLean;
            double y = 0;
            double height = allocation.Height;
            double width = contentWidth - TabSpacing * 2 + lean;

            var image = active ? TabActiveBackImage : TabBackImage;
            image = image.WithSize(width, height);

            ctx.DrawImage(widget, image, x, y);
        }

        private Layout CreateSizedLayout()
        {
            var la = new Layout(PangoContext);
            la.FontDescription = FontService.SansFont.Copy();
            if (!Platform.IsWindows)
                la.FontDescription.Weight = Weight.Bold;
            la.FontDescription.AbsoluteSize = Units.FromPixels(VerticalTextSize);

            return la;
        }

        private Layout CreateTabLayout(DockNotebookTab tab)
        {
            Layout la = CreateSizedLayout();
            if (!string.IsNullOrEmpty(tab.Markup))
                la.SetMarkup(tab.Markup);
            else if (!string.IsNullOrEmpty(tab.Text))
                la.SetText(tab.Text);
            return la;
        }
    }
}
