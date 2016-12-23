using System;
using System.IO;
using System.Reflection;
using System.Threading;
using GLib;
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Components.Docking;
using MonoDevelop.Components.DockNotebook;
using MonoDevelop.Components.Extensions;
using MonoDevelop.Core;
using MonoDevelop.Core.Instrumentation;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Fonts;
using MonoDevelop.Ide.Gui;
using MonoDevelop.SourceEditor;
using Xwt;
using Application = Gtk.Application;
using Counters = MonoDevelop.Ide.Counters;
using Window = Gtk.Window;

namespace TestMonoEditor
{
    class Program
    {
        [STAThread]
        private static void Main()
        {
            Environment.SetEnvironmentVariable("MONODEVELOP_PROFILE", Directory.GetCurrentDirectory());

            var monoDevelopOptions = MonoDevelopOptions.Parse(Array.Empty<string>());
            LoggingService.Initialize(monoDevelopOptions.RedirectOutput);
            if (!Platform.IsWindows)
            {
                ThreadPool.SetMaxThreads(125, 125);
            }
            try
            {
                string text = Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location);
                if (!Platform.IsMac && !Platform.IsWindows)
                {
                    // ReSharper disable once PossibleNullReferenceException
                    text = text.ToLower();
                }
                Runtime.SetProcessName(text);
                Platform.Initialize();
                Counters.Initialization.BeginTiming();
                //IdeApp.Customizer = new IdeCustomizer();
                //IdeApp.Customizer.Initialize();
                GLibLogging.Enabled = true;
                var args = Array.Empty<string>();
                IdeTheme.InitializeGtk(BrandingService.ApplicationName, ref args);

                Assembly.LoadFrom(new FilePath(typeof(IdeStartup).Assembly.Location).ParentDirectory.Combine("Xwt.Gtk.dll"));
                Xwt.Application.InitializeAsGuest(ToolkitType.Gtk);
                Toolkit.CurrentEngine.RegisterBackend<IExtendedTitleBarWindowBackend, GtkExtendedTitleBarWindowBackend>();
                Toolkit.CurrentEngine.RegisterBackend<IExtendedTitleBarDialogBackend, GtkExtendedTitleBarDialogBackend>();
                IdeTheme.SetupXwtTheme();
                if (Platform.IsWindows && GtkWorkarounds.GtkMinorVersion >= 16)
                {
                    Settings @default = Settings.Default;
                    if (string.IsNullOrEmpty(@default.GetProperty("gtk-im-module").Val as string))
                    {
                        @default.SetProperty("gtk-im-module", new Value("ime"));
                    }
                }
                DispatchService.Initialize();

                SynchronizationContext.SetSynchronizationContext(DispatchService.SynchronizationContext);
                Runtime.MainSynchronizationContext = SynchronizationContext.Current;
                //AddinManager.AddinLoadError += (sender, a) => Console.WriteLine("!!!!!" + a.Exception);
                Runtime.Initialize();
                //IdeApp.Customizer.OnCoreInitialized();
                IdeTheme.SetupGtkTheme();
                DesktopService.Initialize(new MonoDevelop.Platform.WindowsPlatform());
                FontService.SetFont("Editor", "_DEFAULT_MONOSPACE");
                FontService.SetFont("Pad", "_DEFAULT_MONOSPACE");
                FontService.SetFont("OutputPad", "_DEFAULT_MONOSPACE");
                ImageService.Initialize();
                ImageService.LoadDefaultStockSet();
                //LocalizationService.Initialize();
                InitializeIdeApp();
                TextEditorFactory.CurrentFactory = new EditorFactory();

                CreateUI(IdeApp.Workbench.RootWindow);
            }
            finally
            {
                //IdeApp.Customizer.OnIdeShutdown();
                Runtime.Shutdown();
                //IdeApp.Customizer.OnCoreShutdown();
                InstrumentationService.Stop();
                LoggingService.Shutdown();
            }
        }

        private static void InitializeIdeApp()
        {
            var workbench = new Workbench();
            IdeApp.Workbench = workbench;
            //ideApp.workspace = new RootWorkspace();
            //ideApp.projectOperations = new ProjectOperations();
            //ideApp.helpOperations = new HelpOperations();
            var commandService = new CommandManager();
            IdeApp.CommandService = commandService;
            //ideApp.ideServices = new IdeServices();

            //commandService.LoadCommands("/MonoDevelop/Ide/Commands");

            var defaultWorkbench = new DefaultWorkbench();
            workbench.workbench = defaultWorkbench;
            defaultWorkbench.DockFrame = new DockFrame();
            var fullViewVBox = new Gtk.VBox(false, 0);
            defaultWorkbench.fullViewVBox = fullViewVBox;
            defaultWorkbench.rootWidget = fullViewVBox;

            MessageService.RootWindow = workbench.RootWindow;
            Xwt.MessageDialog.RootWindow = Toolkit.CurrentEngine.WrapWindow(workbench.RootWindow);

            //IdeApp.Initialize(new ConsoleProgressMonitor());
        }

        private static void CreateUI(Window window)
        {
            window.Title = "RoslynPad";

            window.Maximize();
            window.Destroyed += (sender, args) => Application.Quit();

            var notebook = new SdiDragNotebook((DefaultWorkbench)IdeApp.Workbench.RootWindow);
            DockNotebook.ActiveNotebook = notebook;

            AddDocument();
            //AddDocument();

            window.Add(notebook);

            window.ShowAll();

            Application.Run();
        }

        private static void AddDocument()
        {
            var ctx = IdeApp.Workbench.NewDocument("X", "text/plain", new MemoryStream());
            //var w = ctx.AsDynamic().RoslynWorkspace;
            //var t = ctx.AsDynamic().GetCurrentParseFileName();

            var editor = ctx.Editor;
            var host = new RoslynHost(new MonoDevelopSourceTextContainer(editor));

            var options = new CustomEditorOptions(editor.Options)
            {
                ShowLineNumberMargin = false,
                TabsToSpaces = true,
                ShowWhitespaces = ShowWhitespaces.Never
            };

            editor.Options = options;
            var extension = new RoslynCompletionTextEditorExtension(host, editor, ctx, null);
            editor.SetExtensionChain(ctx, new[] { extension });
            editor.SemanticHighlighting = new RoslynSemanticHighlighting(editor, ctx, host);
        }
    }
}
