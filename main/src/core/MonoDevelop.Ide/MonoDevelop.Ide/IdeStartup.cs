//
// IdeStartup.cs
//
// Author:
//   Lluis Sanchez Gual
//
// Copyright (C) 2011 Xamarin Inc (http://xamarin.com)
// Copyright (C) 2005-2011 Novell, Inc (http://www.novell.com)
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GLib;
using Gtk;
using Microsoft.Win32;
using Mono.Options;
using Mono.Unix;
using Mono.Unix.Native;
using MonoDevelop.Components;
using MonoDevelop.Components.Extensions;
using MonoDevelop.Core;
using MonoDevelop.Core.Instrumentation;
using MonoDevelop.Core.ProgressMonitoring;
using MonoDevelop.Ide.Desktop;
using MonoDevelop.Ide.Gui;
using Xwt;
using Action = System.Action;
using Application = Xwt.Application;
using MessageDialog = Xwt.MessageDialog;
using Process = System.Diagnostics.Process;
using Socket = System.Net.Sockets.Socket;
using Thread = System.Threading.Thread;
using Timeout = GLib.Timeout;

namespace MonoDevelop.Ide
{
    public class IdeStartup
    {
        private const int IpcBasePort = 40000;

        private static DateTime lastIdle;
        private static bool lockupCheckRunning = true;

        private string socketFilename;

        public bool EnablePerfLog { get; set; }

		readonly Func<PlatformService> platformService;

		public IdeStartup(MonoDevelopOptions options = null, ComponentServices componentServices = null, Func<PlatformService> platformService = null)
        {
			this.platformService = platformService;
			ComponentServices = componentServices ?? new MefComponentServices();
            Options = options ?? MonoDevelopOptions.Default;
        }

        public MonoDevelopOptions Options { get; }

        public ComponentServices ComponentServices { get; }

        public int Run(Action createUi = null)
        {
            if (Options.ShowHelp || Options.Error != null)
                return Options.Error != null ? -1 : 0;

            LoggingService.Initialize(Options.RedirectOutput);

            if (!Platform.IsWindows)
            {
                // Limit maximum threads when running on mono
                int threadCount = 125;
                ThreadPool.SetMaxThreads(threadCount, threadCount);
            }

            int ret = -1;
            try
            {
                var exename = Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location);
                if (!Platform.IsMac && !Platform.IsWindows)
                    // ReSharper disable once PossibleNullReferenceException
                    exename = exename.ToLower();
                Runtime.SetProcessName(exename);

                ret = RunInternal(createUi);
            }
            catch (Exception ex)
            {
                LoggingService.LogFatalError(
                    string.Format(
                        "{0} failed to start. Some of the assemblies required to run {0} (for example gtk-sharp)" +
                        "may not be properly installed in the GAC.",
                        BrandingService.ApplicationName
                    ), ex);
            }
            finally
            {
                Runtime.Shutdown();
            }

            LoggingService.Shutdown();

            return ret;
        }

