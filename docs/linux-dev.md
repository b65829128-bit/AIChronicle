# Linux 开发环境（主工作流）

> 从 2026-10 起，AI Chronicle 的主开发环境是 Linux。本文是 Linux 侧的权威工作流；Windows 只保留 net6/Xbox 构建和需要 dnSpy/附加调试的场景。项目仍然是一个 **Windows 游戏（Bannerlord）的 net472 模组**，所以“在 Linux 开发/编译”和“在 Linux 运行游戏”是两件事。

## 1. 本机环境

| 项目 | 值 |
|------|-----|
| 发行版 | Arch / CachyOS |
| .NET SDK | 10.0.x（`dotnet --version`） |
| IDE | Rider（JetBrains Toolbox） |
| 游戏 | Mount & Blade II: Bannerlord v1.4.8，Steam/Proton 的 **Windows 客户端** |
| 游戏根目录 | `/mnt/data/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord` |
| Proton 前缀 | `/mnt/data/SteamLibrary/steamapps/compatdata/261550/pfx` |
| 开发目录 | `~/项目/AIChronicle` |
| 四前置（本机） | Harmony 2.4.2.248 / ButterLib 2.11.1 / UIExtenderEx 2.13.3 / MCM 5.12.2 |

说明：

- Linux 没有 Bannerlord 原生客户端；游戏通过 Steam Play/Proton 运行。**编译不需要启动游戏**，只需要游戏目录里的 `TaleWorlds.*.dll` 和各模块 DLL。
- 游戏目录路径含空格和 `&`，shell 里必须加引号。
- 四前置已解压到 `Modules`（本机版本见上表）；仓库不包含它们。只编译不需要四前置，运行才需要。

## 2. 一次性配置

推荐用 `local.props` 保存本机游戏路径（已 gitignore）：

```bash
cp local.props.example local.props
# 编辑 local.props，把 BANNERLORD_GAME_DIR 改成你的游戏根目录
```

也可以直接用环境变量（优先级更高）：

```bash
export BANNERLORD_GAME_DIR="/mnt/data/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord"
```

`Directory.Build.props` 会自动导入 `local.props`；Rider 从 Toolbox 启动也能读到这个路径。

Rider 打开 `AIChronicle.csproj`（仓库没有 `.sln`），Build 即可。`Properties/launchSettings.json` 里的 Windows `.exe` profile 在 Linux 下不要用；运行游戏走 Steam/Proton。

## 3. 构建与部署

```bash
cd ~/项目/AIChronicle

# 编译 Steam 版（Linux 上 TargetFrameworks 自动只有 net472）
dotnet build -c Release

# 显式指定 TFM（等价）
dotnet build -c Release -f net472

# 全量编译（定期做，避免增量编译掩盖文件损坏）
dotnet clean -c Release && dotnet build -c Release -f net472
```

构建成功后会由 `Bannerlord.BuildResources` 自动部署：

```text
$BANNERLORD_GAME_DIR/Modules/AIChronicle/
├── SubModule.xml
├── GUI/
├── Prompts/
└── bin/Win64_Shipping_Client/
    ├── AIChronicle.dll
    ├── NAudio*.dll
    └── ...（其他运行时依赖）
```

检查部署：

```bash
stat -c '%s %n' "$BANNERLORD_GAME_DIR/Modules/AIChronicle/SubModule.xml"
ls -la "$BANNERLORD_GAME_DIR/Modules/AIChronicle/bin/Win64_Shipping_Client/AIChronicle.dll"
```

`SubModule.xml` 应约 2KB 且非空。如果变成 0 字节（`Bannerlord.BuildResources` 的 `ReplaceFileText` 偶发问题），把项目里的 `_Module/SubModule.xml` 复制过去覆盖即可。

> `_Module/`（`Prompts/`、`GUI/`、`SubModule.xml`）也在此步骤拷到游戏目录。**只改提示词也必须 build**，否则游戏里仍是旧版。

## 4. 打包发布版

