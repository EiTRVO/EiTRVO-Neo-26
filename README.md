# EiTRVO Neo 26

基于 **EiTRVO ProEngine** 的 Minecraft: Java Edition 启动器，项目代码由 DeepSeek V4 Pro 生成。

> WPF / .NET 8 

---

## 核心特性

### 多账号认证
- **Microsoft 正版登录** — OAuth 2.0 设备代码流（Xbox Live → XSTS → Minecraft）
- **Yggdrasil 第三方认证** — 支持自定义验证服务器（authlib-injector 自动下载）
- **离线模式** — 玩家名验证（3-16 位字母/数字/下划线）
- 所有凭据使用 **Windows DPAPI** 加密存储（`DataProtectionScope.CurrentUser`）

### 实例管理
- 多版本隔离目录，互不干扰
- 支持五种 Mod 加载器：**Forge / NeoForge / Fabric / Quilt / OptiFine**
- Minecraft 版本 `inheritsFrom` 继承链自动解析
- 旧版支持（≤1.5.2 资源自动提取）
- 实例打包/导入（自有 `eitrvo-pack` 格式 + Modrinth MRPack 格式）

### SaveLock 存档加密
- **AES-256-CBC** + **PBKDF2-SHA256**（100,000 次迭代）
- 自定义 `.savenc` 格式：Magic Number → Salt → KeyCheck → Metadata → IV → 加密数据 → SHA-256
- 两种锁定模式：
  - **一次性（OneTime）** — 解密后不再重新加密
  - **永久（Permanent）** — 游戏退出后自动重新加密
- 密码提示
- OneDrive 密钥备份 + `.savkey` 导出/导入 + `.savrec` 恢复文件
- 启动流程中无缝集成解密/重加密

### EiTRVO Firewall 进程安全

五层纵深防护体系，从进程创建到运行时全程保护：

| 层级 | 机制 | 功能 |
|------|------|------|
| Layer 0 | `EXTENSION_POINT_DISABLE_ALWAYS_ON` | 禁用 Windows 进程扩展点，阻止第三方 DLL 注入 |
| Layer 1 | `AdjustTokenPrivileges` | 移除 9 项非必需特权（含 `SeDebugPrivilege` 纵深防御） |
| Layer 2 | Windows Job Object | `KILL_ON_JOB_CLOSE` + 50 进程上限 |
| Layer 3 | WMI `Win32_ProcessStartTrace` | 子进程黑名单实时监控（31 项危险进程） |
| Layer 4 | `FileSystemWatcher` × 3 | 游戏目录 / %TEMP% / 启动文件夹 — 检测可执行文件创建并自动删除 |
| Layer 5 | DLL 白名单 + TCP 连接轮询 | 每 2s 检测非白名单模块加载；每 5s 检测非标准端口连接 |

**Layer 0–2 原子化实施：** 使用 `CREATE_SUSPENDED` 创建游戏进程，在挂起态完成扩展点禁用 → 特权移除 → Job Object 绑定，恢复执行前全部防护已就位，消除 `Process.Start` → `HardenProcess` 之间的竞态窗口。

**Layer 3** 黑名单覆盖 31 项危险进程（`cmd.exe`、`powershell.exe`、`pwsh.exe`、`mshta.exe`、`rundll32.exe`、`reg.exe`、`curl.exe`、`certutil.exe`、`schtasks.exe`、`bcdedit.exe`、`diskpart.exe`、`wevtutil.exe`、`vssadmin.exe`、`icacls.exe` 等），检测到即刻终止进程并捕获命令行，触发告警。

**Layer 4** 三层 `FileSystemWatcher` 监控文件系统：游戏 `.minecraft` 目录（递归，排除 `mods/` 和 `.jar`/`.class`）、`%TEMP%`（非递归）、启动文件夹。检测到 `.exe`/`.bat`/`.cmd`/`.ps1`/`.vbs`/`.js`/`.wsf`/`.scr`/`.msi` 等可执行文件创建时告警，游戏目录和启动文件夹中的文件自动删除以阻止持久化。

