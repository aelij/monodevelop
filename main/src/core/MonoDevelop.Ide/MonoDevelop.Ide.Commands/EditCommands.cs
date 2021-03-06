// EditCommands.cs
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


using MonoDevelop.Components.Commands;
using MonoDevelop.Ide.Gui;

namespace MonoDevelop.Ide.Commands
{
	public enum EditCommands
	{
		Copy,
		Cut,
		Paste,
		Delete,
		Rename,
		Undo,
		Redo,
		SelectAll,
		ToggleCodeComment,
		AddCodeComment,
		RemoveCodeComment,
		IndentSelection,
		UnIndentSelection,
		UppercaseSelection,
		LowercaseSelection,
		RemoveTrailingWhiteSpaces,
		
		JoinWithNextLine,
		MonodevelopPreferences,
		DefaultPolicies,
		InsertStandardHeader,
		
		EnableDisableFolding,
		ToggleFolding,
		ToggleAllFoldings,
		FoldDefinitions,
		
		/// <summary>
		/// The DeleteKey command can be used to easily bind the Delete key to a command handler
		/// Notice that the EditCommands.Delete command is not bound to the Delete key since it is not always
		/// convenient to fire the Delete command when pressing Delete (for example, while editing the name of
		/// a file in the solution pad.
		/// </summary>
		DeleteKey,
		InsertGuid,
		SortSelectedLines
	}
	
	internal class DefaultDeleteKeyHandler: CommandHandler
	{
		protected override void Run ()
		{
			// Nothing. Will be forwarded to gtk
		}

		protected override void Update (CommandInfo info)
		{
			// Forward to gtk
			info.Bypass = true;
		}
	}
	
	internal class DefaultDeleteHandler: CommandHandler
	{
		protected override void Run ()
		{
			if (IdeApp.Workbench.RootWindow.HasToplevelFocus) {
				Gtk.Editable editable = IdeApp.Workbench.RootWindow.Focus as Gtk.Editable;
				if (editable != null) {
					int cm;
					int cme;
					if (!editable.GetSelectionBounds (out cm, out cme)) {
						cm = editable.Position;
						cme = cm + 1;
					}
					editable.DeleteText (cm, cme);
					return;
				}
				Gtk.TextView tv = IdeApp.Workbench.RootWindow.Focus as Gtk.TextView;
				if (tv != null) {
					tv.Buffer.BeginUserAction ();
					Gtk.TextIter cm;
					Gtk.TextIter cme;
					if (!tv.Buffer.GetSelectionBounds (out cm, out cme)) {
						cm = tv.Buffer.GetIterAtMark (tv.Buffer.InsertMark);
						cme = cm;
						cme.ForwardCursorPosition ();
					}
					tv.Buffer.Delete (ref cm, ref cme);
					tv.Buffer.EndUserAction ();
					return;
				}
			}
		}
		
		protected override void Update (CommandInfo info)
		{
			object focus = IdeApp.Workbench.RootWindow.Focus;
			if (focus is Gtk.Editable)
				info.Enabled = ((Gtk.Editable)focus).IsEditable;
			else if (focus is Gtk.TextView)
				info.Enabled = ((Gtk.TextView)focus).Editable;
			else
				info.Enabled = false;
				
			info.Bypass = !info.Enabled;
		}
	}
	
	internal class DefaultCopyHandler: CommandHandler
	{
		protected override void Run ()
		{
			#if WIN32
			var wintv = System.Windows.Input.Keyboard.FocusedElement;
			var cmd = System.Windows.Input.ApplicationCommands.Copy;
			if (wintv != null && cmd.CanExecute (null, wintv)) {
				cmd.Execute (null, wintv);
				return;
			}
			#endif

			if (IdeApp.Workbench.RootWindow.HasToplevelFocus) {
				Gtk.Editable editable = IdeApp.Workbench.RootWindow.Focus as Gtk.Editable;
				if (editable != null) {
					editable.CopyClipboard ();
					return;
				}
				Gtk.TextView tv = IdeApp.Workbench.RootWindow.Focus as Gtk.TextView;
				if (tv != null) {
					Gtk.Clipboard clipboard = Gtk.Clipboard.Get (Gdk.Atom.Intern ("CLIPBOARD", false));
					tv.Buffer.CopyClipboard (clipboard);
					return;
				}
			}

			#if MAC
			var mactv = AppKit.NSApplication.SharedApplication.KeyWindow.FirstResponder as AppKit.NSText;
			if (mactv != null) {
				mactv.Copy (mactv);
				return;
			}
			#endif
		}

		protected override void Update (CommandInfo info)
		{
			object focus = IdeApp.Workbench.RootWindow.HasToplevelFocus ? IdeApp.Workbench.RootWindow.Focus : null;
			info.Enabled = (focus is Gtk.Editable || focus is Gtk.TextView);
#if MAC
			var macfocus = AppKit.NSApplication.SharedApplication?.KeyWindow?.FirstResponder;
			info.Enabled |= macfocus is AppKit.NSText;
#endif
#if WIN32
			var winfocus = System.Windows.Input.Keyboard.FocusedElement;
			info.Enabled |= winfocus != null;
#endif
			info.Bypass = !info.Enabled;
		}
	}	
	
