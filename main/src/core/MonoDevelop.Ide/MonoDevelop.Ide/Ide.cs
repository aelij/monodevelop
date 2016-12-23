//
// IdeApp.cs
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


using MonoDevelop.Core;
using MonoDevelop.Core.Instrumentation;
using MonoDevelop.Components.Commands;
using System.Linq;
using MonoDevelop.Ide.Gui;
using System.Collections.Generic;
using MonoDevelop.Core.Assemblies;

namespace MonoDevelop.Ide
{
    public static class IdeApp
    {
        public const int CurrentRevision = 5;

        public static event ExitEventHandler Exiting;
        public static event EventHandler Exited;

        static EventHandler initializedEvent;
        public static event EventHandler Initialized
        {
            add
            {
                Runtime.RunInMainThread(() =>
                {
                    if (IsInitialized) value(null, EventArgs.Empty);
                    else initializedEvent += value;
                });
            }
            remove
            {
                Runtime.RunInMainThread(() =>
                {
                    initializedEvent -= value;
                });
            }
        }

        /// <summary>
        /// Fired when the IDE gets the focus
        /// </summary>
        public static event EventHandler FocusIn
        {
            add { CommandService.ApplicationFocusIn += value; }
            remove { CommandService.ApplicationFocusIn -= value; }
        }

        /// <summary>
        /// Fired when the IDE loses the focus
        /// </summary>
        public static event EventHandler FocusOut
        {
            add { CommandService.ApplicationFocusOut += value; }
            remove { CommandService.ApplicationFocusOut -= value; }
        }

        /// <summary>
        /// Gets a value indicating whether the IDE has the input focus
        /// </summary>
        public static bool HasInputFocus
        {
            get { return CommandService.ApplicationHasFocus; }
        }

        static IdeApp()
        {
            Preferences = new IdePreferences();
        }

        public static Workbench Workbench { get; internal set; }

        public static CommandManager CommandService { get; internal set; }

        public static IdePreferences Preferences { get; }

        public static bool IsInitialized { get; private set; }

        // Returns true if MD is running for the first time after installing
        public static bool IsInitialRun { get; private set; }

        // Returns true if MD is running for the first time after being upgraded from a previous version
        public static bool IsInitialRunAfterUpgrade { get; private set; }

        // If IsInitialRunAfterUpgrade is true, returns the previous version
        public static int UpgradedFromRevision { get; private set; }

        public static Version Version
        {
            get
            {
                return Runtime.Version;
            }
        }

        public static void Initialize(ProgressMonitor monitor)
        {
            // Already done in IdeSetup, but called again since unit tests don't use IdeSetup.
            DispatchService.Initialize();

            Counters.Initialization.Trace("Creating Workbench");
            Workbench = new Workbench();
            Counters.Initialization.Trace("Creating Services");
            CommandService = new CommandManager();

            CommandService.CommandTargetScanStarted += CommandServiceCommandTargetScanStarted;
            CommandService.CommandTargetScanFinished += CommandServiceCommandTargetScanFinished;
            CommandService.KeyBindingFailed += KeyBindingFailed;

            KeyBindingService.LoadBindingsFromExtensionPath("/MonoDevelop/Ide/KeyBindingSchemes");
            KeyBindingService.LoadCurrentBindings("MD2");

            CommandService.CommandError += delegate (object sender, CommandErrorArgs args)
            {
                LoggingService.LogInternalError(args.ErrorMessage, args.Exception);
            };

            FileService.ErrorHandler = FileServiceErrorHandler;

            monitor.BeginTask(("Loading Workbench"), 6);
            Counters.Initialization.Trace("Loading Commands");

            // Before startup commands.
            Counters.Initialization.Trace("Running Pre-Startup Commands");
            monitor.Step();

            Counters.Initialization.Trace("Initializing Workbench");
            Workbench.Initialize(monitor);
            monitor.Step();

            monitor.Step();

            Counters.Initialization.Trace("Restoring Workbench State");
            Workbench.Show();
            monitor.Step();

            Counters.Initialization.Trace("Flushing GUI events");
            DispatchService.RunPendingEvents();
            Counters.Initialization.Trace("Flushed GUI events");

            MessageService.RootWindow = Workbench.RootWindow;
            Xwt.MessageDialog.RootWindow = Xwt.Toolkit.CurrentEngine.WrapWindow(Workbench.RootWindow);

            CommandService.EnableIdleUpdate = true;

            // Startup commands
            Counters.Initialization.Trace("Running Startup Commands");
            monitor.Step();
            monitor.EndTask();

            // Set initial run flags
            Counters.Initialization.Trace("Upgrading Settings");

            if (PropertyService.Get("MonoDevelop.Core.FirstRun", false))
            {
                IsInitialRun = true;
                PropertyService.Set("MonoDevelop.Core.FirstRun", false);
                PropertyService.Set("MonoDevelop.Core.LastRunVersion", BuildInfo.Version);
                PropertyService.Set("MonoDevelop.Core.LastRunRevision", CurrentRevision);
                PropertyService.SaveProperties();
            }

            string lastVersion = PropertyService.Get("MonoDevelop.Core.LastRunVersion", "1.9.1");
            int lastRevision = PropertyService.Get("MonoDevelop.Core.LastRunRevision", 0);
            if (lastRevision != CurrentRevision && !IsInitialRun)
            {
                IsInitialRunAfterUpgrade = true;
                if (lastRevision == 0)
                {
                    switch (lastVersion)
                    {
                        case "1.0": lastRevision = 1; break;
                        case "2.0": lastRevision = 2; break;
                        case "2.2": lastRevision = 3; break;
                        case "2.2.1": lastRevision = 4; break;
                    }
                }
                UpgradedFromRevision = lastRevision;
                PropertyService.Set("MonoDevelop.Core.LastRunVersion", BuildInfo.Version);
                PropertyService.Set("MonoDevelop.Core.LastRunRevision", CurrentRevision);
                PropertyService.SaveProperties();
            }

            // The ide is now initialized

            IsInitialized = true;

            if (IsInitialRun)
            {
                try
                {
                    OnInitialRun();
                }
                catch (Exception e)
                {
                    LoggingService.LogError("Error found while initializing the IDE", e);
                }
            }

            if (IsInitialRunAfterUpgrade)
            {
                try
                {
                    OnUpgraded(UpgradedFromRevision);
                }
                catch (Exception e)
                {
                    LoggingService.LogError("Error found while initializing the IDE", e);
                }
            }

            if (initializedEvent != null)
            {
                initializedEvent(null, EventArgs.Empty);
                initializedEvent = null;
            }

            IdeApp.Preferences.EnableInstrumentation.Changed += delegate { };
            Gtk.LinkButton.SetUriHook((button, uri) => Xwt.Desktop.OpenUrl(uri));
        }

