# 蓝牙音频接收器（BtAudioReceiver）

把 Windows 电脑变成一台"蓝牙耳机/音箱"：手机通过蓝牙连接电脑后，**音乐从电脑播放（A2DP）**，**来电可在电脑上接听/挂断/拨号（HFP）**，通话声音走电脑扬声器和麦克风——**无需依赖微软"手机连接"**。

本项目与微软"手机连接"使用同一套底层通话 API（`Windows.ApplicationModel.Calls`），把它做进了你自己的应用里。

---

## ⚠️ 重要：关于通话功能的兼容性风险（先读）

通话（HFP）控制依赖 `PhoneLineTransportDevice` API。有一个**已知的系统限制**必须提前说明：

- 从 **Windows 11 22H2（build 22621）** 开始，微软收紧了这套 API 的权限。有开发者反馈：第三方应用调用 `RegisterApp()` 可能抛 `UnauthorizedAccessException` 或静默失效，**即使 `RequestAccessAsync()` 返回 "Allowed"**——而微软自己的"手机连接"用同样的 API 却正常。这说明该 API 可能只对微软签名的应用完全放行。
- 因此，**通话功能能否在你的机器上工作，取决于你的 Windows 版本和微软的授权策略**，本应用无法绕过。

**这对不同版本意味着什么：**
| 你的系统 | 媒体播放(A2DP) | 通话控制(HFP) |
|---|---|---|
| Windows 10 (19041+) / Win11 21H2 | ✅ 可用 | ✅ 大概率可用 |
| Windows 11 22H2 及更新 | ✅ 可用 | ⚠️ 可能被系统拒绝（RegisterApp 失败） |

**如果通话在你机器上被系统拒绝**：媒体播放仍完全可用；通话请继续用微软"手机连接"（它有特殊授权）。本项目的通话代码在旧版本系统上验证过思路，但受限于微软策略，无法保证在所有 Win11 版本可用——这是 API 层面的限制，不是代码 bug。

先装上试：连接 HFP 后看日志，若出现 `UnauthorizedAccessException` 即命中此限制。

---

## 工作原理

| 功能 | 蓝牙协议 | 谁来实现 | 本应用做什么 |
|---|---|---|---|
| 手机音乐 → 电脑播放 | A2DP Sink | 系统驱动 + 本应用 | 调用 `AudioPlaybackConnection` 启用接收 |
| 来电控制（接听/挂断/拨号/来电显示） | HFP（控制） | **本应用** | 调用 `PhoneLineTransportDevice`/`PhoneLine` 注册并连接 HFP |
| 通话声音（扬声器 + 麦克风） | HFP（SCO 音频） | **蓝牙适配器的 HFP 音频驱动** | 不搬运音频，交给系统驱动 |

**关键点**：通话的"控制"由本应用完成，通话的"声音"由你蓝牙适配器的 HFP 音频驱动承载。只要你的适配器 + Windows 驱动支持 HFP（用微软"手机连接"能让手机显示"正在通话中"即证明支持），声音就能从电脑出。

> 为什么必须打包成 MSIX？因为 `Windows.ApplicationModel.Calls` 通话 API 需要"包身份（package identity）"。裸 exe 调用会直接崩溃。这也是微软"手机连接"和同类开源项目都做成打包应用的原因。

---

## 系统要求

- Windows 10 版本 2004（内部版本 19041）及以上，或 Windows 11（`Win+R` → `winver` 查看）
- 蓝牙适配器使用 **微软自带蓝牙协议栈** 且支持 **HFP**（免提）
  - 已知可用：Realtek RTL8761B/BU 系列（如华硕 USB-BT500）、多数 Intel 无线网卡内置蓝牙
  - 简单自测：用微软"手机连接"能让手机显示"通话中" → 说明 HFP 音频链路 OK
- 手机支持 A2DP + HFP（所有安卓/iPhone 均支持）

---

## 通过 GitHub 自动编译（推荐）

产物是 `.msix` 安装包（含证书和安装脚本），流程全自动：

1. 在 GitHub 新建仓库，把本项目**全部文件**（含隐藏的 `.github`、`packaging` 目录）推送上去。
2. 打开仓库 **Actions** 页面，推送后自动触发"Build MSIX"；也可点 **Run workflow** 手动触发。
3. 约 3 分钟后，在该次运行页面底部 **Artifacts** 下载 `BtAudioReceiver-msix`，解压得到三个文件：
   - `BtAudioReceiver.msix`（应用包）
   - `BtAudioReceiver.cer`（自签名证书公钥）
   - `Install.ps1`（一键安装脚本）
4. 打 `v1.0.0` 之类的 tag 并推送，会自动创建 Release 并附上以上文件。

CI 会自动生成一个临时自签名证书给包签名，你无需自己准备证书。

---

## 安装（在你自己的电脑上）

自签名包需要先信任证书。两种方式：

