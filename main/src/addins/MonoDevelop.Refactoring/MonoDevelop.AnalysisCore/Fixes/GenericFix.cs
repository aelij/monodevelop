// 
// GenericFix.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2011 Novell, Inc (http://www.novell.com)
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
using Microsoft.CodeAnalysis.Text;
using MonoDevelop.Ide.Editor;

namespace MonoDevelop.AnalysisCore.Fixes
{
    public class GenericFix : IAnalysisFix, IAnalysisFixAction
    {
        private readonly Action fix;
        private readonly Action batchFix;
        public TextSpan DocumentRegion { get; set; }
        public string IdString { get; set; }

        public GenericFix(string label, Action fix, Action batchFix = null)
        {
            this.batchFix = batchFix;
            this.fix = fix;
            Label = label;
        }


        #region IAnalysisFix implementation
        public string FixType => "Generic";

        #endregion

        #region IAnalysisFixAction implementation
        public void Fix()
        {
            fix();
        }

        public bool SupportsBatchFix => batchFix != null;

        public void BatchFix()
        {
            if (!SupportsBatchFix)
            {
                throw new InvalidOperationException("Batch fixing is not supported.");
            }
            batchFix();
        }

        public string Label { get; }

        #endregion
    }

    public class GenericFixHandler : IFixHandler
    {
        #region IFixHandler implementation
        public IEnumerable<IAnalysisFixAction> GetFixes(TextEditor editor, DocumentContext context, object fix)
        {
            yield return (GenericFix)fix;
        }
        #endregion
    }
}

