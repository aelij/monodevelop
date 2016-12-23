using System;
using System.Collections.Generic;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Highlighting;

namespace TestMonoEditor
{
    internal class RoslynSemanticHighlighting : SemanticHighlighting
    {
        private readonly RoslynHost _host;

        public RoslynSemanticHighlighting(TextEditor editor, DocumentContext documentContext, RoslynHost host) : base(editor, documentContext)
        {
            _host = host;
        }

        protected override void DocumentParsed()
        {
        }

        public override IEnumerable<ColoredSegment> GetColoredSegments(ISegment segment)
        {
            //var parsedDocument = documentContext.ParsedDocument;
            //if (parsedDocument == null)
            {
                return Array.Empty<ColoredSegment>();
            }

            //var semanticModel = parsedDocument.GetAst<SemanticModel>();

            //var spans = Classifier.GetClassifiedSpans(semanticModel, TextSpan.FromBounds(segment.Offset, segment.EndOffset),
            //    _host.Workspace);

            //return spans.Select(x => new ColoredSegment(x.TextSpan.Start, x.TextSpan.Length, "User Types"));
        }
    }
}