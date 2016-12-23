//
// Runtime.cs
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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MonoDevelop.Core.Execution;
using MonoDevelop.Core.Instrumentation;
using System.Threading.Tasks;

namespace MonoDevelop.Core
{
    public static class Runtime
    {
        private static SynchronizationContext mainSynchronizationContext;
        private static SynchronizationContext defaultSynchronizationContext;
        private static ProcessService processService;
        private static Thread mainThread;

        public static void GetAddinRegistryLocation(out string configDir, out string addinsDir, out string databaseDir)
        {
            //provides a development-time way to load addins that are being developed in a asperate solution
            var devConfigDir = Environment.GetEnvironmentVariable("MONODEVELOP_DEV_CONFIG");
            if (devConfigDir != null && devConfigDir.Length == 0)
                devConfigDir = null;

            var devAddinDir = Environment.GetEnvironmentVariable("MONODEVELOP_DEV_ADDINS");
            if (devAddinDir != null && devAddinDir.Length == 0)
                devAddinDir = null;

            configDir = devConfigDir ?? UserProfile.Current.ConfigDir;
            addinsDir = devAddinDir ?? UserProfile.Current.LocalInstallDir.Combine("Addins");
            databaseDir = devAddinDir ?? UserProfile.Current.CacheDir;
        }

        public static void Initialize()
        {
            if (Initialized)
                return;

            Counters.RuntimeInitialization.BeginTiming();
            SetupInstrumentation();

            Platform.Initialize();

            mainThread = Thread.CurrentThread;
            // Set a default sync context
            if (SynchronizationContext.Current == null)
            {
                defaultSynchronizationContext = new SynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(defaultSynchronizationContext);
            }
            else
                defaultSynchronizationContext = SynchronizationContext.Current;

            try
            {
                Counters.RuntimeInitialization.Trace("Initializing Addin Manager");

                string configDir, addinsDir, databaseDir;
                GetAddinRegistryLocation(out configDir, out addinsDir, out databaseDir);

                Counters.RuntimeInitialization.Trace("Initialized Addin Manager");

                PropertyService.Initialize();

                Counters.RuntimeInitialization.Trace("Initializing Assembly Service");

                Initialized = true;

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                Counters.RuntimeInitialization.EndTiming();
            }
        }

        private static void SetupInstrumentation()
        {
            InstrumentationService.Enabled = Preferences.EnableInstrumentation;
            Preferences.EnableInstrumentation.Changed += (s, e) => InstrumentationService.Enabled = Preferences.EnableInstrumentation;
        }
        
        internal static bool Initialized { get; private set; }

        public static void Shutdown()
        {
            if (!Initialized)
                return;

            ShuttingDown?.Invoke(null, EventArgs.Empty);

            PropertyService.SaveProperties();

            if (processService != null)
            {
                processService.Dispose();
                processService = null;
            }

            Initialized = false;
        }

        public static ProcessService ProcessService => processService ?? (processService = new ProcessService ());

        public static RuntimePreferences Preferences { get; } = new RuntimePreferences();

        private static Version version;

        public static Version Version
        {
            get
            {
                if (version == null)
                {
                    version = new Version(BuildInfo.Version);
                    var relId = SystemInformation.GetReleaseId();
                    if (relId != null && relId.Length >= 9)
                    {
                        int rev;
                        int.TryParse(relId.Substring(relId.Length - 4), out rev);
                        version = new Version(Math.Max(version.Major, 0), Math.Max(version.Minor, 0), Math.Max(version.Build, 0), Math.Max(rev, 0));
                    }
                }
                return version;
            }
        }

        public static SynchronizationContext MainSynchronizationContext
        {
            get
            {
                return mainSynchronizationContext ?? defaultSynchronizationContext;
            }
            set
            {
                if (mainSynchronizationContext != null && value != null)
                    throw new InvalidOperationException("The main synchronization context has already been set");
                mainSynchronizationContext = value;
                taskScheduler = null;
            }
        }


        private static TaskScheduler taskScheduler;
        public static TaskScheduler MainTaskScheduler
        {
            get
            {
                if (taskScheduler == null)
                    RunInMainThread(() => taskScheduler = TaskScheduler.FromCurrentSynchronizationContext()).Wait();
                return taskScheduler;
            }
        }

