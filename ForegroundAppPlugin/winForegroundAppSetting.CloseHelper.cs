using System.Windows;

namespace ForegroundAppPlugin
{
    public partial class winForegroundAppSetting
    {
        // 仅当本面板是薄壳窗口的直接内容时关闭该窗口；内嵌设置 Tab 时不动作。
        private void CloseOwnerWindow()
        {
            var w = Window.GetWindow(this);
            if (w != null && ReferenceEquals(w.Content, this))
                w.Close();
        }
    }
}
