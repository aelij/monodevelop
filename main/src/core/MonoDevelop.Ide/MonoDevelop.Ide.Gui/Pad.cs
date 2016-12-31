//
// Pad.cs
//
// Author:
//   Lluis Sanchez Gual
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using MonoDevelop.Core;

namespace MonoDevelop.Ide.Gui
{
    public class Pad
    {
        private readonly IPadWindow window;
        private readonly PadDefinition content;
        private readonly IDefaultWorkbenchWindow workbenchWindow;
        private string[] categories;

        internal Pad(IDefaultWorkbenchWindow workbenchWindow, PadDefinition content)
        {
            window = workbenchWindow.GetPadWindow(content);
            window.PadHidden += delegate
            {
                IsOpenedAutomatically = false;
            };
            this.content = content;
            this.workbenchWindow = workbenchWindow;
        }

        internal PadDefinition InternalContent => content;

        public object Content => window.Content;

        public string Title => window.Title;

        public IconId Icon => window.Icon;

        public string Id => window.Id;

        public bool IsOpenedAutomatically
        {
            get;
            set;
        }

        public string[] Categories => categories ?? (categories = new[] { "Pads" });

        public string Group => content.Group;

        public void BringToFront()
        {
            BringToFront(false);
        }

        public void BringToFront(bool grabFocus)
        {
            workbenchWindow.BringToFront(content);
            window.Activate(grabFocus);
        }

        public bool AutoHide
        {
            get { return window.AutoHide; }
            set { window.AutoHide = value; }
        }

        public bool Visible
        {
            get
            {
                return window.Visible;
            }
            set
            {
                window.Visible = value;
            }
        }

        public bool Sticky
        {
            get
            {
                return window.Sticky;
            }
            set
            {
                window.Sticky = value;
            }
        }

        internal IPadWindow Window => window;

        internal IMementoCapable GetMementoCapable()
        {
            PadWindow pw = (PadWindow)window;
            return pw.GetMementoCapable();
        }

        public void Destroy()
        {
            Visible = false;
            workbenchWindow.RemovePad(content);
        }
    }
}
