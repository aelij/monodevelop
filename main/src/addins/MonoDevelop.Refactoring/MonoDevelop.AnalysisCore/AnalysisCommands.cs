// 
// AnalysisCommands.cs
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
using MonoDevelop.Components.Commands;
using System.Linq;
using System.Collections.Generic;
using MonoDevelop.CodeIssues;
using MonoDevelop.CodeActions;
using System.IO;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Gui.Dialogs;

namespace MonoDevelop.AnalysisCore
{
    public enum AnalysisCommands
    {
        FixOperations,
        ShowFixes,
        QuickFix,
        ExportRules
    }

    class ShowFixesHandler : CommandHandler
    {
        protected override void Update(CommandInfo info)
        {
            var doc = Ide.IdeApp.Workbench.ActiveDocument;
            if (doc?.Editor == null)
            {
                info.Enabled = false;
                return;
            }
            var codeActionExtension = doc.GetContent<CodeActionEditorExtension>();
            if (codeActionExtension == null)
            {
                info.Enabled = false;
                return;
            }
            var fixes = codeActionExtension.GetCurrentFixes();
            info.Enabled = !fixes.IsEmpty;
        }

        protected override void Run()
        {
        }
    }

    class FixOperationsHandler : CommandHandler
    {
        protected override void Update(CommandArrayInfo info)
        {
        }

        protected override void Run(object dataItem)
        {
            var item = dataItem as Result;
            if (item != null)
            {
                item.ShowResultOptionsDialog();
                return;
            }
            var item1 = dataItem as Action;
            if (item1 != null)
            {
                item1();
                return;
            }
            var action = dataItem as IAnalysisFixAction;
            action?.Fix();
        }
    }

    class ExportRulesHandler : CommandHandler
    {
        protected override void Run()
        {
            var lang = "text/x-csharp";

            OpenFileDialog dlg = new OpenFileDialog("Export Rules", MonoDevelop.Components.FileChooserAction.Save);
            dlg.InitialFileName = "rules.html";
            if (!dlg.Run())
                return;

            Dictionary<CodeDiagnosticDescriptor, DiagnosticSeverity?> severities = new Dictionary<CodeDiagnosticDescriptor, DiagnosticSeverity?>();

            foreach (var node in BuiltInCodeDiagnosticProvider.GetBuiltInCodeDiagnosticDecsriptorsAsync(CodeRefactoringService.MimeTypeToLanguage(lang), true).Result)
            {
                severities[node] = node.DiagnosticSeverity;
                //				if (node.GetProvider ().SupportedDiagnostics.Length > 1) {
                //					foreach (var subIssue in node.GetProvider ().SupportedDiagnostics) {
                //						severities [subIssue] = node.GetSeverity (subIssue);
                //					}
                //				}
            }

            var grouped = severities.Keys.OfType<CodeDiagnosticDescriptor>()
                .GroupBy(node => node.GetProvider().SupportedDiagnostics.First().Category)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            using (var sw = new StreamWriter(dlg.SelectedFile))
            {
                sw.WriteLine("<h1>Code Rules</h1>");
                foreach (var g in grouped)
                {
                    sw.WriteLine("<h2>" + g.Key + "</h2>");
                    sw.WriteLine("<table border='1'>");

                    foreach (var node in g.OrderBy(n => n.Name, StringComparer.Ordinal))
                    {
                        var title = node.Name;
                        var desc = node.GetProvider().SupportedDiagnostics.First().Description.ToString() != title ? node.GetProvider().SupportedDiagnostics.First().Description : "";
                        sw.WriteLine("<tr><td>" + title + "</td><td>" + desc + "</td><td>" + node.DiagnosticSeverity + "</td></tr>");
                        if (node.GetProvider().SupportedDiagnostics.Length > 1)
                        {
                            foreach (var subIssue in node.GetProvider().SupportedDiagnostics)
                            {
                                title = subIssue.Description.ToString();
                                desc = subIssue.Description.ToString() != title ? subIssue.Description : "";
                                sw.WriteLine("<tr><td> - " + title + "</td><td>" + desc + "</td><td>" + node.GetSeverity(subIssue) + "</td></tr>");
                            }
                        }
                    }
                    sw.WriteLine("</table>");
                }

                var providerStates = new Dictionary<CodeRefactoringDescriptor, bool>();
                foreach (var node in BuiltInCodeDiagnosticProvider.GetBuiltInCodeRefactoringDescriptorsAsync(CodeRefactoringService.MimeTypeToLanguage(lang), true).Result)
                {
                    providerStates[node] = node.IsEnabled;
                }

                sw.WriteLine("<h1>Code Actions</h1>");
                sw.WriteLine("<table border='1'>");
                var sortedAndFiltered = providerStates.Keys.OrderBy(n => n.Name, StringComparer.Ordinal);
                foreach (var node in sortedAndFiltered)
                {
                    sw.WriteLine("<tr><td>" + node.IdString + "</td><td>" + node.Name + "</td></tr>");
                }
                sw.WriteLine("</table>");
            }
        }
    }
}

