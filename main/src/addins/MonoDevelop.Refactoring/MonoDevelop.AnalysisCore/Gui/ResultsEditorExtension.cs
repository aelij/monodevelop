// 
// ResultsEditorExtension.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2010 Novell, Inc.
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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Cairo;
using GLib;
using MonoDevelop.Core.Text;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;

namespace MonoDevelop.AnalysisCore.Gui
{
    internal class AnalysisDocument
    {
        public TextEditor Editor { get; }
        public DocumentLocation CaretLocation { get; }
        public DocumentContext DocumentContext { get; }

        public AnalysisDocument(TextEditor editor, DocumentContext documentContext)
        {
            Editor = editor;
            CaretLocation = editor.CaretLocation;
            DocumentContext = documentContext;
        }
    }

    public class ResultsEditorExtension : TextEditorExtension, IQuickTaskProvider
    {
        private bool disposed;

        protected override void Initialize()
        {
            base.Initialize();

            AnalysisOptions.AnalysisEnabled.Changed += AnalysisOptionsChanged;
            AnalysisOptionsChanged(null, null);
        }

        private void AnalysisOptionsChanged(object sender, EventArgs e)
        {
            Enabled = AnalysisOptions.AnalysisEnabled;
        }

        public override void Dispose()
        {
            if (disposed)
                return;
            enabled = false;
            CancelUpdateTimout();
            AnalysisOptions.AnalysisEnabled.Changed -= AnalysisOptionsChanged;
            while (markers.Count > 0)
                Editor.RemoveMarker(markers.Dequeue());
            disposed = true;
        }

