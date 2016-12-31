using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Completion;
using MonoDevelop.Components;
using MonoDevelop.Ide.CodeCompletion;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;
using MonoDevelop.Ide.Gui;

namespace TestMonoEditor
{
    internal class RoslynCompletionTextEditorExtension : CompletionTextEditorExtension
    {
        private readonly RoslynHost _host;

        public RoslynCompletionTextEditorExtension(RoslynHost host)
        {
            _host = host;
        }

        public override string CompletionLanguage => "C#";

        public override bool KeyPress(KeyDescriptor descriptor)
        {
            var b = base.KeyPress(descriptor);
            if ((descriptor.KeyChar == ',' || descriptor.KeyChar == ')') && CanRunParameterCompletionCommand())
            {
                RunParameterCompletionCommand();
            }
            return b;
        }

        public override async Task<ICompletionDataList> HandleBackspaceOrDeleteCodeCompletionAsync(CodeCompletionContext completionContext, SpecialKey key, char triggerCharacter, CancellationToken token = default(CancellationToken))
        {
            if (!char.IsLetterOrDigit(triggerCharacter) && triggerCharacter != '_')
                return null;

            if (key == SpecialKey.BackSpace || key == SpecialKey.Delete)
            {
                var ch = completionContext.TriggerOffset > 0
                    ? Editor.GetCharAt(completionContext.TriggerOffset - 1)
                    : '\0';
                var ch2 = completionContext.TriggerOffset < Editor.Length
                    ? Editor.GetCharAt(completionContext.TriggerOffset)
                    : '\0';
                if (!IsIdentifierPart(ch) && !IsIdentifierPart(ch2))
                    return null;
            }
            var result = await HandleCodeCompletion(completionContext, CompletionTrigger.CreateDeletionTrigger(triggerCharacter), 0, token).ConfigureAwait(false);
            if (result == null)
                return null;
            result.AutoCompleteUniqueMatch = false;
            result.AutoCompleteEmptyMatch = false;
            return result;
        }

        private static bool IsIdentifierPart(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_';
        }

        public override async Task<ICompletionDataList> HandleCodeCompletionAsync(CodeCompletionContext completionContext, char completionChar,
            CancellationToken token = default(CancellationToken))
        {
            int triggerWordLength = 0;
            if (char.IsLetterOrDigit(completionChar) || completionChar == '_')
            {
                if (completionContext.TriggerOffset > 1 && char.IsLetterOrDigit(Editor.GetCharAt(completionContext.TriggerOffset - 2)))
                    return null;
                triggerWordLength = 1;
            }
            return await HandleCodeCompletion(completionContext, CompletionTrigger.CreateInsertionTrigger(completionChar), triggerWordLength, token);
        }

        public override async Task<ICompletionDataList> CodeCompletionCommand(CodeCompletionContext completionContext)
        {
            return await HandleCodeCompletion(completionContext, CompletionTrigger.Default, 0, default(CancellationToken));
        }

        public override Task<ParameterHintingResult> ParameterCompletionCommand(CodeCompletionContext completionContext)
        {
            char ch = completionContext.TriggerOffset > 0 ? Editor.GetCharAt(completionContext.TriggerOffset - 1) : '\0';
            return HandleParameterCompletionCommand(completionContext, ch, true, default(CancellationToken));
        }

        public override Task<ParameterHintingResult> HandleParameterCompletionAsync(CodeCompletionContext completionContext, char completionChar, CancellationToken token = default(CancellationToken))
        {
            return HandleParameterCompletionCommand(completionContext, completionChar, false, token);
        }

        private async Task<ParameterHintingResult> HandleParameterCompletionCommand(CodeCompletionContext completionContext, char completionChar, bool force, CancellationToken token = default(CancellationToken))
        {
            if (!force && completionChar != '(' && completionChar != '<' && completionChar != '[' && completionChar != ',')
                return null;
            if (Editor.EditMode != EditMode.Edit)
                return null;
            var offset = Editor.CaretOffset;

            //var analysisDocument = _host.GetDocument();
            //var partialDoc = await WithFrozenPartialSemanticsAsync(analysisDocument, token);
            //var semanticModel = await partialDoc.GetSemanticModelAsync();
            //var engine = new ParameterHintingEngine(TypeSystemService.Workspace, new RoslynParameterHintingFactory());
            //var result = await engine.GetParameterDataProviderAsync(analysisDocument, semanticModel, offset, token);
            //return new ParameterHintingResult(result.OfType<ParameterHintingData>().ToList(), result.StartOffset);

            return ParameterHintingResult.Empty;
        }

        //public override async Task<int> GuessBestMethodOverload(
        //    MonoDevelop.Ide.CodeCompletion.ParameterHintingResult provider, int currentOverload, CancellationToken token)
        //{
        //}

        //public override async Task<int> GetCurrentParameterIndex(int startOffset, CancellationToken token)
        //{
        //    var analysisDocument = DocumentContext.AnalysisDocument;
        //    var caretOffset = Editor.CaretOffset;
        //    if (analysisDocument == null || startOffset > caretOffset)
        //        return -1;
        //    var partialDoc = await WithFrozenPartialSemanticsAsync(analysisDocument, default(CancellationToken)).ConfigureAwait(false);
        //    var result = ParameterUtil.GetCurrentParameterIndex(partialDoc, startOffset, caretOffset).Result;
        //    return result.ParameterIndex;
        //}

        private async Task<CompletionDataList> HandleCodeCompletion(CodeCompletionContext completionContext, CompletionTrigger trigger, int triggerWordLength, CancellationToken token)
        {
            var document = _host.GetDocument();
            var text = (await document.GetTextAsync(token).ConfigureAwait(false)).ToString();

            var completions = await CompletionService.GetService(document)
                .GetCompletionsAsync(document, completionContext.TriggerOffset,
                    trigger,
                    cancellationToken: token)
                .ConfigureAwait(false);

            var list = new CompletionDataList(
                completions?.Items.Select(x => new CompletionData(x.DisplayText)) ?? Array.Empty<CompletionData> ())
            {
                TriggerWordLength = triggerWordLength
            };
            //list.AutoCompleteEmptyMatch = completionResult.AutoCompleteEmptyMatch;
            // list.AutoCompleteEmptyMatchOnCurlyBrace = completionResult.AutoCompleteEmptyMatchOnCurlyBracket;
            //list.AutoSelect = completionResult.AutoSelect;
            //list.DefaultCompletionString = completionResult.DefaultCompletionString;
            // list.CloseOnSquareBrackets = completionResult.CloseOnSquareBrackets;
            if (Equals(trigger, CompletionTrigger.Default))
            {
                list.AutoCompleteUniqueMatch = true;
            }
            return list;

        }
    }
}