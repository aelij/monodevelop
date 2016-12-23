//
// TextEncoding.cs
//
// Author:
//   Lluis Sanchez Gual
//
// Copyright (C) 2006 Novell, Inc (http://www.novell.com)
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

using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using MonoDevelop.Core;

namespace MonoDevelop.Projects.Text
{
	public class TextEncoding
	{
		static TextEncoding[] supported;
		static TextEncoding[] conversion;
		
		string name;
		string id;
		int codePage;
		
		internal TextEncoding (string id, string name)
		{
			this.id = id;
			this.name = name;
			codePage = -1;
			try {
				Encoding e = Encoding.GetEncoding (id);
				if (e != null)
					codePage = e.CodePage;
			} catch {
				// Ignore
			}
		}
		
		public string Name {
			get { return name; }
		}
		
		public string Id {
			get { return id; }
		}
		
		public int CodePage {
			get { return codePage; }
		}
		
		// Returns a list of encodings supported by MD
		
		public static TextEncoding[] SupportedEncodings {
			get {
				if (supported == null) {
					supported = new TextEncoding [encodings.GetUpperBound (0)];
					for (int n=0; n<encodings.GetUpperBound (0); n++)
						supported[n] = new TextEncoding (encodings[n,0], encodings[n,1]);
				}
				return supported ?? new TextEncoding[0];
			}
		}
		
		// Returns a list of encodings currently selected by the user
		// that will be considered automatic encoding detection.
		
		public static TextEncoding[] ConversionEncodings {
			get {
				if (conversion == null) {
					string str = PropertyService.Get ("MonoDevelop.Projects.Text.ConversionEncodings", "");
					if (str.Length == 0) {
						// Add some default encodings
						str = DefaultEncoding;
						foreach (string s in defaultEncodings) {
							if (str.IndexOf (s) == -1)
								str += " " + s;
						}
					}
					List<string> encs = new List<string> (str.Split (' '));
					List<TextEncoding> list = new List<TextEncoding> ();
					// hack to insert UTF-16 as 2nd encoding in existing monodevelop properties
					// can be taken out 2010.
					if (!encs.Contains("UTF-16"))
						encs.Insert (encs.Count > 0 ? 1 : 0, "UTF-16");
					foreach (string id in encs) {
						TextEncoding enc = GetEncoding (id);
						if (enc != null)
							list.Add (enc);
					}
					conversion = list.ToArray ();
				}
				return conversion ?? new TextEncoding[0];
			}
			set {
				conversion = value;
				string s = "";
				foreach (TextEncoding e in conversion)
					s += e.Id + " ";
				PropertyService.Set ("MonoDevelop.Projects.Text.ConversionEncodings", s.Trim());
				PropertyService.SaveProperties ();
			}
		}

		public static TextEncoding GetEncoding (string id)
		{
			foreach (TextEncoding e in SupportedEncodings) {
				if (e.Id == id)
					return e;
			}
			return null;
		}

		public static TextEncoding GetEncoding (int codePage)
		{
			foreach (TextEncoding e in SupportedEncodings) {
				if (e.CodePage == codePage)
					return e;
			}
			return null;
		}
		
		public static string DefaultEncoding {
			get { return "UTF-8"; }
		}
		
		static string[] defaultEncodings = {"UTF-8", "ISO-8859-15", "UTF-16"};
		
		static string[,] encodings = {
		
		};
		
	}
}
