//
// MonoDevelopSourceText.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2016 Xamarin Inc. (http://xamarin.com)
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
using MonoDevelop.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace MonoDevelop.Ide.TypeSystem
{
	sealed class MonoDevelopSourceText : SourceText
	{
		readonly ITextSource doc;
		TextLineCollectionWrapper wrapper;

		public override System.Text.Encoding Encoding {
			get {
				return doc.Encoding;
			}
		}

		public MonoDevelopSourceText (ITextSource doc)
		{
			if (doc == null)
				throw new ArgumentNullException (nameof (doc));
			this.doc = doc;
		}

		protected override TextLineCollection GetLinesCore ()
		{
			var textDoc = doc as IReadonlyTextDocument;
			if (textDoc != null) {
				if (wrapper == null)
					wrapper = new TextLineCollectionWrapper (this, textDoc);
				return wrapper;
			}
			return base.GetLinesCore ();
		}

		class TextLineCollectionWrapper : TextLineCollection
		{
			readonly MonoDevelopSourceText parent;
			readonly IReadonlyTextDocument textDoc;

			public TextLineCollectionWrapper (MonoDevelopSourceText parent, IReadonlyTextDocument textDoc)
			{
				this.parent = parent;
				this.textDoc = textDoc;
			}

			public override int Count {
				get {
					return textDoc.LineCount;
				}
			}

			public override TextLine this[int index] {
				get {
					var line = textDoc.GetLine (index + 1);
					return TextLine.FromSpan (parent, new TextSpan(line.Offset, line.Length));
				}
			}

			public override TextLine GetLineFromPosition (int position)
			{
				var line = textDoc.GetLineByOffset (position);
				return TextLine.FromSpan (parent, new TextSpan(line.Offset, line.Length));
			}

			public override LinePosition GetLinePosition (int position)
			{
				var loc = textDoc.OffsetToLocation (position);
				return new LinePosition (loc.Line - 1, loc.Column - 1);
			}

			public override int IndexOf (int position)
			{
				return textDoc.OffsetToLineNumber (position) - 1;
			}
		}

		#region implemented abstract members of SourceText
		public override void CopyTo (int sourceIndex, char[] destination, int destinationIndex, int count)
		{
			doc.CopyTo (sourceIndex, destination, destinationIndex, count);
		}

		public override int Length {
			get {
				return doc.Length;
			}
		}

		public override char this [int index] {
			get {
				return doc.GetCharAt (index);
			}
		}
		#endregion
	}
	
}