        /// <summary>
        /// Runs an action in the main thread (usually the UI thread). The method returns a task, so it can be awaited.
        /// </summary>
        public static Task RunInMainThread(Action action)
        {
            var ts = new TaskCompletionSource<int>();
            if (IsMainThread)
            {
                try
                {
                    action();
                    ts.SetResult(0);
                }
                catch (Exception ex)
                {
                    ts.SetException(ex);
                }
            }
            else
            {
                MainSynchronizationContext.Post(delegate
                {
                    try
                    {
                        action();
                        ts.SetResult(0);
                    }
                    catch (Exception ex)
                    {
                        ts.SetException(ex);
                    }
                }, null);
            }
            return ts.Task;
        }

        /// <summary>
        /// Runs a function in the main thread (usually the UI thread). The method returns a task, so it can be awaited.
        /// </summary>
        public static Task<T> RunInMainThread<T>(Func<T> func)
        {
            var ts = new TaskCompletionSource<T>();
            if (IsMainThread)
            {
                try
                {
                    ts.SetResult(func());
                }
                catch (Exception ex)
                {
                    ts.SetException(ex);
                }
            }
            else
            {
                MainSynchronizationContext.Post(delegate
                {
                    try
                    {
                        ts.SetResult(func());
                    }
                    catch (Exception ex)
                    {
                        ts.SetException(ex);
                    }
                }, null);
            }
            return ts.Task;
        }

        /// <summary>
        /// Runs an action in the main thread (usually the UI thread). The method returns a task, so it can be awaited.
        /// </summary>
        /// <remarks>This version of the method is useful when the operation to be executed in the main
        /// thread is asynchronous.</remarks>
        public static Task<T> RunInMainThread<T>(Func<Task<T>> func)
        {
            if (IsMainThread)
            {
                return func();
            }
            else
            {
                var ts = new TaskCompletionSource<T>();
                MainSynchronizationContext.Post(async state =>
                {
                    try
                    {
                        ts.SetResult(await func());
                    }
                    catch (Exception ex)
                    {
                        ts.SetException(ex);
                    }
                }, null);
                return ts.Task;
            }
        }

        /// <summary>
        /// Runs an action in the main thread (usually the UI thread). The method returns a task, so it can be awaited.
        /// </summary>
        /// <remarks>This version of the method is useful when the operation to be executed in the main
        /// thread is asynchronous.</remarks>
        public static Task RunInMainThread(Func<Task> func)
        {
            if (IsMainThread)
            {
                return func();
            }
            else
            {
                var ts = new TaskCompletionSource<int>();
                MainSynchronizationContext.Post(async state =>
                {
                    try
                    {
                        await func();
                        ts.SetResult(0);
                    }
                    catch (Exception ex)
                    {
                        ts.SetException(ex);
                    }
                }, null);
                return ts.Task;
            }
        }

        /// <summary>
        /// Returns true if current thread is GUI thread.
        /// </summary>
        public static bool IsMainThread => mainThread == Thread.CurrentThread;

        /// <summary>
        /// Asserts that the current thread is the main thread. It will throw an exception if it isn't.
        /// </summary>
        public static void AssertMainThread()
        {
            if (!IsMainThread)
                throw new InvalidOperationException("Operation not supported in background thread");
        }

        public static void SetProcessName(string name)
        {
            if (!Platform.IsMac && !Platform.IsWindows)
            {
                try
                {
                    unixSetProcessName(name);
                }
                catch (Exception e)
                {
                    LoggingService.LogError("Error setting process name", e);
                }
            }
        }

        [DllImport("libc")] // Linux
        private static extern int prctl(int option, byte[] arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);

        [DllImport("libc")] // BSD
        private static extern void setproctitle(byte[] fmt, byte[] str_arg);