**Layer 5a** — 每 2 秒通过 `CreateToolhelp32Snapshot` 枚举游戏进程已加载模块，白名单放行 Java 运行时、Windows 系统目录、GPU 驱动、LWJGL natives、.NET/VC++ 运行时。非白名单 DLL 加载时记录告警。

**Layer 5b** — 每 5 秒通过 `GetExtendedTcpTable` 枚举游戏进程 TCP 连接，仅放行 Minecraft 默认端口（25565）和 HTTPS（443），非标准端口连接记录告警。

### 启动安全（JVM 参数 + mainClass）

针对 `version.json` 可能携带恶意 JVM 参数的多层防护：

**危险参数过滤** — `-javaagent:` / `-agentlib:` / `-agentpath:` 三类代理注入参数在 String 和 Object（`{"rules": [...], "value": [...]}`）两种格式中均被过滤，防止攻击者通过 Object 分支绕过安全检查。

**mainClass 三层验证** — 导入和启动双重关卡，覆盖恶意/未知主类：

| 层级 | 判定 | 行为 |
|------|------|------|
| 白名单（6 前缀） | `net.minecraft.` / `cpw.mods.` / `net.minecraftforge.` / `net.fabricmc.` / `net.neoforged.` / `org.quiltmc.` | 静默放行 |
| 未知主类 | 不在白名单也不在黑名单 | 弹窗确认风险后决定 |
| 黑名单（7 前缀） | `java.lang.` / `javax.script.` / `java.lang.reflect.` / `jdk.jshell.` / `javax.tools.` / `com.sun.` / `sun.` | 硬阻断，拒绝导入/启动 |

### Mod 管理
- **Modrinth API v2** 集成 — 搜索、下载、依赖递归解析
- SHA-1 哈希校验 — 启动前验证所有 Mod 是否被 Modrinth 收录
- 未收录 Mod 警告 — 弹窗确认后再启动
- 本地 Mod 启用/禁用（扩展名切换 `.jar` / `.modtemp`）
- 资源包与光影包管理（支持导入 zip 验证 `pack.mcmeta` / `shaders/`）
- 原理图管理（`.schematic` / `.schem` / `.litematic`）

### 下载安全
- **域名白名单** — 仅允许 23 个受信 CDN/API 域名（Modrinth CDN、CurseForge CDN、Mojang/Microsoft 官方、Maven 镜像），非白名单 URL 拒绝下载
- **SHA-256 完整性校验** — 下载后验证文件哈希
- **路径穿越防护** — 检测 ZIP 中的 `../` 恶意路径，防止任意文件写入

### 游戏启动核心
- JVM 参数智能构建 — 从 Mojang version.json 解析，按 Java 版本兼容性过滤
- Classpath 去重 — 按 Maven artifact 标识保留最高版本
- 模块路径冲突自动排除
- NeoForge/Forge `--add-opens` 自动注入
- 游戏时长统计（≥30 秒会话记录到 `instance.json`）
- 完整的诊断日志（启动参数脱敏，stderr 尾部捕获）

### UI/UX
- **Catppuccin Mocha** 暗色主题（深色/浅色切换）
- 三栏布局：侧边导航 → 内容区 → 实时运行日志
- HarmonyOS Sans SC 字体
- 3D 玩家皮肤预览
- 通知弹窗动画
- Windows Hello 生物识别集成（设置锁）

### 隐私保护
- **零遥测、零分析、零用户行为追踪**
- 崩溃日志仅写入本地文件，不上传至任何远程服务器
- 所有网络请求仅限于：Mojang/Microsoft 官方服务、用户指定的 Yggdrasil 服务器、Modrinth/Forge/Fabric 等模组镜像

---

## 技术架构

```
EiTRVO.Tests (MSTest)
   ├── 引用 ProEngine + UI
   └── 50+ 测试文件，12 个 Fake 实现
        ↓
EiTRVO.UI (WPF Application)
   ├── 引用 ProEngine
   ├── 14 个 XAML Panel + 5 个 Dialog
   ├── Platforms/WPF/ — 平台服务实现
   └── App.xaml.cs — DI 容器 + 全局异常处理
        ↓
EiTRVO.ProEngine (Class Library)
   ├── ViewModels/ — 12 个 MVVM ViewModel
   ├── Orchestrators/ — 8 个业务编排器
   ├── Services/ — 16 个服务接口与实现
   ├── Models/ — 24 个数据模型
   └── Helpers/ — 6 个工具类
```