        static void KeyBindingFailed(object sender, KeyBindingFailedEventArgs e)
        {
            Ide.IdeApp.Workbench.StatusBar.ShowWarning(e.Message);
        }

        //this method is MIT/X11, 2009, Michael Hutchinson / (c) Novell
        public static void OpenFiles(IEnumerable<FileOpenInformation> files)
        {
            if (!files.Any())
                return;

            if (!IsInitialized)
            {
                EventHandler onInit = null;
                onInit = delegate
                {
                    Initialized -= onInit;
                    OpenFiles(files);
                };
                Initialized += onInit;
                return;
            }

            var filteredFiles = new List<FileOpenInformation>();

            //open the firsts sln/workspace file, and remove the others from the list
            //FIXME: can we handle multiple slns?
            bool foundSln = false;

            foreach (var file in filteredFiles)
            {
                try
                {
                    Workbench.OpenDocument(file.FileName, null, file.Line, file.Column, file.Options);
                }
                catch (Exception ex)
                {
                    MessageService.ShowError(string.Format("Could not open file: {0}", file.FileName), ex);
                }
            }

            Workbench.Present();
        }

        static bool FileServiceErrorHandler(string message, Exception ex)
        {
            MessageService.ShowError(message, ex);
            return true;
        }

        public static void Run()
        {
            // finally run the workbench window ...
            Gtk.Application.Run();
        }

        /// <summary>
        /// Exits MonoDevelop. Returns false if the user cancels exiting.
        /// </summary>
        public static bool Exit()
        {
            if (Workbench.Close())
            {
                Gtk.Application.Quit();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Restarts MonoDevelop
        /// </summary>
        /// <returns> false if the user cancels exiting. </returns>
        /// <param name="reopenWorkspace"> true to reopen current workspace. </param>
        /// <remarks>
        /// Starts a new MonoDevelop instance in a new process and 
        /// stops the current MonoDevelop instance.
        /// </remarks>
        public static bool Restart(bool reopenWorkspace = false)
        {
            if (Exit())
            {
                try
                {
                    DesktopService.RestartIde(reopenWorkspace);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("Restarting IDE failed", ex);
                }
                // return true here even if DesktopService.RestartIde has failed,
                // because the Ide has already been closed.
                return true;
            }
            return false;
        }

        internal static bool OnExit()
        {
            if (Exiting != null)
            {
                ExitEventArgs args = new ExitEventArgs();
                Exiting(null, args);
                return !args.Cancel;
            }
            return true;
        }

        internal static void OnExited()
        {
            if (Exited != null)
                Exited(null, EventArgs.Empty);
        }

        static void OnInitialRun()
        {
            SetInitialLayout();
        }

        static void OnUpgraded(int previousRevision)
        {
            if (previousRevision <= 3)
            {
                // Reset the current runtime when upgrading from <2.2, to ensure the default runtime is not stuck to an old mono install
                IdeApp.Preferences.DefaultTargetRuntime.Value = SystemAssemblyService.Instance.CurrentRuntime;
            }
            if (previousRevision < 5)
                SetInitialLayout();
        }

        static void SetInitialLayout()
        {
            if (!IdeApp.Workbench.Layouts.Contains("Solution"))
            {
                // Create the Solution layout, based on Default
                IdeApp.Workbench.CurrentLayout = "Solution";
                foreach (Pad p in IdeApp.Workbench.Pads)
                {
                    if (p.Visible)
                        p.AutoHide = true;
                }
            }
        }

        static ITimeTracker commandTimeCounter;

        static void CommandServiceCommandTargetScanStarted(object sender, EventArgs e)
        {
            commandTimeCounter = Counters.CommandTargetScanTime.BeginTiming();
        }

        static void CommandServiceCommandTargetScanFinished(object sender, EventArgs e)
        {
            commandTimeCounter.End();
        }
    }
}
