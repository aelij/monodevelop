﻿//
// XwtBoxTooltip.cs
//
// Author:
//       Vsevolod Kukol <sevoku@microsoft.com>
//
// Copyright (c) 2016 Microsoft Corporation
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
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Gui;
using Xwt;
namespace MonoDevelop.Components
{
	public class XwtBoxTooltip : Widget
	{
		string tip;
		DiagnosticSeverity? severity;
		TooltipPopoverWindow tooltipWindow;
		Xwt.Popover xwtPopover;
		bool mouseOver, mouseOverTooltip;

		public string ToolTip {
			get { return tip; }
			set {
				tip = value;
				if (!string.IsNullOrEmpty (tip)) {
					if (tooltipWindow != null)
						tooltipWindow.Markup = value;
					if (xwtPopover != null)
						((Label)xwtPopover.Content).Markup = value;
				} else {
					if (tooltipWindow != null)
						tooltipWindow.Markup = string.Empty;
					if (xwtPopover != null) 
						((Label)xwtPopover.Content).Markup = string.Empty;
					HideTooltip ();
				}
			}
		}

		public DiagnosticSeverity? Severity {
			get {
				return severity;
			}
			set {
				severity = value;
				if (tooltipWindow != null)
					tooltipWindow.Severity = Severity;
				if (xwtPopover != null) {
					((Label)xwtPopover.Content).Font = Xwt.Drawing.Font.SystemFont.WithScaledSize (Styles.FontScale11);

					switch (severity.Value) {
					case DiagnosticSeverity.Info:
						xwtPopover.BackgroundColor = Styles.PopoverWindow.InformationBackgroundColor;
						break;

					case DiagnosticSeverity.Hidden:
						xwtPopover.BackgroundColor = Styles.PopoverWindow.InformationBackgroundColor;
						break;

					case DiagnosticSeverity.Error:
						xwtPopover.BackgroundColor = Styles.PopoverWindow.ErrorBackgroundColor;
						return;

					case DiagnosticSeverity.Warning:
						xwtPopover.BackgroundColor = Styles.PopoverWindow.WarningBackgroundColor;
						return;
					}
				}
			}
		}

		PopupPosition position;

		public PopupPosition Position {
			get {
				return position;
			}
			set {
				if (position != value) {
					position = value;
					if (tooltipWindow?.Visible == true || xwtPopover != null) {
						HideTooltip ();
						ShowTooltip ();
					}
				}
			}
		}

		public XwtBoxTooltip (Widget child)
		{
			if (child == null)
				throw new ArgumentNullException (nameof (child));
			
			Content = child;
			// FIXME: WPF blocks the main Gtk loop and makes TooltipPopoverWindow unusable.
			//        We use the Xwt.Popover as a workaround for now.
			if (Surface.ToolkitEngine.Type == ToolkitType.Wpf) {
				xwtPopover = new Popover ();
				xwtPopover.BackgroundColor = Styles.PopoverWindow.DefaultBackgroundColor;
				xwtPopover.Content = new Label { Wrap = WrapMode.Word };
				xwtPopover.Padding = 3;
			} else {
				tooltipWindow = new TooltipPopoverWindow ();
				tooltipWindow.ShowArrow = true;
			}
			Position = PopupPosition.Top;
			Severity = DiagnosticSeverity.Info;
		}

		protected override void OnMouseEntered (EventArgs args)
		{
			base.OnMouseEntered (args);
			mouseOver = true;
			ShowTooltip ();
		}

		protected override void OnMouseExited (EventArgs args)
		{
			base.OnMouseExited (args);
			mouseOver = false;
			HideTooltip ();
		}

		public bool ShowTooltip ()
		{
			if (!string.IsNullOrEmpty (tip)) {
				var rect = new Rectangle (0, 0, Content.Size.Width, Content.Size.Height);
				if (tooltipWindow != null)
					tooltipWindow.ShowPopup (Content, rect, Position);
				if (xwtPopover != null)
					xwtPopover.Show (GetXwtPosition(Position), Content, rect);
			}
			return false;
		}

		static Popover.Position GetXwtPosition (PopupPosition position)
		{
			switch (position) {
				case PopupPosition.Bottom:
					return Popover.Position.Bottom;
				default: 
					return Popover.Position.Top;
			}
		}

		public void HideTooltip ()
		{
			if (tooltipWindow != null) {
				tooltipWindow.Hide ();
			}
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing) {
				HideTooltip ();
				tooltipWindow?.Dispose ();
			}
			base.Dispose (disposing);
		}
	}
}