**设计模式：**
- **MVVM** — CommunityToolkit.Mvvm 源代码生成器（`[ObservableProperty]` / `[RelayCommand]`）
- **依赖注入** — `Microsoft.Extensions.DependencyInjection`
- **接口抽象** — 核心服务在 ProEngine 定义接口，UI 层提供平台实现（可移植到 WinUI 3）
- **Fakes 测试** — 无 Mock 框架依赖，纯手写 Fake 实现

---

## 项目结构

```
EiTRVO Neo 26 Preview (1006)/
├── EiTRVO.slnx
├── Directory.Build.props
├── LICENSE                         # MIT License
├── LICENSE.HarmonyOS_Sans_Font.txt  # 字体许可协议
├── README.md
├── .gitignore
│
├── EiTRVO.ProEngine/               # 核心引擎（net8.0，无 UI 依赖）
│   ├── Helpers/                    # 工具类（端点、JVM 参数、占位符、UUID、国际化）
│   ├── Models/                     # 数据模型（认证、实例、设置、Modrinth、SaveLock 等）
│   ├── Services/                   # 核心服务（认证、下载、Mod 加载器、Modrinth、存档锁等）
│   │   └── Loaders/               # Mod 加载器安装器（Fabric/Forge/NeoForge/Quilt/OptiFine）
│   ├── Orchestrators/              # 业务编排（账户管理、实例管理、启动编排、备份等）
│   └── ViewModels/                 # MVVM ViewModel（Home/Download/Settings/Manage/Account 等）
│
├── EiTRVO.UI/                      # WPF 桌面应用（net8.0-windows10.0.18362.0）
│   ├── App.xaml / App.xaml.cs      # 入口点、DI 容器、全局异常处理
│   ├── MainWindow.xaml             # 主窗口（5 列 Grid 布局）
│   ├── Panels/                     # 14 个功能面板 + 5 个对话框
│   ├── Themes/                     # 主题（DarkTheme / Converters / DataTemplates）
│   ├── Converters/                 # WPF 值转换器
│   ├── Platforms/WPF/              # 平台服务实现
│   ├── Services/                   # Windows 特定服务（防火墙 / Windows Hello）
│   ├── Rendering/                  # 皮肤渲染器
│   ├── ViewModels/                 # UI 专用 ViewModel
│   └── font/Font.ttf              # HarmonyOS Sans SC 字体
│
├── EiTRVO.Tests/                   # MSTest 单元测试（net8.0-windows）
│   ├── Fakes/                      # 12 个 Fake 实现
│   ├── Helpers/                    # 工具类测试（39 个）
│   ├── Services/                   # 服务测试（~120 个）
│   ├── Orchestrators/              # 编排器测试（~60 个）
│   ├── ViewModels/                 # ViewModel 测试（~40 个）
│   ├── Loaders/                    # 加载器测试（~40 个）
│   ├── Converters/                 # 转换器测试（24 个）
│   └── Models/                     # 模型序列化测试（23 个）
│
└── publish/                        # 发布输出（.gitignore 排除）
```

---

## 环境要求

| 要求 | 说明 |
|------|------|
| **操作系统** | Windows 10 1903 (Build 18362) 或更高版本 |
| **架构** | x86_64（64 位） |
| **运行时** | .NET 8（自包含发布无需安装） |
| **Java** | 自动检测系统中已安装的 Java（Java 8 / 17 / 21） |
| **Minecraft** | 需要正版 Minecraft 账号（Microsoft 登录）或 Yggdrasil 第三方账号 |

---

## 构建与运行

### 开发构建

```powershell
# 还原依赖并构建
dotnet build

# 运行单元测试
dotnet test

# 运行 UI 应用（Debug 模式）
dotnet run --project EiTRVO.UI/EiTRVO.UI.csproj
```

