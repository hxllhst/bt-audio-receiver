using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace BtAudioReceiver;

/// <summary>一台支持 HFP 的已配对设备（手机）。</summary>
public sealed class HfpDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public override string ToString() => Name;
}

/// <summary>通话/日志事件参数。</summary>
public sealed class CallEventArgs : EventArgs
{
    public required string Message { get; init; }
    public PhoneCall? Call { get; init; }
}

/// <summary>
/// HFP 通话管理器（与微软"手机连接"同一套底层 API）。
///
/// 流程：
///   1. PhoneLineTransportDevice.FromId + RequestAccessAsync + RegisterApp
///   2. ConnectAsync 建立 HFP 服务级连接（手机随即显示"已连接免提"）
///   3. 监听 PhoneCallManager.CallStateChanged，扫描各 PhoneLine 的活动通话
///   4. 对来电 AcceptIncoming / End，主动拨号 PhoneLine.Dial
///
/// 只做通话"控制"；通话"声音"(SCO)由蓝牙适配器 HFP 音频驱动承载。
///
/// 关键 API 契约（已核对官方元数据）：
///   PhoneCall.CallId : string        （不是 Guid）
///   PhoneCall.Status : PhoneCallStatus
///   PhoneCall.AcceptIncoming() / End() / StatusChanged
///   PhoneCall.GetPhoneCallInfo() -> PhoneCallInfo { PhoneNumber, DisplayName, CallDirection }
///   PhoneLine.GetAllActivePhoneCalls() -> PhoneCallsResult { AllActivePhoneCalls }
/// </summary>
public sealed class HfpCallManager : IDisposable
{
    private PhoneCallStore? _store;
    private PhoneLineWatcher? _lineWatcher;

    private readonly Dictionary<string, PhoneLineTransportDevice> _transportDevices = new();
    private readonly Dictionary<Guid, PhoneLine> _lines = new();
    private readonly HashSet<string> _seenCallIds = new();

    public event EventHandler<CallEventArgs>? Event;
    public event EventHandler<CallEventArgs>? IncomingCall;
    public event EventHandler<CallEventArgs>? CallEnded;

    private void Raise(string message, PhoneCall? call = null)
        => Event?.Invoke(this, new CallEventArgs { Message = message, Call = call });

    // ---------- 设备枚举 ----------