**方式一：一键脚本（简单）**
把三个文件放同一目录，右键 `Install.ps1` → "使用 PowerShell 运行"。脚本会自动请求管理员权限、信任证书、安装应用。装好后在开始菜单搜"蓝牙音频接收器"。

**方式二：手动**
1. 双击 `BtAudioReceiver.cer` → 安装证书 → 本地计算机 → 放入"受信任的根证书颁发机构"（再重复一次放入"受信任人"）。
2. 双击 `BtAudioReceiver.msix` → 安装。

> 首次安装需信任证书，是自签名应用的正常步骤。若走微软商店签名或购买正式代码签名证书，可省去这步——但对自用而言，信任一次即可。

---

## 使用步骤

### 第一步：配对（只需一次）
Windows 设置 → 蓝牙和其他设备 → 添加设备，与手机完成配对。

### 第二步：听音乐（A2DP）
1. 启动"蓝牙音频接收器"，选中手机，点 **启用媒体接收**。
2. 手机播放音乐，声音从电脑输出。程序保持运行即可（可最小化）。

### 第三步：打电话（HFP）
1. 选中手机，点 **连接通话(HFP)**。手机随即显示"已连接（免提）"。
   - 首次会弹系统授权请求，允许即可。
2. 来电时，程序窗口顶部弹出绿色 **接听** / 红色 **挂断** 按钮。
3. 主动拨号：在输入框填号码，点 **拨号**。
4. 通话声音走电脑扬声器和麦克风。

---

## 常见问题

- **点"连接通话"后手机没反应/没进入免提**
  确认适配器支持 HFP（用"手机连接"验证过）；先在系统蓝牙里点一下手机再重试；确认已允许授权请求。

- **能接听但没声音 / 声音只在手机**
  这是 HFP **音频驱动**层的问题，不是本应用能控制的。检查：Windows 声音设置里是否出现了"免提电话"音频设备；把它设为通话默认设备；必要时在设备管理器更新蓝牙驱动。

- **来电时窗口没弹接听按钮**
  确认已成功"连接通话(HFP)"并看到"发现电话线路"日志；部分手机需在蓝牙设置里把该电脑的"通话音频"开关打开。

- **安装报证书不受信任**
  用 `Install.ps1`，或手动把 `.cer` 同时导入"受信任根"和"受信任人"。

- **提示需要 Windows 2004 以上 / 程序闪退**
  通话 API 与 `AudioPlaybackConnection` 需要 19041+，请升级系统。

- **声音卡顿**
  蓝牙与 Wi-Fi 同在 2.4GHz，靠近电脑；USB 适配器远离 USB3.0 接口。

---

## 本地编译（进阶，需 Windows）

需要 .NET 8 SDK 和 Windows SDK（含 makeappx/signtool）。

```powershell
# 1) 发布
dotnet publish src/BtAudioReceiver.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish

# 2) 组装布局
New-Item -ItemType Directory -Force -Path msix
Copy-Item -Recurse -Force publish\* msix\
Copy-Item -Force packaging\AppxManifest.xml msix\AppxManifest.xml
New-Item -ItemType Directory -Force -Path msix\Assets
Copy-Item -Recurse -Force packaging\Assets\* msix\Assets\

# 3) 生成自签名证书（Subject 必须 = manifest 的 Publisher）
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=BtAudioReceiverDev" -KeyUsage DigitalSignature -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
$pwd = ConvertTo-SecureString -String "BtAudio!2026" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath cert.pfx -Password $pwd

# 4) 打包 + 签名（makeappx/signtool 在 Windows Kits\10\bin\<版本>\x64 下）
makeappx pack /d msix /p BtAudioReceiver.msix /o
signtool sign /fd SHA256 /a /f cert.pfx /p "BtAudio!2026" BtAudioReceiver.msix
```

---

## 自定义

- **改证书 Subject**：同时改 `packaging/AppxManifest.xml` 的 `Identity.Publisher` 和工作流里的 `PUBLISHER`，两者必须逐字符一致。
- **改应用名/图标**：编辑 `AppxManifest.xml` 的 DisplayName，替换 `packaging/Assets/` 下的图标。

---

## 项目结构

```
├── .github/workflows/build.yml    # GitHub Actions：发布→打包→签名 MSIX
├── packaging/
│   ├── AppxManifest.xml           # MSIX 清单（包身份 + phoneCall/蓝牙能力）
│   └── Assets/                    # 应用图标
├── src/
│   ├── BtAudioReceiver.csproj     # .NET 8 + WinForms（自包含）
│   ├── Program.cs                 # 入口
│   ├── MainForm.cs                # 界面：媒体 + 通话面板
│   └── HfpCallManager.cs          # HFP 通话逻辑（注册/连接/接听/拨号）
├── Install.ps1                    # 用户一键安装脚本
└── README.md
```

## 许可证
MIT
