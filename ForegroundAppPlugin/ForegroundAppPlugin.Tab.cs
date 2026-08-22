using System.Windows;

namespace ForegroundAppPlugin
{
    public partial class ForegroundAppPlugin
    {
        public string TabTitle => Lang.T(CurrentLanguage, "tab_title");

        public FrameworkElement CreatePanel() => new winForegroundAppSetting(this);
    }
}
