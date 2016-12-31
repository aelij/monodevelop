//
// PlatformService.cs
//
// Author:
//   Geoff Norton  <gnorton@novell.com>
//
// Copyright (C) 2007 Novell, Inc (http://www.novell.com)
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
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using MonoDevelop.Core;
using Mono.Unix;
using MonoDevelop.Core.Execution;
using MonoDevelop.Components;
using MonoDevelop.Components.MainToolbar;


namespace MonoDevelop.Ide.Desktop
{
    public interface IPlatformService
    {
        string DefaultMonospaceFont { get; }
        string DefaultSansFont { get; }
        string Name { get; }

        /// <summary>
        /// True if both OpenTerminal and StartConsoleProcess are implemented.
        /// </summary>
        bool CanOpenTerminal { get; }

        RecentFiles RecentFiles { get; }

        void Initialize();
        void SetGlobalProgressBar(double progress);
        void ShowGlobalProgressBarError();
        void ShowGlobalProgressBarIndeterminate();
        void OpenFile(string filename);
        void OpenFolder(FilePath folderPath, FilePath[] selectFiles);
        void ShowUrl(string url);

        /// <summary>
        /// Loads the XWT toolkit backend for the native toolkit (Cocoa on Mac, WPF on Windows)
        /// </summary>
        /// <returns>The native toolkit.</returns>
        Xwt.Toolkit LoadNativeToolkit();

        string GetMimeTypeForUri(string uri);
        string GetMimeTypeDescription(string mimeType);
        bool GetMimeTypeIsText(string mimeType);
        bool GetMimeTypeIsSubtype(string subMimeType, string baseMimeType);
        IEnumerable<string> GetMimeTypeInheritanceChain(string mimeType);
        Xwt.Drawing.Image GetIconForFile(string filename);
        Xwt.Drawing.Image GetIconForType(string mimeType);

        bool SetGlobalMenu(Components.Commands.CommandManager commandManager,
            string commandMenuAddinPath, string appMenuAddinPath);

        object GetFileAttributes(string fileName);
        void SetFileAttributes(string fileName, object attributes);

        ProcessAsyncOperation StartConsoleProcess(
            string command, string arguments, string workingDirectory,
            IDictionary<string, string> environmentVariables,
            string title, bool pauseWhenFinished);

        void OpenTerminal(FilePath directory, IDictionary<string, string> environmentVariables, string title);
        string GetUpdaterUrl();
        IEnumerable<string> GetUpdaterEnviromentFlags();

        /// <summary>
        /// Starts the installer.
        /// </summary>
        /// <param name='installerDataFile'>
        /// File containing the list of updates to install
        /// </param>
        /// <param name='updatedInstallerPath'>
        /// Optional path to an updated installer executable
        /// </param>
        /// <remarks>
        /// This method should start the installer in an independent process.
        /// </remarks>
        void StartUpdatesInstaller(FilePath installerDataFile, FilePath updatedInstallerPath);

        IEnumerable<DesktopApplication> GetApplications(string filename);
        Xwt.Rectangle GetUsableMonitorGeometry(int screenNumber, int monitorNumber);

        /// <summary>
        /// Grab the desktop focus for the window.
        /// </summary>
        void GrabDesktopFocus(Gtk.Window window);

        void RemoveWindowShadow(Gtk.Window window);
        void SetMainWindowDecorations(Gtk.Window window);
        IMainToolbarView CreateMainToolbar(Gtk.Window window);
        void AttachMainToolbar(Gtk.VBox parent, IMainToolbarView toolbar);
        bool GetIsFullscreen(Window window);
        bool IsModalDialogRunning();
        void SetIsFullscreen(Window window, bool isFullscreen);
        void AddChildWindow(Gtk.Window parent, Gtk.Window child);
        void RemoveChildWindow(Gtk.Window parent, Gtk.Window child);
        void PlaceWindow(Gtk.Window window, int x, int y, int width, int height);

        /// <summary>
        /// Restarts MonoDevelop
        /// </summary>
        /// <param name="reopenWorkspace"> true to reopen current workspace. </param>
        void RestartIde(bool reopenWorkspace);
    }

    public abstract class PlatformService : IPlatformService
    {
        private static bool UsePlatformFileIcons = false;
        private readonly Hashtable iconHash = new Hashtable();

        public abstract string DefaultMonospaceFont { get; }
        public virtual string DefaultSansFont => null;

        public abstract string Name { get; }

        public virtual void Initialize()
        {
        }

        public virtual void SetGlobalProgressBar(double progress)
        {
        }

        public virtual void ShowGlobalProgressBarError()
        {
        }

        public virtual void ShowGlobalProgressBarIndeterminate()
        {
        }

        public virtual void OpenFile(string filename)
        {
            Process.Start(filename);
        }

        public virtual void OpenFolder(FilePath folderPath, FilePath[] selectFiles)
        {
            Process.Start(folderPath);
        }

        public virtual void ShowUrl(string url)
        {
            Process.Start(url);
        }

