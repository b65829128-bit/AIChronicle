# CLAUDE.md — AI编年史·言出法随 开发入口

> 本文件是 Claude Code 的入口文档。**权威文档是 AGENTS.md**，实现任何功能前必须先读它。

## 必读文档（按顺序，两份一起看）

1. **AGENTS.md** — AI 开发工作流（架构、构建命令、Harmony 模式、BLSource、调试方法、文档维护规则）。一切技术决策以此为准。
2. **README_MOD.md** — 模组功能文档（能做什么、UI 入口、MCM 配置项）。

AGENTS.md 告诉你「怎么做」，README_MOD.md 告诉你「能做什么」——实现功能前两份都要读，不要只凭一份做决策。

## Claude Code 特有规则（强制）

- **文件路径**：所有文件操作必须使用完整 Windows 绝对路径（`C:\Users\<用户名>\BLMods\AIChronicle\...`），禁止相对路径和 `/c/...` 形式。这是本环境的硬性要求。
- **改代码必须更新文档**：完成任何代码变更后，对照 AGENTS.md 的「代码修改后文档自检清单」逐项检查 README_MOD.md / AGENTS.md 是否需要更新，未更新视为未完成。**修改 AGENTS.md 或 README_MOD.md 前必须先向用户说明改动并征得同意。**

## 构建与部署

```bash
# 编译 + 自动部署（BANNERLORD_GAME_DIR 已设置）
cd "C:\Users\<用户名>\BLMods\AIChronicle"
dotnet build -c Release

# 全量编译（增量编译可能掩盖文件损坏，定期执行）
dotnet clean -c Release && dotnet build -c Release
```

部署后 DLL 自动复制到 `D:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AIChronicle\bin\Win64_Shipping_Client\AIChronicle.dll`。

## Git 提交

- 提交信息一句简短描述（建议英文、单行），如 `fix: secretary permissions`——不要多行长文
- 提交前 `git status` / `git diff` 检查改动
- 提交与推送只在用户要求时执行

## 禁区

- **BLSource/**：反编译的游戏源码（5332 文件），只读参考——绝不修改、绝不删除、绝不提交
- **_Module/Prompts/Campaigns/**：运行时生成的战役存档数据——绝不提交
- **工具定义同步**：`tools.json` / `agent_tools.json` 与 `ToolExecutor.cs` 的 switch 必须同步（新增工具：定义 → case → 显示映射，详见 AGENTS.md「扩展方式」）
