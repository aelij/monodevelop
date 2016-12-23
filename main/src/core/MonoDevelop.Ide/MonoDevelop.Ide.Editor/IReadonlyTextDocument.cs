//
// IReadonlyTextDocument.cs
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
using System.Collections.Generic;
using System.Text;
using MonoDevelop.Core;
using MonoDevelop.Ide.Editor.Util;

namespace MonoDevelop.Ide.Editor
{
	public interface IReadonlyTextDocument : ITextSource
	{
		bool IsReadOnly { get; }

		FilePath FileName { get; }

		string MimeType { get; }

		/// <summary>
		/// Gets the number of lines in the document.
		/// </summary>
		int LineCount { get; }

		int LocationToOffset (int line, int column);

		DocumentLocation OffsetToLocation (int offset);

		IDocumentLine GetLine (int lineNumber);

		IDocumentLine GetLineByOffset (int offset);
	}

	public sealed class DiffOptions
	{
		public bool IncludeEol { get; private set; }
		public bool TrimLines { get; private set; }

		public DiffOptions (bool includeEol = true, bool trimLines = false)
		{
			IncludeEol = includeEol;
			TrimLines = trimLines;
		}
	}

	public static class ReadonlyTextDocumentExtensions
	{
		/// <summary>
		/// Retrieves the text for a portion of the document.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">offset or length is outside the valid range.</exception>
		public static string GetTextAt(this IReadonlyTextDocument source, ISegment segment)
		{
			if (source == null)
				throw new ArgumentNullException (nameof (source));
			return source.GetTextAt (segment.Offset, segment.Length);
		}

		public static IEnumerable<IDocumentLine> GetLines (this IReadonlyTextDocument document)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			return document.GetLinesStartingAt (1);
		}

		public static IEnumerable<IDocumentLine> GetLinesBetween (this IReadonlyTextDocument document, int startLine, int endLine)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (startLine < 1 || startLine > document.LineCount)
				throw new ArgumentOutOfRangeException (nameof (startLine), startLine, string.Format ("value should be between 1 and {0}", document.LineCount));
			if (endLine < 1 || endLine > document.LineCount)
				throw new ArgumentOutOfRangeException (nameof (endLine), endLine, string.Format ("value should be between 1 and {0}", document.LineCount));

