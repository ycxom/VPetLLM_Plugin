using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MinecraftVersionPlugin
{
    public partial class winMinecraftSetting : Window
    {
        private readonly MinecraftVersionPlugin _plugin;
        private readonly ObservableCollection<MinecraftVersionPlugin.ServerConfig> _servers = new();

        public winMinecraftSetting(MinecraftVersionPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;

            // 初始化常规设置
            TxtDelay.Text = _plugin.GetJitterDelay().ToString();
            
            ChkLocalSLP.IsChecked = _plugin.GetEnableLocalStatusQuery();

            // 初始化服务器列表
            var serversFromPlugin = _plugin.GetServers();
            if (serversFromPlugin is not null)
            {
                foreach (var s in serversFromPlugin)
                    _servers.Add(new MinecraftVersionPlugin.ServerConfig { Enabled = s.Enabled, Address = s.Address ?? "", Alias = s.Alias ?? "", Product = string.IsNullOrWhiteSpace(s.Product) ? "java" : s.Product });
            }
            GridServers.ItemsSource = _servers;

            // 默认选中第一项
            if (GridServers.Items.Count > 0)
                GridServers.SelectedIndex = 0;

            RefreshUIForSelectedServer();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtDelay.Text, out int ms) || ms <= 0)
            {
                MessageBox.Show("监听间隔必须为正整数。", "输入无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _plugin.SetJitterDelay(ms);

            
            _plugin.SetEnableLocalStatusQuery(ChkLocalSLP.IsChecked == true);

            // 保存服务器列表
            var list = _servers.Select(s => new MinecraftVersionPlugin.ServerConfig
            {
                Enabled = s.Enabled,
                Address = s.Address?.Trim() ?? "",
                Alias = s.Alias?.Trim() ?? "",
                Product = string.IsNullOrWhiteSpace(s.Product) ? "java" : s.Product
            }).ToList();
            _plugin.SetServers(list);

            RefreshUIForSelectedServer();
            MessageBox.Show("已保存设置。", "Minecraft Version Plugin", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshUIForSelectedServer();
        }

        private async void BtnTestSelected_Click(object sender, RoutedEventArgs e)
        {
            var item = GridServers.SelectedItem as MinecraftVersionPlugin.ServerConfig;
            if (item is null)
            {
                MessageBox.Show("请先在服务器列表中选择一条记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var address = (item.Address ?? "").Trim();
            if (string.IsNullOrEmpty(address))
            {
                MessageBox.Show("所选服务器地址为空。请填写 host 或 host:port。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 开启测试期间事件发送抑制
            _plugin.SetSuppressSend(true);
            SetLocalPreview("测试中...", "-", "-");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                (bool online, string version, string description) res;

                ParseHostPort(address, out _, out var port);
                var product = (item.Product ?? "java").ToLowerInvariant();
                bool isBedrock = product == "bedrock" || product == "be" || port == 19132;

                if (isBedrock)
                {
                    res = await QueryBedrockStatusAsync(address, cts.Token);
                }
                else
                {
                    res = await QueryJavaStatusAsync(address, cts.Token);
                }

                SetLocalPreview(res.online ? "在线" : "离线/未知", res.version ?? "-", res.description ?? "-");
                if (!string.IsNullOrWhiteSpace(res.description))
                    TxtDetectedName.Text = res.description.Trim();
            }
            catch (Exception ex)
            {
                SetLocalPreview("错误", "-", ex.Message);
            }
            finally
            {
                // 关闭抑制，恢复正常发送
                _plugin.SetSuppressSend(false);
            }
        }

        private void BtnAddServer_Click(object sender, RoutedEventArgs e)
        {
            _servers.Add(new MinecraftVersionPlugin.ServerConfig
            {
                Enabled = true,
                Address = "127.0.0.1:25565",
                Alias = "",
                Product = "java"
            });
        }

        private void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
        {
            var item = GridServers.SelectedItem as MinecraftVersionPlugin.ServerConfig;
            if (item is null)
            {
                MessageBox.Show("请先选择要删除的服务器。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _servers.Remove(item);
        }

        private void BtnUseDetectedAsAlias_Click(object sender, RoutedEventArgs e)
        {
            var item = GridServers.SelectedItem as MinecraftVersionPlugin.ServerConfig;
            var detected = TxtDetectedName.Text?.Trim() ?? "";
            if (item is null)
            {
                MessageBox.Show("请先选择要设置别名的服务器。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(detected))
            {
                MessageBox.Show("没有可用的检测名。请先测试所选服务器。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            item.Alias = detected;
            GridServers.Items.Refresh(); // 刷新UI以显示新别名
            MessageBox.Show("已将检测名设为所选服务器的别名。别忘了点击保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void GridServers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshUIForSelectedServer();
        }

        private void RefreshUIForSelectedServer()
        {
            var item = GridServers.SelectedItem as MinecraftVersionPlugin.ServerConfig;
            if (item is null)
            {
                // 清理预览区
                SetLocalPreview("-", "-", "-");
                TxtDetectedName.Text = "";
                LstPlayers.ItemsSource = null;
                return;
            }

            // 刷新玩家列表
            var players = _plugin.GetOnlinePlayersForServer(item.Address);
            LstPlayers.ItemsSource = null;
            LstPlayers.ItemsSource = players;
            LstPlayers.Items.Refresh();

            // 清空状态预览，提示用户测试
            SetLocalPreview("待测试", "-", "-");
            TxtDetectedName.Text = "";
        }

        private void SetLocalPreview(string online, string version, string desc)
        {
            TxtLocalOnline.Text = online;
            TxtLocalVersion.Text = version;
            TxtLocalDesc.Text = desc;
        }

        #region SLP Test Logic
        private async Task<(bool online, string version, string description)> QueryBedrockStatusAsync(string serverAddress, CancellationToken token)
        {
            ParseHostPort(serverAddress, out var host, out var port);
            if (port == 25565) port = 19132; // 如果是默认Java端口，则切换为默认BE端口

            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 4000;
            udp.Client.SendTimeout = 4000;

            var magic = new byte[] { 0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE, 0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78 };
            var guid = BitConverter.GetBytes((long)DateTime.UtcNow.Ticks);
            if (BitConverter.IsLittleEndian) Array.Reverse(guid);
            var ts = BitConverter.GetBytes((long)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (BitConverter.IsLittleEndian) Array.Reverse(ts);

            using var ms = new MemoryStream();
            ms.WriteByte(0x01);
            ms.Write(ts, 0, ts.Length);
            ms.Write(magic, 0, magic.Length);
            ms.Write(guid, 0, guid.Length);
            var packet = ms.ToArray();

            await udp.SendAsync(packet, packet.Length, host, port);

            var receiveTask = udp.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(4000, token));
            if (completed != receiveTask) throw new IOException("Bedrock status timeout");
            var result = receiveTask.Result;

            var buf = result.Buffer ?? Array.Empty<byte>();
            var text = Encoding.UTF8.GetString(buf);

            int idx = text.IndexOf("MCPE;", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (false, "", "无法解析响应");

            var payload = text.Substring(idx);
            var parts = payload.Split(';');
            if (parts.Length < 6) return (true, "", payload);

            return (true, parts[3], parts[1]);
        }

        private async Task<(bool online, string version, string description)> QueryJavaStatusAsync(string serverAddress, CancellationToken token)
        {
            ParseHostPort(serverAddress, out var host, out var port);
            using var client = new TcpClient();
            client.ReceiveTimeout = 4000;
            client.SendTimeout = 4000;
            await client.ConnectAsync(host, port);
            using var stream = client.GetStream();

            var hsPayload = BuildHandshakePayload(host, port);
            var handshake = PrependVarInt(hsPayload);
            await stream.WriteAsync(handshake, 0, handshake.Length, token);
            await stream.WriteAsync(new byte[] { 0x01, 0x00 }, 0, 2, token);

            var json = await ReadStatusJsonAsync(stream, token);
            dynamic jo = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            string versionName = "";
            string descText = "";
            try
            {
                versionName = (string?)jo.version?.name ?? "";
                if (jo.description is not null)
                {
                    if (jo.description is string s) descText = s;
                    else if (jo.description.text is not null) descText = (string)jo.description.text;
                    else descText = jo.description.ToString();
                }
            }
            catch { }
            return (true, versionName ?? "", descText ?? "");
        }
        private static string RemoveColorAndFormatting(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return System.Text.RegularExpressions.Regex.Replace(s, "§.", "");
        }
        private static void ParseHostPort(string addr, out string host, out int port)
        {
            addr = (addr ?? "").Trim();
            if (string.IsNullOrEmpty(addr)) { host = "127.0.0.1"; port = 25565; return; }
            var parts = addr.Split(':');
            host = parts[0];
            port = 25565;
            if (parts.Length > 1 && int.TryParse(parts[1], out var p) && p > 0 && p <= 65535) port = p;
        }
        private static byte[] BuildHandshakePayload(string host, int port)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x00); WriteVarInt(ms, 759); WriteString(ms, host);
            ms.WriteByte((byte)((port >> 8) & 0xFF)); ms.WriteByte((byte)(port & 0xFF));
            WriteVarInt(ms, 1); return ms.ToArray();
        }
        private static byte[] PrependVarInt(byte[] payload)
        {
            using var ms = new MemoryStream();
            WriteVarInt(ms, payload.Length); ms.Write(payload, 0, payload.Length);
            return ms.ToArray();
        }
        private static void WriteVarInt(Stream s, int value)
        {
            uint u = (uint)value;
            do {
                byte temp = (byte)(u & 0b0111_1111); u >>= 7;
                if (u != 0) temp |= 0b1000_0000;
                s.WriteByte(temp);
            } while (u != 0);
        }
        private static void WriteString(Stream s, string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? "");
            WriteVarInt(s, data.Length); s.Write(data, 0, data.Length);
        }
        private static async Task<string> ReadStatusJsonAsync(NetworkStream stream, CancellationToken token)
        {
            int length = await ReadVarIntAsync(stream, token);
            if (length <= 1) return "{}"; // Packet Length must be > 1
            int packetId = await ReadVarIntAsync(stream, token);
            if (packetId != 0x00) return "{}";
            int jsonLen = await ReadVarIntAsync(stream, token);
            if (jsonLen <= 0) return "{}";
            var buf = new byte[jsonLen];
            int read = 0;
            while (read < jsonLen)
            {
                int r = await stream.ReadAsync(buf, read, jsonLen - read, token);
                if (r == 0) break; read += r;
            }
            return Encoding.UTF8.GetString(buf, 0, read);
        }
        private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken token)
        {
            int numRead = 0, result = 0; byte read;
            do {
                var b = new byte[1];
                int r = await stream.ReadAsync(b, 0, 1, token);
                if (r == 0) throw new IOException("Stream closed");
                read = b[0]; int value = (read & 0b0111_1111);
                result |= (value << (7 * numRead)); numRead++;
                if (numRead > 5) throw new IOException("VarInt too big");
            } while ((read & 0b1000_0000) != 0);
            return result;
        }
        #endregion
    }
}