//
// SemanticHighlightingSyntaxMode.cs
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
using System.Collections.Generic;
using Gtk;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Highlighting;
using ColorScheme = Mono.TextEditor.Highlighting.ColorScheme;
using TextSegment = MonoDevelop.Core.Text.TextSegment;
using TreeSegment = Mono.TextEditor.TreeSegment;

namespace MonoDevelop.SourceEditor.Wrappers
{
    internal sealed class SemanticHighlightingSyntaxMode : SyntaxMode, IDisposable
    {
        private readonly ExtensibleTextEditor editor;
        private SemanticHighlighting semanticHighlighting;

        public override TextDocument Document
        {
            get { return UnderlyingSyntaxMode.Document; }
            set { UnderlyingSyntaxMode.Document = value; }
        }

        public SyntaxMode UnderlyingSyntaxMode { get; }

        private bool isDisposed;

        private Queue<Tuple<DocumentLine, HighlightingSegmentTree>> lineSegments = new Queue<Tuple<DocumentLine, HighlightingSegmentTree>>();

        public SemanticHighlightingSyntaxMode(ExtensibleTextEditor editor, ISyntaxMode syntaxMode, SemanticHighlighting semanticHighlighting)
        {
            if (editor == null)
                throw new ArgumentNullException(nameof(editor));
            if (syntaxMode == null)
                throw new ArgumentNullException(nameof(syntaxMode));
            if (semanticHighlighting == null)
                throw new ArgumentNullException(nameof(semanticHighlighting));
            this.editor = editor;
            this.semanticHighlighting = semanticHighlighting;
            UnderlyingSyntaxMode = syntaxMode as SyntaxMode;
            semanticHighlighting.SemanticHighlightingUpdated += SemanticHighlighting_SemanticHighlightingUpdated;
        }

        public void UpdateSemanticHighlighting(SemanticHighlighting newHighlighting)
        {
            if (isDisposed)
                return;
            if (semanticHighlighting != null)
                semanticHighlighting.SemanticHighlightingUpdated -= SemanticHighlighting_SemanticHighlightingUpdated;
            semanticHighlighting = newHighlighting;
            if (semanticHighlighting != null)
                semanticHighlighting.SemanticHighlightingUpdated += SemanticHighlighting_SemanticHighlightingUpdated;
        }

        private void SemanticHighlighting_SemanticHighlightingUpdated(object sender, EventArgs e)
        {
            Application.Invoke(delegate
            {
                if (isDisposed)
                    return;
                UnregisterLineSegmentTrees();
                lineSegments.Clear();

                var margin = editor.TextViewMargin;
                if (margin == null)
                    return;
                margin.PurgeLayoutCache();
                editor.QueueDraw();
            });
        }

        private void UnregisterLineSegmentTrees()
        {
            if (isDisposed)
                return;
            foreach (var kv in lineSegments)
            {
                try
                {
                    kv.Item2.RemoveListener();
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;
            UnregisterLineSegmentTrees();
            lineSegments = null;
            semanticHighlighting.SemanticHighlightingUpdated -= SemanticHighlighting_SemanticHighlightingUpdated;
        }

        public override SpanParser CreateSpanParser(DocumentLine line, CloneableStack<Span> spanStack)
        {
            return UnderlyingSyntaxMode.CreateSpanParser(line, spanStack);
        }

        public override ChunkParser CreateChunkParser(SpanParser spanParser, ColorScheme style, DocumentLine line)
        {
            return new CSharpChunkParser(this, spanParser, style, line);
        }

        private class CSharpChunkParser : ChunkParser
        {
            private const int MaximumCachedLineSegments = 200;
            private readonly SemanticHighlightingSyntaxMode semanticMode;

            public CSharpChunkParser(SemanticHighlightingSyntaxMode semanticMode, SpanParser spanParser, ColorScheme style, DocumentLine line) : base(semanticMode, spanParser, style, line)
            {
                this.semanticMode = semanticMode;
            }

            protected override void AddRealChunk(Chunk chunk)
            {
                if (!DefaultSourceEditorOptions.Instance.EnableSemanticHighlighting)
                {
                    base.AddRealChunk(chunk);
                    return;
                }
                StyledTreeSegment treeseg = null;

                try
                {
                    Tuple<DocumentLine, HighlightingSegmentTree> tree = null;
                    foreach (var t in semanticMode.lineSegments)
                    {
                        if (t.Item1 == line)
                        {
                            tree = t;
                            break;
                        }
                    }
                    if (tree == null)
                    {
                        tree = Tuple.Create(line, new HighlightingSegmentTree());
                        tree.Item2.InstallListener(semanticMode.Document);
                        int lineOffset = line.Offset;
                        foreach (var seg in semanticMode.semanticHighlighting.GetColoredSegments(new TextSegment(lineOffset, line.Length)))
                        {
                            tree.Item2.AddStyle(seg, seg.ColorStyleKey);
                        }
                        while (semanticMode.lineSegments.Count > MaximumCachedLineSegments)
                        {
                            var removed = semanticMode.lineSegments.Dequeue();
                            try
                            {
                                removed.Item2.RemoveListener();
                            }
                            catch (Exception)
                            {
                                // ignored
                            }
                        }
                        semanticMode.lineSegments.Enqueue(tree);
                    }
                    foreach (var s in tree.Item2.GetSegmentsOverlapping(chunk))
                    {
                        if (s.Offset < chunk.EndOffset && s.EndOffset > chunk.Offset)
                        {
                            treeseg = s;
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error in semantic highlighting: " + e);
                }

                if (treeseg != null)
                {
                    if (treeseg.Offset - chunk.Offset > 0)
                        AddRealChunk(new Chunk(chunk.Offset, treeseg.Offset - chunk.Offset, chunk.Style));

                    var startOffset = Math.Max(chunk.Offset, treeseg.Offset);
                    var endOffset = Math.Min(treeseg.EndOffset, chunk.EndOffset);

                    base.AddRealChunk(new Chunk(startOffset, endOffset - startOffset, treeseg.Style));

                    if (endOffset < chunk.EndOffset)
                        AddRealChunk(new Chunk(treeseg.EndOffset, chunk.EndOffset - endOffset, chunk.Style));
                    return;
                }

                base.AddRealChunk(chunk);
            }
        }

        internal class StyledTreeSegment : TreeSegment
        {
            public string Style { get; }

            public StyledTreeSegment(int offset, int length, string style) : base(offset, length)
            {
                Style = style;
            }
        }

        private class HighlightingSegmentTree : Mono.TextEditor.SegmentTree<StyledTreeSegment>
        {
            public void AddStyle(ISegment segment, string style)
            {
                if (IsDirty)
                    return;
                Add(new StyledTreeSegment(segment.Offset, segment.Length, style));
            }
        }
    }
}