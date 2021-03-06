﻿using MonoDevelop.Ide;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MonoDevelop.Core;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Components.MainToolbar;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Animation;
using MonoDevelop.Ide.Gui.Components;
using System.Threading;

namespace WindowsPlatform.MainToolbar
{
	public enum StatusBarStatus
	{
		Normal,
		Ready,
		Warning,
		Error,
	}

	/// <summary>
	/// Interaction logic for StatusBar.xaml
	/// </summary>
	public partial class StatusBarControl : IStatusBar, INotifyPropertyChanged
	{
	    private readonly StatusBarContextHandler ctxHandler;

		public StatusBarControl ()
		{
			InitializeComponent ();
			DataContext = this;

			ctxHandler = new StatusBarContextHandler (this);

			ShowReady ();

			BrandingService.ApplicationNameChanged += ApplicationNameChanged;

			StatusText.ToolTipOpening += (o, e) => {
				e.Handled = !TextTrimmed ();
			};
		}

	    private bool TextTrimmed ()
		{
			StatusText.Measure (new Size (double.PositiveInfinity, double.PositiveInfinity));
			return StatusText.ActualWidth < StatusText.DesiredSize.Width;
		}

		public bool AutoPulse {
			get { return ProgressBar.IsIndeterminate; }
			set { ProgressBar.IsIndeterminate = value; }
		}

		public IStatusBar MainContext => ctxHandler.MainContext;

	    public void BeginProgress (string name)
		{
			EndProgress();
			Status = StatusBarStatus.Normal;
			ShowMessage (name);
		}

		public void BeginProgress (IconId image, string name)
		{
			EndProgress();
			Status = StatusBarStatus.Normal;
			ShowMessage(image, name);
		}

		public StatusBarContext CreateContext ()
		{
			return ctxHandler.CreateContext ();
		}

		public void Dispose ()
		{
			BrandingService.ApplicationNameChanged -= ApplicationNameChanged;
		}

		public void EndProgress ()
		{
			oldWork = 0;
            ProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
		}

		public void Pulse ()
		{
			// Nothing to do here.
		}

	    private static Pad sourcePad;
		public void SetMessageSourcePad (Pad pad)
		{
			sourcePad = pad;
		}

	    private void OnShowError(object sender, MouseButtonEventArgs e)
		{
		}

	    private void OnShowPad(object sender, MouseButtonEventArgs e)
	    {
	        sourcePad?.BringToFront(true);
	    }

	    private double oldWork;

		public void SetProgressFraction (double work)
		{
			if (work == oldWork)
				return;

			var anim = new DoubleAnimation
			{
				From = oldWork,
				To = work,
				Duration = TimeSpan.FromSeconds(0.2),
				FillBehavior = FillBehavior.HoldEnd,
			};
			oldWork = work;

			ProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
		}

		public void ShowError (string error)
		{
			Status = StatusBarStatus.Error;
			ShowMessage (error);
		}

		public void ShowMessage (string message)
		{
			ShowMessage (null, message, false);
		}

		public void ShowMessage (IconId image, string message)
		{
			ShowMessage (image, message, false);
		}

		public void ShowMessage (string message, bool isMarkup)
		{
			ShowMessage (null, message, true);
		}

	    private IconId currentIcon;
	    private AnimatedIcon animatedIcon;
	    private IDisposable xwtAnimation;
		public void ShowMessage (IconId iconId, string message, bool isMarkup)
		{
			Message = message;
			StatusText.ToolTip = message;

			if (iconId.IsNull)
				iconId = BrandingService.StatusSteadyIconId;

			// don't reload same icon
			if (currentIcon == iconId)
				return;

			currentIcon = iconId;

			if (xwtAnimation != null) {
				xwtAnimation.Dispose ();
				xwtAnimation = null;
			}

			if (ImageService.IsAnimation (currentIcon, Gtk.IconSize.Menu)) {
				animatedIcon = ImageService.GetAnimatedIcon (currentIcon, Gtk.IconSize.Menu);
				StatusImage = animatedIcon.FirstFrame;
				xwtAnimation = animatedIcon.StartAnimation (p => {
					StatusImage = p;
				});
			} else
				StatusImage = currentIcon.GetStockIcon ().WithSize (Xwt.IconSize.Small);
		}