        private int RunInternal(Action createUi)
        {
            LoggingService.LogInfo("Starting {0} {1}", BrandingService.ApplicationName, IdeVersionInfo.MonoDevelopVersion);
            LoggingService.LogInfo("Running on {0}", IdeVersionInfo.GetRuntimeInfo());

            //ensure native libs initialized before we hit anything that p/invokes
            Platform.Initialize();

            LoggingService.LogInfo("Operating System: {0}", SystemInformation.GetOperatingSystemDescription());

            Counters.Initialization.BeginTiming();

            if (EnablePerfLog)
            {
                string logFile = Path.Combine(Environment.CurrentDirectory, "monodevelop.perf-log");
                LoggingService.LogInfo("Logging instrumentation service data to file: " + logFile);
                InstrumentationService.StartAutoSave(logFile, 1000);
            }

            Counters.Initialization.Trace("Initializing GTK");
            if (Platform.IsWindows && !CheckWindowsGtk())
                return 1;
            SetupExceptionManager();

            try
            {
                GLibLogging.Enabled = true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error initialising GLib logging.", ex);
            }

            IdeTheme.InitializeGtk(BrandingService.ApplicationName);

            LoggingService.LogInfo("Using GTK+ {0}", IdeVersionInfo.GetGtkVersion());

            // XWT initialization
            FilePath p = typeof(IdeStartup).Assembly.Location;
            Assembly.LoadFrom(p.ParentDirectory.Combine("Xwt.Gtk.dll"));
            Application.InitializeAsGuest(ToolkitType.Gtk);
            Toolkit.CurrentEngine.RegisterBackend<IExtendedTitleBarWindowBackend, GtkExtendedTitleBarWindowBackend>();
            Toolkit.CurrentEngine.RegisterBackend<IExtendedTitleBarDialogBackend, GtkExtendedTitleBarDialogBackend>();
            IdeTheme.SetupXwtTheme();

            //default to Windows IME on Windows
            if (Platform.IsWindows && GtkWorkarounds.GtkMinorVersion >= 16)
            {
                var settings = Settings.Default;
                var val = settings.GetProperty("gtk-im-module");
                if (string.IsNullOrEmpty(val.Val as string))
                    settings.SetProperty("gtk-im-module", new Value("ime"));
            }

            DispatchService.Initialize();

            // Set a synchronization context for the main gtk thread
            SynchronizationContext.SetSynchronizationContext(DispatchService.SynchronizationContext);
            Runtime.MainSynchronizationContext = SynchronizationContext.Current;

            Counters.Initialization.Trace("Initializing Runtime");
            Runtime.Initialize();

            Counters.Initialization.Trace("Initializing theme");

            IdeTheme.SetupGtkTheme();

            ProgressMonitor monitor = new ConsoleProgressMonitor();

            monitor.BeginTask(GettextCatalog.GetString("Starting {0}", BrandingService.ApplicationName), 2);

            //make sure that the platform service is initialised so that the Mac platform can subscribe to open-document events
            Counters.Initialization.Trace("Initializing Platform Service");
			DesktopService.Initialize(platformService());

            monitor.Step();

            if (Options.SingleProcess)
            {
                EnsureSingleProcess();
            }

            Counters.Initialization.Trace("Checking System");

            CheckFileWatcher();

            Exception error = null;

            try
            {
                Counters.Initialization.Trace("Loading Icons");
                //force initialisation before the workbench so that it can register stock icons for GTK before they get requested
                ImageService.Initialize();
                ImageService.LoadDefaultStockSet();

                // If we display an error dialog before the main workbench window on OS X then a second application menu is created
                // which is then replaced with a second empty Apple menu.
                // XBC #33699
                Counters.Initialization.Trace("Initializing IdeApp");
                IdeApp.Initialize(monitor);

                // Load requested files
                Counters.Initialization.Trace("Opening Files");

                monitor.Step();

            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                monitor.Dispose();
            }

            if (error != null)
            {
                string message = BrandingService.BrandApplicationName("MonoDevelop failed to start");
                MessageService.ShowFatalError(message, null, error);
                return 1;
            }

            Initialized = true;
            MessageService.RootWindow = IdeApp.Workbench.RootWindow;
            MessageDialog.RootWindow = Toolkit.CurrentEngine.WrapWindow(IdeApp.Workbench.RootWindow);
            Thread.CurrentThread.Name = "GUI Thread";
            Counters.Initialization.Trace("Running IdeApp");
            Counters.Initialization.EndTiming();

            StartLockupTracker();

            createUi?.Invoke();

            IdeApp.Run();

            // unloading services
            if (socketFilename != null)
                File.Delete(socketFilename);
            lockupCheckRunning = false;
            Runtime.Shutdown();

            InstrumentationService.Stop();

            return 0;
        }

        private void EnsureSingleProcess()
        {
            var startupInfo = new StartupInfo(Options.RemainingArgs);
            socketFilename = null;
            Socket listenSocket;
            EndPoint ep;

            if (Options.IpcTcp)
            {
                listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
                ep = new IPEndPoint(IPAddress.Loopback, IpcBasePort + HashSdbmBounded(Environment.UserName));
            }
            else
            {
                socketFilename = "/tmp/md-" + Environment.GetEnvironmentVariable("USER") + "-socket";
                listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                ep = new UnixEndPoint(socketFilename);
            }

            // If not opening a combine, connect to existing monodevelop and pass filename(s) and exit
            if (!Options.NewWindow && startupInfo.HasFiles)
            {
                try
                {
                    StringBuilder builder = new StringBuilder();
                    foreach (var file in startupInfo.RequestedFileList)
                    {
                        builder.AppendFormat("{0};{1};{2}\n", file.FileName, file.Line, file.Column);
                    }
                    listenSocket.Connect(ep);
                    listenSocket.Send(Encoding.UTF8.GetBytes(builder.ToString()));
                    Environment.Exit(0);
                }
                catch
                {
                    // Reset the socket
                    if (socketFilename != null && File.Exists(socketFilename))
                        File.Delete(socketFilename);
                }
            }
            else
            {
                // FIXME: we should probably track the last 'selected' one
                // and do this more cleanly
                try
                {
                    listenSocket.Bind(ep);
                    listenSocket.Listen(5);
                    listenSocket.BeginAccept(ListenCallback, listenSocket);
                }
                catch
                {
                    // Socket already in use
                }
            }
        }

