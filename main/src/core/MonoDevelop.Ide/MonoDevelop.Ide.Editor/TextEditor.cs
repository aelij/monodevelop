﻿//
// ITextEditor.cs
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
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Editor.Extension;
using System.IO;
using MonoDevelop.Ide.Editor.Highlighting;
using MonoDevelop.Core;
using System.Linq;
using MonoDevelop.Components;
using System.ComponentModel;
using System.Threading;
using Xwt;

namespace MonoDevelop.Ide.Editor
{
	public sealed class TextEditor : Control, ITextDocument, IDisposable
	{
		readonly ITextEditorImpl textEditorImpl;
		IReadonlyTextDocument ReadOnlyTextDocument { get { return textEditorImpl.Document; } }
		ITextDocument ReadWriteTextDocument { get { return (ITextDocument)textEditorImpl.Document; } }

		public ITextSourceVersion Version {
			get {
				return ReadOnlyTextDocument.Version;
			}
		}

		public TextEditorType TextEditorType { get; internal set; }

		public event EventHandler SelectionChanged {
			add { textEditorImpl.SelectionChanged += value; }
			remove { textEditorImpl.SelectionChanged -= value; }
		}

		public event EventHandler CaretPositionChanged {
			add { textEditorImpl.CaretPositionChanged += value; }
			remove { textEditorImpl.CaretPositionChanged -= value; }
		}

		public event EventHandler BeginAtomicUndoOperation {
			add { textEditorImpl.BeginAtomicUndoOperation += value; }
			remove { textEditorImpl.BeginAtomicUndoOperation -= value; }
		}

		public event EventHandler EndAtomicUndoOperation {
			add { textEditorImpl.EndAtomicUndoOperation += value; }
			remove { textEditorImpl.EndAtomicUndoOperation -= value; }
		}

		public event EventHandler<TextChangeEventArgs> TextChanging {
			add { ReadWriteTextDocument.TextChanging += value; }
			remove { ReadWriteTextDocument.TextChanging -= value; }
		}

		public event EventHandler<TextChangeEventArgs> TextChanged {
			add { ReadWriteTextDocument.TextChanged += value; }
			remove { ReadWriteTextDocument.TextChanged -= value; }
		}

		public event EventHandler<MouseMovedEventArgs> MouseMoved {
			add { textEditorImpl.MouseMoved += value; }
			remove { textEditorImpl.MouseMoved -= value; }
		}

		internal event EventHandler VAdjustmentChanged {
			add { textEditorImpl.VAdjustmentChanged += value; }
			remove { textEditorImpl.VAdjustmentChanged -= value; }
		}

		internal event EventHandler HAdjustmentChanged {
			add { textEditorImpl.HAdjustmentChanged += value; }
			remove { textEditorImpl.HAdjustmentChanged -= value; }
		}

		public char this[int offset] {
			get {
				return ReadOnlyTextDocument [offset];
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument [offset] = value;
			}
		}

//		public event EventHandler<LineEventArgs> LineChanged {
//			add { textEditorImpl.LineChanged += value; }
//			remove { textEditorImpl.LineChanged -= value; }
//		}
//
//		public event EventHandler<LineEventArgs> LineInserted {
//			add { textEditorImpl.LineInserted += value; }
//			remove { textEditorImpl.LineInserted -= value; }
//		}
//
//		public event EventHandler<LineEventArgs> LineRemoved {
//			add { textEditorImpl.LineRemoved += value; }
//			remove { textEditorImpl.LineRemoved -= value; }
//		}

		public ITextEditorOptions Options {
			get {
				return textEditorImpl.Options;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.Options = value;
				OnOptionsChanged (EventArgs.Empty);
			}
		}

		public event EventHandler OptionsChanged;

		void OnOptionsChanged (EventArgs e)
		{
			var handler = OptionsChanged;
			if (handler != null)
				handler (this, e);
		}

		public EditMode EditMode {
			get {
				return textEditorImpl.EditMode;
			}
		}

		public DocumentLocation CaretLocation {
			get {
				return textEditorImpl.CaretLocation;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.CaretLocation = value;
			}
		}

		public SemanticHighlighting SemanticHighlighting {
			get {
				return textEditorImpl.SemanticHighlighting;
			}
			set {
				textEditorImpl.SemanticHighlighting = value;
			}
		}

