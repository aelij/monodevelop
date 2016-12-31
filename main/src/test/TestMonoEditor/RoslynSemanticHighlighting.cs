using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Highlighting;
using ColorScheme = Mono.TextEditor.Highlighting.ColorScheme;

namespace TestMonoEditor
{
    internal class RoslynSemanticHighlighting : SemanticHighlighting, ISyntaxMode
    {
        private readonly RoslynHost host;

        public RoslynSemanticHighlighting(TextEditor editor, DocumentContext documentContext, RoslynHost host) : base(editor, documentContext)
        {
            this.host = host;
        }

        protected override void DocumentParsed()
        {
        }

        public override IEnumerable<ColoredSegment> GetColoredSegments(ISegment segment)
        {
            throw new NotSupportedException();
        }

        public TextDocument Document { get; set; }

        public IEnumerable<Chunk> GetChunks(ColorScheme style, DocumentLine line, int offset, int length)
        {
            var spans = GetSpans(offset, length);
            var currentOffset = offset;
            foreach (var classifiedSpan in spans)
            {
                var spanOffset = classifiedSpan.TextSpan.Start;
                var spanLength = classifiedSpan.TextSpan.Length;
                if (spanOffset < currentOffset)
                {
                    spanLength -= currentOffset - spanOffset;
                    spanOffset = currentOffset;
                }
                else if (spanOffset > currentOffset)
                {
                    yield return new Chunk(currentOffset, spanOffset - currentOffset, ColorScheme.PlainTextKey);
                }

                yield return new Chunk(spanOffset, spanLength, GetStyleKey(classifiedSpan.ClassificationType));

                currentOffset = spanOffset + spanLength;
            }
            yield return new Chunk(currentOffset, offset + length - currentOffset, ColorScheme.PlainTextKey);
        }

        private IEnumerable<ClassifiedSpan> GetSpans(int offset, int length)
        {
            var document = host.GetDocument();

            var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult(); // TODO
            var spans = Classifier.GetClassifiedSpans(semanticModel, new TextSpan(offset, length),
                host.Workspace);
            return spans;
        }

        private static string GetStyleKey(string classificationType)
        {
            string styleKey;
            if (!ClassificationMap.TryGetValue(classificationType, out styleKey))
            {
                styleKey = ColorScheme.PlainTextKey;
            }
            return styleKey;
        }

        private static readonly ImmutableDictionary<string, string> ClassificationMap = new Dictionary<string, string>
        {
            [ClassificationTypeNames.ClassName] = ColorScheme.UserTypesKey,
            [ClassificationTypeNames.StructName] = ColorScheme.UserTypesValueTypesKey,
            [ClassificationTypeNames.InterfaceName] = ColorScheme.UserTypesInterfacesKey,
            [ClassificationTypeNames.DelegateName] = ColorScheme.UserTypesDelegatesKey,
            [ClassificationTypeNames.EnumName] = ColorScheme.UserTypesEnumsKey,
            [ClassificationTypeNames.ModuleName] = ColorScheme.UserTypesKey,
            [ClassificationTypeNames.TypeParameterName] = ColorScheme.UserTypesTypeParametersKey,
            [ClassificationTypeNames.Comment] = ColorScheme.CommentTagsKey,
            [ClassificationTypeNames.XmlDocCommentAttributeName] = ColorScheme.XmlAttributeKey,
            [ClassificationTypeNames.XmlDocCommentAttributeQuotes] = ColorScheme.XmlAttributeQuotesKey,
            [ClassificationTypeNames.XmlDocCommentAttributeValue] = ColorScheme.XmlAttributeValueKey,
            [ClassificationTypeNames.XmlDocCommentCDataSection] = ColorScheme.XmlCDataSectionKey,
            [ClassificationTypeNames.XmlDocCommentComment] = ColorScheme.XmlCommentKey,
            [ClassificationTypeNames.XmlDocCommentDelimiter] = ColorScheme.XmlDelimiterKey,
            [ClassificationTypeNames.XmlDocCommentEntityReference] = ColorScheme.XmlTextKey,
            [ClassificationTypeNames.XmlDocCommentName] = ColorScheme.XmlCommentKey,
            [ClassificationTypeNames.XmlDocCommentProcessingInstruction] = ColorScheme.XmlTextKey,
            [ClassificationTypeNames.XmlDocCommentText] = ColorScheme.XmlCommentKey,
            [ClassificationTypeNames.Keyword] = ColorScheme.KeywordOtherKey,
            [ClassificationTypeNames.PreprocessorKeyword] = ColorScheme.PreprocessorKey,
            [ClassificationTypeNames.StringLiteral] = ColorScheme.StringKey,
            [ClassificationTypeNames.VerbatimStringLiteral] = ColorScheme.StringVerbatimKey
        }.ToImmutableDictionary();
    }
}