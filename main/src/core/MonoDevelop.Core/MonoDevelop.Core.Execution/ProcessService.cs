using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix.Native;

namespace MonoDevelop.Core.Execution
{
    public class ProcessService
    {
        private ExternalConsoleHandler externalConsoleHandler;

        private Dictionary<string, string> environmentVariableOverrides;

        public IDictionary<string, string> EnvironmentVariableOverrides => environmentVariableOverrides ??
                                                                           (environmentVariableOverrides = new Dictionary<string, string>());

        private void ProcessEnvironmentVariableOverrides(ProcessStartInfo info)
        {
            if (environmentVariableOverrides == null)
                return;
            foreach (KeyValuePair<string, string> kvp in environmentVariableOverrides)
            {
                if (kvp.Value == null && info.EnvironmentVariables.ContainsKey(kvp.Key))
                    info.EnvironmentVariables.Remove(kvp.Key);
                else
                    info.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        internal ProcessService()
        {
        }

        public void SetExternalConsoleHandler(ExternalConsoleHandler handler)
        {
            if (externalConsoleHandler != null)
                throw new InvalidOperationException("External console handler already set");
            externalConsoleHandler = handler;
        }

        public ProcessWrapper StartProcess(string command, string arguments, string workingDirectory, EventHandler exited)
        {
            return StartProcess(command, arguments, workingDirectory, null, (ProcessEventHandler)null, exited);
        }

        public ProcessWrapper StartProcess(string command, string arguments, string workingDirectory, ProcessEventHandler outputStreamChanged, ProcessEventHandler errorStreamChanged)
        {
            return StartProcess(command, arguments, workingDirectory, outputStreamChanged, errorStreamChanged, null);
        }

        public ProcessWrapper StartProcess(string command, string arguments, string workingDirectory, TextWriter outWriter, TextWriter errorWriter, EventHandler exited)
        {
            return StartProcess(command, arguments, workingDirectory, outWriter, errorWriter, exited, false);
        }

        public ProcessWrapper StartProcess(string command, string arguments, string workingDirectory, TextWriter outWriter, TextWriter errorWriter, EventHandler exited, bool redirectStandardInput)
        {
            ProcessEventHandler wout = OutWriter.GetWriteHandler(outWriter);
            ProcessEventHandler werr = OutWriter.GetWriteHandler(errorWriter);
            return StartProcess(command, arguments, workingDirectory, wout, werr, exited, redirectStandardInput);
        }

        public ProcessWrapper StartProcess(string command, string arguments, string workingDirectory, ProcessEventHandler outputStreamChanged, ProcessEventHandler errorStreamChanged, EventHandler exited)
        {
            return StartProcess(command, arguments, workingDirectory, outputStreamChanged, errorStreamChanged, exited, false);
        }

        public ProcessWrapper StartProcess(string command, string arguments, string workingDirectory, ProcessEventHandler outputStreamChanged, ProcessEventHandler errorStreamChanged, EventHandler exited, bool redirectStandardInput)
        {
            return StartProcess(CreateProcessStartInfo(command, arguments, workingDirectory, redirectStandardInput),
                outputStreamChanged, errorStreamChanged, exited);
        }

        public ProcessWrapper StartProcess(ProcessStartInfo startInfo, TextWriter outWriter, TextWriter errorWriter, EventHandler exited)
        {
            ProcessEventHandler wout = OutWriter.GetWriteHandler(outWriter);
            ProcessEventHandler werr = OutWriter.GetWriteHandler(errorWriter);
            return StartProcess(startInfo, wout, werr, exited);
        }

        public ProcessWrapper StartProcess(ProcessStartInfo startInfo, ProcessEventHandler outputStreamChanged, ProcessEventHandler errorStreamChanged, EventHandler exited)
        {
            if (startInfo == null)
                throw new ArgumentException("startInfo");

            ProcessWrapper p = new ProcessWrapper();

            if (outputStreamChanged != null)
            {
                startInfo.RedirectStandardOutput = true;
                p.OutputStreamChanged += outputStreamChanged;
            }

            if (errorStreamChanged != null)
            {
                startInfo.RedirectStandardError = true;
                p.ErrorStreamChanged += errorStreamChanged;
            }

            startInfo.CreateNoWindow = true;
            p.StartInfo = startInfo;
            ProcessEnvironmentVariableOverrides(p.StartInfo);

            // FIXME: the bug is long gone, but removing the hacks in ProcessWrapper w/o bugs will be tricky
            // WORKAROUND for "bug 410743 - wapi leak in System.Diagnostic.Process"
            // Process leaks when an exit event is registered
            // instead we use another thread to monitor I/O and wait for exit
            // if (exited != null)
            // 	p.Exited += exited;
            // p.EnableRaisingEvents = true;

            Counters.ProcessesStarted++;
            p.Start();

            if (exited != null)
                p.Task.ContinueWith(t => exited(p, EventArgs.Empty), Runtime.MainTaskScheduler);

            return p;
        }

        public ProcessStartInfo CreateProcessStartInfo(string command, string arguments, string workingDirectory, bool redirectStandardInput)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (command.Length == 0)
                throw new ArgumentException("command");

            var startInfo = String.IsNullOrEmpty(arguments) ? new ProcessStartInfo(command) : new ProcessStartInfo(command, arguments);

            if (!string.IsNullOrEmpty(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardInput = redirectStandardInput;
            startInfo.UseShellExecute = false;

            return startInfo;
        }

        public ProcessAsyncOperation StartConsoleProcess(string command, string arguments, string workingDirectory, OperationConsole console,
            IDictionary<string, string> environmentVariables = null, EventHandler exited = null)
        {
            var externalConsole = console as ExternalConsole;

            if ((console == null || externalConsole != null) && externalConsoleHandler != null)
            {

                var dict = new Dictionary<string, string>();
                if (environmentVariables != null)
                    foreach (var kvp in environmentVariables)
                        dict[kvp.Key] = kvp.Value;
                if (environmentVariableOverrides != null)
                    foreach (var kvp in environmentVariableOverrides)
                        dict[kvp.Key] = kvp.Value;

                var p = externalConsoleHandler(command, arguments, workingDirectory, dict,
                    $"{BrandingService.ApplicationName} External Console",
                    externalConsole != null && !externalConsole.CloseOnDispose);

                if (p != null)
                {
                    if (exited != null)
                        p.Task.ContinueWith(t => exited(p, EventArgs.Empty), Runtime.MainTaskScheduler);
                    Counters.ProcessesStarted++;
                    return p;
                }
                LoggingService.LogError("Could not create external console for command: " + command + " " + arguments);
            }
            ProcessStartInfo psi = CreateProcessStartInfo(command, arguments, workingDirectory, false);
            if (environmentVariables != null)
                foreach (KeyValuePair<string, string> kvp in environmentVariables)
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            try
            {
                ProcessWrapper pw = StartProcess(psi, console.Out, console.Error, null);
                new ProcessMonitor(console, pw.ProcessAsyncOperation, exited);
                return pw.ProcessAsyncOperation;
            }
            catch (Exception ex)
            {
                // If the process can't be started, dispose the console now since ProcessMonitor won't do it
                console.Error.WriteLine(("The application could not be started"));
                LoggingService.LogError("Could not start process for command: " + psi.FileName + " " + psi.Arguments, ex);
                console.Dispose();
                return NullProcessAsyncOperation.Failure;
            }
        }

        public bool IsValidForRemoteHosting(IExecutionHandler handler)
        {
            string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            location = Path.Combine(location, "mdhost.exe");
            return handler.CanExecute(new DotNetExecutionCommand(location));
        }

        internal void Dispose()
        {
        }
    }

    internal class ProcessMonitor
    {
        public OperationConsole Console { get; }
        private readonly EventHandler exited;
        private readonly ProcessAsyncOperation operation;
        private readonly IDisposable cancelRegistration;

        public ProcessMonitor(OperationConsole console, ProcessAsyncOperation operation, EventHandler exited)
        {
            this.exited = exited;
            this.operation = operation;
            Console = console;
            operation.Task.ContinueWith(t => OnOperationCompleted());
            cancelRegistration = console.CancellationToken.Register(operation.Cancel);
        }

        public void OnOperationCompleted()
        {
            cancelRegistration.Dispose();
            try
            {
                if (exited != null)
                    Runtime.RunInMainThread(() =>
                    {
                        exited(operation, EventArgs.Empty);
                    });

                if (!Platform.IsWindows && Syscall.WIFSIGNALED(operation.ExitCode))
                    Console.Log.WriteLine("The application was terminated by a signal: {0}", Syscall.WTERMSIG(operation.ExitCode));
                else if (operation.ExitCode != 0)
                    Console.Log.WriteLine("The application exited with code: {0}", operation.ExitCode);
            }
            finally
            {
                Console.Dispose();
            }
        }
    }

    internal class OutWriter
    {
        private readonly TextWriter writer;

        public OutWriter(TextWriter writer)
        {
            this.writer = writer;
        }

        public void WriteOut(object sender, string s)
        {
            writer.Write(s);
        }

        public static ProcessEventHandler GetWriteHandler(TextWriter tw)
        {
            return tw != null ? new ProcessEventHandler(new OutWriter(tw).WriteOut) : null;
        }
    }

    public delegate void ProcessEventHandler(object sender, string message);

    [DesignerCategory("Code")]
    public class ProcessWrapper : Process
    {
        private ManualResetEvent endEventOut = new ManualResetEvent(false);
        private ManualResetEvent endEventErr = new ManualResetEvent(false);
        private bool done;
        private readonly object lockObj = new object();
        private ProcessAsyncOperation operation;
        private IDisposable customCancelToken;
        private readonly TaskCompletionSource<int> taskCompletionSource = new TaskCompletionSource<int>();

        public bool CancelRequested { get; private set; }

        public Task Task => taskCompletionSource.Task;

        public ProcessAsyncOperation ProcessAsyncOperation => operation;

        public new void Start()
        {
            CheckDisposed();
            base.Start();

            var cs = new CancellationTokenSource();
            operation = new ProcessAsyncOperation(Task, cs);
            cs.Token.Register(Cancel);

            Task.Run(CaptureOutput, cs.Token);

            if (ErrorStreamChanged != null)
            {
                Task.Run(CaptureError, cs.Token);
            }
            else
            {
                endEventErr.Set();
            }
            operation.ProcessId = Id;
        }

        public void SetCancellationToken(CancellationToken cancelToken)
        {
            customCancelToken = cancelToken.Register(Cancel);
        }

        public void WaitForOutput(int milliseconds)
        {
            CheckDisposed();
            WaitForExit(milliseconds);
            endEventOut.WaitOne();
        }

        public void WaitForOutput()
        {
            WaitForOutput(-1);
        }

        private async Task CaptureOutput()
        {
            try
            {
                if (OutputStreamChanged != null)
                {
                    char[] buffer = new char[1024];
                    int nr;
                    while ((nr = await StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        OutputStreamChanged?.Invoke(this, new string(buffer, 0, nr));
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // There is no need to keep propagating the abort exception
                Thread.ResetAbort();
            }
            finally
            {
                // WORKAROUND for "bug 410743 - wapi leak in System.Diagnostic.Process"
                // Process leaks when an exit event is registered
                endEventErr?.WaitOne();

                try
                {
                    if (HasExited)
                        operation.ExitCode = ExitCode;
                }
                catch
                {
                    // Ignore
                }

                try
                {
                    OnExitedInternal();
                }
                catch
                {
                    // Ignore
                }

                lock (lockObj)
                {
                    //call this AFTER the exit event, or the ProcessWrapper may get disposed and abort this thread
                    endEventOut?.Set();
                }
                taskCompletionSource.SetResult(operation.ExitCode);
            }
        }

        private async Task CaptureError()
        {
            try
            {
                char[] buffer = new char[1024];
                int nr;
                while ((nr = await StandardError.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    ErrorStreamChanged?.Invoke(this, new string(buffer, 0, nr));
                }
            }
            finally
            {
                lock (lockObj)
                {
                    endEventErr?.Set();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            lock (lockObj)
            {
                if (endEventOut == null)
                    return;

                if (!done)
                    Cancel();

                endEventOut.Close();
                endEventErr.Close();
                endEventOut = endEventErr = null;
            }

            // HACK: try/catch is a workaround for broken Process.Dispose implementation in Mono < 3.2.7
            // https://bugzilla.xamarin.com/show_bug.cgi?id=10883
            try
            {
                base.Dispose(disposing);
            }
            catch
            {
                if (disposing)
                    throw;
            }
        }

        private void CheckDisposed()
        {
            if (endEventOut == null)
                throw new ObjectDisposedException("ProcessWrapper");
        }

        public void Cancel()
        {
            try
            {
                if (!done)
                {
                    try
                    {
                        CancelRequested = true;
                        this.KillProcessTree();
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError(ex.ToString());
            }
        }

        private void OnExitedInternal()
        {
            if (customCancelToken != null)
            {
                customCancelToken.Dispose();
                customCancelToken = null;
            }
            try
            {
                if (!HasExited)
                    WaitForExit();
            }
            catch
            {
                // Ignore
            }
            finally
            {
                lock (lockObj)
                {
                    done = true;
                }
            }
        }

        public event ProcessEventHandler OutputStreamChanged;
        public event ProcessEventHandler ErrorStreamChanged;
    }

    public delegate ProcessAsyncOperation ExternalConsoleHandler(string command, string arguments, string workingDirectory, IDictionary<string, string> environmentVariables, string title, bool pauseWhenFinished);
}