        [Conditional("DEBUG_PARANOIA")]
        private static void StartLockupTracker()
        {
            if (Platform.IsWindows)
                return;
            if (!string.Equals(Environment.GetEnvironmentVariable("MD_LOCKUP_TRACKER"), "ON", StringComparison.OrdinalIgnoreCase))
                return;
            Timeout.Add(2000, () =>
            {
                lastIdle = DateTime.Now;
                return true;
            });
            lastIdle = DateTime.Now;
            var lockupCheckThread = new Thread(delegate ()
            {
                while (lockupCheckRunning)
                {
                    const int waitTimeout = 5000;
                    const int maxResponseTime = 10000;
                    Thread.Sleep(waitTimeout);
                    if ((DateTime.Now - lastIdle).TotalMilliseconds > maxResponseTime)
                    {
                        var pid = Process.GetCurrentProcess().Id;
                        Syscall.kill(pid, Signum.SIGQUIT);
                        return;
                    }
                }
            })
            {
                Name = "Lockup check",
                IsBackground = true
            };
            lockupCheckThread.Start();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static bool CheckWindowsGtk()
        {
            string location = null;
            Version version = null;
            Version minVersion = new Version(2, 12, 22);

            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Xamarin\GtkSharp\InstallFolder"))
            {
                if (key != null)
                    location = key.GetValue(null) as string;
            }
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Xamarin\GtkSharp\Version"))
            {
                if (key != null)
                    Version.TryParse(key.GetValue(null) as string, out version);
            }

            //TODO: check build version of GTK# dlls in GAC
            if (version == null || version < minVersion || location == null || !File.Exists(Path.Combine(location, "bin", "libgtk-win32-2.0-0.dll")))
            {
                LoggingService.LogError("Did not find required GTK# installation");
                string url = "http://monodevelop.com/Download";
                string caption = "Fatal Error";
                string message =
                    "{0} did not find the required version of GTK#. Please click OK to open the download page, where " +
                    "you can download and install the latest version.";
                if (DisplayWindowsOkCancelMessage(
                    string.Format(message, BrandingService.ApplicationName, url), caption)
                )
                {
                    Process.Start(url);
                }
                return false;
            }

            LoggingService.LogInfo("Found GTK# version " + version);

            var path = Path.Combine(location, @"bin");
            try
            {
                if (SetDllDirectory(path))
                {
                    return true;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            // this shouldn't happen unless something is weird in Windows
            LoggingService.LogError("Unable to set GTK+ dll directory");
            return true;
        }

        private static bool DisplayWindowsOkCancelMessage(string message, string caption)
        {
            var name = typeof(int).Assembly.FullName.Replace("mscorlib", "System.Windows.Forms");
            var asm = Assembly.Load(name);
            var md = asm.GetType("System.Windows.Forms.MessageBox");
            var mbb = asm.GetType("System.Windows.Forms.MessageBoxButtons");
            var okCancel = Enum.ToObject(mbb, 1);
            var dr = asm.GetType("System.Windows.Forms.DialogResult");
            var ok = Enum.ToObject(dr, 1);

            const BindingFlags flags = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static;
            return md.InvokeMember("Show", flags, null, null, new[] { message, caption, okCancel }).Equals(ok);
        }

        public bool Initialized { get; private set; }

        private static void ListenCallback(IAsyncResult state)
        {
            Socket sock = (Socket)state.AsyncState;

            Socket client = sock.EndAccept(state);
            ((Socket)state.AsyncState).BeginAccept(ListenCallback, sock);
            byte[] buf = new byte[1024];
            client.Receive(buf);
            foreach (string filename in Encoding.UTF8.GetString(buf).Split('\n'))
            {
                string trimmed = filename.Trim();
                string file = "";
                foreach (char c in trimmed)
                {
                    if (c == 0x0000)
                        continue;
                    file += c;
                }
                Idle.Add(() => OpenFile(file));
            }
        }

        private static bool OpenFile(string file)
        {
            if (string.IsNullOrEmpty(file))
                return false;

            Match fileMatch = StartupInfo.FileExpression.Match(file);
            if (!fileMatch.Success)
                return false;

            int line = 1,
                column = 1;

            file = fileMatch.Groups["filename"].Value;
            if (fileMatch.Groups["line"].Success)
                int.TryParse(fileMatch.Groups["line"].Value, out line);
            if (fileMatch.Groups["column"].Success)
                int.TryParse(fileMatch.Groups["column"].Value, out column);

            try
            {
                IdeApp.Workbench.OpenDocument(file, null, line, column, OpenDocumentOptions.DefaultInternal);
            }
            catch
            {
                // ignored
            }
            IdeApp.Workbench.Present();
            return false;
        }

        private static void CheckFileWatcher()
        {
            string watchesFile = "/proc/sys/fs/inotify/max_user_watches";
            try
            {
                if (File.Exists(watchesFile))
                {
                    string val = File.ReadAllText(watchesFile);
                    int n = int.Parse(val);
                    if (n <= 9000)
                    {
                        string msg = "Inotify watch limit is too low (" + n + ").\n";
                        msg += "MonoDevelop will switch to managed file watching.\n";
                        msg += "See http://monodevelop.com/Inotify_Watches_Limit for more info.";
                        LoggingService.LogWarning(BrandingService.BrandApplicationName(msg));
                        Runtime.ProcessService.EnvironmentVariableOverrides["MONO_MANAGED_WATCHER"] =
                            Environment.GetEnvironmentVariable("MONO_MANAGED_WATCHER");
                        Environment.SetEnvironmentVariable("MONO_MANAGED_WATCHER", "1");
                    }
                }
            }
            catch (Exception e)
            {
                LoggingService.LogWarning("There was a problem checking whether to use managed file watching", e);
            }
        }

        private static void SetupExceptionManager()
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                HandleException(e.Exception.Flatten(), false);
                e.SetObserved();
            };
            ExceptionManager.UnhandledException += delegate (UnhandledExceptionArgs args)
            {
                HandleException((Exception)args.ExceptionObject, args.IsTerminating);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs args)
            {
                HandleException((Exception)args.ExceptionObject, args.IsTerminating);
            };
            Application.UnhandledException += (sender, e) =>
            {
                HandleException(e.ErrorException, false);
            };
        }

        private static void HandleException(Exception ex, bool willShutdown)
        {
            var msg =
                $"An unhandled exception has occured. Terminating {BrandingService.ApplicationName}? {willShutdown}";
            var aggregateException = ex as AggregateException;
            if (aggregateException != null)
            {
                aggregateException.Flatten().Handle(innerEx =>
                {
                    HandleException(innerEx, willShutdown);
                    return true;
                });
                return;
            }

            if (willShutdown)
                LoggingService.LogFatalError(msg, ex);
            else
                LoggingService.LogInternalError(msg, ex);
        }

        /// <summary>SDBM-style hash, bounded to a range of 1000.</summary>
        private static int HashSdbmBounded(string input)
        {
            ulong hash = 0;
            foreach (char t in input)
            {
                unchecked
                {
                    hash = t + (hash << 6) + (hash << 16) - hash;
                }
            }

            return (int)(hash % 1000);
        }
    }

