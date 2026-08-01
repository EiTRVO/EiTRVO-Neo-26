# EiTRVO Neo 26.1

基于 **EiTRVO ProEngine** 的 Minecraft: Java Edition 启动器，项目代码由 DeepSeek V4 Pro 生成。

> WPF / .NET 8  |  578 个单元测试  |  零遥测

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
- 实例打包/导入（自有 `eitrvo-pack:1` 格式 + Modrinth `.mrpack` 格式）
- **拖拽导入** — 直接将 `.zip` 或 `.mrpack` 文件拖放到管理面板即可导入

### 整合包安全扫描

导入整合包时自动检测：危险文件类型拦截（19 种）、路径穿越防护、资源耗尽防护（大小/数量/深度限制）、JVM 恶意参数扫描、下载 URL 审查。覆盖 `eitrvo-pack:1` 和 `.mrpack` 两种格式。

### Modrinth .mrpack 支持

- 本地导入（按钮 + 拖拽）、Mod 加载器自动检测、并行下载管线（16 并发 / 3 次重试 / SHA-1 校验）、Overrides 安全提取

### 首次启动向导（OOBE）

首次启动时显示 4 步设置向导（安全配置 → 账号登录 → 完成），向导完成后写入设置文件，后续启动不再显示。

### 安装与进度面板

卡片式 Mod 加载器选择（6 种）+ 版本卡片 + 共存安装选项。双标签进度面板（下载进度 + 实时运行日志，stdout/stderr 分色显示）。

### SaveLock 存档加密
- AES-256-CBC + PBKDF2-SHA256 加密，支持一次性解密和永久锁定两种模式
- 密码提示、OneDrive 密钥备份、`.savkey` 导出/导入、`.savrec` 恢复文件
- 启动流程中无缝集成解密/重加密

### EiTRVO Firewall 进程安全

五层纵深防护体系，从进程创建到运行时全程保护：

| 层级 | 功能 |
|------|------|
| Layer 0 | 禁用 Windows 进程扩展点，阻止第三方 DLL 注入 |
| Layer 1+2 | Job Object 统一加固：进程熔断回收 + 进程数上限 + 剪贴板保护 + 跨进程句柄隔离 + 桌面/系统参数锁定 + 子进程自动继承 |
| Layer 3 | 子进程黑名单实时拦截（IOCP 内核同步监控，微秒级响应，全进程树覆盖） |
| Layer 4 | 文件系统监控：游戏目录 / %TEMP% / 启动文件夹，检测可疑可执行文件创建并自动删除 |
| Layer 5 | DLL 模块白名单监控 + TCP 连接监控 |

**Layer 0–2** 使用 `CREATE_SUSPENDED` 在进程挂起态完成加固，恢复执行前全部防护已就位。

**Layer 3** 黑名单覆盖 27 项危险进程，命中后即刻终止并捕获完整命令行，触发熔断告警。

### 启动安全（JVM 参数 + mainClass）

- 过滤 `-javaagent:` / `-agentlib:` 等危险 JVM 参数（覆盖 String 和 Object 两种 JSON 格式）
- mainClass 三层验证：白名单（6 前缀静默放行）→ 未知主类（弹窗确认）→ 黑名单（7 前缀硬阻断）

### Mod 管理
- Modrinth API v2 集成（搜索/下载/依赖解析）
- SHA-1 哈希校验 + 未收录 Mod 弹窗确认
- 本地 Mod 启用/禁用 + 资源包/光影包/原理图管理
- 元数据缓存（24 小时 TTL，离线可用）

### 下载安全
- 域名白名单（23 个受信 CDN/API）+ HTTPS 强制 + SHA-256 / SHA-1 完整性校验
- ZIP 路径穿越防护 + NTFS 重解析点检测
- 启动器自完整性校验（DPAPI 保护）

### 游戏启动
- JVM 参数智能构建 + Classpath 去重 + 模块冲突自动排除 + `--add-opens` 自动注入
- 游戏时长统计 + 完整诊断日志