```bash
# 编译 + 部署 + 打包（产物 dist/AIChronicle_v<版本>.zip）
python3 scripts/package.py

# 跳过编译，仅重新打包
python3 scripts/package.py --skip-build
```

脚本用 Python 标准库（无需 `zip` 命令），会：

- 打包 `_Module/SubModule.xml`、`GUI`、`Prompts`（排除运行期 `Prompts/Campaigns`）；
- 打包 `bin/Release/net472` 下**全部** `.dll`（包含 NAudio 等运行时依赖，不只 `AIChronicle.dll`）；
- 附带 `README_MOD.md`、`LICENSE`、`安装说明.txt`；
- 不包含 `.pdb`、源码或战役数据。

Windows 上仍可用 `scripts/package.ps1`（保留兼容）。

## 5. 运行与测试（Proton）

1. 在启动器里启用四前置（本机已解压到 `Modules`）：Harmony、ButterLib、UIExtenderEx、MCM（MBOptionScreen）。
2. 启用 AIChronicle。
3. 通过 Steam 启动游戏（Proton）。游戏的 Windows 客户端在 `$BANNERLORD_GAME_DIR`，存档/配置在 Proton prefix 里。

注意：

- 本模组的 `SubModule.xml` 依赖四前置；缺任何一个，启动器会拒绝加载。
- ButterLib 在新版本可能需要 BLSE。Proton 下 BLSE/Harmony 的加载路径有已知问题（需要调整启动项、处理 `Mono.Cecil.dll`/Harmony 的符号链接）；按四前置的 Linux/Proton 说明处理。
- 本机四前置已安装，但尚未启动游戏验证；“编译通过”≠“已经进游戏验证”。第一次进游戏前先备份存档。
- TTS 走 NAudio 的 Windows 音频后端（WinMM）；Proton 下由 Wine 提供。若语音异常，先在 MCM 关闭 TTS 验证主流程。

### 日志位置

```bash
PREFIX="/mnt/data/SteamLibrary/steamapps/compatdata/261550/pfx"

# 游戏引擎日志
tail -n 200 "$(ls -t "$PREFIX/drive_c/ProgramData/Mount and Blade II Bannerlord/logs"/rgl_log_*.txt | head -1)"

# ButterLib / 模组日志
ls -t "$PREFIX/drive_c/users/steamuser/Documents/Mount and Blade II Bannerlord/Configs/ModLogs"/*.txt | head

# 战役内 AI 调试日志（DebugLogger）
ls -t "$BANNERLORD_GAME_DIR/Modules/AIChronicle/Prompts/Campaigns"/*/debug_logs/*.log | head
```

### 调试限制

- **不能**在 Linux 的 Rider 里直接附加到 Proton 中的 `.NET Framework` 游戏进程；断点调试需要 Windows。
- Linux 上用 `debug_logs/`、引擎日志和 `ilspycmd` 反编译定位问题：
  ```bash
  dotnet tool install -g ilspycmd
  export PATH="$PATH:$HOME/.dotnet/tools"
  ilspycmd -p -o "BLSource/TaleWorlds.CampaignSystem" "$BANNERLORD_GAME_DIR/bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll"
  ```
- 本机已生成 `BLSource/`（7375 个 .cs，约 118MB，gitignored），可直接 `grep` 或让 Rider 索引。
- `BLSource/` 仍在 `.gitignore` 中，仅本地阅读，不提交。

## 6. 平台边界

- Linux 只构建 `net472`（Steam）。`net6`（Xbox/Store）只在 Windows 上构建；`AIChronicle.csproj` 的 `TargetFrameworks` 已按 `$(OS)` 区分。
- 游戏本身没有 Linux 原生客户端；Proton 是唯一运行方式。
- 游戏目录大小写敏感。模块目录名（`SandBox`、`SandBoxCore` 等）保持原样，不要改成小写。
- `Properties/launchSettings.json` 里的启动配置是 Windows `.exe` 路径，Linux 下不要用；运行游戏走 Steam/Proton。
