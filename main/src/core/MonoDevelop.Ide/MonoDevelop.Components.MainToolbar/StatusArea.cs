//
// StatusArea.cs
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
using System.Threading;
using Cairo;
using Gdk;
using GLib;
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Components;
using Xwt;
using Xwt.Motion;
using Alignment = Gtk.Alignment;
using Box = Gtk.Box;
using CairoHelper = Gdk.CairoHelper;
using Color = Cairo.Color;
using Context = Pango.Context;
using HBox = Gtk.HBox;
using IconSize = Xwt.IconSize;
using Image = Xwt.Drawing.Image;
using Label = Gtk.Label;
using Point = Gdk.Point;
using Rectangle = Gdk.Rectangle;
using StockIcons = MonoDevelop.Ide.Gui.Stock;
using Timeout = GLib.Timeout;
using VBox = Gtk.VBox;
using Widget = Gtk.Widget;

namespace MonoDevelop.Components.MainToolbar
{
    internal class StatusArea : EventBox, IStatusBar, IAnimatable
    {
        private struct Message
        {
            public readonly string Text;
            public readonly IconId Icon;
            public readonly bool IsMarkup;

            public Message(IconId icon, string text, bool markup)
            {
                Text = text;
                Icon = icon;
                IsMarkup = markup;
            }
        }

        public struct RenderArg
        {
            public Rectangle Allocation { get; set; }
            public double BuildAnimationProgress { get; set; }
            public double BuildAnimationOpacity { get; set; }
            public Rectangle ChildAllocation { get; set; }
            public Image CurrentPixbuf { get; set; }
            public string CurrentText { get; set; }
            public bool CurrentTextIsMarkup { get; set; }
            public double ErrorAnimationProgress { get; set; }
            public double HoverProgress { get; set; }
            public string LastText { get; set; }
            public bool LastTextIsMarkup { get; set; }
            public Image LastPixbuf { get; set; }
            public Point MousePosition { get; set; }
            public Context Pango { get; set; }
            public double ProgressBarAlpha { get; set; }
            public float ProgressBarFraction { get; set; }
            public bool ShowProgressBar { get; set; }
            public double TextAnimationProgress { get; set; }
        }

        private readonly StatusAreaTheme theme;
        private RenderArg renderArg;

        private readonly HBox contentBox = new HBox(false, 8);

        private readonly StatusAreaSeparator statusIconSeparator;
        private readonly Widget buildResultWidget;

        private readonly HBox messageBox = new HBox();
        internal readonly HBox StatusIconBox = new HBox();

        private uint animPauseHandle;

        private readonly MouseTracker tracker;

        private AnimatedIcon iconAnimation;
        private IconId currentIcon;
        private static Pad sourcePad;
        private IDisposable currentIconAnimation;

        private bool errorAnimPending;

        private readonly StatusBarContextHandler ctxHandler;
        private bool progressBarVisible;

        private string currentApplicationName = String.Empty;

        private readonly Queue<Message> messageQueue;

        public IStatusBar MainContext => ctxHandler.MainContext;

        public int MaxWidth { get; set; }

        private void MessageBoxToolTip(object o, QueryTooltipArgs e)
        {
            if (theme.IsEllipsized && (e.X < messageBox.Allocation.Width))
            {
                var label = new Label();
                if (renderArg.CurrentTextIsMarkup)
                {
                    label.Markup = renderArg.CurrentText;
                }
                else
                {
                    label.Text = renderArg.CurrentText;
                }

                label.Wrap = true;
                label.WidthRequest = messageBox.Allocation.Width;

                e.Tooltip.Custom = label;
                e.RetVal = true;
            }
            else
            {
                e.RetVal = false;
            }
        }

