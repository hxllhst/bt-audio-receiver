using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BtAudioReceiver;

/// <summary>
/// 蓝牙音频接收器主界面。
///
/// 媒体音频 (A2DP Sink)：AudioPlaybackConnection，让手机音乐从电脑播放。
/// 通话控制 (HFP)：HfpCallManager，注册并连接 HFP，让手机进入通话状态，
///                 并在电脑上接听/挂断/拨号。通话声音由蓝牙 HFP 音频驱动承载。
/// </summary>
public sealed class MainForm : Form
{
    // ===== A2DP 部分 =====
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        Dock = DockStyle.Fill,
    };
    private readonly Button _btnRefresh = new() { Text = "刷新设备", AutoSize = true };
    private readonly Button _btnEnable = new() { Text = "启用媒体接收", AutoSize = true };
    private readonly Button _btnConnect = new() { Text = "立即连接媒体", AutoSize = true };
    private readonly Button _btnDisconnect = new() { Text = "断开媒体", AutoSize = true };
    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new();

    // ===== HFP 通话部分 =====
    private readonly HfpCallManager _hfp = new();
    private readonly Button _btnHfpConnect = new() { Text = "连接通话(HFP)", AutoSize = true };
    private readonly Button _btnHfpDisconnect = new() { Text = "断开通话", AutoSize = true };
    private readonly TextBox _dialNumber = new() { Width = 160, PlaceholderText = "输入号码拨打" };
    private readonly Button _btnDial = new() { Text = "拨号", AutoSize = true };

    // 来电面板
    private readonly Panel _incomingPanel = new()
    {
        Dock = DockStyle.Top,
        Height = 56,
        BackColor = System.Drawing.Color.FromArgb(230, 245, 230),
        Visible = false,
        Padding = new Padding(8),
    };
    private readonly Label _incomingLabel = new()
    {
        Text = "来电",
        AutoSize = true,
        Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold),
        Location = new System.Drawing.Point(10, 16),
    };
    private readonly Button _btnAnswer = new()
    {
        Text = "接听",
        Width = 90,
        Height = 36,
        BackColor = System.Drawing.Color.FromArgb(76, 175, 80),
        ForeColor = System.Drawing.Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Button _btnHangup = new()
    {
        Text = "挂断",
        Width = 90,
        Height = 36,
        BackColor = System.Drawing.Color.FromArgb(244, 67, 54),
        ForeColor = System.Drawing.Color.White,
        FlatStyle = FlatStyle.Flat,
    };

    private PhoneCall? _currentCall;

    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        WordWrap = true,
    };

    public MainForm()
    {
        Text = "蓝牙音频接收器 — 手机媒体播放 + 通话 (A2DP + HFP)";
        Width = 900;
        Height = 660;
        StartPosition = FormStartPosition.CenterScreen;

        _list.Columns.Add("设备名称", 240);
        _list.Columns.Add("媒体状态", 150);
        _list.Columns.Add("设备 Id", 360);

        // 媒体按钮行
        var mediaButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0) };
        mediaButtons.Controls.AddRange(new Control[] { _btnRefresh, _btnEnable, _btnConnect, _btnDisconnect });

        // 通话按钮行
        var callButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 2, 6, 6) };
        callButtons.Controls.AddRange(new Control[]
        {
            _btnHfpConnect, _btnHfpDisconnect,
            new Label { Text = "  |  ", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _dialNumber, _btnDial,
        });

        // 来电面板
        _btnAnswer.Location = new System.Drawing.Point(300, 8);
        _btnHangup.Location = new System.Drawing.Point(400, 8);
        _incomingPanel.Controls.Add(_incomingLabel);
        _incomingPanel.Controls.Add(_btnAnswer);
        _incomingPanel.Controls.Add(_btnHangup);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 240,
        };
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_log);

        // 组装（注意添加顺序：后添加的 Top 停靠在更上方）
        Controls.Add(split);
        Controls.Add(callButtons);
        Controls.Add(mediaButtons);
        Controls.Add(_incomingPanel);

        // 事件绑定
        _btnRefresh.Click += async (_, _) => await RefreshDevicesAsync();
        _btnEnable.Click += async (_, _) => await WithSelectedAsync(EnableMediaAsync);
        _btnConnect.Click += async (_, _) => await WithSelectedAsync(ConnectMediaAsync);
        _btnDisconnect.Click += (_, _) => WithSelected(DisconnectMedia);

        _btnHfpConnect.Click += async (_, _) => await WithSelectedAsync(ConnectHfpAsync);
        _btnHfpDisconnect.Click += (_, _) => WithSelected((id, name) => _hfp.DisconnectAndUnregister(id, name));
        _btnDial.Click += (_, _) => DialCurrent();

        _btnAnswer.Click += (_, _) => { if (_currentCall != null) _hfp.AnswerCall(_currentCall); };
        _btnHangup.Click += (_, _) => { if (_currentCall != null) _hfp.EndCall(_currentCall); HideIncoming(); };

        // HFP 事件
        _hfp.Event += (_, e) => Log(e.Message);
        _hfp.IncomingCall += (_, e) => ShowIncoming(e);
        _hfp.CallEnded += (_, _) => HideIncoming();

        Shown += async (_, _) =>
        {
            Log("=== 使用说明 ===");
            Log("前提：先在 Windows 设置里与手机完成蓝牙配对。");
            Log("");
            Log("【听音乐】选中手机 → 点\"启用媒体接收\" → 手机播放音乐即从电脑出声。");
            Log("【打电话】选中手机 → 点\"连接通话(HFP)\" → 手机会显示已连接（免提）。");
            Log("          来电时本窗口顶部弹出接听/挂断；也可输入号码点\"拨号\"。");
            Log("          通话声音走电脑扬声器和麦克风，由蓝牙适配器 HFP 驱动承载。");
            Log("");
            await RefreshDevicesAsync();
        };

        FormClosed += (_, _) => { DisposeAll(); _hfp.Dispose(); };
    }

    // ---------- 设备枚举（同时列出 A2DP 与 HFP 设备并集）----------

    private async Task RefreshDevicesAsync()
    {
        _btnRefresh.Enabled = false;
        try
        {
            _list.Items.Clear();
            var added = new HashSet<string>();

            // A2DP 可播放设备
            try
            {
                string a2dpSelector = AudioPlaybackConnection.GetDeviceSelector();
                DeviceInformationCollection a2dp = await DeviceInformation.FindAllAsync(a2dpSelector);
                foreach (DeviceInformation d in a2dp)
                {
                    if (added.Add(d.Id))
                        _list.Items.Add(new ListViewItem(new[] { d.Name, GetMediaStateText(d.Id), d.Id }) { Tag = d });
                }
                Log($"媒体(A2DP)可用设备：{a2dp.Count} 台。");
            }
            catch (Exception ex) { Log("枚举 A2DP 设备失败：" + ex.Message); }

            // HFP 通话设备（补充仅支持通话、未在上面列出的）
            List<HfpDevice> hfp = await _hfp.FindHfpDevicesAsync();
            foreach (HfpDevice h in hfp)
            {
                if (added.Add(h.Id))
                    _list.Items.Add(new ListViewItem(new[] { h.Name + "（仅通话）", "—", h.Id }));
            }
            Log($"通话(HFP)可用设备：{hfp.Count} 台。");

            if (_list.Items.Count == 0)
                Log("未找到设备：请先在 Windows\"设置 > 蓝牙和其他设备\"中与手机配对，然后点\"刷新设备\"。");
        }
        finally { _btnRefresh.Enabled = true; }
    }

    // ---------- A2DP 媒体 ----------

    private AudioPlaybackConnection? GetOrCreateConnection(string id, string name)
    {
        if (_connections.TryGetValue(id, out AudioPlaybackConnection? existing)) return existing;
        AudioPlaybackConnection? conn = AudioPlaybackConnection.TryCreateFromId(id);
        if (conn is null) { Log($"[{name}] 无法创建媒体音频连接（设备可能不支持 A2DP Sink）。"); return null; }
        conn.StateChanged += OnMediaStateChanged;
        _connections[id] = conn;
        return conn;
    }

    private async Task EnableMediaAsync(string id, string name)
    {
        AudioPlaybackConnection? conn = GetOrCreateConnection(id, name);
        if (conn is null) return;
        try { await conn.StartAsync(); Log($"[{name}] 媒体接收已启用，手机播放音乐即可出声。"); }
        catch (Exception ex) { Log($"[{name}] 启用媒体失败：{ex.Message}"); }
        UpdateMediaStates();
    }

    private async Task ConnectMediaAsync(string id, string name)
    {
        AudioPlaybackConnection? conn = GetOrCreateConnection(id, name);
        if (conn is null) return;
        try
        {
            await conn.StartAsync();
            Log($"[{name}] 正在连接媒体……");
            AudioPlaybackConnectionOpenResult r = await conn.OpenAsync();
            Log(r.Status switch
            {
                AudioPlaybackConnectionOpenResultStatus.Success => $"[{name}] 媒体连接成功！",
                AudioPlaybackConnectionOpenResultStatus.RequestTimedOut => $"[{name}] 媒体连接超时：确认手机蓝牙已开、在附近。",
                AudioPlaybackConnectionOpenResultStatus.DeniedBySystem => $"[{name}] 被系统拒绝：可能有其他程序占用。",
                _ => $"[{name}] 媒体连接失败：{r.Status}",
            });
        }
        catch (Exception ex) { Log($"[{name}] 媒体连接出错：{ex.Message}"); }
        UpdateMediaStates();
    }

    private void DisconnectMedia(string id, string name)
    {
        if (_connections.TryGetValue(id, out AudioPlaybackConnection? conn))
        {
            try { conn.StateChanged -= OnMediaStateChanged; conn.Dispose(); } catch { }
            _connections.Remove(id);
            Log($"[{name}] 已断开媒体接收。");
        }
        else Log($"[{name}] 媒体当前未启用。");
        UpdateMediaStates();
    }

    private void OnMediaStateChanged(AudioPlaybackConnection sender, object args)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            Log($"媒体连接状态：{(sender.State == AudioPlaybackConnectionState.Opened ? "已连接 ♪" : "已断开")}");
            UpdateMediaStates();
        }));
    }

    private string GetMediaStateText(string id)
    {
        if (_connections.TryGetValue(id, out AudioPlaybackConnection? conn))
            return conn.State == AudioPlaybackConnectionState.Opened ? "已连接 ♪" : "已启用，等待连接";
        return "未启用";
    }

    private void UpdateMediaStates()
    {
        foreach (ListViewItem item in _list.Items)
            if (item.Tag is DeviceInformation d)
                item.SubItems[1].Text = GetMediaStateText(d.Id);
    }

    // ---------- HFP 通话 ----------

    private async Task ConnectHfpAsync(string id, string name)
    {
        _btnHfpConnect.Enabled = false;
        try { await _hfp.RegisterAndConnectAsync(id, name); }
        finally { _btnHfpConnect.Enabled = true; }
    }

    private void DialCurrent()
    {
        string number = _dialNumber.Text.Trim();
        if (string.IsNullOrWhiteSpace(number)) { Log("请先输入要拨打的号码。"); return; }
        _hfp.Dial(number, number);
    }

    private void ShowIncoming(CallEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action<CallEventArgs>(ShowIncoming), e); return; }
        _currentCall = e.Call;
        _incomingLabel.Text = "📞 " + e.Message;
        _incomingPanel.Visible = true;
        Log(e.Message);
        // 尝试把窗口带到前台提醒用户
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideIncoming()
    {
        if (InvokeRequired) { BeginInvoke(new Action(HideIncoming)); return; }
        _incomingPanel.Visible = false;
        _currentCall = null;
    }

    // ---------- 通用辅助 ----------

    private (string id, string name)? GetSelected()
    {
        if (_list.SelectedItems.Count == 0) { Log("请先在列表中选择一台设备。"); return null; }
        ListViewItem item = _list.SelectedItems[0];
        string id = item.SubItems[2].Text;
        string name = item.SubItems[0].Text.Replace("（仅通话）", "");
        return (id, name);
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

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void DisposeAll()
    {
        foreach (AudioPlaybackConnection conn in _connections.Values)
        {
            try { conn.StateChanged -= OnMediaStateChanged; conn.Dispose(); } catch { }
        }
        _connections.Clear();
    }
}