        /// <summary>
        /// Loads the XWT toolkit backend for the native toolkit (Cocoa on Mac, WPF on Windows)
        /// </summary>
        /// <returns>The native toolkit.</returns>
        public virtual Xwt.Toolkit LoadNativeToolkit()
        {
            return Xwt.Toolkit.CurrentEngine;
        }

        public string GetMimeTypeForUri(string uri)
        {
            return OnGetMimeTypeForUri(uri) ?? "application/octet-stream";
        }

        public string GetMimeTypeDescription(string mimeType)
        {
            return OnGetMimeTypeDescription(mimeType) ?? string.Empty;
        }

        public bool GetMimeTypeIsText(string mimeType)
        {
            return GetMimeTypeIsSubtype(mimeType, "text/plain");
        }

        public bool GetMimeTypeIsSubtype(string subMimeType, string baseMimeType)
        {
            foreach (string mt in GetMimeTypeInheritanceChain(subMimeType))
                if (mt == baseMimeType)
                    return true;
            return false;
        }

        public IEnumerable<string> GetMimeTypeInheritanceChain(string mimeType)
        {
            yield return mimeType;
        }

        public Xwt.Drawing.Image GetIconForFile(string filename)
        {
            Xwt.Drawing.Image pic = null;

            string icon = GetIconIdForFile(filename);
            if (icon != null) {
                pic = ImageService.GetIcon(icon, false);
            }

            if (pic == null && UsePlatformFileIcons)
            {
                pic = Xwt.Desktop.GetFileIcon(filename);
            }

            if (pic == null)
            {
                string mtype = GetMimeTypeForUri(filename);
                if (mtype != null)
                {
                    foreach (string mt in GetMimeTypeInheritanceChain(mtype))
                    {
                        pic = GetIconForType(mt);
                        if (pic != null)
                            return pic;
                    }
                }
            }
            return pic ?? GetDefaultIcon();
        }

        public Xwt.Drawing.Image GetIconForType(string mimeType)
        {
            Xwt.Drawing.Image bf = (Xwt.Drawing.Image)iconHash[mimeType];
            if (bf != null)
                return bf;

            foreach (string type in GetMimeTypeInheritanceChain(mimeType))
            {
                // Try getting an icon name for the type
                string icon = GetIconIdForType(type);
                if (icon != null)
                {
                    bf = ImageService.GetIcon(icon, false);
                    if (bf != null)
                        break;
                }

                // Try getting a pixbuff
                if (UsePlatformFileIcons)
                {
                    bf = OnGetIconForType(type);
                    if (bf != null)
                        break;
                }
            }

            if (bf == null)
                bf = GetDefaultIcon();

            iconHash[mimeType] = bf;
            return bf;
        }

        private Xwt.Drawing.Image GetDefaultIcon()
        {
            string id = "__default";
            Xwt.Drawing.Image bf = (Xwt.Drawing.Image)iconHash[id];
            if (bf != null)
                return bf;

            string icon = DefaultFileIconId;
            if (icon != null)
                bf = ImageService.GetIcon(icon, false);
            if (bf == null)
                bf = DefaultFileIcon;
            if (bf == null)
                bf = ImageService.GetIcon("md-regular-file", true);
            iconHash[id] = bf;
            return bf;
        }

        private string GetIconIdForFile(string fileName)
        {
            return OnGetIconIdForFile(fileName);
        }

        private string GetIconIdForType(string type)
        {
            if (type == "text/plain")
                return "md-text-file-icon";
            if (UsePlatformFileIcons)
                return OnGetIconIdForType(type);
            return null;
        }

        protected virtual string OnGetMimeTypeForUri(string uri)
        {
            return null;
        }

        protected virtual string OnGetMimeTypeDescription(string mimeType)
        {
            return null;
        }

        protected virtual bool OnGetMimeTypeIsText(string mimeType)
        {
            return false;
        }

        protected virtual string OnGetIconIdForFile(string filename)
        {
            return null;
        }

        protected virtual string OnGetIconIdForType(string type)
        {
            return null;
        }

        protected virtual Xwt.Drawing.Image OnGetIconForFile(string filename)
        {
            return null;
        }

        protected virtual Xwt.Drawing.Image OnGetIconForType(string type)
        {
            return null;
        }

        protected virtual string DefaultFileIconId => null;

        protected virtual Xwt.Drawing.Image DefaultFileIcon => null;

        public virtual bool SetGlobalMenu(Components.Commands.CommandManager commandManager,
            string commandMenuAddinPath, string appMenuAddinPath)
        {
            return false;
        }

        // Used for preserve the file attributes when monodevelop opens & writes a file.
        // This should work on unix & mac platform.
        public virtual object GetFileAttributes(string fileName)
        {
            UnixFileSystemInfo info = UnixFileSystemInfo.GetFileSystemEntry(fileName);
            return info?.FileAccessPermissions;
        }

        public virtual void SetFileAttributes(string fileName, object attributes)
        {
            if (attributes == null)
                return;
            UnixFileSystemInfo info = UnixFileSystemInfo.GetFileSystemEntry(fileName);
            info.FileAccessPermissions = (FileAccessPermissions)attributes;
        }