        public StatusArea()
        {
            theme = new StatusAreaTheme();
            renderArg = new RenderArg();

            ctxHandler = new StatusBarContextHandler(this);
            VisibleWindow = false;
            NoShowAll = true;
            WidgetFlags |= WidgetFlags.AppPaintable;

            StatusIconBox.BorderWidth = 0;
            StatusIconBox.Spacing = 3;

            Action<bool> animateProgressBar =
                showing => this.Animate("ProgressBarFade",
                                         val => renderArg.ProgressBarAlpha = val,
                                         renderArg.ProgressBarAlpha,
                                         showing ? 1.0f : 0.0f,
                                         easing: Easing.CubicInOut);

            ProgressBegin += delegate
            {
                renderArg.ShowProgressBar = true;
                //				StartBuildAnimation ();
                renderArg.ProgressBarFraction = 0;
                QueueDraw();
                animateProgressBar(true);
            };

            ProgressEnd += delegate
            {
                renderArg.ShowProgressBar = false;
                //				StopBuildAnimation ();
                QueueDraw();
                animateProgressBar(false);
            };

            ProgressFraction += delegate (object sender, FractionEventArgs e)
            {
                renderArg.ProgressBarFraction = (float)e.Work;
                QueueDraw();
            };

            contentBox.PackStart(messageBox, true, true, 0);
            contentBox.PackEnd(StatusIconBox, false, false, 0);
            contentBox.PackEnd(statusIconSeparator = new StatusAreaSeparator(), false, false, 0);
            contentBox.PackEnd(buildResultWidget = CreateBuildResultsWidget(Orientation.Horizontal), false, false, 0);

            HasTooltip = true;
            QueryTooltip += MessageBoxToolTip;

            var mainAlign = new Alignment(0, 0.5f, 1, 0)
            {
                LeftPadding = 12,
                RightPadding = 8
            };
            mainAlign.Add(contentBox);
            Add(mainAlign);

            mainAlign.ShowAll();
            StatusIconBox.Hide();
            statusIconSeparator.Hide();
            buildResultWidget.Hide();
            Show();

            ButtonPressEvent += (o, args) => sourcePad?.BringToFront(true);

            StatusIconBox.Shown += delegate
            {
                UpdateSeparators();
            };

            StatusIconBox.Hidden += delegate
            {
                UpdateSeparators();
            };

            messageQueue = new Queue<Message>();

            tracker = new MouseTracker(this);
            tracker.MouseMoved += (sender, e) => QueueDraw();
            tracker.HoveredChanged += (sender, e) =>
            {
                this.Animate("Hovered",
                              x => renderArg.HoverProgress = x,
                              renderArg.HoverProgress,
                              tracker.Hovered ? 1.0f : 0.0f,
                              easing: Easing.SinInOut);
            };

            IdeApp.FocusIn += delegate
            {
                // If there was an error while the application didn't have the focus,
                // trigger the error animation again when it gains the focus
                if (errorAnimPending)
                {
                    errorAnimPending = false;
                    TriggerErrorAnimation();
                }
            };
        }

        protected override void OnDestroyed()
        {
            theme?.Dispose();
            base.OnDestroyed();
        }

        void IAnimatable.BatchBegin() { }
        void IAnimatable.BatchCommit() { QueueDraw(); }

        private void StartBuildAnimation()
        {
            this.Animate("Build",
                          val => renderArg.BuildAnimationProgress = val,
                          length: 5000,
                          repeat: () => true);

            this.Animate("BuildOpacity",
                          start: renderArg.BuildAnimationOpacity,
                          end: 1.0f,
                          callback: x => renderArg.BuildAnimationOpacity = x);
        }

        private void StopBuildAnimation()
        {
            this.Animate("BuildOpacity",
                          x => renderArg.BuildAnimationOpacity = x,
                          renderArg.BuildAnimationOpacity,
                          0.0f,
                          finished: (val, aborted) => { if (!aborted) this.AbortAnimation("Build"); });
        }

        protected override void OnSizeAllocated(Rectangle allocation)
        {
            if (MaxWidth > 0 && allocation.Width > MaxWidth)
            {
                allocation = new Rectangle(allocation.X + (allocation.Width - MaxWidth) / 2, allocation.Y, MaxWidth, allocation.Height);
            }
            base.OnSizeAllocated(allocation);
        }

        private void TriggerErrorAnimation()
        {
            /* Hack for a compiler error - csc crashes on this:
                        this.Animate (name: "statusAreaError",
                                      length: 700,
                                      callback: val => renderArg.ErrorAnimationProgress = val);
            */
            this.Animate("statusAreaError",
                          val => renderArg.ErrorAnimationProgress = val,
                          length: 900);
        }