        //this is from http://abock.org/2006/02/09/changing-process-name-in-mono/
        private static void unixSetProcessName(string name)
        {
            try
            {
                if (prctl(15 /* PR_SET_NAME */, Encoding.ASCII.GetBytes(name + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0)
                {
                    throw new ApplicationException("Error setting process name: " + Mono.Unix.Native.Stdlib.GetLastError());
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Not every BSD has setproctitle
                try
                {
                    setproctitle(Encoding.ASCII.GetBytes("%s\0"), Encoding.ASCII.GetBytes(name + "\0"));
                }
                catch (EntryPointNotFoundException) { }
            }
        }

        public static event EventHandler ShuttingDown;
    }

    internal static class Counters
    {
        public static TimerCounter RuntimeInitialization = InstrumentationService.CreateTimerCounter("Runtime initialization", "Runtime", id: "Core.RuntimeInitialization");
        public static TimerCounter PropertyServiceInitialization = InstrumentationService.CreateTimerCounter("Property Service initialization", "Runtime");

        public static Counter AddinsLoaded = InstrumentationService.CreateCounter("Add-ins loaded", "Add-in Engine", true, id: "Core.AddinsLoaded");

        public static Counter ProcessesStarted = InstrumentationService.CreateCounter("Processes started", "Process Service");
        public static Counter ExternalObjects = InstrumentationService.CreateCounter("External objects", "Process Service");
        public static Counter ExternalHostProcesses = InstrumentationService.CreateCounter("External processes hosting objects", "Process Service");

        public static TimerCounter TargetRuntimesLoading = InstrumentationService.CreateTimerCounter("Target runtimes loaded", "Assembly Service", 0, true);
        public static Counter PcFilesParsed = InstrumentationService.CreateCounter(".pc Files parsed", "Assembly Service");

        public static Counter FileChangeNotifications = InstrumentationService.CreateCounter("File change notifications", "File Service");
        public static Counter FilesRemoved = InstrumentationService.CreateCounter("Files removed", "File Service");
        public static Counter FilesCreated = InstrumentationService.CreateCounter("Files created", "File Service");
        public static Counter FilesRenamed = InstrumentationService.CreateCounter("Files renamed", "File Service");
        public static Counter DirectoriesRemoved = InstrumentationService.CreateCounter("Directories removed", "File Service");
        public static Counter DirectoriesCreated = InstrumentationService.CreateCounter("Directories created", "File Service");
        public static Counter DirectoriesRenamed = InstrumentationService.CreateCounter("Directories renamed", "File Service");

        public static Counter LogErrors = InstrumentationService.CreateCounter("Errors", "Log");
        public static Counter LogWarnings = InstrumentationService.CreateCounter("Warnings", "Log");
        public static Counter LogMessages = InstrumentationService.CreateCounter("Information messages", "Log");
        public static Counter LogFatalErrors = InstrumentationService.CreateCounter("Fatal errors", "Log");
        public static Counter LogDebug = InstrumentationService.CreateCounter("Debug messages", "Log");
    }

    public class RuntimePreferences
    {
        internal RuntimePreferences() { }

        public readonly ConfigurationProperty<bool> EnableInstrumentation = ConfigurationProperty.Create("MonoDevelop.EnableInstrumentation", false);
        public readonly ConfigurationProperty<bool> EnableAutomatedTesting = ConfigurationProperty.Create("MonoDevelop.EnableAutomatedTesting", false);
        public readonly ConfigurationProperty<string> UserInterfaceLanguage = ConfigurationProperty.Create("MonoDevelop.Ide.UserInterfaceLanguage", "");
        public readonly ConfigurationProperty<bool> BuildWithMSBuild = ConfigurationProperty.Create("MonoDevelop.Ide.BuildWithMSBuild", false);
        public readonly ConfigurationProperty<bool> ParallelBuild = ConfigurationProperty.Create("MonoDevelop.ParallelBuild", true);

        public readonly ConfigurationProperty<string> AuthorName = ConfigurationProperty.Create("Author.Name", Environment.UserName, oldName: "ChangeLogAddIn.Name");
        public readonly ConfigurationProperty<string> AuthorEmail = ConfigurationProperty.Create("Author.Email", "", oldName: "ChangeLogAddIn.Email");
        public readonly ConfigurationProperty<string> AuthorCopyright = ConfigurationProperty.Create("Author.Copyright", (string)null);
        public readonly ConfigurationProperty<string> AuthorCompany = ConfigurationProperty.Create("Author.Company", "");
        public readonly ConfigurationProperty<string> AuthorTrademark = ConfigurationProperty.Create("Author.Trademark", "");
    }
}