### UI/UX
- Catppuccin Mocha 暗色主题 + HarmonyOS Sans SC 字体
- 三栏布局 + 卡片式安装面板 + 双标签进度面板
- 3D 皮肤预览 + 通知动画 + Windows Hello 设置锁

### 隐私保护
- **零遥测、零分析、零用户行为追踪**
- 崩溃日志仅写入本地文件，不上传至任何远程服务器
- 所有网络请求仅限于：Mojang/Microsoft 官方服务、用户指定的 Yggdrasil 服务器、Modrinth/Forge/Fabric 等模组镜像

---

## 技术架构

WPF / .NET 8，MVVM（CommunityToolkit.Mvvm）+ DI（Microsoft.Extensions.DependencyInjection）。核心服务接口定义在 ProEngine，UI 层提供平台实现。MSTest 测试，无 Mock 框架依赖。

---

## 项目结构

```
EiTRVO.ProEngine/     # 核心引擎（net8.0，无 UI 依赖）
├── Helpers/          # 工具类
├── Models/           # 数据模型
├── Services/         # 核心服务 + Mod 加载器
├── Orchestrators/    # 业务编排
└── ViewModels/       # MVVM ViewModel

EiTRVO.UI/            # WPF 桌面应用（net8.0-windows）
├── Panels/           # 20+ 功能面板与对话框
├── Themes/           # Catppuccin Mocha 主题
├── Services/         # Windows 特定服务（Firewall / Windows Hello）
└── Platforms/WPF/    # 平台服务实现

EiTRVO.Tests/         # MSTest 单元测试（60+ 测试文件）
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

```powershell
dotnet build                          # 构建
dotnet test                           # 测试
dotnet run --project EiTRVO.UI        # 运行

# 单文件发布（框架依赖）
dotnet publish EiTRVO.UI/EiTRVO.UI.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish-fd
```

---

## 开放源代码许可

所有 NuGet 依赖均为 MIT License：CommunityToolkit.Mvvm、Microsoft.Extensions.DependencyInjection、System.Security.Cryptography.ProtectedData、MSTest 等。

---

## 开源许可

本项目基于 **MIT License** 开源。详见 [LICENSE](LICENSE) 文件。


### 字体许可

本软件使用 **HarmonyOS Sans** 字体（汉仪字库为华为定制，免费商用授权）。

- 字体文件：`EiTRVO.UI/font/Font.ttf`
- 许可协议：[LICENSE.HarmonyOS_Sans_Font.txt](LICENSE.HarmonyOS_Sans_Font.txt)
- 字体版权归华为设备有限公司所有
- 使用条件：突出显示使用声明、不得修改字体文件、不得单独重新分发字体、保留版权声明

> ⚠️ 字体文件已通过 `.gitignore` 排除在版本控制之外。克隆仓库后需自行获取字体文件：
> 
> 1. 从 [华为 CDN](https://developer.huawei.com/images/download/next/HarmonyOS-Sans-v2.zip) 下载字体 zip
> 2. 解压后将 `HarmonyOS_SansSC_Regular.ttf` 改名为 `Font.ttf`
> 3. 放置于 `EiTRVO.UI/font/`

---

## 个人信息使用说明

- 收集的信息（玩家名/UUID/OAuth 令牌/游戏时长）仅用于游戏启动与账号识别，全部以 Windows DPAPI 加密存储在本地
- 不包含任何遥测、分析或用户行为追踪代码，网络请求仅限 Minecraft 官方服务和用户指定的第三方服务器

---

## 法律合规性说明

- 请确保您使用本软件或修改代码的行为符合所在地的法律法规。
- EiTRVO 的开发者对因主动修改代码或添加不在 EiTRVO 开发计划内的功能导致的法律合规性问题不予负责。

---

## 致谢

- **HarmonyOS Sans** 字体由汉仪字库为华为设计
- **Catppuccin Mocha** 配色方案为 UI 主题提供灵感
- 所有 NuGet 依赖的作者和维护者