		public void ShowReady ()
		{
			Status = StatusBarStatus.Ready;
			ShowMessage (BrandingService.StatusSteadyIconId, BrandingService.ApplicationLongName);
			SetMessageSourcePad (null);
		}

	    private void ApplicationNameChanged (object sender, EventArgs e)
		{
			if (Status == StatusBarStatus.Ready)
				ShowReady ();
		}

		public StatusBarIcon ShowStatusIcon (Xwt.Drawing.Image pixbuf)
		{
			var icon = new StatusIcon (this) {
				Image = pixbuf,
				Margin = new Thickness (5, 5, 5, 5),
				MaxWidth = 16,
				MaxHeight = 16,
			};

			StatusIconsPanel.Children.Add (icon);

			return icon;
        }

		public void ShowWarning (string warning)
		{
			Status = StatusBarStatus.Warning;
			ShowMessage (warning);
		}

	    private string message;
		public string Message
		{
			get { return message; }
			set { message = value; RaisePropertyChanged (); }
		}

		public static readonly DependencyProperty StatusProperty =
			DependencyProperty.Register("Status", typeof(StatusBarStatus), typeof(StatusBarControl), new FrameworkPropertyMetadata(StatusBarStatus.Normal, FrameworkPropertyMetadataOptions.AffectsRender));

		public StatusBarStatus Status {
			get { return (StatusBarStatus)GetValue(StatusProperty); }  
			private set { SetValue(StatusProperty, value); RaisePropertyChanged (); }
		}

		public static readonly DependencyProperty StatusTextBrushProperty =
			DependencyProperty.Register("StatusTextBrush", typeof(Brush), typeof(StatusBarControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
		
		public Brush StatusTextBrush
		{
			get { return GetValue (StatusTextBrushProperty) as Brush; }
			set { SetValue (StatusTextBrushProperty, value); }
		}

	    private Xwt.Drawing.Image statusImage;
		public Xwt.Drawing.Image StatusImage
		{
			get { return statusImage; }
			set { statusImage = value; RaisePropertyChanged (); }
		}

	    private int buildResultCount;
		public int BuildResultCount
		{
			get { return buildResultCount; }
			set { buildResultCount = value; RaisePropertyChanged (); }
		}

	    private Xwt.Drawing.Image buildResultIcon;
		public Xwt.Drawing.Image BuildResultIcon
		{
			get { return buildResultIcon; }
			set { buildResultIcon = value; RaisePropertyChanged (); }
		}

	    private Visibility buildResultPanelVisibility = Visibility.Collapsed;
		public Visibility BuildResultPanelVisibility
		{
            get { return buildResultPanelVisibility; }
			set { buildResultPanelVisibility = value; RaisePropertyChanged (); }
		}

		public void SetCancellationTokenSource (CancellationTokenSource source)
		{
		}

	    private void RaisePropertyChanged ([CallerMemberName] string propName = null)
		{
			if (PropertyChanged != null)
				PropertyChanged (this, new System.ComponentModel.PropertyChangedEventArgs (propName));
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}

    internal class StatusIcon : ImageBox, StatusBarIcon
	{
	    private IStatusBar bar;

		public StatusIcon (IStatusBar bar)
		{
			this.bar = bar;
		}

		public void SetAlertMode (int seconds)
		{
			// Create fade-out fade-in animation.
		}

		public void Dispose ()
		{
			((StackPanel)Parent).Children.Remove (this);
		}

		public new string ToolTip
		{
			get { return (string)base.ToolTip; }
			set { base.ToolTip = value; }
		}

		protected override void OnMouseUp (MouseButtonEventArgs e)
		{
			base.OnMouseUp (e);

			Xwt.PointerButton button;
			switch (e.ChangedButton) {
				case MouseButton.Left:
					button = Xwt.PointerButton.Left;
					break;
				case MouseButton.Middle:
					button = Xwt.PointerButton.Middle;
					break;
				case MouseButton.Right:
					button = Xwt.PointerButton.Right;
					break;
				case MouseButton.XButton1:
					button = Xwt.PointerButton.ExtendedButton1;
					break;
				case MouseButton.XButton2:
					button = Xwt.PointerButton.ExtendedButton2;
					break;
				default:
					throw new NotSupportedException ();
			}

			if (Clicked != null)
				Clicked (this, new StatusBarIconClickedEventArgs {
					Button = button,
				});
		}

		public event EventHandler<StatusBarIconClickedEventArgs> Clicked;
	}
}