        private void UpdateSeparators()
        {
            statusIconSeparator.Visible = StatusIconBox.Visible && buildResultWidget.Visible;
        }

        public Widget CreateBuildResultsWidget(Orientation orientation)
        {
            EventBox ebox = new EventBox();

            Box box;
            if (orientation == Orientation.Horizontal)
                box = new HBox();
            else
                box = new VBox();
            box.Spacing = 3;

            var errorIcon = ImageService.GetIcon(StockIcons.Error).WithSize(IconSize.Small);
            var warningIcon = ImageService.GetIcon(StockIcons.Warning).WithSize(IconSize.Small);

            var errorImage = new Xwt.ImageView(errorIcon);
            var warningImage = new Xwt.ImageView(warningIcon);

            box.PackStart(errorImage.ToGtkWidget(), false, false, 0);
            Label errors = new Label();
            box.PackStart(errors, false, false, 0);

            box.PackStart(warningImage.ToGtkWidget(), false, false, 0);
            Label warnings = new Label();
            box.PackStart(warnings, false, false, 0);
            box.NoShowAll = true;
            box.Show();

            currentApplicationName = BrandingService.ApplicationLongName;
            BrandingService.ApplicationNameChanged += ApplicationNameChanged;

            box.Destroyed += delegate
            {
                BrandingService.ApplicationNameChanged -= ApplicationNameChanged;
            };

            ebox.VisibleWindow = false;
            ebox.Add(box);
            ebox.ShowAll();

            errors.Visible = false;
            errorImage.Visible = false;
            warnings.Visible = false;
            warningImage.Visible = false;

            return ebox;
        }

        private void ApplicationNameChanged(object sender, EventArgs e)
        {
            if (renderArg.CurrentText == currentApplicationName)
            {
                LoadText(BrandingService.ApplicationLongName, false);
                LoadPixbuf(null);
                QueueDraw();
            }
            currentApplicationName = BrandingService.ApplicationLongName;
        }

        protected override void OnRealized()
        {
            base.OnRealized();
            ModifyText(StateType.Normal, Styles.StatusBarTextColor.ToGdkColor());
            ModifyFg(StateType.Normal, Styles.StatusBarTextColor.ToGdkColor());
        }

        protected override void OnSizeRequested(ref Requisition requisition)
        {
            requisition.Height = 32;
            base.OnSizeRequested(ref requisition);
        }

        protected override bool OnExposeEvent(EventExpose evnt)
        {
            using (var context = CairoHelper.Create(evnt.Window))
            {
                renderArg.Allocation = Allocation;
                renderArg.ChildAllocation = messageBox.Allocation;
                renderArg.MousePosition = tracker.MousePosition;
                renderArg.Pango = PangoContext;

                theme.Render(context, renderArg, this);
            }
            return base.OnExposeEvent(evnt);
        }


        #region StatusBar implementation

        public StatusBarIcon ShowStatusIcon(Image pixbuf)
        {
            Runtime.AssertMainThread();
            StatusIcon icon = new StatusIcon(this, pixbuf);
            StatusIconBox.PackEnd(icon.box);
            StatusIconBox.ShowAll();
            return icon;
        }

        private void HideStatusIcon(StatusIcon icon)
        {
            StatusIconBox.Remove(icon.EventBox);
            if (StatusIconBox.Children.Length == 0)
                StatusIconBox.Hide();
            icon.EventBox.Destroy();
        }

        public StatusBarContext CreateContext()
        {
            return ctxHandler.CreateContext();
        }

        public void ShowReady()
        {
            ShowMessage("");
            SetMessageSourcePad(null);
        }

        public void SetMessageSourcePad(Pad pad)
        {
            sourcePad = pad;
        }

        public bool HasResizeGrip
        {
            get;
            set;
        }

        public class StatusIcon : StatusBarIcon
        {
            private readonly StatusArea statusBar;
            internal EventBox box;
            private string tip;
            private DateTime alertEnd;
            private Image icon;
            private uint animation;
            private readonly Xwt.ImageView image;