    public async Task<List<HfpDevice>> FindHfpDevicesAsync()
    {
        var result = new List<HfpDevice>();
        try
        {
            string selector = PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth);
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);
            foreach (DeviceInformation d in devices)
                result.Add(new HfpDevice { Id = d.Id, Name = d.Name });
        }
        catch (Exception ex)
        {
            Raise("枚举 HFP 设备失败：" + ex.Message);
        }
        return result;
    }

    // ---------- 注册 + 连接 HFP ----------

    public async Task<bool> RegisterAndConnectAsync(string deviceId, string deviceName)
    {
        try
        {
            PhoneLineTransportDevice transportDevice = PhoneLineTransportDevice.FromId(deviceId);
            _transportDevices[deviceId] = transportDevice;

            DeviceAccessStatus access = await transportDevice.RequestAccessAsync();
            if (access != DeviceAccessStatus.Allowed)
            {
                Raise($"[{deviceName}] 访问被拒绝：{access}。请在系统隐私设置中允许应用访问通话记录。");
                return false;
            }

            if (!transportDevice.IsRegistered())
            {
                transportDevice.RegisterApp();
                Raise($"[{deviceName}] 已向系统注册通话服务。");
            }

            transportDevice.AudioRoutingStatusChanged += (s, _) =>
                Raise($"[{deviceName}] 通话音频路由：{s.AudioRoutingStatus}");

            await EnsureLineWatcherAsync();

            Raise($"[{deviceName}] 正在建立 HFP 连接……请确保手机蓝牙已开启并在附近。");
            bool connected = await transportDevice.ConnectAsync();
            Raise(connected
                ? $"[{deviceName}] HFP 已连接！手机应显示已连接（免提），来电可在电脑上接听。"
                : $"[{deviceName}] HFP 连接未成功：可先在系统蓝牙里点一下该手机，或稍后重试。");
            return connected;
        }
        catch (Exception ex)
        {
            Raise($"[{deviceName}] 注册/连接 HFP 出错：{ex.Message}");
            return false;
        }
    }

    public void DisconnectAndUnregister(string deviceId, string deviceName)
    {
        if (_transportDevices.TryGetValue(deviceId, out PhoneLineTransportDevice? dev))
        {
            try
            {
                if (dev.IsRegistered()) dev.UnregisterApp();
                Raise($"[{deviceName}] 已断开并注销 HFP。");
            }
            catch (Exception ex)
            {
                Raise($"[{deviceName}] 注销出错：{ex.Message}");
            }
            _transportDevices.Remove(deviceId);
        }
    }

    // ---------- 线路 & 通话监听 ----------

    private async Task EnsureLineWatcherAsync()
    {
        if (_lineWatcher != null) return;

        _store ??= await PhoneCallManager.RequestStoreAsync();
        PhoneCallManager.CallStateChanged += OnCallStateChanged;

        _lineWatcher = _store.RequestLineWatcher();
        _lineWatcher.LineAdded += async (_, args) =>
        {
            try
            {
                PhoneLine line = await PhoneLine.FromIdAsync(args.LineId);
                _lines[args.LineId] = line;
                Raise($"发现电话线路：{line.DisplayName}（可拨号：{line.CanDial}）");
                ScanActiveCalls();
            }
            catch (Exception ex)
            {
                Raise("处理新线路出错：" + ex.Message);
            }
        };
        _lineWatcher.LineRemoved += (_, args) => _lines.Remove(args.LineId);
        _lineWatcher.Start();
    }

    private void OnCallStateChanged(object? sender, object e) => ScanActiveCalls();

    /// <summary>遍历所有线路的活动通话，识别来电/结束并派发事件。</summary>
    private void ScanActiveCalls()
    {
        var stillActive = new HashSet<string>();

        foreach (PhoneLine line in _lines.Values)
        {
            PhoneCallsResult result;
            try
            {
                result = line.GetAllActivePhoneCalls();
            }
            catch (Exception ex)
            {
                Raise("读取活动通话失败：" + ex.Message);
                continue;
            }

            foreach (PhoneCall call in result.AllActivePhoneCalls)
            {
                string id = call.CallId ?? Guid.NewGuid().ToString();
                stillActive.Add(id);

                if (_seenCallIds.Add(id))
                {
                    call.StatusChanged += OnCallStatusChanged;

                    if (call.Status == PhoneCallStatus.Incoming)
                    {
                        IncomingCall?.Invoke(this, new CallEventArgs
                        {
                            Message = $"来电：{DescribeCall(call)}",
                            Call = call,
                        });
                    }
                    else
                    {
                        Raise($"通话：{DescribeCall(call)}（{TranslateStatus(call.Status)}）", call);
                    }
                }
            }
        }

        foreach (string id in new List<string>(_seenCallIds))
        {
            if (!stillActive.Contains(id))
            {
                _seenCallIds.Remove(id);
                CallEnded?.Invoke(this, new CallEventArgs { Message = "通话已结束。" });
            }
        }
    }

    private void OnCallStatusChanged(PhoneCall call, object args)
    {
        Raise($"通话状态：{TranslateStatus(call.Status)}（{DescribeCall(call)}）", call);
        if (call.Status == PhoneCallStatus.Ended)
            CallEnded?.Invoke(this, new CallEventArgs { Message = "通话已结束。", Call = call });
    }

    // ---------- 拨号 / 接听 / 挂断 ----------

    public PhoneLine? GetDialablePhoneLine()
    {
        foreach (PhoneLine line in _lines.Values)
            if (line.CanDial) return line;
        foreach (PhoneLine line in _lines.Values)
            return line;
        return null;
    }

    public bool Dial(string number, string displayName)
    {
        PhoneLine? line = GetDialablePhoneLine();
        if (line == null)
        {
            Raise("没有可用于拨号的线路。请先连接 HFP 并等待线路出现。");
            return false;
        }
        try
        {
            line.Dial(number, string.IsNullOrWhiteSpace(displayName) ? number : displayName);
            Raise($"正在拨号：{number}");
            return true;
        }
        catch (Exception ex)
        {
            Raise("拨号失败：" + ex.Message);
            return false;
        }
    }

    public void AnswerCall(PhoneCall call)
    {
        try
        {
            call.AcceptIncoming();
            Raise("已接听。", call);
        }
        catch (Exception ex)
        {
            Raise("接听失败：" + ex.Message, call);
        }
    }

    public void EndCall(PhoneCall call)
    {
        try
        {
            call.End();
            Raise("已挂断。", call);
        }
        catch (Exception ex)
        {
            Raise("挂断失败：" + ex.Message, call);
        }
    }

    // ---------- 辅助 ----------

    /// <summary>通过 GetPhoneCallInfo 取来电号码/显示名。</summary>
    private static string DescribeCall(PhoneCall call)
    {
        try
        {
            PhoneCallInfo info = call.GetPhoneCallInfo();
            string name = info.DisplayName;
            string number = info.PhoneNumber;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(number))
                return $"{name} {number}";
            if (!string.IsNullOrWhiteSpace(name)) return name;
            if (!string.IsNullOrWhiteSpace(number)) return number;
        }
        catch { /* 取不到就用占位 */ }
        return "未知号码";
    }

    private static string TranslateStatus(PhoneCallStatus status) => status switch
    {
        PhoneCallStatus.Incoming => "来电响铃",
        PhoneCallStatus.Talking => "通话中",
        PhoneCallStatus.Held => "已保持",
        PhoneCallStatus.Dialing => "拨号中",
        PhoneCallStatus.Ended => "已结束",
        _ => status.ToString(),
    };

    public void Dispose()
    {
        try { PhoneCallManager.CallStateChanged -= OnCallStateChanged; } catch { }
        try { _lineWatcher?.Stop(); } catch { }
        foreach (PhoneLineTransportDevice dev in _transportDevices.Values)
        {
            try { if (dev.IsRegistered()) dev.UnregisterApp(); } catch { }
        }
        _transportDevices.Clear();
        _lines.Clear();
    }
}
