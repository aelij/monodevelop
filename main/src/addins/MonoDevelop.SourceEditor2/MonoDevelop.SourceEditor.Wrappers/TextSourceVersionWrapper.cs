﻿//
// TextSourceVersionWrapper.cs
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
using MonoDevelop.Core.Text;

namespace MonoDevelop.SourceEditor.Wrappers
{
	class TextSourceVersionWrapper : ITextSourceVersion
	{
		readonly Mono.TextEditor.NRefactory.ITextSourceVersion version;

		public TextSourceVersionWrapper (Mono.TextEditor.NRefactory.ITextSourceVersion version)
		{
			if (version == null)
				throw new ArgumentNullException ("version");
			this.version = version;
		}

		#region ITextSourceVersion implementation

		bool ITextSourceVersion.BelongsToSameDocumentAs (ITextSourceVersion other)
		{
			var otherWrapper = other as TextSourceVersionWrapper;
			return otherWrapper != null && version.BelongsToSameDocumentAs (otherWrapper.version);
		}

		int ITextSourceVersion.CompareAge (ITextSourceVersion other)
		{
			var otherWrapper = (TextSourceVersionWrapper)other;
			return version.CompareAge (otherWrapper.version);
		}

		System.Collections.Generic.IEnumerable<TextChangeEventArgs> ITextSourceVersion.GetChangesTo (ITextSourceVersion other)
		{
			var otherWrapper = (TextSourceVersionWrapper)other;
			foreach (var change in version.GetChangesTo (otherWrapper.version))
				yield return new TextChangeEventArgsWrapper (change);
		}

		int ITextSourceVersion.MoveOffsetTo (ITextSourceVersion other, int oldOffset)
		{
			var otherWrapper = (TextSourceVersionWrapper)other;
			return version.MoveOffsetTo (otherWrapper.version, oldOffset);
		}
		#endregion
	}
}