            private int astep;
            private Image[] images;
            private TooltipPopoverWindow tooltipWindow;
            private bool mouseOver;
            private uint tipShowTimeoutId;
            private DateTime scheduledTipTime;
            private const int TooltipTimeout = 350;

            public StatusIcon(StatusArea statusBar, Image icon)
            {
                if (!icon.HasFixedSize)
                    icon = icon.WithSize(Gtk.IconSize.Menu);

                this.statusBar = statusBar;
                this.icon = icon;
                box = new EventBox { VisibleWindow = false };
                image = new Xwt.ImageView(icon);
                box.Child = image.ToGtkWidget();
                box.Events |= EventMask.EnterNotifyMask | EventMask.LeaveNotifyMask;
                box.EnterNotifyEvent += HandleEnterNotifyEvent;
                box.LeaveNotifyEvent += HandleLeaveNotifyEvent;
                box.ButtonPressEvent += (o, e) =>
                {
                    // TODO: Refactor this in Xwt as an extension method.
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

                    Clicked?.Invoke(o, new StatusBarIconClickedEventArgs
                    {
                        Button = (PointerButton)e.Event.Button,
                        Modifiers = m
                    });
                };
            }

            [ConnectBefore]
            private void HandleLeaveNotifyEvent(object o, LeaveNotifyEventArgs args)
            {
                mouseOver = false;
                HideTooltip();
            }

            [ConnectBefore]
            private void HandleEnterNotifyEvent(object o, EnterNotifyEventArgs args)
            {
                mouseOver = true;
                ShowTooltip();
            }

            private void ShowTooltip()
            {
                scheduledTipTime = DateTime.Now + TimeSpan.FromMilliseconds(TooltipTimeout);
                if (tipShowTimeoutId == 0)
                    tipShowTimeoutId = Timeout.Add(TooltipTimeout, ShowTooltipEvent);
            }

            private bool ShowTooltipEvent()
            {
                tipShowTimeoutId = 0;

                if (!mouseOver)
                    return false;

                int remainingMs = (int)(scheduledTipTime - DateTime.Now).TotalMilliseconds;
                if (remainingMs > 50)
                {
                    // Still some significant time left. Re-schedule the timer
                    tipShowTimeoutId = Timeout.Add((uint)remainingMs, ShowTooltipEvent);
                    return false;
                }

                if (!string.IsNullOrEmpty(tip))
                {
                    HideTooltip();
                    tooltipWindow = new TooltipPopoverWindow
                    {
                        ShowArrow = true,
                        Text = tip
                    };
                    tooltipWindow.ShowPopup(box, PopupPosition.Top);
                }
                return false;
            }

            private void HideTooltip()
            {
                if (tooltipWindow != null)
                {
                    tooltipWindow.Destroy();
                    tooltipWindow = null;
                }
            }

            public void Dispose()
            {
                HideTooltip();
                statusBar.HideStatusIcon(this);
                if (images != null)
                {
                    foreach (Image img in images)
                    {
                        img.Dispose();
                    }
                }
                if (animation != 0)
                {
                    Source.Remove(animation);
                    animation = 0;
                }
            }

            public string ToolTip
            {
                get { return tip; }
                set
                {
                    tip = value;
                    if (tooltipWindow != null)
                    {
                        if (!string.IsNullOrEmpty(tip))
                            tooltipWindow.Text = value;
                        else
                            HideTooltip();
                    }
                    else if (!string.IsNullOrEmpty(tip) && mouseOver)
                        ShowTooltip();
                }
            }

            public EventBox EventBox => box;

            public Image Image
            {
                get { return icon; }
                set
                {
                    icon = value;
                    if (!icon.HasFixedSize)
                        icon = icon.WithSize(Gtk.IconSize.Menu);
                    image.Image = icon;
                }
            }

            public void SetAlertMode(int seconds)
            {
                astep = 0;
                alertEnd = DateTime.Now.AddSeconds(seconds);

                if (animation != 0)
                    Source.Remove(animation);

                animation = Timeout.Add(60, AnimateIcon);

                if (images == null)
                {
                    images = new Image[10];
                    for (int n = 0; n < 10; n++)
                        images[n] = icon.WithAlpha((9 - n) / 10.0);
                }
            }

