using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;

namespace GUI.Types.Viewers
{
    class NavSpaceView(VrfGuiContext guiContext) : IViewer, IDisposable
    {
        private readonly NavSpaceFile navSpaceFile = new();
        private GLNavSpaceViewer? glViewer;

        public static bool IsAccepted(uint magic)
        {
            return magic == NavSpaceFile.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            if (stream != null)
            {
                navSpaceFile.Read(stream);
            }
            else
            {
                navSpaceFile.Read(guiContext.FileName);
            }

            RendererContext? rendererContext = null;

            try
            {
                rendererContext = guiContext.CreateRendererContext();

                glViewer = new GLNavSpaceViewer(guiContext, rendererContext, navSpaceFile);
                glViewer.InitializeLoad();
                rendererContext = null;
            }
            finally
            {
                rendererContext?.Dispose();
            }
        }

        public Control Create()
        {
            var tabControl = ViewerContentPresenter.CreateTabControl();

            tabControl.Items.Add(new TabItem { Header = "NAV SPACE", Content = glViewer!.InitializeUiControls() });
            tabControl.Items.Add(new TabItem { Header = "NAV SPACE INFO", Content = new DeferredContent(() => CodeTextBox.Create(navSpaceFile.ToString(), HighlightLanguage.None)) });

            tabControl.SelectedIndex = 0;
            return tabControl;
        }

        public void NotifyVisible() => glViewer?.NotifyVisible();

        public void Dispose()
        {
            glViewer?.Dispose();
        }
    }
}