		public int CaretLine {
			get {
				return CaretLocation.Line;
			}
			set {
				CaretLocation = new DocumentLocation (value, CaretColumn);
			}
		}

		public int CaretColumn {
			get {
				return CaretLocation.Column;
			}
			set {
				CaretLocation = new DocumentLocation (CaretLine, value);
			}
		}

		public int CaretOffset {
			get {
				return textEditorImpl.CaretOffset;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.CaretOffset = value;
			}
		}

		public bool IsReadOnly {
			get {
				return ReadOnlyTextDocument.IsReadOnly;
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument.IsReadOnly = value;
			}
		}

		public bool IsSomethingSelected {
			get {
				return textEditorImpl.IsSomethingSelected;
			}
		}

		public SelectionMode SelectionMode {
			get {
				return textEditorImpl.SelectionMode;
			}
		}

		public ISegment SelectionRange {
			get {
				return textEditorImpl.SelectionRange;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.SelectionRange = value;
			}
		}

		public DocumentRegion SelectionRegion {
			get {
				return textEditorImpl.SelectionRegion;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.SelectionRegion = value;
			}
		}
		
		public int SelectionAnchorOffset {
			get {
				return textEditorImpl.SelectionAnchorOffset;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.SelectionAnchorOffset = value;
			}
		}

		public int SelectionLeadOffset {
			get {
				return textEditorImpl.SelectionLeadOffset;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.SelectionLeadOffset = value;
			}
		}

		public string SelectedText {
			get {
				return IsSomethingSelected ? ReadOnlyTextDocument.GetTextAt (SelectionRange) : null;
			}
			set {
				Runtime.AssertMainThread ();
				var selection = SelectionRange;
				ReplaceText (selection, value);
				SelectionRange = new TextSegment (selection.Offset, value.Length);
			}
		}

		public bool IsInAtomicUndo {
			get {
				return ReadWriteTextDocument.IsInAtomicUndo;
			}
		}

		public double LineHeight {
			get {
				return textEditorImpl.LineHeight;
			}
		}

		/// <summary>
		/// Gets or sets the type of the MIME.
		/// </summary>
		/// <value>The type of the MIME.</value>
		public string MimeType {
			get {
				return ReadOnlyTextDocument.MimeType;
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument.MimeType = value;
			}
		}

		public event EventHandler MimeTypeChanged {
			add { ReadWriteTextDocument.MimeTypeChanged += value; }
			remove { ReadWriteTextDocument.MimeTypeChanged -= value; }
		}

		public string Text {
			get {
				return ReadOnlyTextDocument.Text;
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument.Text = value;
			}
		}

		/// <summary>
		/// Gets the eol marker. On a text editor always use that and not GetEolMarker.
		/// The EOL marker of the document may get overwritten my the one from the options.
		/// </summary>
		public string EolMarker {
			get {
				if (Options.OverrideDocumentEolMarker)
					return Options.DefaultEolMarker;
				return ReadOnlyTextDocument.GetEolMarker ();
			}
		}

		public bool UseBOM {
			get {
				return ReadOnlyTextDocument.UseBOM;
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument.UseBOM = value;
			}
		}

		public Encoding Encoding {
			get {
				return ReadOnlyTextDocument.Encoding;
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument.Encoding = value;
			}
		}

		public int LineCount {
			get {
				return ReadOnlyTextDocument.LineCount;
			}
		}

		/// <summary>
		/// Gets the name of the file the document is stored in.
		/// Could also be a non-existent dummy file name or null if no name has been set.
		/// </summary>
		public FilePath FileName {
			get {
				return ReadOnlyTextDocument.FileName;
			}
			set {
				Runtime.AssertMainThread ();
				ReadWriteTextDocument.FileName = value;
			}
		}

		public event EventHandler FileNameChanged {
			add {
				ReadWriteTextDocument.FileNameChanged += value;
			}
			remove {
				ReadWriteTextDocument.FileNameChanged -= value;
			}
		}

		public int Length {
			get {
				return ReadOnlyTextDocument.Length;
			}
		}

		public double ZoomLevel {
			get {
				return textEditorImpl.ZoomLevel;
			}
			set {
				Runtime.AssertMainThread ();
				textEditorImpl.ZoomLevel = value;
			}
		}