### 单文件发布

**自包含**（内置 .NET 运行时，无需用户安装）：

```powershell
dotnet publish "EiTRVO.UI\EiTRVO.UI.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugType=embedded `
  -o publish
```

**框架依赖**（单文件但不含运行时，用户需安装 .NET 8 桌面运行时）：

```powershell
dotnet publish "EiTRVO.UI\EiTRVO.UI.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=embedded `
  -o publish
```

---

## 开放源代码许可

本软件使用了以下开源项目：

| 依赖 | 作者 | 许可证 |
|------|------|--------|
| CommunityToolkit.Mvvm | .NET Foundation | MIT License |
| Microsoft.Extensions.DependencyInjection | Microsoft | MIT License |
| System.Security.Cryptography.ProtectedData | Microsoft | MIT License |
| System.Management | Microsoft | MIT License |
| Microsoft.NET.Test.Sdk | Microsoft | MIT License |
| MSTest.TestFramework | Microsoft | MIT License |
| MSTest.TestAdapter | Microsoft | MIT License |

所有依赖均为 MIT License，与项目许可证完全兼容。

---

## 开源许可

本项目基于 **MIT License** 开源。详见 [LICENSE](LICENSE) 文件。

```
```
### 字体许可

本软件使用 **HarmonyOS Sans** 字体（汉仪字库为华为定制，免费商用授权）。

- 字体文件：`EiTRVO.UI/font/Font.ttf`
- 许可协议：[LICENSE.HarmonyOS_Sans_Font.txt](LICENSE.HarmonyOS_Sans_Font.txt)
- 字体版权归华为设备有限公司所有
- 使用条件：突出显示使用声明、不得修改字体文件、不得单独重新分发字体、保留版权声明

> ⚠️ 字体文件已通过 `.gitignore` 排除在版本控制之外。克隆仓库后需自行获取字体文件：
> 
> 1. 访问 [HarmonyOS Sans 字体下载页](https://developer.huawei.com/consumer/cn/doc/design-guides-V1/font-0000001157868583-V1)
> 2. 将 `HarmonyOS_Sans_SC_Regular.ttf` 改名为 `Font.ttf`
> 3. 放置于 `EiTRVO.UI/font/`

---

## 个人信息使用说明

数据安全是项目的基石。以下是软件获取的个人信息及其处理方式。

### 信息收集与用途
- **Minecraft 玩家名与 UUID** — 用于游戏启动与账号识别
- **Microsoft OAuth 刷新令牌** — 用于自动登录续期，无需重复输入密码
- **Yggdrasil 邮箱与密码** — 用于第三方认证服务器登录（密码使用 Windows DPAPI 加密）
- **游戏时长统计** — 仅记录各实例的累计游玩时间，存储于本地 `instance.json`

### 数据存储与安全
- 所有个人信息均存储在本地 `.minecraft` 目录下的 `accounts.json`
- 账户数据使用 Windows DPAPI 加密（`DataProtectionScope.CurrentUser`），仅当前 Windows 用户可解密
- SaveLock 存档加密的 AES 密钥可选择性备份至您指定的驱动器（需主动授权）

### 无数据上传声明
- 本软件不包含任何遥测、分析、埋点或用户行为追踪代码
- 程序崩溃日志仅写入本地文件，不上传至任何远程服务器
- 您的个人信息不会被传输至项目开发方服务器
- 所有网络请求仅限于：Minecraft 官方服务（Mojang/Microsoft）、您指定的第三方认证服务器（Yggdrasil）、模组与资源下载镜像（Modrinth/Forge/Fabric 等）

---

## 法律合规性说明

- 请确保您使用本软件或修改代码的行为符合所在地的法律法规。
- EiTRVO 的开发者对因主动修改代码或添加不在 EiTRVO 开发计划内的功能导致的法律合规性问题不予负责。

---

## 致谢

- **HarmonyOS Sans** 字体由汉仪字库为华为设计
- **Catppuccin Mocha** 配色方案为 UI 主题提供灵感
- 所有 NuGet 依赖的作者和维护者
