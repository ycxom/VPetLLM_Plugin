using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace OneBotPlugin
{
    public partial class winOneBotSetting : Window
    {
        private readonly OneBotPlugin _plugin;
        private readonly OneBotSettings _settings;
        private readonly string _lang;
        private readonly Action<OneBotSettings> _onSave;
        private ObservableCollection<OneBotNodeSetting> _nodes = new();
        private OneBotNodeSetting? _selectedNode;
        private bool _isUpdatingUI = false;

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
                    lblNodeManagement.Text = "节点管理";
                    btnAddNode.Content = "添加节点";
                    btnRemoveNode.Content = "删除选中节点";
                    lblNodeConfig.Text = "节点配置";
                    lblAuth.Text = "认证与权限";
                    lblFilter.Text = "消息过滤";
                    lblBehavior.Text = "行为";
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
                    lblNodeManagement.Text = "節點管理";
                    btnAddNode.Content = "添加節點";
                    btnRemoveNode.Content = "刪除選中節點";
                    lblNodeConfig.Text = "節點配置";
                    lblAuth.Text = "認證與權限";
                    lblFilter.Text = "訊息過濾";
                    lblBehavior.Text = "行為";
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
                    lblNodeManagement.Text = "ノード管理";
                    btnAddNode.Content = "ノード追加";
                    btnRemoveNode.Content = "選択したノードを削除";
                    lblNodeConfig.Text = "ノード設定";
                    lblAuth.Text = "認証と権限";
                    lblFilter.Text = "メッセージフィルター";
                    lblBehavior.Text = "動作";
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
                    break;
            }
        }

        private void LoadSettingsToUI()
        {
            _nodes.Clear();
            foreach (var node in _settings.Nodes)
            {
                _nodes.Add(node);
            }
            ListView_Nodes.ItemsSource = _nodes;

            txtBotQQ.Text = _settings.BotQQ;
            txtMasterQQ.Text = _settings.MasterQQ;
            chkAllowAllPrivate.IsChecked = _settings.AllowAllPrivate;
            chkAllowAllGroup.IsChecked = _settings.AllowAllGroup;
            txtAllowedUsers.Text = string.Join(",", _settings.AllowedUsers);
            txtAllowedGroups.Text = string.Join(",", _settings.AllowedGroups);
            chkAtTriggerOnly.IsChecked = _settings.AtTriggerOnly;
            chkReplyWithAt.IsChecked = _settings.ReplyWithAt;

            if (_nodes.Count > 0)
            {
                ListView_Nodes.SelectedIndex = 0;
            }
        }

        private OneBotSettings CollectSettingsFromUI()
        {
            var s = new OneBotSettings
            {
                Nodes = new List<OneBotNodeSetting>(_nodes),
                AllowAllPrivate = chkAllowAllPrivate.IsChecked == true,
                AllowAllGroup = chkAllowAllGroup.IsChecked == true,
                AtTriggerOnly = chkAtTriggerOnly.IsChecked == true,
                ReplyWithAt = chkReplyWithAt.IsChecked == true,
                MaxMessageLength = _settings.MaxMessageLength,
                MessageQueueInterval = _settings.MessageQueueInterval,
            };

            s.BotQQ = txtBotQQ.Text.Trim();
            s.MasterQQ = txtMasterQQ.Text.Trim();
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

        private void BtnAddNode_Click(object sender, RoutedEventArgs e)
        {
            var newNode = new OneBotNodeSetting
            {
                Name = $"新节点{_nodes.Count + 1}",
                Type = OneBotNodeType.ForwardWS,
                Url = "ws://127.0.0.1:3001",
                Enabled = true
            };
            _nodes.Add(newNode);
            ListView_Nodes.SelectedItem = newNode;
        }

        private void BtnRemoveNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode != null)
            {
                var index = _nodes.IndexOf(_selectedNode);
                _nodes.Remove(_selectedNode);
                if (_nodes.Count > 0)
                {
                    ListView_Nodes.SelectedIndex = Math.Min(index, _nodes.Count - 1);
                }
            }
        }

        private void ListView_Nodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedNode = ListView_Nodes.SelectedItem as OneBotNodeSetting;
            LoadSelectedNodeToUI();
        }

        private void LoadSelectedNodeToUI()
        {
            _isUpdatingUI = true;

            if (_selectedNode == null)
            {
                Panel_NodeDetails.IsEnabled = false;
                txtNodeName.Text = "";
                txtUrl.Text = "";
                txtHost.Text = "";
                txtPort.Text = "";
                txtAccessToken.Text = "";
                cmbNodeType.SelectedIndex = -1;
                _isUpdatingUI = false;
                return;
            }

            Panel_NodeDetails.IsEnabled = true;
            txtNodeName.Text = _selectedNode.Name;
            txtAccessToken.Text = _selectedNode.AccessToken ?? "";

            foreach (ComboBoxItem item in cmbNodeType.Items)
            {
                if (item.Tag as string == _selectedNode.Type.ToString())
                {
                    cmbNodeType.SelectedItem = item;
                    break;
                }
            }
            UpdateNodeTypeUI(_selectedNode.Type);

            if (_selectedNode.Type == OneBotNodeType.ReverseWS)
            {
                txtHost.Text = _selectedNode.Host ?? "127.0.0.1";
                txtPort.Text = _selectedNode.Port?.ToString() ?? "8080";
            }
            else
            {
                txtUrl.Text = _selectedNode.Url ?? (_selectedNode.Type == OneBotNodeType.ForwardWS ? "ws://127.0.0.1:3001" : "http://127.0.0.1:3000");
            }

            _isUpdatingUI = false;
        }

        private void UpdateNodeTypeUI(OneBotNodeType type)
        {
            switch (type)
            {
                case OneBotNodeType.ForwardWS:
                case OneBotNodeType.HttpApi:
                    lblUrl.Visibility = Visibility.Visible;
                    txtUrl.Visibility = Visibility.Visible;
                    lblHost.Visibility = Visibility.Collapsed;
                    Grid_ReverseWS.Visibility = Visibility.Collapsed;
                    break;
                case OneBotNodeType.ReverseWS:
                    lblUrl.Visibility = Visibility.Collapsed;
                    txtUrl.Visibility = Visibility.Collapsed;
                    lblHost.Visibility = Visibility.Visible;
                    Grid_ReverseWS.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void CmbNodeType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI || _selectedNode == null) return;

            var selectedItem = cmbNodeType.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is string tag)
            {
                var newType = Enum.Parse<OneBotNodeType>(tag);
                _selectedNode.Type = newType;
                UpdateNodeTypeUI(newType);

                if (newType == OneBotNodeType.ForwardWS && string.IsNullOrWhiteSpace(_selectedNode.Url))
                    _selectedNode.Url = "ws://127.0.0.1:3001";
                else if (newType == OneBotNodeType.HttpApi && string.IsNullOrWhiteSpace(_selectedNode.Url))
                    _selectedNode.Url = "http://127.0.0.1:3000";
                else if (newType == OneBotNodeType.ReverseWS)
                {
                    _selectedNode.Host = _selectedNode.Host ?? "127.0.0.1";
                    _selectedNode.Port = _selectedNode.Port ?? 8080;
                }

                ListView_Nodes.Items.Refresh();
            }
        }

        private void TxtNodeName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI || _selectedNode == null) return;
            _selectedNode.Name = txtNodeName.Text.Trim();
            ListView_Nodes.Items.Refresh();
        }

        private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI || _selectedNode == null) return;
            _selectedNode.Url = txtUrl.Text.Trim();
            ListView_Nodes.Items.Refresh();
        }

        private void TxtHost_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI || _selectedNode == null) return;
            _selectedNode.Host = txtHost.Text.Trim();
            ListView_Nodes.Items.Refresh();
        }

        private void TxtPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI || _selectedNode == null) return;
            if (int.TryParse(txtPort.Text.Trim(), out var port))
            {
                _selectedNode.Port = port;
                ListView_Nodes.Items.Refresh();
            }
        }

        private void TxtAccessToken_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI || _selectedNode == null) return;
            _selectedNode.AccessToken = txtAccessToken.Text.Trim();
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