			var curLine = document.GetLine (startLine);
			int count = endLine - startLine;
			while (curLine != null && count --> 0) {
				yield return curLine;
				curLine = curLine.NextLine;
			}
		}

		public static IEnumerable<IDocumentLine> GetLinesStartingAt (this IReadonlyTextDocument document, int startLine)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (startLine < 1 || startLine > document.LineCount)
				throw new ArgumentOutOfRangeException (nameof (startLine), startLine, string.Format ("value should be between 1 and {0}", document.LineCount));
			var curLine = document.GetLine (startLine);
			while (curLine != null) {
				yield return curLine;
				curLine = curLine.NextLine;
			}
		}

		public static IEnumerable<IDocumentLine> GetLinesReverseStartingAt (this IReadonlyTextDocument document, int startLine)
		{
			if (startLine < 1 || startLine > document.LineCount)
				throw new ArgumentOutOfRangeException (nameof (startLine), startLine, string.Format ("value should be between 1 and {0}", document.LineCount));
			var curLine = document.GetLine (startLine);
			while (curLine != null) {
				yield return curLine;
				curLine = curLine.PreviousLine;
			}
		}

		public static string GetTextBetween (this IReadonlyTextDocument document, int startLine, int startColumn, int endLine, int endColumn)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			return document.GetTextBetween (new DocumentLocation (startLine, startColumn), new DocumentLocation (endLine, endColumn));
		}

		public static string GetLineIndent (this IReadonlyTextDocument document, int lineNumber)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			return document.GetLineIndent (document.GetLine (lineNumber));
		}

		public static string GetLineIndent (this IReadonlyTextDocument document, IDocumentLine segment)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return segment.GetIndentation (document);
		}

		public static string GetLineText (this IReadonlyTextDocument document, IDocumentLine line, bool includeDelimiter = false)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (line == null)
				throw new ArgumentNullException (nameof (line));
			return document.GetTextAt (includeDelimiter ? line.SegmentIncludingDelimiter : line);
		}

		public static string GetLineText (this IReadonlyTextDocument document, int lineNumber, bool includeDelimiter = false)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			var line = document.GetLine (lineNumber);
			return document.GetTextAt (includeDelimiter ? line.SegmentIncludingDelimiter : line);
		}

		static int [] GetDiffCodes (IReadonlyTextDocument document, ref int codeCounter, Dictionary<string, int> codeDictionary, DiffOptions options)
		{
			int i = 0;
			var result = new int [document.LineCount];
			foreach (var line in document.GetLinesStartingAt (1)) {
				string lineText = document.GetTextAt (line.Offset, options.IncludeEol ? line.LengthIncludingDelimiter : line.Length);
				if (options.TrimLines)
					lineText = lineText.Trim ();
				int curCode;
				if (!codeDictionary.TryGetValue (lineText, out curCode)) {
					codeDictionary [lineText] = curCode = ++codeCounter;
				}
				result [i] = curCode;
				i++;
			}
			return result;
		}

		public static IEnumerable<DiffHunk> GetDiff (this IReadonlyTextDocument document, IReadonlyTextDocument changedDocument, bool includeEol = true)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (changedDocument == null)
				throw new ArgumentNullException (nameof (changedDocument));
			var codeDictionary = new Dictionary<string, int> ();
			int codeCounter = 0;
			var options = new DiffOptions (includeEol);
			return Diff.GetDiff<int> (GetDiffCodes (document, ref codeCounter, codeDictionary, options),
				GetDiffCodes (changedDocument, ref codeCounter, codeDictionary, options));
		}

		public static IEnumerable<DiffHunk> GetDiff (this IReadonlyTextDocument document, IReadonlyTextDocument changedDocument, DiffOptions options)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (changedDocument == null)
				throw new ArgumentNullException (nameof (changedDocument));
			var codeDictionary = new Dictionary<string, int> ();
			int codeCounter = 0;
			return Diff.GetDiff<int> (GetDiffCodes (document, ref codeCounter, codeDictionary, options),
				GetDiffCodes (changedDocument, ref codeCounter, codeDictionary, options));
		}

		public static string GetDiffAsString (this IReadonlyTextDocument document, IReadonlyTextDocument changedDocument, bool includeEol = true)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (changedDocument == null)
				throw new ArgumentNullException (nameof (changedDocument));
			return Diff.GetDiffString (GetDiff (document, changedDocument, includeEol), document, changedDocument, document.FileName, changedDocument.FileName);
		}

		public static int OffsetToLineNumber (this IReadonlyTextDocument document, int offset)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (offset < 0 || offset > document.Length)
				throw new ArgumentOutOfRangeException (nameof (offset), string.Format ("offset should be between 0 and <={0} but was {1}.", document.Length, offset));
			return document.OffsetToLocation (offset).Line;
		}

		public static int LocationToOffset (this IReadonlyTextDocument document, DocumentLocation location)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			return document.LocationToOffset (location.Line, location.Column);
		}

		public static string GetTextBetween (this IReadonlyTextDocument document, int startOffset, int endOffset)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			if (startOffset < 0 || startOffset > document.Length)
				throw new ArgumentNullException (nameof (startOffset));
			if (endOffset < 0 || endOffset > document.Length)
				throw new ArgumentNullException (nameof (endOffset));
			if (startOffset > endOffset)
				throw new InvalidOperationException ();
			return document.GetTextAt (startOffset, endOffset - startOffset);
		}

		public static string GetTextBetween (this IReadonlyTextDocument document, DocumentLocation start, DocumentLocation end)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			return document.GetTextBetween (document.LocationToOffset (start), document.LocationToOffset (end));
		}

		public static string GetEolMarker (this IReadonlyTextDocument document)
		{
			if (document == null)
				throw new ArgumentNullException (nameof (document));
			string eol = null;
			if (document.LineCount > 0) {
				var line = document.GetLine (1);
				if (line.DelimiterLength > 0) 
					eol = document.GetTextAt (line.Length, line.DelimiterLength);
			}

			return !string.IsNullOrEmpty (eol) ? eol : DefaultSourceEditorOptions.Instance.DefaultEolMarker;
		}
	}
}