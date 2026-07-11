using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BtAudioReceiver;

/// <summary>
/// 蓝牙音频接收器主界面。
/// 通过 Windows.Media.Audio.AudioPlaybackConnection (Win10 2004+) 启用 A2DP Sink，
/// 让已配对的手机把媒体音频播放到这台电脑上。
/// 通话音频 (HFP) 由 Windows 自带蓝牙免提驱动处理，无需本程序参与。
/// </summary>
public sealed class MainForm : Form
{
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        Dock = DockStyle.Fill,
    };

    private readonly Button _btnRefresh = new() { Text = "刷新设备", AutoSize = true };
    private readonly Button _btnEnable = new() { Text = "启用接收(所选)", AutoSize = true };
    private readonly Button _btnEnableAll = new() { Text = "启用全部", AutoSize = true };
    private readonly Button _btnConnect = new() { Text = "立即连接(所选)", AutoSize = true };
    private readonly Button _btnDisconnect = new() { Text = "断开/停用(所选)", AutoSize = true };

    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        WordWrap = true,
    };

    // 设备 Id -> 连接对象
    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new();

    public MainForm()
    {
        Text = "蓝牙音频接收器 (A2DP Sink) — 手机音频播放到电脑";
        Width = 820;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        _list.Columns.Add("设备名称", 260);
        _list.Columns.Add("接收状态", 160);
        _list.Columns.Add("设备 Id", 340);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
        };
        buttons.Controls.AddRange(new Control[]
        {
            _btnRefresh, _btnEnable, _btnEnableAll, _btnConnect, _btnDisconnect,
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260,
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_log);

        Controls.Add(split);
        Controls.Add(buttons);

        _btnRefresh.Click += async (_, _) => await RefreshDevicesAsync();
        _btnEnable.Click += async (_, _) => await WithSelectedAsync(EnableAsync);
        _btnEnableAll.Click += async (_, _) => await EnableAllAsync();
        _btnConnect.Click += async (_, _) => await WithSelectedAsync(ConnectAsync);
        _btnDisconnect.Click += (_, _) => WithSelected(Disconnect);

        Shown += async (_, _) =>
        {
            Log("使用说明：");
            Log("1. 先在 Windows 设置中与手机完成蓝牙配对（本程序不负责配对）。");
            Log("2. 选中下方列表中的手机，点“启用接收”。");
            Log("3. 在手机的蓝牙菜单里点击这台电脑的名称，或直接播放音乐（也可点“立即连接”由电脑主动发起）。");
            Log("4. 通话音频(HFP)由 Windows 自带驱动处理：配对后确认已启用“免提电话”服务即可，详见 README。");
            Log("");
            await RefreshDevicesAsync();
        };

        FormClosed += (_, _) => DisposeAll();
    }

    // ---------- 设备枚举 ----------

    private async Task RefreshDevicesAsync()
    {
        _btnRefresh.Enabled = false;
        try
        {
            // 返回“已配对且支持向本机播放音频”的设备（即支持 A2DP Source 的手机等）
            string selector = AudioPlaybackConnection.GetDeviceSelector();
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);

            _list.Items.Clear();
            foreach (DeviceInformation d in devices)
            {
                var item = new ListViewItem(new[] { d.Name, GetStateText(d.Id), d.Id })
                {
                    Tag = d,
                };
                _list.Items.Add(item);
            }

            Log($"找到 {devices.Count} 台已配对且支持音频连接的设备。");
            if (devices.Count == 0)
            {
                Log("未找到设备：请先在 Windows“设置 > 蓝牙和其他设备”中与手机配对，然后点“刷新设备”。");
            }
        }
        catch (Exception ex)
        {
            Log("枚举设备失败：" + ex.Message);
        }
        finally
        {
            _btnRefresh.Enabled = true;
        }
    }

    // ---------- 连接管理 ----------

    private AudioPlaybackConnection? GetOrCreateConnection(string id, string name)
    {
        if (_connections.TryGetValue(id, out AudioPlaybackConnection? existing))
        {
            return existing;
        }

        AudioPlaybackConnection? conn = AudioPlaybackConnection.TryCreateFromId(id);
        if (conn is null)
        {
            Log($"[{name}] 无法创建音频连接对象（系统可能不支持或设备不可用）。");
            return null;
        }

        conn.StateChanged += OnStateChanged;
        _connections[id] = conn;
        return conn;
    }

    /// <summary>启用接收：之后手机端主动连接即可出声。</summary>
    private async Task EnableAsync(string id, string name)
    {
        AudioPlaybackConnection? conn = GetOrCreateConnection(id, name);
        if (conn is null) return;

        try
        {
            await conn.StartAsync();
            Log($"[{name}] 接收已启用。现在在手机蓝牙菜单里点这台电脑，或直接播放音乐即可。");
        }
        catch (Exception ex)
        {
            Log($"[{name}] 启用失败：{ex.Message}");
        }
        UpdateListStates();
    }

    private async Task EnableAllAsync()
    {
        foreach (ListViewItem item in _list.Items)
        {
            var d = (DeviceInformation)item.Tag!;
            await EnableAsync(d.Id, d.Name);
        }
    }

    /// <summary>由电脑主动向手机发起 A2DP 连接。</summary>
    private async Task ConnectAsync(string id, string name)
    {
        AudioPlaybackConnection? conn = GetOrCreateConnection(id, name);
        if (conn is null) return;

        try
        {
            await conn.StartAsync();
            Log($"[{name}] 正在连接……（请确保手机蓝牙已开启）");
            AudioPlaybackConnectionOpenResult result = await conn.OpenAsync();

            switch (result.Status)
            {
                case AudioPlaybackConnectionOpenResultStatus.Success:
                    Log($"[{name}] 连接成功！手机播放的媒体声音将从电脑输出。");
                    break;
                case AudioPlaybackConnectionOpenResultStatus.RequestTimedOut:
                    Log($"[{name}] 连接超时：请确认手机蓝牙已开启、在附近，且未连接其他音频设备。");
                    break;
                case AudioPlaybackConnectionOpenResultStatus.DeniedBySystem:
                    Log($"[{name}] 被系统拒绝：可能有其他程序占用了该连接，或系统策略限制。");
                    break;
                default:
                    Log($"[{name}] 连接失败：{result.Status}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"[{name}] 连接出错：{ex.Message}");
        }
        UpdateListStates();
    }

    private void Disconnect(string id, string name)
    {
        if (_connections.TryGetValue(id, out AudioPlaybackConnection? conn))
        {
            try
            {
                conn.StateChanged -= OnStateChanged;
                conn.Dispose();
            }
            catch { /* 忽略释放异常 */ }
            _connections.Remove(id);
            Log($"[{name}] 已断开并停用接收。");
        }
        else
        {
            Log($"[{name}] 当前未启用。");
        }
        UpdateListStates();
    }

    private void OnStateChanged(AudioPlaybackConnection sender, object args)
    {
        AudioPlaybackConnectionState state = sender.State;
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            Log($"连接状态变更：{(state == AudioPlaybackConnectionState.Opened ? "已连接 (Opened)" : "已断开 (Closed)")}");
            UpdateListStates();
        }));
    }

    // ---------- 界面辅助 ----------

    private string GetStateText(string id)
    {
        if (_connections.TryGetValue(id, out AudioPlaybackConnection? conn))
        {
            return conn.State == AudioPlaybackConnectionState.Opened ? "已连接 ♪" : "已启用，等待连接";
        }
        return "未启用";
    }

    private void UpdateListStates()
    {
        foreach (ListViewItem item in _list.Items)
        {
            var d = (DeviceInformation)item.Tag!;
            item.SubItems[1].Text = GetStateText(d.Id);
        }
    }

    private async Task WithSelectedAsync(Func<string, string, Task> action)
    {
        (string id, string name)? sel = GetSelected();
        if (sel is null) return;
        await action(sel.Value.id, sel.Value.name);
    }

    private void WithSelected(Action<string, string> action)
    {
        (string id, string name)? sel = GetSelected();
        if (sel is null) return;
        action(sel.Value.id, sel.Value.name);
    }

    private (string id, string name)? GetSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            Log("请先在列表中选择一台设备。");
            return null;
        }
        var d = (DeviceInformation)_list.SelectedItems[0].Tag!;
        return (d.Id, d.Name);
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), message);
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void DisposeAll()
    {
        foreach (AudioPlaybackConnection conn in _connections.Values)
        {
            try
            {
                conn.StateChanged -= OnStateChanged;
                conn.Dispose();
            }
            catch { /* 忽略 */ }
        }
        _connections.Clear();
    }
}
