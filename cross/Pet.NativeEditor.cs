using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private NativeFileEditor fileEditor;

        internal void OpenFiles(int which)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(() => OpenFiles(which)); return; }
            if (fileEditor == null || fileEditor.Over)
            {
                var editor = new NativeFileEditor();
                fileEditor = editor;
                editor.Saved += () => mainframe?.InvalidateVisual();
                editor.Closed += (_, _) => { if (ReferenceEquals(fileEditor, editor)) fileEditor = null; };
            }
            fileEditor.Open(which);
        }

        private bool keepingFileDrafts;
        private async void FinishAfterFileDrafts(Action finish)
        {
            if (keepingFileDrafts) return;
            keepingFileDrafts = true;
            try
            {
                string error = fileEditor == null || fileEditor.Over ? null : await fileEditor.PrepareShutdownAsync();
                if (error != null) { Brain.Log("file-editor-draft", -1, error); return; }
                finish();
            }
            finally { keepingFileDrafts = false; }
        }
    }
}
