//
// NativePlatformExecutionHandler.cs
//
// Author:
//   Lluis Sanchez Gual
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MonoDevelop.Core.Execution
{
	public class NativePlatformExecutionHandler: IExecutionHandler
	{
		IDictionary<string, string> defaultEnvironmentVariables;
		
		public NativePlatformExecutionHandler ()
		{
		}
		
		public NativePlatformExecutionHandler (IDictionary<string, string> defaultEnvironmentVariables)
		{
			this.defaultEnvironmentVariables = defaultEnvironmentVariables;
		}
		
		public virtual ProcessAsyncOperation Execute (ExecutionCommand command, OperationConsole console)
		{
			ProcessExecutionCommand cmd = (ProcessExecutionCommand) command;
			IDictionary<string, string> vars;
			if (defaultEnvironmentVariables != null && defaultEnvironmentVariables.Count > 0) {
				if (cmd.EnvironmentVariables.Count == 0) {
					vars = defaultEnvironmentVariables;
				} else {
					// Merge the variables.
					vars = new Dictionary<string, string> (defaultEnvironmentVariables);
					foreach (KeyValuePair<string,string> evar in cmd.EnvironmentVariables)
						vars [evar.Key] = evar.Value;
				}
			} else
				vars = cmd.EnvironmentVariables;
			
            // TODO-AELIJ: probably need ProcessService back
		    return null;
		    //return Runtime.StartConsoleProcess (cmd.Command, cmd.Arguments, cmd.WorkingDirectory, console, vars);
		}
	
		public virtual bool CanExecute (ExecutionCommand command)
		{
			return command is NativeExecutionCommand;
		}
	}
}
