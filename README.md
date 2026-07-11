# 蓝牙音频接收器（BtAudioReceiver）

把 Windows 电脑变成一台"蓝牙耳机/音箱"：手机通过蓝牙连接电脑后，**媒体音乐从电脑播放（A2DP Sink）**，**通话音频经电脑输出/输入（HFP 免提）**。

## 工作原理（重要，先读这一段）

Windows 对这两种音频的支持方式不同，本项目据此分工：

| 功能 | 蓝牙协议 | 由谁实现 |
|---|---|---|
| 手机音乐 → 电脑播放 | A2DP Sink | **本程序**。Windows 10 2004+ 的驱动支持 A2DP Sink，但必须由应用程序调用 `Windows.Media.Audio.AudioPlaybackConnection` API 才能启用——这正是本 exe 做的事。 |
| 手机通话音频 → 电脑（含麦克风） | HFP 免提角色 | **Windows 自带蓝牙驱动**。HFP 是内核驱动层协议，第三方程序无法（也无需）自行实现。配对后启用"免提电话"服务即可，通话时手机把声音切到电脑。 |
| 在电脑上接听/拨打电话 | HFP 控制 | 微软**"手机连接"（Phone Link）**应用，Windows 自带。 |

本程序纯本地运行，不联网、不采集任何数据。

## 系统要求

- Windows 10 版本 2004（内部版本 19041）及以上，或 Windows 11（按 `Win+R` 输入 `winver` 查看）
- 蓝牙适配器使用 **Windows 自带微软蓝牙协议栈**（绝大多数内置/USB 适配器都是；老式 BlueSoleil、CSR Harmony 等第三方协议栈不支持）
- 手机支持 A2DP（所有安卓/iPhone 均支持）

## 通过 GitHub 自动编译 exe

1. 在 GitHub 新建一个仓库（Public 或 Private 均可）。
2. 把本项目**全部文件**（含 `.github` 隐藏目录）上传/推送到仓库。
3. 打开仓库的 **Actions** 标签页——推送后会自动触发"Build Windows EXE"工作流；也可点 **Run workflow** 手动触发。
4. 等待约 2 分钟构建完成，在该次运行页面底部的 **Artifacts** 中下载 `BtAudioReceiver-win-x64`，解压得到 `BtAudioReceiver.exe`。
5. （可选）给仓库打一个 `v1.0.0` 之类的 tag 并推送，会自动创建 Release 并附上 exe。

exe 为自包含单文件，目标机器**无需安装 .NET 运行库**，双击即用。

## 本地编译（可选）

安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 后：

```powershell
dotnet publish src/BtAudioReceiver.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

生成物在 `out\BtAudioReceiver.exe`。

## 使用步骤

### 第一步：配对（只需一次）

1. 电脑：设置 → 蓝牙和其他设备 → 添加设备，与手机完成配对。
2. 配对后建议检查：控制面板 → 设备和打印机 → 右键手机 → 属性 → "服务"选项卡，勾选 **免提电话（Handsfree Telephony）** 和 **音频接收器/音频源** 相关服务（不同系统版本名称略有差异）。

### 第二步：听音乐（A2DP，本程序负责）

1. 运行 `BtAudioReceiver.exe`，列表中会出现已配对的手机。
2. 选中手机，点 **启用接收**。
3. 在手机的蓝牙设备列表里点这台电脑的名字完成连接（或在本程序点 **立即连接** 由电脑主动发起），确认手机端"媒体音频"开关已打开。
4. 手机播放音乐，声音即从电脑的默认播放设备输出。程序需保持运行（可最小化）。

### 第三步：打电话（HFP，Windows 负责）

1. 确认第一步中"免提电话"服务已启用，且手机端该电脑条目下"通话音频/手机音频"开关已打开。
2. 手机来电或拨号时，在手机通话界面选择音频输出为"蓝牙/电脑名"，通话声音即经电脑扬声器和麦克风。
3. 想直接在电脑上接听、拨号、看短信：打开 Windows 自带的 **手机连接（Phone Link）** 应用并按提示绑定手机。

## 常见问题

- **列表里没有设备** → 还没配对，或手机不支持；先在系统设置里完成配对再点"刷新设备"。
- **提示 DeniedBySystem（被系统拒绝）** → 有其他程序（如微软商店的"Bluetooth Audio Receiver"）占用了连接，关闭后重试。
- **连接超时** → 手机蓝牙没开、距离太远，或手机正连着别的耳机；断开其他音频设备后重试。
- **音乐有声、通话没声** → 检查"免提电话"服务是否勾选；部分适配器驱动较老，可在设备管理器中更新蓝牙驱动。
- **声音卡顿** → 蓝牙 2.4GHz 与 Wi-Fi 同频段，尽量靠近电脑；USB 适配器可换到远离 USB3.0 设备的接口。
- **API 不存在 / 程序闪退** → Windows 版本低于 2004，请升级系统。

## 项目结构

```
├── .github/workflows/build.yml   # GitHub Actions 自动编译
├── src/
│   ├── BtAudioReceiver.csproj    # .NET 8 + Windows SDK 19041 项目
│   ├── Program.cs                # 入口
│   └── MainForm.cs               # 界面与 AudioPlaybackConnection 逻辑
└── README.md
```

## 许可证

MIT