        private bool enabled;

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled != value)
                {
                    if (value)
                        Enable();
                    else
                        Disable();
                }
            }
        }

        private void Enable()
        {
            if (enabled)
                return;
            enabled = true;
        }

        private void Disable()
        {
            if (!enabled)
                return;
            enabled = false;
            new ResultsUpdater(this, new Result[0], CancellationToken.None).Update();
        }

        private uint updateTimeout;

        public void CancelUpdateTimout()
        {
            if (updateTimeout != 0)
            {
                Source.Remove(updateTimeout);
                updateTimeout = 0;
            }
        }


        private class ResultsUpdater
        {
            private readonly ResultsEditorExtension ext;
            private readonly CancellationToken cancellationToken;

            //the number of markers at the head of the queue that need tp be removed
            private int oldMarkers;
            private readonly IEnumerator<Result> enumerator;
            private readonly ImmutableArray<QuickTask>.Builder builder;

            public ResultsUpdater(ResultsEditorExtension ext, IEnumerable<Result> results, CancellationToken cancellationToken)
            {
                if (ext == null)
                    throw new ArgumentNullException(nameof(ext));
                if (results == null)
                    throw new ArgumentNullException(nameof(results));
                this.ext = ext;
                this.cancellationToken = cancellationToken;
                oldMarkers = ext.markers.Count;
                // ReSharper disable once ImpureMethodCallOnReadonlyValueField
                builder = ImmutableArray<QuickTask>.Empty.ToBuilder();
                enumerator = results.GetEnumerator();
            }

            public void Update()
            {
                if (!AnalysisOptions.EnableFancyFeatures || cancellationToken.IsCancellationRequested)
                    return;
                ext.tasks = ext.tasks.Clear();
                Idle.Add(IdleHandler);
            }

            private static Color GetColor(Result result)
            {
                switch (result.Level)
                {
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden:
                        return DefaultSourceEditorOptions.Instance.GetColorStyle().PlainText.Background;
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Error:
                        return DefaultSourceEditorOptions.Instance.GetColorStyle().UnderlineError.Color;
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Warning:
                        return DefaultSourceEditorOptions.Instance.GetColorStyle().UnderlineWarning.Color;
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Info:
                        return DefaultSourceEditorOptions.Instance.GetColorStyle().UnderlineSuggestion.Color;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            //this runs as a glib idle handler so it can add/remove text editor markers
            //in order to to block the GUI thread, we batch them in UPDATE_COUNT
            private bool IdleHandler()
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                var editor = ext.Editor;
                if (editor == null)
                    return false;
                //clear the old results out at the same rate we add in the new ones
                for (int i = 0; oldMarkers > 0 && i < UPDATE_COUNT; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return false;
                    editor.RemoveMarker(ext.markers.Dequeue());
                    oldMarkers--;
                }
                //add in the new markers
                for (int i = 0; i < UPDATE_COUNT; i++)
                {
                    if (!enumerator.MoveNext())
                    {
                        ext.tasks = builder.ToImmutable();
                        ext.OnTasksUpdated(EventArgs.Empty);
                        return false;
                    }
                    if (cancellationToken.IsCancellationRequested)
                        return false;
                    var currentResult = enumerator.Current;
                    if (currentResult.InspectionMark != IssueMarker.None)
                    {
                        int start = currentResult.Region.Start;
                        int end = currentResult.Region.End;
                        if (start >= end)
                            continue;
                        if (currentResult.InspectionMark == IssueMarker.GrayOut)
                        {
                            var marker = TextMarkerFactory.CreateGenericTextSegmentMarker(editor, TextSegmentMarkerEffect.GrayOut, TextSegment.FromBounds(start, end));
                            marker.IsVisible = currentResult.Underline;
                            marker.Tag = currentResult;
                            editor.AddMarker(marker);
                            ext.markers.Enqueue(marker);
                        }
                        else
                        {
                            var effect = currentResult.InspectionMark == IssueMarker.DottedLine ? TextSegmentMarkerEffect.DottedLine : TextSegmentMarkerEffect.WavedLine;
                            var marker = TextMarkerFactory.CreateGenericTextSegmentMarker(editor, effect, TextSegment.FromBounds(start, end));
                            marker.Color = GetColor(currentResult);
                            marker.IsVisible = currentResult.Underline;
                            marker.Tag = currentResult;
                            editor.AddMarker(marker);
                            ext.markers.Enqueue(marker);
                        }
                    }
                    builder.Add(new QuickTask(currentResult.Message, currentResult.Region.Start, TranslateSeverity(currentResult.Level)));
                }

                return true;
            }

            private DiagnosticSeverity TranslateSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity level)
            {
                switch (level)
                {
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden:
                        return DiagnosticSeverity.Hidden;
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Info:
                        return DiagnosticSeverity.Info;
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Warning:
                        return DiagnosticSeverity.Warning;
                    case Microsoft.CodeAnalysis.DiagnosticSeverity.Error:
                        return DiagnosticSeverity.Error;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(level), level, null);
                }
            }
        }

        //all markers known to be in the editor
        private readonly Queue<IGenericTextSegmentMarker> markers = new Queue<IGenericTextSegmentMarker>();

        private const int UPDATE_COUNT = 20;

        public IList<Result> GetResultsAtOffset(int offset, CancellationToken token = default(CancellationToken))
        {
            var list = new List<Result>();
            foreach (var marker in Editor.GetTextSegmentMarkersAt(offset))
            {
                if (token.IsCancellationRequested)
                    break;
                var resultMarker = marker as IGenericTextSegmentMarker;
                var result = resultMarker?.Tag as Result;
                if (result != null)
                {
                    list.Add(result);
                }
            }
            return list;
        }

        public IEnumerable<Result> GetResults()
        {
            return markers.Select(m => m.Tag).OfType<Result>();
        }

        #region IQuickTaskProvider implementation

        private ImmutableArray<QuickTask> tasks = ImmutableArray<QuickTask>.Empty;

        public event EventHandler TasksUpdated;

        protected virtual void OnTasksUpdated(EventArgs e)
        {
            TasksUpdated?.Invoke(this, e);
        }

        public ImmutableArray<QuickTask> QuickTasks => tasks;

        #endregion
    }
}
