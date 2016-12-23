// PropertyBag.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
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
using System.Xml;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Collections.Immutable;

namespace MonoDevelop.Core
{
	public sealed class PropertyBag: IDisposable
	{
		ImmutableDictionary<string,object> properties = ImmutableDictionary<string,object>.Empty;
		bool isShared;

		void AssertMainThread ()
		{
			if (isShared)
				Runtime.AssertMainThread ();
		}

		public void SetShared ()
		{
			isShared = true;
		}

		public bool IsEmpty {
			get { return properties.Count == 0; }
		}
		
		public T GetValue<T> ()
		{
			return GetValue<T> (typeof(T).FullName);
		}
		
		public T GetValue<T> (string name)
		{
			return GetValue<T> (name, (object) null);
		}
		
		public T GetValue<T> (string name, object ctx)
		{
			return GetValue<T> (name, default(T), ctx);
		}
		
		public T GetValue<T> (string name, T defaultValue)
		{
			return GetValue<T> (name, defaultValue, null);
		}
		
		public T GetValue<T> (string name, T defaultValue, object ctx)
		{
			if (properties != null) {
				object val;
				if (properties.TryGetValue (name, out val)) {
					return (T) val;
				}
			}
			return defaultValue;
		}
		
		public void SetValue<T> (T value)
		{
			SetValue<T> (typeof(T).FullName, value);
		}
		
		public void SetValue<T> (string name, T value)
		{
			AssertMainThread ();
			properties = properties.SetItem (name, value);
			OnChanged (name);
		}
		
		public bool RemoveValue<T> ()
		{
			return RemoveValue (typeof(T).FullName);
		}
		
		public bool RemoveValue (string name)
		{
			AssertMainThread ();
			var cc = properties.Count;

			properties = properties.Remove (name);
			if (cc == properties.Count)
				return false;

			OnChanged (name);
			return true;
		}
		
		public bool HasValue<T> ()
		{
			return HasValue (typeof(T).FullName);
		}
		
		public bool HasValue (string name)
		{
			return properties.ContainsKey (name);
		}

		public event EventHandler<PropertyBagChangedEventArgs> Changed;

		void OnChanged (string name)
		{
			var handler = Changed;

			if (handler != null)
				handler (this, new PropertyBagChangedEventArgs (name));
		}
		
		public void Dispose ()
		{
			AssertMainThread ();
			foreach (object ob in properties.Values) {
				IDisposable disp = ob as IDisposable;
				if (disp != null)
					disp.Dispose ();
			}
			properties = properties.Clear ();
		}
	}

	public class PropertyBagChangedEventArgs : EventArgs
	{
		public string PropertyName { get; private set; }

		public PropertyBagChangedEventArgs (string name)
		{
			PropertyName = name;
		}
	}
}
