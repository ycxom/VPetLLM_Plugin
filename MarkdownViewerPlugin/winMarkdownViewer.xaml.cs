using System.Windows;
using System.Windows.Documents;

namespace MarkdownViewerPlugin
{
    public partial class winMarkdownViewer : Window
    {
        private string _lastText = "";
        private string _titleSource = "Markdown Viewer";

        public winMarkdownViewer()
        {
            InitializeComponent();
        }

        public void SetTitleFromSource(string src)
        {
            _titleSource = src;
            this.Title = $"Markdown Viewer - {src}";
        }

        public void RenderMarkdown(string markdown)
        {
            _lastText = markdown ?? "";
            FlowDocument doc = MinimalMarkdownRenderer.Render(_lastText);
            DocViewer.Document = doc;
        }

        private void BtnCopyText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_lastText ?? "");
                MessageBox.Show("已复制文本到剪贴板。", "复制", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("复制失败。", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}