            private bool AnimateIcon()
            {
                if (DateTime.Now >= alertEnd && astep == 0)
                {
                    image.Image = icon;
                    animation = 0;
                    return false;
                }
                image.Image = astep < 10 ? images[astep] : images[20 - astep - 1];

                astep = (astep + 1) % 20;
                return true;
            }

            public event EventHandler<StatusBarIconClickedEventArgs> Clicked;
        }

        #endregion

        #region StatusBarContextBase implementation

        public void ShowError(string error)
        {
            ShowMessage(StockIcons.StatusError, error);
        }

        public void ShowWarning(string warning)
        {
            ShowMessage(StockIcons.StatusWarning, warning);
        }

        public void ShowMessage(string message)
        {
            ShowMessage(null, message, false);
        }

        public void ShowMessage(string message, bool isMarkup)
        {
            ShowMessage(null, message, isMarkup);
        }

        public void ShowMessage(IconId image, string message)
        {
            ShowMessage(image, message, false);
        }

        public void ShowMessage(IconId image, string message, bool isMarkup)
        {
            if (this.AnimationIsRunning("Text") || animPauseHandle > 0)
            {
                messageQueue.Clear();
                messageQueue.Enqueue(new Message(image, message, isMarkup));
            }
            else
            {
                ShowMessageInner(image, message, isMarkup);
            }
        }

        private void ShowMessageInner(IconId image, string message, bool isMarkup)
        {
            Runtime.AssertMainThread();

            if (image == StockIcons.StatusError)
            {
                // If the application doesn't have the focus, trigger the animation
                // again when it gains the focus
                if (!IdeApp.CommandService.ApplicationHasFocus)
                    errorAnimPending = true;
                TriggerErrorAnimation();
            }

            LoadText(message, isMarkup);
            LoadPixbuf(image);
            /* Hack for a compiler error - csc crashes on this:
			this.Animate ("Text", easing: Easing.SinInOut,
			              callback: x => renderArg.TextAnimationProgress = x,
			              finished: x => { animPauseHandle = GLib.Timeout.Add (1000, () => {
					if (messageQueue.Count > 0) {
						Message m = messageQueue.Dequeue();
						ShowMessageInner (m.Icon, m.Text, m.IsMarkup);
					}
					animPauseHandle = 0;
					return false;
				});
			});
			*/
            this.Animate("Text",
                          x => renderArg.TextAnimationProgress = x,
                          easing: Easing.SinInOut,
                          finished: (x, b) =>
                          {
                              animPauseHandle = Timeout.Add(1000, () =>
                              {
                                  if (messageQueue.Count > 0)
                                  {
                                      Message m = messageQueue.Dequeue();
                                      ShowMessageInner(m.Icon, m.Text, m.IsMarkup);
                                  }
                                  animPauseHandle = 0;
                                  return false;
                              });
                          });


            if (renderArg.CurrentText == renderArg.LastText)
                this.AbortAnimation("Text");

            QueueDraw();
        }

        private void LoadText(string message, bool isMarkup)
        {
            if (string.IsNullOrEmpty(message))
                message = BrandingService.ApplicationLongName;
            message = message ?? "";

            renderArg.LastText = renderArg.CurrentText;
            renderArg.CurrentText = message.Replace(Environment.NewLine, " ").Replace("\n", " ").Trim();

            renderArg.LastTextIsMarkup = renderArg.CurrentTextIsMarkup;
            renderArg.CurrentTextIsMarkup = isMarkup;
        }

        private static bool iconLoaded;

