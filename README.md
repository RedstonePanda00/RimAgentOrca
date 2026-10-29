# RimAgent

[简体中文](#简体中文) · [English](#english)

## 简体中文

### 什么是 RimAgent？

RimAgent 是一款基于 .NET 10 核心打造的 RimWorld 模组，为边缘世界带来了真正意义上的 AI 叙事者。

通过接入 LLM API 并运行内置的 Agent 工作流，RimAgent 能实时感知你的殖民地局势，为你演化出千人千面的动态体验。

在这里，你的 Agent 伙伴拥有真实的记忆、鲜明的人格与独特的叙事倾向。除了直接选用内置人格外，你还可以亲手定制专属的 AI 叙事者——用 **.skill** 技能文件教会它如何推动你的故事，或是挂载专属知识库，让它彻底读懂你的世界观。

### 一切皆插件

RimAgent 采用纯粹的模块化架构，能自动识别并加载所有已声明的插件。每个插件均由独立的 WorkerClass 驱动，并可在设置中随时自由开关。

模组目前已内置多款实用插件——比如根据你的游玩进度，将殖民地的兴衰起落自动提炼、续写成专属于你的**殖民地小说**，把这一路的珍贵回忆真正留存下来。

### 他们会记得

只要接入 Embedding 模型，即可唤醒 RimAgent 内置的 RAG 记忆系统。配合独特的**“快速眼动期（REM）”记忆整理机制**，你的伙伴会在后台沉淀过往点滴，记住你的每一次凯旋与挫败——让那些惊心动魄的游戏瞬间，从此不再只有你一个人记得。

### 现在，一键搬迁

RimAgent 支持作为 Runtime MCP 直接运行。无论是 Codex、Antigravity、Claude Code 还是 DeepSeek Harness，你都可以用日常顺手的 Agent 客户端直接连入正在运行的 RimWorld，把你最熟悉的 AI 伙伴无缝“搬”进边缘世界。

### 不只是你自己

RimAgent 配备了完备的 Agent 自主游玩工具链。只要你需要，你的伙伴随时能接手打理殖民地日常，或是结合当下战局，给出充满个人色彩（Agent Flavor）的实时战术建议。

*注：部分界面交互功能需要 Agent 具备视觉理解能力。*

### 快速开始

1. 安装并启用 RimAgent。本版本仅支持 RimWorld 1.7。
2. 使用内置工作流：在模组设置中添加模型连接、填写连接信息和 API 密钥，并为需要的职责分配模型。选择模组叙事者并启用 Agent 规划，即可使用 AI 叙事。
3. 使用外部 Agent：启用“外部 Agent 桥接”，按[接入文档](MCP/CONNECT.md)配置本地 MCP 客户端。它与让模组调用其他服务器的“外部 HTTP MCP”是两个不同功能。
4. 按需启用人格、技能、知识库、记忆与殖民地小说等功能。模型连接和密钥由玩家填写；外部 Agent 可协助调整获准的非密钥配置。

希望 Agent 自行完成接入时，可将[英文连接教程](Source/RimAgent.McpHost/CONNECT.md)交给它。发布包中同一份教程位于 `MCP/CONNECT.md`，末尾附有可直接复制给 Agent 的提示词。

### 补充说明

- RAG 语义记忆需要 embedding 模型和对应选项。未配置 embedding 时，基础记忆仍可使用关键词回退，但不提供相同的语义检索体验。
- 技能使用 `SKILL.md` 文件提供任务指令，并非微调或训练模型。技能可区分聊天、小说整理和小说正文等用途。
- 外部 Agent 必须持续运行并处理请求，模组不会自动唤醒一个已停止的对话。模组内小说生成、委派给已配置模型的分析、内置视觉和搜索等工作流仍需各自的模型或服务配置。
- 无可用 AI 规划连接且未由外部 Agent 接管时，模组仍保留 XML 定义的叙事者后备行为。
- API 密钥和令牌保存在 RimWorld 模组设置中。联网服务按所启用的功能接收数据，请自行选择服务与预算。

## English

### What Is RimAgent?

RimAgent is a RimWorld mod powered by .NET 10 that brings a true AI storyteller to life on the Rim.

By connecting to an LLM API and running its built-in agent workflow, RimAgent reads the pulse of your colony in real time—crafting a dynamically evolving experience tailored to every run.

Your agent companion comes with persistent memories, a distinct personality, and a storytelling style of their own. Roll with one of the built-in personas or design your own AI storyteller from scratch: use **.skill** files to teach them how to drive your narrative, or attach a custom knowledge base so they truly understand your world's lore.

### Everything Is a Plugin

Built on a purely modular architecture, RimAgent automatically discovers and loads any declared plugin. Each plugin is powered by its own independent WorkerClass and can be toggled on or off anytime in the settings.

Several built-in plugins are ready out of the box—including a **Colony Novel** generator that chronicles the rise and fall of your settlement as you play, turning your favorite memories into a story worth keeping.

### They Will Remember

Hook up an embedding model to awaken RimAgent's built-in RAG memory system. Through its signature **REM (Rapid Eye Movement) memory consolidation**, your companion processes and distills past events in the background, remembering every triumph and disaster—so those unforgettable moments on the Rim are never yours to carry alone.

### One-Click Move-In

RimAgent can run directly as a Runtime MCP. Whether your daily driver is Codex, Antigravity, Claude Code, or DeepSeek Harness, you can link your favorite agent client straight into a running RimWorld session and seamlessly bring your go-to AI companion onto the Rim.

### You're Not in This Alone

Equipped with a full toolkit for autonomous play, your companion can step in whenever you need a hand—helping manage day-to-day colony operations or reading the battlefield to offer real-time tactical advice with their own unmistakable **Agent Flavor**.

*\* Note: Certain UI interactions require an agent with vision capabilities.*

### Quick start

1. Install and enable RimAgent. This release supports RimWorld 1.7 only.
2. For built-in workflows, add model connections in mod settings, enter connection details and API keys, and assign models to the roles you need. Select the mod's storyteller and enable agent planning for AI storytelling.
3. For an external agent, enable **External agent bridge** and configure your local MCP client using the [connection guide](MCP/CONNECT.md). This is separate from **External HTTP MCP**, which lets the mod call other tool servers.
4. Enable personas, skills, knowledge, memory, and Colony Novel as needed. Connections and credentials remain player-managed; external agents can help adjust permitted non-secret settings.

To let your agent set up the connection, give it the [connection walkthrough](Source/RimAgent.McpHost/CONNECT.md). The same guide ships as `MCP/CONNECT.md` and includes a ready-to-copy player prompt.

### Additional details

- RAG semantic memory requires an embedding model and the corresponding options. Without embeddings, basic memory can fall back to keywords, which does not provide the same semantic retrieval experience.
- `SKILL.md` files supply task instructions; they do not fine-tune or train a model. Skills can target chat, novel preparation, novel prose, or extension-defined tasks.
- An external agent must remain running and serve requests. The mod does not wake a stopped conversation. In-mod novel generation, analysis delegated to configured models, built-in vision, and search still require their respective model or service configurations.
- Without a usable AI planning connection or external narrative takeover, the mod retains XML-defined fallback storyteller behavior.
- API keys and tokens are stored in RimWorld mod settings. Enabled network services receive the data needed for those features; choose your services and budgets accordingly.

## Documentation / 文档

- [外部 Agent 连接教程 / External agent connection walkthrough](MCP/CONNECT.md)

## Development / 开发

Build the mod and publish the bundled MCP host with the .NET 10 SDK:

```powershell
dotnet build Source/RimAgent/RimAgent.sln -c Release
dotnet publish Source/RimAgent.McpHost/RimAgent.McpHost.csproj -c Release --no-self-contained -o MCP
```

Build against an installed RimWorld 1.7. The default game path is `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`; for another installation, append `-p:RimWorldDir="D:\Games\RimWorld"` to the build command. Game assemblies are referenced from that installation and are not redistributed here. Local regression suites and helper scripts are not included in this repository.

## License / 许可

[MIT License](LICENSE). Applies to project-owned material; it grants no rights to RimWorld or other third-party software and services.
