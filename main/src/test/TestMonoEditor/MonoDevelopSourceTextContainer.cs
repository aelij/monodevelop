﻿//
// RoslynTypeSystemService.cs
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
using Microsoft.CodeAnalysis;
using MonoDevelop.Core;
using MonoDevelop.Ide.Editor;
using Microsoft.CodeAnalysis.Text;

namespace TestMonoEditor
{
    internal sealed class MonoDevelopSourceTextContainer : SourceTextContainer, IDisposable
    {
        private readonly ITextDocument document;
        private bool isDisposed;
        private SourceText currentText;

        public DocumentId Id
        {
            get;
            private set;
        }

        public MonoDevelopSourceTextContainer(DocumentId documentId, ITextDocument document) : this(document)
        {
            Id = documentId;
        }

        public MonoDevelopSourceTextContainer(ITextDocument document)
        {
            this.document = document;
            this.document.TextChanging += HandleTextReplacing;
        }

        private void HandleTextReplacing(object sender, MonoDevelop.Core.Text.TextChangeEventArgs e)
        {
            var handler = TextChanged;
            if (handler != null)
            {
                var oldText = CurrentText;
                var newText = oldText.Replace(e.Offset, e.RemovalLength, e.InsertedText.Text);
                currentText = newText;
                try
                {
                    handler(this, new TextChangeEventArgs(oldText, newText, new TextChangeRange(TextSpan.FromBounds(e.Offset, e.Offset + e.RemovalLength), e.InsertionLength)));
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("Error while text replacing", ex);
                }
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;
            currentText = null;
            document.TextChanging -= HandleTextReplacing;
            isDisposed = true;
        }

        #region implemented abstract members of SourceTextContainer
        public override SourceText CurrentText
        {
            get
            {
                if (currentText == null)
                {
                    currentText = new MonoDevelopSourceText(document.CreateDocumentSnapshot());
                }
                return currentText;
            }
        }

        public ITextDocument Document => document;

        public override event EventHandler<TextChangeEventArgs> TextChanged;
        #endregion
    }
}