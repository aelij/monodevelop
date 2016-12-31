using System;
using System.Collections.Generic;

namespace MonoDevelop.Ide.Gui
{
    public interface IDefaultWorkbenchWindow
    {
        void InitializeWorkspace();
        void InitializeLayout();
        bool Close();

        void LockActiveWindowChangeEvent();
        void UnlockActiveWindowChangeEvent();

        void ReorderTab(int oldPlacement, int newPlacement);
        void ShowView(ViewContent content, bool bringToFront, IViewDisplayBinding binding = null);
        void ShowView(ViewContent content, FileOpenInformation fileInfo, IViewDisplayBinding binding = null);
        void DeleteLayout(string name);

        void ShowPad(PadDefinition content);
        void AddPad(PadDefinition content);
        void ToggleFullViewMode();
        void BringToFront(PadDefinition content);
        void RemovePad(PadDefinition content);
        IPadWindow GetPadWindow(PadDefinition content);

        void ShowCommandBar(string barId);
        void HideCommandBar(string barId);

        List<ViewContent> ViewContentCollection { get; }
        bool Visible { get; set; }
        string CurrentLayout { get; set; }
        IWorkbenchWindow ActiveWorkbenchWindow { get; }
        List<PadDefinition> PadContentCollection { get; }
        bool FullScreen { get; set; }
        IList<string> Layouts { get; }
        IStatusBar StatusBar { get; }

        event EventHandler ActiveWorkbenchWindowChanged;
    }
}