        //must be implemented if CanOpenTerminal returns true
        public virtual ProcessAsyncOperation StartConsoleProcess(
            string command, string arguments, string workingDirectory,
            IDictionary<string, string> environmentVariables,
            string title, bool pauseWhenFinished)
        {
            throw new InvalidOperationException();
        }

        /// <summary>
        /// True if both OpenTerminal and StartConsoleProcess are implemented.
        /// </summary>
        public virtual bool CanOpenTerminal => false;

        public virtual void OpenTerminal(FilePath directory, IDictionary<string, string> environmentVariables, string title)
        {
            throw new InvalidOperationException();
        }

        protected virtual RecentFiles CreateRecentFilesProvider()
        {
            return new FdoRecentFiles();
        }

        private RecentFiles recentFiles;
        public RecentFiles RecentFiles => recentFiles ?? (recentFiles = CreateRecentFilesProvider());

        public virtual string GetUpdaterUrl()
        {
            return null;
        }

        public virtual IEnumerable<string> GetUpdaterEnviromentFlags()
        {
            return new string[0];
        }

        /// <summary>
        /// Starts the installer.
        /// </summary>
        /// <param name='installerDataFile'>
        /// File containing the list of updates to install
        /// </param>
        /// <param name='updatedInstallerPath'>
        /// Optional path to an updated installer executable
        /// </param>
        /// <remarks>
        /// This method should start the installer in an independent process.
        /// </remarks>
        public virtual void StartUpdatesInstaller(FilePath installerDataFile, FilePath updatedInstallerPath)
        {
        }

        public virtual IEnumerable<DesktopApplication> GetApplications(string filename)
        {
            return new DesktopApplication[0];
        }

        public virtual Xwt.Rectangle GetUsableMonitorGeometry(int screenNumber, int monitorNumber)
        {
            var screen = Gdk.Display.Default.GetScreen(screenNumber);
            var rect = screen.GetMonitorGeometry(monitorNumber);

            return new Xwt.Rectangle
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
            };
        }

        /// <summary>
        /// Grab the desktop focus for the window.
        /// </summary>
        public virtual void GrabDesktopFocus(Gtk.Window window)
        {
            if (Platform.IsWindows && window.IsRealized)
            {
                /* On Windows calling Present() will break out of window edge snapping mode. */
                window.GdkWindow.Focus(0);
                window.GdkWindow.Raise();
            }
            else
            {
                window.Present();
            }
        }

        public virtual void RemoveWindowShadow(Gtk.Window window)
        {
        }

        public virtual void SetMainWindowDecorations(Gtk.Window window)
        {
        }

        public virtual IMainToolbarView CreateMainToolbar(Gtk.Window window)
        {
            // TODO-AELIJ: toolbar
            return null;
            //return new MainToolbar();
        }

        public virtual void AttachMainToolbar(Gtk.VBox parent, IMainToolbarView toolbar)
        {
            var toolbarBox = new Gtk.HBox();
            parent.PackStart(toolbarBox, false, false, 0);
            toolbarBox.PackStart((MainToolbar)toolbar, true, true, 0);
        }

        public virtual bool GetIsFullscreen(Window window)
        {
            return ((bool?)window.GetNativeWidget<Gtk.Window>().Data["isFullScreen"]) ?? false;
        }

        public virtual bool IsModalDialogRunning()
        {
            var windows = Gtk.Window.ListToplevels();
            return windows.Any(w => w.Modal && w.Visible);
        }

        public virtual void SetIsFullscreen(Window window, bool isFullscreen)
        {
            Gtk.Window windowControl = window;
            windowControl.Data["isFullScreen"] = isFullscreen;
            if (isFullscreen)
            {
                windowControl.Fullscreen();
            }
            else
            {
                windowControl.Unfullscreen();
                SetMainWindowDecorations(windowControl);
            }
        }

        public virtual void AddChildWindow(Gtk.Window parent, Gtk.Window child)
        {
        }

        public virtual void RemoveChildWindow(Gtk.Window parent, Gtk.Window child)
        {
        }

        public virtual void PlaceWindow(Gtk.Window window, int x, int y, int width, int height)
        {
            window.Move(x, y);
            window.Resize(width, height);
        }

        /// <summary>
        /// Restarts MonoDevelop
        /// </summary>
        /// <param name="reopenWorkspace"> true to reopen current workspace. </param>
        public virtual void RestartIde(bool reopenWorkspace)
        {
            var reopen = reopenWorkspace;

            FilePath path = Environment.GetCommandLineArgs()[0];
            if (Platform.IsMac && path.Extension == ".exe")
                path = path.ChangeExtension(null);

            if (!File.Exists(path))
                throw new Exception(path + " not found");

            var proc = new Process();

            var psi = new ProcessStartInfo(path)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            var recentWorkspace = reopen ? DesktopService.RecentFiles.GetProjects().FirstOrDefault()?.FileName : string.Empty;
            if (!string.IsNullOrEmpty(recentWorkspace))
                psi.Arguments = recentWorkspace;

            proc.StartInfo = psi;
            proc.Start();
        }
    }
}
