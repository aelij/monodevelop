//
// MainToolbarController.cs
//
// Author:
//       Marius Ungureanu <marius.ungureanu@xamarin.com>
//
// Copyright (c) 2015 Xamarin, Inc (http://www.xamarin.com)
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
using System.Linq;
using MonoDevelop.Ide;
using MonoDevelop.Components.Commands;

namespace MonoDevelop.Components.MainToolbar
{
    public interface IMainToolbarController : ICommandBar
    {
        IMainToolbarView ToolbarView { get; }
        IStatusBar StatusBar { get; }
        void Initialize();
        void SetSearchCategory(string category);
        void ShowCommandBar(string barId);
        void HideCommandBar(string barId);
    }

    internal class MainToolbarController : IMainToolbarController
    {
        public IMainToolbarView ToolbarView
        {
            get;
        }

        public IStatusBar StatusBar => ToolbarView.StatusBar;

        private int ignoreConfigurationChangedCount;
        private int ignoreRuntimeChangedCount;
        private bool settingGlobalConfig;

        public MainToolbarController(IMainToolbarView toolbarView)
        {
            ToolbarView = toolbarView;
            // Attach run button click handler.
            toolbarView.RunButtonClicked += HandleStartButtonClicked;

            toolbarView.ConfigurationChanged += HandleConfigurationChanged;
            toolbarView.RuntimeChanged += HandleRuntimeChanged;

            // Update Search Entry on keybinding change.
            var cmd = IdeApp.CommandService.GetCommand(Commands.NavigateTo);
            cmd.KeyBindingChanged += delegate
            {
                UpdateSearchEntryLabel();
            };
        }

        public void Initialize()
        {
            var items = new[]
            {
                new SearchMenuModel ("Search Files", "file"),
                new SearchMenuModel ("Search Types", "type"),
                new SearchMenuModel ("Search Members", "member"),
            };

            // Attach menu category handlers.
            foreach (var item in items)
                item.Activated += (o, e) => SetSearchCategory(item.Category);
            ToolbarView.SearchMenuItems = items;

            // Update Search Entry label initially.
            UpdateSearchEntryLabel();

            UpdateCombos();

            // Register this controller as a commandbar.
            IdeApp.CommandService.RegisterCommandBar(this);
        }

        private void UpdateCombos()
        {
            if (settingGlobalConfig)
                return;

            ignoreConfigurationChangedCount++;
            try
            {

                ToolbarView.ConfigurationModel = Enumerable.Empty<IConfigurationModel>();
                ToolbarView.RuntimeModel = Enumerable.Empty<IRuntimeModel>();
                ToolbarView.RunConfigurationModel = Enumerable.Empty<IRunConfigurationModel>();
                ToolbarView.RunConfigurationVisible = false;

            }
            finally
            {
                ignoreConfigurationChangedCount--;
            }

            ToolbarView.RunConfigurationVisible = ToolbarView.RunConfigurationModel.Count() > 1;

            FillRuntimes();
        }

        private void FillRuntimes()
        {
            ignoreRuntimeChangedCount++;
            try
            {
                ToolbarView.RuntimeModel = Enumerable.Empty<IRuntimeModel>();
            }
            finally
            {
                ignoreRuntimeChangedCount--;
            }
        }

        private void HandleRuntimeChanged(object sender, HandledEventArgs e)
        {
            if (ignoreRuntimeChangedCount == 0)
            {
                NotifyConfigurationChange();
            }
        }

        private void HandleConfigurationChanged(object sender, EventArgs e)
        {
            if (ignoreConfigurationChangedCount == 0)
                NotifyConfigurationChange();
        }

        private void NotifyConfigurationChange()
        {
            if (ToolbarView.ActiveConfiguration == null)
                return;

            FillRuntimes();
        }

        private void UpdateSearchEntryLabel()
        {
            var info = IdeApp.CommandService.GetCommand(Commands.NavigateTo);
            ToolbarView.SearchPlaceholderMessage = !string.IsNullOrEmpty(info.AccelKey) ?
                $"Press \u2018{KeyBindingManager.BindingToDisplayLabel(info.AccelKey, false)}\u2019 to search"
                :
                "Search solution";
        }

        public void SetSearchCategory(string category)
        {
            IdeApp.Workbench.Present();
            ToolbarView.SearchCategory = category + ":";
        }

        private readonly HashSet<string> visibleBars = new HashSet<string>();
        public void ShowCommandBar(string barId)
        {
            visibleBars.Add(barId);
        }

        public void HideCommandBar(string barId)
        {
            visibleBars.Remove(barId);
        }

        private static void HandleStartButtonClicked(object sender, EventArgs e)
        {
            OperationIcon operation;
            var ci = GetStartButtonCommandInfo(out operation);
            if (ci.Enabled)
                IdeApp.CommandService.DispatchCommand(ci.Command.Id, CommandSource.MainToolbar);
        }

        private static CommandInfo GetStartButtonCommandInfo(out OperationIcon operation)
        {
            operation = OperationIcon.Run;
            var ci = IdeApp.CommandService.GetCommandInfo("MonoDevelop.Debugger.DebugCommands.Debug");
            return ci;
        }

        #region ICommandBar implementation

        private bool toolbarEnabled = true;

        void ICommandBar.Update(object activeTarget)
        {
            if (!toolbarEnabled)
                return;
            OperationIcon operation;
            var ci = GetStartButtonCommandInfo(out operation);
            if (ci.Enabled != ToolbarView.RunButtonSensitivity)
                ToolbarView.RunButtonSensitivity = ci.Enabled;

            ToolbarView.RunButtonIcon = operation;
            var stopped = operation != OperationIcon.Stop;
            if (ToolbarView.ConfigurationPlatformSensitivity != stopped)
                ToolbarView.ConfigurationPlatformSensitivity = stopped;
        }

        void ICommandBar.SetEnabled(bool enabled)
        {
            toolbarEnabled = enabled;
            ToolbarView.RunButtonSensitivity = enabled;
            ToolbarView.SearchSensivitity = enabled;
            ToolbarView.ButtonBarSensitivity = enabled;
        }
        #endregion

        private class SearchMenuModel : ISearchMenuModel
        {
            public SearchMenuModel(string displayString, string category)
            {
                DisplayString = displayString;
                Category = category;
            }

            public void NotifyActivated()
            {
                Activated?.Invoke(null, null);
            }

            public string DisplayString { get; }
            public string Category { get; }
            public event EventHandler Activated;
        }
    }
}