		public event EventHandler ZoomLevelChanged {
			add {
				textEditorImpl.ZoomLevelChanged += value;
			}
			remove {
				textEditorImpl.ZoomLevelChanged -= value;
			}
		}

		public string ContextMenuPath {
			get {
				return textEditorImpl.ContextMenuPath;
			}
			set {
				textEditorImpl.ContextMenuPath = value;
			}
		}

		public IDisposable OpenUndoGroup ()
		{
			Runtime.AssertMainThread ();
			return ReadWriteTextDocument.OpenUndoGroup ();
		}

		public void SetSelection (int anchorOffset, int leadOffset)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.SetSelection (anchorOffset, leadOffset);
		}

		public void SetSelection (DocumentLocation anchor, DocumentLocation lead)
		{
			Runtime.AssertMainThread ();
			SetSelection (LocationToOffset (anchor), LocationToOffset (lead));
		}

		public void SetCaretLocation (DocumentLocation location, bool usePulseAnimation = false, bool centerCaret = true)
		{
			Runtime.AssertMainThread ();
			CaretLocation = location;
			if (centerCaret) {
				CenterTo (CaretLocation);
			} else {
				ScrollTo (CaretLocation);
			}
			if (usePulseAnimation)
				StartCaretPulseAnimation ();
		}

		public void SetCaretLocation (int line, int col, bool usePulseAnimation = false, bool centerCaret = true)
		{
			Runtime.AssertMainThread ();
			CaretLocation = new DocumentLocation (line, col);
			if (centerCaret) {
				CenterTo (CaretLocation);
			} else {
				ScrollTo (CaretLocation);
			}
			if (usePulseAnimation)
				StartCaretPulseAnimation ();
		}

		public void ClearSelection ()
		{
			Runtime.AssertMainThread ();
			textEditorImpl.ClearSelection ();
		}

		public void CenterToCaret ()
		{
			Runtime.AssertMainThread ();
			textEditorImpl.CenterToCaret ();
		}

		public void StartCaretPulseAnimation ()
		{
			Runtime.AssertMainThread ();
			textEditorImpl.StartCaretPulseAnimation ();
		}

		public int EnsureCaretIsNotVirtual ()
		{
			Runtime.AssertMainThread ();
			return textEditorImpl.EnsureCaretIsNotVirtual ();
		}

		public void FixVirtualIndentation ()
		{
			Runtime.AssertMainThread ();
			textEditorImpl.FixVirtualIndentation ();
		}

		public void RunWhenLoaded (Action action)
		{
			if (action == null)
				throw new ArgumentNullException (nameof (action));
			textEditorImpl.RunWhenLoaded (action);
		}

		public void RunWhenRealized (Action action)
		{
			if (action == null)
				throw new ArgumentNullException (nameof (action));
			textEditorImpl.RunWhenRealized (action);
		}

		public string FormatString (DocumentLocation insertPosition, string code)
		{
			return textEditorImpl.FormatString (LocationToOffset (insertPosition), code);
		}

		public string FormatString (int offset, string code)
		{
			return textEditorImpl.FormatString (offset, code);
		}

		public void StartInsertionMode (InsertionModeOptions insertionModeOptions)
		{
			if (insertionModeOptions == null)
				throw new ArgumentNullException (nameof (insertionModeOptions));
			Runtime.AssertMainThread ();
			textEditorImpl.StartInsertionMode (insertionModeOptions);
		}

		public void StartTextLinkMode (TextLinkModeOptions textLinkModeOptions)
		{
			if (textLinkModeOptions == null)
				throw new ArgumentNullException (nameof (textLinkModeOptions));
			Runtime.AssertMainThread ();
			textEditorImpl.StartTextLinkMode (textLinkModeOptions);
		}

		public void InsertAtCaret (string text)
		{
			Runtime.AssertMainThread ();
			InsertText (CaretOffset, text);
		}

		public DocumentLocation PointToLocation (double xp, double yp, bool endAtEol = false)
		{
			return textEditorImpl.PointToLocation (xp, yp, endAtEol);
		}

		public Xwt.Point LocationToPoint (DocumentLocation location)
		{
			return textEditorImpl.LocationToPoint (location.Line, location.Column);
		}