    public class MonoDevelopOptions
    {
        private MonoDevelopOptions()
        {
            IpcTcp = (PlatformID.Unix != Environment.OSVersion.Platform);
            RedirectOutput = true;
        }

        private OptionSet GetOptionSet()
        {
            return new OptionSet {
                { "no-splash", "Do not display splash screen (deprecated).", s => {} },
                { "ipc-tcp", "Use the Tcp channel for inter-process communication.", s => IpcTcp = true },
                { "new-window", "Do not open in an existing instance of " + BrandingService.ApplicationName, s => NewWindow = true },
                { "h|?|help", "Show help", s => ShowHelp = true },
                { "perf-log", "Enable performance counter logging", s => PerfLog = true },
                { "no-redirect", "Disable redirection of stdout/stderr to a log file", s => RedirectOutput = false }
            };
        }

        public static MonoDevelopOptions Parse(string[] args)
        {
            var opt = new MonoDevelopOptions();
            var optSet = opt.GetOptionSet();

            try
            {
                opt.RemainingArgs = optSet.Parse(args);
            }
            catch (OptionException ex)
            {
                opt.Error = ex.ToString();
            }

            if (opt.Error != null)
            {
                Console.WriteLine("ERROR: {0}", opt.Error);
                Console.WriteLine("Pass --help for usage information.");
            }

            if (opt.ShowHelp)
            {
                Console.WriteLine(BrandingService.ApplicationName + " " + BuildInfo.VersionLabel);
                Console.WriteLine("Options:");
                optSet.WriteOptionDescriptions(Console.Out);
                const string openFileText = "      file.ext;line;column";
                Console.Write(openFileText);
                Console.Write(new string(' ', 29 - openFileText.Length));
                Console.WriteLine("Opens a file at specified integer line and column");
            }

            return opt;
        }

        public static MonoDevelopOptions Default { get; } =
            new MonoDevelopOptions { RemainingArgs = Environment.GetCommandLineArgs() };

        public bool SingleProcess { get; set; }
        public bool IpcTcp { get; set; }
        public bool NewWindow { get; set; }
        public bool ShowHelp { get; set; }
        public bool PerfLog { get; set; }
        public bool RedirectOutput { get; set; }
        public string Error { get; set; }
        public IList<string> RemainingArgs { get; set; }
    }

    public class AddinError
    {
        public AddinError(string addin, string message, Exception exception, bool fatal)
        {
            AddinFile = addin;
            Message = message;
            Exception = exception;
            Fatal = fatal;
        }

        public string AddinFile { get; }

        public string Message { get; }

        public Exception Exception { get; }

        public bool Fatal { get; }
    }
}
