//
// TextEditorViewContent.cs
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
using MonoDevelop.Ide.Gui;
using MonoDevelop.Components.Commands;
using System.Collections.Generic;
using System.IO;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide.Editor.Extension;
using MonoDevelop.Components;
using System.Threading.Tasks;
using System.Threading;

namespace MonoDevelop.Ide.Editor
{
	/// <summary>
	/// The TextEditor object needs to be available through BaseViewContent.GetContent therefore we need to insert a 
	/// decorator in between.
	/// </summary>
	class TextEditorViewContent : ViewContent, ICommandRouter
	{
		readonly TextEditor textEditor;
		readonly ITextEditorImpl textEditorImpl;

		public TextEditorViewContent (TextEditor textEditor, ITextEditorImpl textEditorImpl)
		{
			if (textEditor == null)
				throw new ArgumentNullException (nameof (textEditor));
			if (textEditorImpl == null)
				throw new ArgumentNullException (nameof (textEditorImpl));
			this.textEditor = textEditor;
			this.textEditorImpl = textEditorImpl;
			textEditorImpl.ViewContent.ContentNameChanged += ViewContent_ContentNameChanged;
			textEditorImpl.ViewContent.DirtyChanged += ViewContent_DirtyChanged; ;

		}

		void ViewContent_ContentNameChanged (object sender, EventArgs e)
		{
			this.ContentName = textEditorImpl.ViewContent.ContentName;
		}

		void ViewContent_DirtyChanged (object sender, EventArgs e)
		{
			OnDirtyChanged ();
		}

		void HandleDirtyChanged (object sender, EventArgs e)
		{
			IsDirty = textEditorImpl.ViewContent.IsDirty;
		}

		void CancelDocumentParsedUpdate ()
		{
			src.Cancel ();
			src = new CancellationTokenSource ();
		}

		CancellationTokenSource src = new CancellationTokenSource ();

		async Task RunFirstTimeFoldUpdate (string text)
		{
            // TODO-AELIJ: folding

			//if (string.IsNullOrEmpty (text)) 
			//	return;
			//ParsedDocument parsedDocument = null;

			//var foldingParser = TypeSystemService.GetFoldingParser (textEditor.MimeType);
			//if (foldingParser != null) {
			//	parsedDocument = foldingParser.Parse (textEditor.FileName, text);
			//} else {
			//	var normalParser = TypeSystemService.GetParser (textEditor.MimeType);
			//	if (normalParser != null) {
			//		parsedDocument = await normalParser.Parse(
			//			new TypeSystem.ParseOptions {
			//				FileName = textEditor.FileName,
			//				Content = new StringTextSource(text),
			//				Project = Project
			//			});
			//	}
			//}
			//if (parsedDocument != null) {
			//	await FoldingTextEditorExtension.UpdateFoldings (textEditor, parsedDocument, textEditor.CaretLocation, true);
			//}
		}

		#region IViewFContent implementation

		public override async Task Load (FileOpenInformation fileOpenInformation)
		{
			textEditorImpl.ViewContent.DirtyChanged -= HandleDirtyChanged;
			await textEditorImpl.ViewContent.Load (fileOpenInformation);
			await RunFirstTimeFoldUpdate (textEditor.Text);
			textEditorImpl.InformLoadComplete ();
			textEditorImpl.ViewContent.DirtyChanged += HandleDirtyChanged;
		}

		public override async Task LoadNew (Stream content, string mimeType)
		{
			textEditor.MimeType = mimeType;
			string text = null;
			if (content != null) {
				var res = await TextFileUtility.GetTextAsync (content);
				text = textEditor.Text = res.Text;
				textEditor.Encoding = res.Encoding;
				textEditor.UseBOM = res.HasBom;
			}
			await RunFirstTimeFoldUpdate (text);
			textEditorImpl.InformLoadComplete ();
		}

		public override Task Save (FileSaveInformation fileSaveInformation)
		{
			return textEditorImpl.ViewContent.Save (fileSaveInformation);
		}

		public override Task Save ()
		{
			return textEditorImpl.ViewContent.Save ();
		}

		public override void DiscardChanges ()
		{
			textEditorImpl.ViewContent.DiscardChanges ();
		}

		protected override void OnSetProject (MonoDevelop.Projects.Project p)
		{
			base.OnSetProject (p);
			textEditorImpl.ViewContent.Project = p;
		}

		public override ProjectReloadCapability ProjectReloadCapability {
			get {
				return textEditorImpl.ViewContent.ProjectReloadCapability;
			}
		}

		#endregion

		#region BaseViewContent implementation

		protected override IEnumerable<object> OnGetContents (Type type)
		{
			foreach (var r in base.OnGetContents (type))
				yield return r;

			if (type.IsAssignableFrom (typeof (TextEditor))) {
				yield return textEditor;
				yield break;
			}

			var ext = textEditorImpl.EditorExtension;
			while (ext != null) {
				foreach (var r in ext.OnGetContents (type))
					yield return r;
				ext = ext.Next;
			}

			foreach (var r in textEditorImpl.ViewContent.GetContents (type))
				yield return r;
		}

		protected override void OnWorkbenchWindowChanged ()
		{
			base.OnWorkbenchWindowChanged ();
			textEditorImpl.ViewContent.WorkbenchWindow = WorkbenchWindow;
		}

		public override Control Control {
			get {
				return textEditor;
			}
		}

		public override string TabPageLabel {
			get {
				return textEditorImpl.ViewContent.TabPageLabel;
			}
		}

		public override bool IsDirty {
			get { return textEditorImpl.ViewContent.IsDirty; }
			set {
				textEditorImpl.ViewContent.IsDirty = value;
			}
		}


		#endregion

		#region IDisposable implementation
		bool isDisposed;

		public override void Dispose ()
		{
			if (isDisposed)
				return;
			
			base.Dispose ();

			isDisposed = true;
			CancelDocumentParsedUpdate ();
			textEditorImpl.ViewContent.DirtyChanged -= HandleDirtyChanged;
			textEditorImpl.ViewContent.ContentNameChanged -= ViewContent_ContentNameChanged;
			textEditorImpl.ViewContent.DirtyChanged -= ViewContent_DirtyChanged; ;
		}

		#endregion

		#region ICommandRouter implementation

		object ICommandRouter.GetNextCommandTarget ()
		{
			return textEditor;
		}

		#endregion
	


	}
}