		public Xwt.Point LocationToPoint (int line, int column)
		{
			return textEditorImpl.LocationToPoint (line, column);
		}

		public string GetLineText (int line, bool includeDelimiter = false)
		{
			var segment = GetLine (line);
			return GetTextAt (includeDelimiter ? segment.SegmentIncludingDelimiter : segment);
		}

		public int LocationToOffset (int line, int column)
		{
			return ReadOnlyTextDocument.LocationToOffset (new DocumentLocation (line, column));
		}

		public int LocationToOffset (DocumentLocation location)
		{
			return ReadOnlyTextDocument.LocationToOffset (location);
		}

		public DocumentLocation OffsetToLocation (int offset)
		{
			return ReadOnlyTextDocument.OffsetToLocation (offset);
		}

		public void InsertText (int offset, string text)
		{
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.InsertText (offset, text);
		}

		public void InsertText (int offset, ITextSource text)
		{
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.InsertText (offset, text);
		}

		public void RemoveText (int offset, int count)
		{
			Runtime.AssertMainThread ();
			RemoveText (new TextSegment (offset, count));
		}

		public void RemoveText (ISegment segment)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.RemoveText (segment);
		}

		public void ReplaceText (int offset, int count, string value)
		{
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.ReplaceText (offset, count, value);
		}

		public void ReplaceText (int offset, int count, ITextSource value)
		{
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.ReplaceText (offset, count, value);
		}

		public void ReplaceText (ISegment segment, string value)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.ReplaceText (segment.Offset, segment.Length, value);
		}

		public void ReplaceText (ISegment segment, ITextSource value)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			Runtime.AssertMainThread ();
			ReadWriteTextDocument.ReplaceText (segment.Offset, segment.Length, value);
		}

		public IDocumentLine GetLine (int lineNumber)
		{
			return ReadOnlyTextDocument.GetLine (lineNumber);
		}

		public IDocumentLine GetLineByOffset (int offset)
		{
			return ReadOnlyTextDocument.GetLineByOffset (offset);
		}

		public int OffsetToLineNumber (int offset)
		{
			return ReadOnlyTextDocument.OffsetToLineNumber (offset);
		}

		public void AddMarker (IDocumentLine line, ITextLineMarker lineMarker)
		{
			if (line == null)
				throw new ArgumentNullException (nameof (line));
			if (lineMarker == null)
				throw new ArgumentNullException (nameof (lineMarker));
			Runtime.AssertMainThread ();
			textEditorImpl.AddMarker (line, lineMarker);
		}

		public void AddMarker (int lineNumber, ITextLineMarker lineMarker)
		{
			if (lineMarker == null)
				throw new ArgumentNullException (nameof (lineMarker));
			Runtime.AssertMainThread ();
			AddMarker (GetLine (lineNumber), lineMarker);
		}

		public void RemoveMarker (ITextLineMarker lineMarker)
		{
			if (lineMarker == null)
				throw new ArgumentNullException (nameof (lineMarker));
			Runtime.AssertMainThread ();
			textEditorImpl.RemoveMarker (lineMarker);
		}

		public IEnumerable<ITextLineMarker> GetLineMarkers (IDocumentLine line)
		{
			if (line == null)
				throw new ArgumentNullException (nameof (line));
			return textEditorImpl.GetLineMarkers (line);
		}

		public IEnumerable<ITextSegmentMarker> GetTextSegmentMarkersAt (ISegment segment)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return textEditorImpl.GetTextSegmentMarkersAt (segment);
		}

		public IEnumerable<ITextSegmentMarker> GetTextSegmentMarkersAt (int offset, int length)
		{
			if (offset < 0 || offset >= Length)
				throw new ArgumentOutOfRangeException (nameof (offset), "needs to be 0 <= offset < Length=" + this.Length);

			if (offset + length < 0 || offset  + length > Length)
				throw new ArgumentOutOfRangeException (nameof (length), "needs to be 0 <= offset + length (" + length + ") < Length=" + this.Length);
			
			return textEditorImpl.GetTextSegmentMarkersAt (new TextSegment (offset, length));
		}

		public IEnumerable<ITextSegmentMarker> GetTextSegmentMarkersAt (int offset)
		{
			return textEditorImpl.GetTextSegmentMarkersAt (offset);
		}

		public void AddMarker (ITextSegmentMarker marker)
		{
			if (marker == null)
				throw new ArgumentNullException (nameof (marker));
			Runtime.AssertMainThread ();
			textEditorImpl.AddMarker (marker);
		}

		public bool RemoveMarker (ITextSegmentMarker marker)
		{
			if (marker == null)
				throw new ArgumentNullException (nameof (marker));
			Runtime.AssertMainThread ();
			return textEditorImpl.RemoveMarker (marker);
		}

		public void SetFoldings (IEnumerable<IFoldSegment> foldings)
		{
			if (foldings == null)
				throw new ArgumentNullException (nameof (foldings));
			Runtime.AssertMainThread ();
			textEditorImpl.SetFoldings (foldings);
		}


		public IEnumerable<IFoldSegment> GetFoldingsContaining (int offset)
		{
			return textEditorImpl.GetFoldingsContaining (offset);
		}

		public IEnumerable<IFoldSegment> GetFoldingsIn (ISegment segment)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return textEditorImpl.GetFoldingsIn (segment.Offset, segment.Length);
		}

		/// <summary>
		/// Gets a character at the specified position in the document.
		/// </summary>
		/// <paramref name="offset">The index of the character to get.</paramref>
		/// <exception cref="ArgumentOutOfRangeException">Offset is outside the valid range (0 to TextLength-1).</exception>
		/// <returns>The character at the specified position.</returns>
		/// <remarks>This is the same as Text[offset], but is more efficient because
		///  it doesn't require creating a String object.</remarks>
		public char GetCharAt (int offset)
		{
			if (offset < 0 || offset >= Length)
				throw new ArgumentOutOfRangeException (nameof (offset), "offset needs to be >= 0 && < " + Length + ", was :" + offset);
			return ReadOnlyTextDocument.GetCharAt (offset);
		}

		public string GetTextAt (int offset, int length)
		{
			if (offset < 0 || offset > Length)
				throw new ArgumentOutOfRangeException (nameof (offset), "offset needs to be >= 0 && <= " + Length + ", was :" + offset);
			if (offset + length > Length)
				throw new ArgumentOutOfRangeException (nameof (Length), "Length needs to <= " + (Length - offset) + ", was :" + length);
			return ReadOnlyTextDocument.GetTextAt (offset, length);
		}

		public string GetTextAt (ISegment segment)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return ReadOnlyTextDocument.GetTextAt (segment);
		}

		public IReadonlyTextDocument CreateDocumentSnapshot ()
		{
			return ReadWriteTextDocument.CreateDocumentSnapshot ();
		}

		public string GetVirtualIndentationString (int lineNumber)
		{
			if (lineNumber < 1 || lineNumber > LineCount)
				throw new ArgumentOutOfRangeException (nameof (lineNumber));
			return textEditorImpl.GetVirtualIndentationString (lineNumber);
		}

		public string GetVirtualIndentationString (IDocumentLine line)
		{
			if (line == null)
				throw new ArgumentNullException (nameof (line));
			return textEditorImpl.GetVirtualIndentationString (line.LineNumber);
		}

		public int GetVirtualIndentationColumn (int lineNumber)
		{
			if (lineNumber < 1 || lineNumber > LineCount)
				throw new ArgumentOutOfRangeException (nameof (lineNumber));
			return 1 + textEditorImpl.GetVirtualIndentationString (lineNumber).Length;
		}

		public int GetVirtualIndentationColumn (IDocumentLine line)
		{
			if (line == null)
				throw new ArgumentNullException (nameof (line));
			return 1 + textEditorImpl.GetVirtualIndentationString (line.LineNumber).Length;
		}

		public TextReader CreateReader ()
		{
			return ReadOnlyTextDocument.CreateReader ();
		}

		public TextReader CreateReader (int offset, int length)
		{
			return ReadOnlyTextDocument.CreateReader (offset, length);
		}

		public ITextSource CreateSnapshot ()
		{
			return ReadOnlyTextDocument.CreateSnapshot ();
		}

		public ITextSource CreateSnapshot (int offset, int length)
		{
			return ReadOnlyTextDocument.CreateSnapshot (offset, length);
		}

		public ITextSource CreateSnapshot (ISegment segment)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return ReadOnlyTextDocument.CreateSnapshot (segment.Offset, segment.Length);
		}

		public void WriteTextTo (TextWriter writer)
		{
			if (writer == null)
				throw new ArgumentNullException (nameof (writer));
			ReadOnlyTextDocument.WriteTextTo (writer);
		}

		public void WriteTextTo (TextWriter writer, int offset, int length)
		{
			if (writer == null)
				throw new ArgumentNullException (nameof (writer));
			ReadOnlyTextDocument.WriteTextTo (writer, offset, length);
		}

		/// <inheritdoc/>
		public void CopyTo (int sourceIndex, char [] destination, int destinationIndex, int count)
		{
			ReadOnlyTextDocument.CopyTo (sourceIndex, destination, destinationIndex, count); 
		}

		public void ScrollTo (int offset)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.ScrollTo (offset);
		}

		public void ScrollTo (DocumentLocation loc)
		{
			Runtime.AssertMainThread ();
			ScrollTo (LocationToOffset (loc));
		}

		public void CenterTo (int offset)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.CenterTo (offset);
		}

		public void CenterTo (DocumentLocation loc)
		{
			Runtime.AssertMainThread ();
			CenterTo (LocationToOffset (loc));
		}

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		public void SetIndentationTracker (IndentationTracker indentationTracker)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.SetIndentationTracker (indentationTracker);
		}

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		public void SetSelectionSurroundingProvider (SelectionSurroundingProvider surroundingProvider)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.SetSelectionSurroundingProvider (surroundingProvider);
		}

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		public void SetTextPasteHandler (TextPasteHandler textPasteHandler)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.SetTextPasteHandler (textPasteHandler);
		}

		public EditSession CurrentSession {
			get {
				return textEditorImpl.CurrentSession;
			}
		}

		public void StartSession (EditSession session)
		{
			if (session == null)
				throw new ArgumentNullException (nameof (session));
			Runtime.AssertMainThread ();
			session.SetEditor (this);
			textEditorImpl.StartSession (session);
		}

		internal void EndSession ()
		{
			if (CurrentSession == null)
				throw new InvalidOperationException ("No session started.");
			Runtime.AssertMainThread ();
			textEditorImpl.EndSession ();
		}

		bool isDisposed;

		protected override void Dispose (bool disposing)
		{
			if (isDisposed)
				return;
			isDisposed = true;
			DetachExtensionChain ();
			foreach (var provider in textEditorImpl.TooltipProvider)
				provider.Dispose ();
			textEditorImpl.Dispose ();

			base.Dispose (disposing);
		}

		protected override object CreateNativeWidget<T> ()
		{
			return textEditorImpl.CreateNativeControl ();
		}

		#region Internal API

		internal IEditorActionHost EditorActionHost {
			get {
				return textEditorImpl.Actions;
			}
		}

		internal TextEditorExtension TextEditorExtensionChain {
			get {
				return textEditorImpl.EditorExtension;
			}
		} 

		internal ITextMarkerFactory TextMarkerFactory {
			get {
				return textEditorImpl.TextMarkerFactory;
			}
		}

		internal TextEditor (ITextEditorImpl textEditorImpl, TextEditorType textEditorType)
		{
			if (textEditorImpl == null)
				throw new ArgumentNullException (nameof (textEditorImpl));
			this.textEditorImpl = textEditorImpl;
			this.TextEditorType = textEditorType;
			commandRouter = new InternalCommandRouter (this);
		}

		TextEditorViewContent viewContent;
		internal ViewContent GetViewContent ()
		{
			if (viewContent == null) {
				viewContent = new TextEditorViewContent (this, textEditorImpl);
			}

			return viewContent;
		}

		internal IFoldSegment CreateFoldSegment (int offset, int length, bool isFolded = false)
		{
			return textEditorImpl.CreateFoldSegment (offset, length, isFolded);
		}
		#endregion

		#region Editor extensions
		InternalCommandRouter commandRouter;
		class InternalCommandRouter : MonoDevelop.Components.Commands.IMultiCastCommandRouter
		{
			readonly TextEditor editor;

			public InternalCommandRouter (TextEditor editor)
			{
				this.editor = editor;
			}

			#region IMultiCastCommandRouter implementation

			System.Collections.IEnumerable MonoDevelop.Components.Commands.IMultiCastCommandRouter.GetCommandTargets ()
			{
				yield return editor.textEditorImpl;
				yield return editor.textEditorImpl.EditorExtension;
			}
			#endregion
		}

		internal object CommandRouter {
			get {
				return commandRouter;
			}
		}

		protected override object GetNextCommandTarget ()
		{
			return commandRouter;
		}

		DocumentContext documentContext;
		internal DocumentContext DocumentContext {
			get {
				return documentContext;
			}
			set {
				documentContext = value;
				OnDocumentContextChanged (EventArgs.Empty);
			}
		}

		public event EventHandler DocumentContextChanged;

		void OnDocumentContextChanged (EventArgs e)
		{
			if (DocumentContext != null) {
				textEditorImpl.SetQuickTaskProviders (DocumentContext.GetContents<IQuickTaskProvider> ());
			} else {
				textEditorImpl.SetQuickTaskProviders (Enumerable.Empty<IQuickTaskProvider> ());
			}
			var handler = DocumentContextChanged;
			if (handler != null)
				handler (this, e);
		}

		internal void InitializeExtensionChain (DocumentContext context)
		{
			if (context == null)
				throw new ArgumentNullException (nameof (context));
			DetachExtensionChain ();
            
			SetExtensionChain (context, Enumerable.Empty<TextEditorExtension> ());
		}

	    public void SetExtensionChain (DocumentContext context, IEnumerable<TextEditorExtension> extensions)
		{
			if (context == null)
				throw new ArgumentNullException (nameof (context));
			if (extensions == null)
				throw new ArgumentNullException (nameof (extensions));
			
			TextEditorExtension last = null;
			foreach (var ext in extensions) {
				if (ext.IsValidInContext (context)) {
					if (last != null) {
						last.Next = ext;
						last = ext;
					} else {
						textEditorImpl.EditorExtension = last = ext;
					}
					ext.Initialize (this, context);
				}
			}
			DocumentContext = context;
		}


		void DetachExtensionChain ()
		{
			var editorExtension = textEditorImpl.EditorExtension;
			while (editorExtension != null) {
				try {
					editorExtension.Dispose ();
				} catch (Exception ex) {
					LoggingService.LogError ("Exception while disposing extension:" + editorExtension, ex);
				}
				editorExtension = editorExtension.Next;
			}
			textEditorImpl.EditorExtension = null;
		}

		public T GetContent<T>() where T : class
		{
			return GetContents<T> ().FirstOrDefault ();
		}

		public IEnumerable<T> GetContents<T>() where T : class
		{
			T result = textEditorImpl as T;
			if (result != null)
				yield return result;
			var ext = textEditorImpl.EditorExtension;
			while (ext != null) {
				result = ext as T;
				if (result != null)
					yield return result;
				ext = ext.Next;
			}
		}

		public IEnumerable<object> GetContents (Type type)
		{
			var res = Enumerable.Empty<object> ();
			if (type.IsInstanceOfType (textEditorImpl))
				res = res.Concat (textEditorImpl);
			
			var ext = textEditorImpl.EditorExtension;
			while (ext != null) {
				res = res.Concat (ext.OnGetContents (type));
				ext = ext.Next;
			}
			return res;
		}

		#endregion

		[Obsolete ("Use GetMarkup")]
		public string GetPangoMarkup (int offset, int length, bool fitIdeStyle = false)
		{
			return GetMarkup (offset, length, new MarkupOptions (MarkupFormat.Pango, fitIdeStyle));
		}

		[Obsolete ("Use GetMarkup")]
		public string GetPangoMarkup (ISegment segment, bool fitIdeStyle = false)
		{
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return GetMarkup (segment, new MarkupOptions (MarkupFormat.Pango, fitIdeStyle));
		}

		public string GetMarkup (int offset, int length, MarkupOptions options)
		{
			if (options == null)
				throw new ArgumentNullException (nameof (options));
			return textEditorImpl.GetMarkup (offset, length, options);
		}

		public string GetMarkup (ISegment segment, MarkupOptions options)
		{
			if (options == null)
				throw new ArgumentNullException (nameof (options));
			if (segment == null)
				throw new ArgumentNullException (nameof (segment));
			return textEditorImpl.GetMarkup (segment.Offset, segment.Length, options);
		}

		#region Annotations
		// Annotations: points either null (no annotations), to the single annotation,
		// or to an AnnotationList.
		// Once it is pointed at an AnnotationList, it will never change (this allows thread-safety support by locking the list)

		object annotations;
		sealed class AnnotationList : List<object>, ICloneable
		{
			// There are two uses for this custom list type:
			// 1) it's private, and thus (unlike List<object>) cannot be confused with real annotations
			// 2) It allows us to simplify the cloning logic by making the list behave the same as a clonable annotation.
			public AnnotationList (int initialCapacity) : base (initialCapacity)
			{
			}

			public object Clone ()
			{
				lock (this) {
					AnnotationList copy = new AnnotationList (Count);
					for (int i = 0; i < Count; i++) {
						object obj = this [i];
						ICloneable c = obj as ICloneable;
						copy.Add (c != null ? c.Clone () : obj);
					}
					return copy;
				}
			}
		}

		public void AddAnnotation (object annotation)
		{
			if (annotation == null)
				throw new ArgumentNullException (nameof (annotation));
			retry: // Retry until successful
			object oldAnnotation = Interlocked.CompareExchange (ref annotations, annotation, null);
			if (oldAnnotation == null) {
				return; // we successfully added a single annotation
			}
			AnnotationList list = oldAnnotation as AnnotationList;
			if (list == null) {
				// we need to transform the old annotation into a list
				list = new AnnotationList (4);
				list.Add (oldAnnotation);
				list.Add (annotation);
				if (Interlocked.CompareExchange (ref annotations, list, oldAnnotation) != oldAnnotation) {
					// the transformation failed (some other thread wrote to this.annotations first)
					goto retry;
				}
			} else {
				// once there's a list, use simple locking
				lock (list) {
					list.Add (annotation);
				}
			}
		}

		public void RemoveAnnotations<T>() where T : class
		{
		retry: // Retry until successful
			object oldAnnotations = annotations;
			var list = oldAnnotations as AnnotationList;
			if (list != null) {
				lock (list)
					list.RemoveAll (obj => obj is T);
			} else if (oldAnnotations is T) {
				if (Interlocked.CompareExchange (ref annotations, null, oldAnnotations) != oldAnnotations) {
					// Operation failed (some other thread wrote to this.annotations first)
					goto retry;
				}
			}
		}

		public T Annotation<T>() where T : class
		{
			object annotations = this.annotations;
			var list = annotations as AnnotationList;
			if (list != null) {
				lock (list) {
					foreach (object obj in list) {
						T t = obj as T;
						if (t != null)
							return t;
					}
					return null;
				}
			}
			return annotations as T;
		}

		/// <summary>
		/// Gets all annotations stored on this AstNode.
		/// </summary>
		public IEnumerable<object> Annotations
		{
			get
			{
				object annotations = this.annotations;
				AnnotationList list = annotations as AnnotationList;
				if (list != null) {
					lock (list) {
						return list.ToArray ();
					}
				}
				if (annotations != null)
					return new [] { annotations };
				return Enumerable.Empty<object> ();
			}
		}

		internal bool SuppressTooltips {
			get { return textEditorImpl.SuppressTooltips; } 
			set { textEditorImpl.SuppressTooltips = value; }
		}
		#endregion

		internal void AddOverlay (Control messageOverlayContent, Func<int> sizeFunc)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.AddOverlay (messageOverlayContent, sizeFunc);
		}

		internal void RemoveOverlay (Control messageOverlayContent)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.RemoveOverlay (messageOverlayContent);
		}

		internal void UpdateBraceMatchingResult (BraceMatchingResult? result)
		{
			Runtime.AssertMainThread ();
			textEditorImpl.UpdateBraceMatchingResult (result);
		}

		internal IEnumerable<IDocumentLine> VisibleLines { get { return textEditorImpl.VisibleLines; } }
		internal event EventHandler<LineEventArgs> LineShown { add { textEditorImpl.LineShown += value; } remove { textEditorImpl.LineShown -= value; } }

		internal ITextEditorImpl Implementation { get { return this.textEditorImpl; } }

		public event EventHandler FocusLost { add { textEditorImpl.FocusLost += value; } remove { textEditorImpl.FocusLost -= value; } }

		public new void GrabFocus ()
		{
			this.textEditorImpl.GrabFocus ();
		}
	}
}