        private void LoadPixbuf(IconId image)
        {
            // We dont need to load the same image twice
            if (currentIcon == image && iconLoaded)
                return;

            currentIcon = image;
            iconAnimation = null;

            // clean up previous running animation
            if (currentIconAnimation != null)
            {
                currentIconAnimation.Dispose();
                currentIconAnimation = null;
            }

            // if we have nothing, use the default icon
            if (image == IconId.Null)
                image = "md-status-steady";

            // load image now
            if (ImageService.IsAnimation(image, Gtk.IconSize.Menu))
            {
                iconAnimation = ImageService.GetAnimatedIcon(image, Gtk.IconSize.Menu);
                renderArg.CurrentPixbuf = iconAnimation.FirstFrame.WithSize(16, 16);
                currentIconAnimation = iconAnimation.StartAnimation(delegate (Image p)
                {
                    renderArg.CurrentPixbuf = p.WithSize(16, 16);
                    QueueDraw();
                });
            }
            else
            {
                renderArg.CurrentPixbuf = ImageService.GetIcon(image).WithSize(16, 16);
            }

            iconLoaded = true;
        }
        #endregion


        #region Progress Monitor implementation
        public static event EventHandler ProgressBegin, ProgressEnd, ProgressPulse;
        public static event EventHandler<FractionEventArgs> ProgressFraction;

        public sealed class FractionEventArgs : EventArgs
        {
            public double Work { get; }

            public FractionEventArgs(double work)
            {
                Work = work;
            }
        }

        private static void OnProgressBegin(EventArgs e)
        {
            ProgressBegin?.Invoke(null, e);
        }

        private static void OnProgressEnd(EventArgs e)
        {
            ProgressEnd?.Invoke(null, e);
        }

        private static void OnProgressPulse(EventArgs e)
        {
            ProgressPulse?.Invoke(null, e);
        }

        private static void OnProgressFraction(FractionEventArgs e)
        {
            ProgressFraction?.Invoke(null, e);
        }

        public void BeginProgress(string name)
        {
            ShowMessage(name);
            if (!progressBarVisible)
            {
                progressBarVisible = true;
                OnProgressBegin(EventArgs.Empty);
            }
        }

        public void BeginProgress(IconId image, string name)
        {
            ShowMessage(image, name);
            if (!progressBarVisible)
            {
                progressBarVisible = true;
                OnProgressBegin(EventArgs.Empty);
            }
        }

        public void SetProgressFraction(double work)
        {
            Runtime.AssertMainThread();
            OnProgressFraction(new FractionEventArgs(work));
        }

        public void EndProgress()
        {
            if (!progressBarVisible)
                return;

            progressBarVisible = false;
            OnProgressEnd(EventArgs.Empty);
            AutoPulse = false;
        }

        public void Pulse()
        {
            Runtime.AssertMainThread();
            OnProgressPulse(EventArgs.Empty);
        }

        private uint autoPulseTimeoutId;
        public bool AutoPulse
        {
            get { return autoPulseTimeoutId != 0; }
            set
            {
                Runtime.AssertMainThread();
                if (value)
                {
                    if (autoPulseTimeoutId == 0)
                    {
                        autoPulseTimeoutId = Timeout.Add(100, delegate
                        {
                            Pulse();
                            return true;
                        });
                    }
                }
                else
                {
                    if (autoPulseTimeoutId != 0)
                    {
                        Source.Remove(autoPulseTimeoutId);
                        autoPulseTimeoutId = 0;
                    }
                }
            }
        }

        public void SetCancellationTokenSource(CancellationTokenSource source)
        {
        }
        #endregion
    }

    internal class StatusAreaSeparator : HBox
    {
        protected override bool OnExposeEvent(EventExpose evnt)
        {
            using (var ctx = CairoHelper.Create(GdkWindow))
            {
                var alloc = Allocation;
                //alloc.Inflate (0, -2);
                ctx.Rectangle(alloc.X, alloc.Y, 1, alloc.Height);

                // FIXME: VV: Remove gradient features
                using (LinearGradient gr = new LinearGradient(alloc.X, alloc.Y, alloc.X, alloc.Y + alloc.Height))
                {
                    gr.AddColorStop(0, new Color(0, 0, 0, 0));
                    gr.AddColorStop(0.5, new Color(0, 0, 0, 0.2));
                    gr.AddColorStop(1, new Color(0, 0, 0, 0));
                    ctx.SetSource(gr);
                    ctx.Fill();
                }
            }
            return true;
        }

        protected override void OnSizeRequested(ref Requisition requisition)
        {
            base.OnSizeRequested(ref requisition);
            requisition.Width = 1;
        }
    }
}