	internal class DefaultCutHandler: CommandHandler
	{
		protected override void Run ()
		{
			#if WIN32
			var wintv = System.Windows.Input.Keyboard.FocusedElement;
			var cmd = System.Windows.Input.ApplicationCommands.Cut;
			if (wintv != null && cmd.CanExecute(null, wintv)) {
				cmd.Execute(null, wintv);
				return;
			}
			#endif

			if (IdeApp.Workbench.RootWindow.HasToplevelFocus) {
				Gtk.Editable editable = IdeApp.Workbench.RootWindow.Focus as Gtk.Editable;
				if (editable != null) {
					editable.CutClipboard ();
					return;
				}
				Gtk.TextView tv = IdeApp.Workbench.RootWindow.Focus as Gtk.TextView;
				if (tv != null) {
					Gtk.Clipboard clipboard = Gtk.Clipboard.Get (Gdk.Atom.Intern ("CLIPBOARD", false));
					tv.Buffer.CutClipboard (clipboard, true);
					return;
				}
			}

			#if MAC
			var mactv = AppKit.NSApplication.SharedApplication.KeyWindow.FirstResponder as AppKit.NSText;
			if (mactv != null) {
				mactv.Cut (mactv);
				return;
			}
			#endif
		}

		protected override void Update (CommandInfo info)
		{
			object focus = IdeApp.Workbench.RootWindow.HasToplevelFocus ? IdeApp.Workbench.RootWindow.Focus : null;
			if (focus is Gtk.Editable)
				info.Enabled = ((Gtk.Editable)focus).IsEditable;
			else if (focus is Gtk.TextView)
				info.Enabled = ((Gtk.TextView)focus).Editable;
			else
				info.Enabled = false;

#if MAC
			var macfocus = AppKit.NSApplication.SharedApplication?.KeyWindow?.FirstResponder;
			info.Enabled |= macfocus is AppKit.NSText;
#endif

#if WIN32
			var winfocus = System.Windows.Input.Keyboard.FocusedElement;
			info.Enabled |= winfocus != null;
#endif

			info.Bypass = !info.Enabled;
		}
	}
	
	internal class DefaultPasteHandler: CommandHandler
	{
		protected override void Run ()
		{
			#if WIN32
			var wintv = System.Windows.Input.Keyboard.FocusedElement;
			var cmd = System.Windows.Input.ApplicationCommands.Paste;
			if (wintv != null && cmd.CanExecute (null, wintv)) {
				cmd.Execute (null, wintv);
				return;
			}
			#endif
			if (IdeApp.Workbench.RootWindow.HasToplevelFocus) {
				Gtk.Editable editable = IdeApp.Workbench.RootWindow.Focus as Gtk.Editable;
				if (editable != null) {
					editable.PasteClipboard ();
					return;
				}
				Gtk.TextView tv = IdeApp.Workbench.RootWindow.Focus as Gtk.TextView;
				if (tv != null) {
					Gtk.Clipboard clipboard = Gtk.Clipboard.Get (Gdk.Atom.Intern ("CLIPBOARD", false));
					tv.Buffer.PasteClipboard (clipboard);
					return;
				}
			}

			#if MAC
			var mactv = AppKit.NSApplication.SharedApplication.KeyWindow.FirstResponder as AppKit.NSText;
			if (mactv != null) {
				mactv.Paste (mactv);
				return;
			}
			#endif
		}

		protected override void Update (CommandInfo info)
		{
			object focus = IdeApp.Workbench.RootWindow.HasToplevelFocus ? IdeApp.Workbench.RootWindow.Focus : null;
			if (focus is Gtk.Editable)
				info.Enabled = ((Gtk.Editable)focus).IsEditable;
			else if (focus is Gtk.TextView)
				info.Enabled = ((Gtk.TextView)focus).Editable;
			else
				info.Enabled = false;

#if MAC
			var macfocus = AppKit.NSApplication.SharedApplication?.KeyWindow?.FirstResponder;
			info.Enabled |= macfocus is AppKit.NSText;
#endif

#if WIN32
			var winfocus = System.Windows.Input.Keyboard.FocusedElement;
			info.Enabled |= winfocus != null;
#endif

			info.Bypass = !info.Enabled;
		}
	}
	
	internal class DefaultSelectAllHandler: CommandHandler
	{
		protected override void Run ()
		{
			#if WIN32
			var wintv = System.Windows.Input.Keyboard.FocusedElement;
			var cmd = System.Windows.Input.ApplicationCommands.SelectAll;
			if (wintv != null && cmd.CanExecute (null, wintv)) {
				cmd.Execute (null, wintv);
				return;
			}
			#endif
			if (IdeApp.Workbench.RootWindow.HasToplevelFocus) {
				Gtk.Editable editable = IdeApp.Workbench.RootWindow.Focus as Gtk.Editable;
				if (editable != null) {
					editable.SelectRegion (0, -1);
					return;
				}
				Gtk.TextView tv = IdeApp.Workbench.RootWindow.Focus as Gtk.TextView;
				if (tv != null) {
					tv.Buffer.SelectRange (tv.Buffer.StartIter, tv.Buffer.EndIter);
					return;
				}
			}

			#if MAC
			var mactv = AppKit.NSApplication.SharedApplication.KeyWindow.FirstResponder as AppKit.NSText;
			if (mactv != null) {
				mactv.SelectAll (mactv);
				return;
			}
			#endif
		}

		protected override void Update (CommandInfo info)
		{
			object focus = IdeApp.Workbench.RootWindow.HasToplevelFocus ? IdeApp.Workbench.RootWindow.Focus : null;
			info.Enabled = (focus is Gtk.Editable || focus is Gtk.TextView);
			info.Bypass = !IdeApp.Workbench.RootWindow.HasToplevelFocus;

#if MAC
			var macfocus = AppKit.NSApplication.SharedApplication?.KeyWindow?.FirstResponder;
			info.Enabled |= macfocus is AppKit.NSText;
#endif

#if WIN32
			var winfocus = System.Windows.Input.Keyboard.FocusedElement;
			info.Enabled |= winfocus != null;
#endif
		}
	}	
}
