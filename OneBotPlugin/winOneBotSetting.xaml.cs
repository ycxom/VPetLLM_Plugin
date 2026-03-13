using System.Windows;

namespace OneBotPlugin
{
    public partial class winOneBotSetting : Window
    {
        private readonly OneBotPlugin _plugin;
        private readonly OneBotSettings _settings;
        private readonly string _lang;
        private readonly Action<OneBotSettings> _onSave;

        public winOneBotSetting(OneBotPlugin plugin, OneBotSettings settings, string lang, Action<OneBotSettings> onSave)
        {
            InitializeComponent();
            _plugin = plugin;
            _settings = settings;
            _lang = lang;
            _onSave = onSave;

            ApplyLocalization();
            LoadSettingsToUI();
        }

        private void ApplyLocalization()
        {
            switch (_lang)
            {
                case "zh-hans":
                    Title = "OneBot 设置";
                    lblForwardWS.Text = "正向 WebSocket（客户端 → NapCat/Lagrange）";
                    lblReverseWS.Text = "反向 WebSocket（服务端 ← AstrBot/NoneBot）";
                    lblHttpApi.Text = "HTTP API";
                    lblAuth.Text = "认证";
                    lblFilter.Text = "消息过滤";
                    lblBehavior.Text = "行为";
                    chkForwardWS.Content = "启用";
                    chkReverseWS.Content = "启用";
                    chkHttpApi.Content = "启用";
                    chkAllowAllPrivate.Content = "允许所有私聊消息";
                    chkAllowAllGroup.Content = "允许所有群消息";
                    chkAtTriggerOnly.Content = "群聊：仅 @机器人 时响应";
                    chkReplyWithAt.Content = "群聊：回复时 @发送者";
                    lblAllowedUsers.Text = "用户白名单:";
                    lblAllowedGroups.Text = "群白名单:";
                    lblMasterQQ.Text = "主人 QQ:";
                    btnCancel.Content = "取消";
                    btnSave.Content = "保存";
                    break;
                case "zh-hant":
                    Title = "OneBot 設定";
                    lblForwardWS.Text = "正向 WebSocket（客戶端 → NapCat/Lagrange）";
                    lblReverseWS.Text = "反向 WebSocket（服務端 ← AstrBot/NoneBot）";
                    lblHttpApi.Text = "HTTP API";
                    lblAuth.Text = "認證";
                    lblFilter.Text = "訊息過濾";
                    lblBehavior.Text = "行為";
                    chkForwardWS.Content = "啟用";
                    chkReverseWS.Content = "啟用";
                    chkHttpApi.Content = "啟用";
                    chkAllowAllPrivate.Content = "允許所有私聊訊息";
                    chkAllowAllGroup.Content = "允許所有群訊息";
                    chkAtTriggerOnly.Content = "群聊：僅 @機器人 時回應";
                    chkReplyWithAt.Content = "群聊：回覆時 @傳送者";
                    lblAllowedUsers.Text = "用戶白名單:";
                    lblAllowedGroups.Text = "群白名單:";
                    lblMasterQQ.Text = "主人 QQ:";
                    btnCancel.Content = "取消";
                    btnSave.Content = "儲存";
                    break;
                case "ja":
                    Title = "OneBot 設定";
                    lblForwardWS.Text = "フォワードWebSocket（クライアント → NapCat/Lagrange）";
                    lblReverseWS.Text = "リバースWebSocket（サーバー ← AstrBot/NoneBot）";
                    lblHttpApi.Text = "HTTP API";
                    lblAuth.Text = "認証";
                    lblFilter.Text = "メッセージフィルター";
                    lblBehavior.Text = "動作";
                    chkForwardWS.Content = "有効";
                    chkReverseWS.Content = "有効";
                    chkHttpApi.Content = "有効";
                    chkAllowAllPrivate.Content = "すべてのプライベートメッセージを許可";
                    chkAllowAllGroup.Content = "すべてのグループメッセージを許可";
                    chkAtTriggerOnly.Content = "グループ：@ボット時のみ応答";
                    chkReplyWithAt.Content = "グループ：返信時に@送信者";
                    lblAllowedUsers.Text = "ユーザーホワイトリスト:";
                    lblAllowedGroups.Text = "グループホワイトリスト:";
                    lblMasterQQ.Text = "マスター QQ:";
                    btnCancel.Content = "キャンセル";
                    btnSave.Content = "保存";
                    break;
                default:
                    // English defaults from XAML
                    break;
            }
        }

        private void LoadSettingsToUI()
        {
            chkForwardWS.IsChecked = _settings.EnableForwardWS;
            txtForwardWSUrl.Text = _settings.ForwardWSUrl;
            chkReverseWS.IsChecked = _settings.EnableReverseWS;
            txtReverseWSHost.Text = _settings.ReverseWSHost;
            txtReverseWSPort.Text = _settings.ReverseWSPort.ToString();
            chkHttpApi.IsChecked = _settings.EnableHttpApi;
            txtHttpApiUrl.Text = _settings.HttpApiUrl;
            txtAccessToken.Text = _settings.AccessToken;
            txtBotQQ.Text = _settings.BotQQ;
            txtMasterQQ.Text = _settings.MasterQQ;
            chkAllowAllPrivate.IsChecked = _settings.AllowAllPrivate;
            chkAllowAllGroup.IsChecked = _settings.AllowAllGroup;
            txtAllowedUsers.Text = string.Join(",", _settings.AllowedUsers);
            txtAllowedGroups.Text = string.Join(",", _settings.AllowedGroups);
            chkAtTriggerOnly.IsChecked = _settings.AtTriggerOnly;
            chkReplyWithAt.IsChecked = _settings.ReplyWithAt;
        }

        private OneBotSettings CollectSettingsFromUI()
        {
            var s = new OneBotSettings
            {
                EnableForwardWS = chkForwardWS.IsChecked == true,
                ForwardWSUrl = txtForwardWSUrl.Text.Trim(),
                EnableReverseWS = chkReverseWS.IsChecked == true,
                ReverseWSHost = txtReverseWSHost.Text.Trim(),
                EnableHttpApi = chkHttpApi.IsChecked == true,
                HttpApiUrl = txtHttpApiUrl.Text.Trim(),
                AccessToken = txtAccessToken.Text.Trim(),
                BotQQ = txtBotQQ.Text.Trim(),
                MasterQQ = txtMasterQQ.Text.Trim(),
                AllowAllPrivate = chkAllowAllPrivate.IsChecked == true,
                AllowAllGroup = chkAllowAllGroup.IsChecked == true,
                AtTriggerOnly = chkAtTriggerOnly.IsChecked == true,
                ReplyWithAt = chkReplyWithAt.IsChecked == true,
                MaxMessageLength = _settings.MaxMessageLength,
                MessageQueueInterval = _settings.MessageQueueInterval,
            };

            if (int.TryParse(txtReverseWSPort.Text.Trim(), out var port))
                s.ReverseWSPort = port;

            s.AllowedUsers = ParseIdList(txtAllowedUsers.Text);
            s.AllowedGroups = ParseIdList(txtAllowedGroups.Text);

            return s;
        }

        private static HashSet<long> ParseIdList(string text)
        {
            var set = new HashSet<long>();
            if (string.IsNullOrWhiteSpace(text)) return set;
            foreach (var part in text.Split(',', ';', ' '))
            {
                if (long.TryParse(part.Trim(), out var id) && id > 0)
                    set.Add(id);
            }
            return set;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var newSettings = CollectSettingsFromUI();
            _onSave(newSettings);
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
