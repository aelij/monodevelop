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
using Window = Gtk.Window;

namespace TestMonoEditor
{
    class Program
    {
        [STAThread]
        private static int Main()
        {
            Environment.SetEnvironmentVariable("MONODEVELOP_PROFILE", Directory.GetCurrentDirectory());
            EditorFactory.Initialize();

            var startup = new IdeStartup();
            return startup.Run(() => AddDocument());
        }

        private static void InitializeIdeApp()
        {
            //var workbench = new Workbench();
            //IdeApp.Workbench = workbench;
            ////ideApp.workspace = new RootWorkspace();
            ////ideApp.projectOperations = new ProjectOperations();
            ////ideApp.helpOperations = new HelpOperations();
            //var commandService = new CommandManager();
            //IdeApp.CommandService = commandService;
            ////ideApp.ideServices = new IdeServices();

            ////commandService.LoadCommands("/MonoDevelop/Ide/Commands");

            //var defaultWorkbench = new DefaultWorkbench();
            //workbench.workbench = defaultWorkbench;
            //defaultWorkbench.DockFrame = new DockFrame();
            //var fullViewVBox = new Gtk.VBox(false, 0);
            //defaultWorkbench.fullViewVBox = fullViewVBox;
            //defaultWorkbench.rootWidget = fullViewVBox;

            //MessageService.RootWindow = workbench.RootWindow;
            //Xwt.MessageDialog.RootWindow = Toolkit.CurrentEngine.WrapWindow(workbench.RootWindow);

            //IdeApp.Initialize(new ConsoleProgressMonitor());
        }

        private static void CreateUI(Window window)
        {
            //window.Title = "RoslynPad";

            //window.Maximize();
            //window.Destroyed += (sender, args) => Application.Quit();

            //var notebook = new SdiDragNotebook((DefaultWorkbench)IdeApp.Workbench.RootWindow);
            //DockNotebook.ActiveNotebook = notebook;

            AddDocument();
            //AddDocument();

            //window.Add(notebook);

            //window.ShowAll();

            //Application.Run();
        }

        private static void AddDocument()
        {
            var ctx = IdeApp.Workbench.NewDocument("X", "text/plain", string.Empty);

            var editor = ctx.Editor;
            var host = new RoslynHost(new MonoDevelopSourceTextContainer(editor));

            var options = new CustomEditorOptions(editor.Options)
            {
                ShowLineNumberMargin = false,
                TabsToSpaces = true,
                ShowWhitespaces = ShowWhitespaces.Never
            };

            editor.Options = options;
            var extension = new RoslynCompletionTextEditorExtension(host);
            editor.SetExtensionChain(ctx, new[] { extension });
            editor.SemanticHighlighting = new RoslynSemanticHighlighting(editor, ctx, host);
        }
    }
}
