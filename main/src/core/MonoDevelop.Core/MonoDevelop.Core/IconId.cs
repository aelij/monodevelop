// 
// IconId.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
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

namespace MonoDevelop.Core
{
    [System.Diagnostics.DebuggerDisplay("{" + nameof (id) + "}")]
    public struct IconId : IEquatable<IconId>
    {
        private readonly string id;

        public static readonly IconId Null = new IconId(null);

        public static IconNameRequestHandler IconNameRequestHandler;

        public IconId(string id)
        {
            this.id = id;
        }

        public bool IsNull => id == null;

        public string Name
        {
            get
            {
                // If the icon is converted to string, fire the icon request event, to ensure that
                // the icon it represents is loaded.
                IconNameRequestHandler?.Invoke(id);
                return id;
            }
        }

        public static implicit operator IconId(string name)
        {
            return new IconId(name);
        }

        public static implicit operator string(IconId icon)
        {
            return icon.Name;
        }

        public static bool operator ==(IconId name1, IconId name2)
        {
            return name1.id == name2.id;
        }

        public static bool operator !=(IconId name1, IconId name2)
        {
            return name1.id != name2.id;
        }

        public static bool operator ==(IconId name1, string name2)
        {
            return name1.id == name2;
        }

        public static bool operator !=(IconId name1, string name2)
        {
            return name1.id != name2;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is IconId))
                return false;

            IconId fn = (IconId)obj;
            return id == fn.id;
        }

        public override int GetHashCode()
        {
            if (id == null)
                return 0;
            return id.GetHashCode();
        }

        public override string ToString()
        {
            return Name;
        }

        bool IEquatable<IconId>.Equals(IconId other)
        {
            return id == other.id;
        }
    }

    public delegate void IconNameRequestHandler(string id);
}
