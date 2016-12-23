// 
// IPadContainer.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
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
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using MonoDevelop.Components.Docking;

namespace MonoDevelop.Ide.Gui
{
    public static class DockItemToolbarLoader
    {
        public static void Add(this DockItemToolbar toolbar, CommandEntrySet entrySet, Control commandTarget)
        {
            CommandDockBar dockBar = new CommandDockBar(toolbar, commandTarget);
            foreach (CommandEntry entry in entrySet)
            {
                dockBar.Add(entry);
            }
        }

    }

    internal class CommandDockBar : ICommandBar
    {
        private readonly List<ToolButtonStatus> buttons = new List<ToolButtonStatus>();
        private readonly DockItemToolbar toolbar;
        private readonly object initialTarget;

        public CommandDockBar(DockItemToolbar toolbar, object initialTarget)
        {
            this.toolbar = toolbar;
            this.initialTarget = initialTarget;
            if (toolbar.DockItem.ContentVisible)
                IdeApp.CommandService.RegisterCommandBar(this);
            toolbar.DockItem.ContentVisibleChanged += HandleToolbarDockItemContentVisibleChanged;
        }

        private void HandleToolbarDockItemContentVisibleChanged(object sender, EventArgs e)
        {
            if (toolbar.DockItem.ContentVisible)
                IdeApp.CommandService.RegisterCommandBar(this);
            else
                IdeApp.CommandService.UnregisterCommandBar(this);
        }

        public void Add(CommandEntry entry)
        {
            Widget w = CreateWidget(entry);
            var button = w as Button;
            if (button != null)
            {
                buttons.Add(new ToolButtonStatus(entry.CommandId, button, entry.DisplayType));
                button.Clicked += delegate
                {
                    IdeApp.CommandService.DispatchCommand(entry.CommandId, null, initialTarget, CommandSource.MainToolbar);
                };
            }
            toolbar.Add(w);
        }

        public void Update(object activeTarget)
        {
            CommandTargetRoute route = new CommandTargetRoute(initialTarget ?? activeTarget);
            foreach (ToolButtonStatus bs in buttons)
                bs.Update(route);
        }

        public void SetEnabled(bool enabled)
        {
            toolbar.Sensitive = enabled;
        }

        private static Widget CreateWidget(CommandEntry entry)
        {
            if (entry.CommandId == Command.Separator)
                return new SeparatorToolItem();

            Command cmd = entry.GetCommand(IdeApp.CommandService);
            if (cmd == null)
                return new Label();

            var command = cmd as CustomCommand;
            if (command != null)
            {
                Widget ti = (Widget)Activator.CreateInstance(command.WidgetType);
                if (!string.IsNullOrEmpty (command.Text))
                {
                    //strip "_" accelerators from tooltips
                    string text = command.Text;
                    while (true)
                    {
                        int underscoreIndex = text.IndexOf('_');
                        if (underscoreIndex > -1)
                            text = text.Remove(underscoreIndex, 1);
                        else
                            break;
                    }
                    ti.TooltipText = text;
                }
                return ti;
            }

            ActionCommand acmd = cmd as ActionCommand;
            if (acmd == null)
                throw new InvalidOperationException("Unknown cmd type.");

            if (acmd.CommandArray)
            {
                throw new InvalidOperationException("Command arrays not supported.");
            }
            if (acmd.ActionType == ActionType.Normal)
                return new Button();
            return new ToggleButton();
        }
    }

    internal class ToolButtonStatus
    {
        private string lastDesc;
        private string stockId;
        private readonly Button button;
        private readonly object cmdId;
        private ImageView image;
        private readonly CommandEntryDisplayType displayType;

        public ToolButtonStatus(object cmdId, Button button, CommandEntryDisplayType displayType = CommandEntryDisplayType.Default)
        {
            this.cmdId = cmdId;
            this.button = button;
            this.displayType = displayType == CommandEntryDisplayType.Default ? CommandEntryDisplayType.IconHasPriority : displayType;
        }

        public void Update(CommandTargetRoute targetRoute)
        {
            CommandInfo cmdInfo = IdeApp.CommandService.GetCommandInfo(cmdId, targetRoute);

            bool hasAccel = string.IsNullOrEmpty(cmdInfo.AccelKey);
            bool hasIcon = !cmdInfo.Icon.IsNull;
            string desc = cmdInfo.Description;
            bool displayText = !(displayType == CommandEntryDisplayType.IconHasPriority && hasIcon) &&
                                      displayType != CommandEntryDisplayType.IconOnly;

            //If the button only has an icon it's not always clear what it does. In such cases, use the label as a
            //fallback tooltip. Also do this if there's an accelerator, so the user can see what it is.
            if (string.IsNullOrEmpty(desc) && (!displayText || hasAccel))
                desc = cmdInfo.Text;

            if (lastDesc != desc)
            {
                string toolTip;
                if (hasAccel)
                {
                    toolTip = desc;
                }
                else
                {
                    toolTip = desc + " (" + KeyBindingManager.BindingToDisplayLabel(cmdInfo.AccelKey, false) + ")";
                }
                button.TooltipText = toolTip;
                lastDesc = desc;
            }

            if (displayText && button.Label != cmdInfo.Text)
            {
                button.Label = cmdInfo.Text;
            }

            if (displayType != CommandEntryDisplayType.TextOnly && cmdInfo.Icon != stockId)
            {
                stockId = cmdInfo.Icon;
                button.Image = image = new ImageView(cmdInfo.Icon, IconSize.Menu);
            }
            if (button.Image != null && cmdInfo.Enabled != button.Sensitive)
                image.Image = image.Image.WithStyles(cmdInfo.Enabled ? "" : "disabled").WithAlpha(cmdInfo.Enabled ? 1.0 : 0.4);
            if (cmdInfo.Enabled != button.Sensitive)
                button.Sensitive = cmdInfo.Enabled;
            if (cmdInfo.Visible != button.Visible)
                button.Visible = cmdInfo.Visible;

            ToggleButton toggle = button as ToggleButton;
            if (toggle != null && cmdInfo.Checked != toggle.Active)
                toggle.Active = cmdInfo.Checked;

            button.Image?.Show();
        }
    }
}
