# 大肥鱼

> 一只住在 Windows 桌面上的桌宠：会物理乱飞、会睡觉、会陪你玩数独、还会告诉你 **DeepSeek Harness 正在干什么**。

本项目由两部分组成：

| 部分 | 说明 |
|---|---|
| **大肥鱼桌宠** | Windows 桌面宠物（WPF / .NET 8）：物理效果、睡眠、互动、AI 聊天、长期记忆、数独小游戏 |
| **dsh-pet-status** | DeepSeek Harness 插件：把 DSH 的真实工作状态写成状态文件，供桌宠（或任何程序）读取 |

两者通过一个 JSONL 状态文件解耦——插件负责产出，桌宠负责显示，互不依赖。

---

## 它解决什么问题

用 DeepSeek Harness 干活时，你得一直盯着浏览器页面，才知道它是**在思考**、**在跑工具**、**在等你确认**，还是**已经出错/完成**了。

接上本项目的状态插件后，这些信息会变成桌宠的动作和气泡：你切到 VS Code、浏览器或文件管理器时，瞥一眼桌面角落就知道 DSH 现在的处境——尤其是「**等你确认**」，桌宠会举手提醒你回去处理。

---

## 功能

### 🐟 桌宠

| 功能 | 说明 |
|---|---|
| **物理效果** | 可拖拽、可抛掷，落体、碰撞、天花板反弹；关闭后回到普通拖动 |
| **睡眠系统** | 120 秒无互动开始犯困，之后三级递进睡眠，睡够了自动醒；任何互动（睡熟除外）都会把它叫醒 |
| **互动动作** | 摸头、砸（击飞）、敲、打招呼、蹲大牢，各有专属动画序列 |
| **情绪表情** | AI 回复里的 `[情绪]` 标签驱动 18 种表情动画（开心 / 大哭 / 比心 / 害羞 / 生气 …） |
| **AI 聊天** | 基于 DeepSeek API；可自定义人设提示词、回复字数上限、历史轮数、是否开启深度思考 |
| **长期记忆** | `Memory.txt` 记录长期记忆，AI 在对话中自动沉淀，也可手动编辑 |
| **主动搭话 / 漫游** | 长时间没理它，它会自己找话题；也可以让它在桌面上随机蹦跳 |
| **数独** | 完整玩法：唯一解出题（三档难度）、**右键数字填备注小数字**、提示、检查（只报错误数不标位置）、内置 AI 陪玩（拿得到答案但只给提示） |
| **余额显示** | 输入框下方灰字显示 DeepSeek 账号余额 + 当前是**峰期/谷期** |
| **托盘与自启** | 可收纳到系统托盘（同时释放内存）、支持开机自启 |
| **DSH 状态联动** | 见下方 |

### 🔌 DSH 状态联动

| DSH 状态 | 触发时机 | 桌宠反应 |
|---|---|---|
| `THINKING` | 回合开始、模型生成中、整理工具结果 | `thinking` 动画循环 + 气泡「🤔 思考中」 |
| `WORKING` | 正在调用工具（搜索/读取/修改/执行/测试） | 多只工作动画**随机轮换**循环，偶尔插播一次打瞌睡 + 气泡「🔧 干活中（进度）」 |
| `WAITING` | 需要你确认（提问工具 / 回合被阻塞） | `waiting` 动画循环 + 气泡「✋ 等你确认」 |
| `SUCCESS` | 回合正常结束 | 开心动画 + 气泡「🎉 完成任务」 |
| `ERROR` | 回合异常结束（如超长失败） | 大哭动画 + 气泡「😢 出错了」 |
| `IDLE` | 空闲 / DSH 关闭 | 回到待机，恢复正常行为 |

工作期间桌宠会进入**工作模式**，同时上三把锁：

- 🔒 **气泡锁**——状态气泡常驻，不被其他气泡顶掉（你自己发消息的回复可临时顶开，几秒后自动恢复）
- 🔒 **小动作锁**——不播随机发呆动画
- 🔒 **睡觉锁**——不犯困、不睡觉，被摸被敲也保持清醒

---

## 架构

```
DeepSeek Harness
      │  session/event
      ▼
dsh-pet-status 插件            ── 订阅全局会话事件，归约成 6 种状态
      │  写入 JSONL
      ▼
%LOCALAPPDATA%\dsh-pet-status\status.jsonl
      │  增量读取
      ▼
大肥鱼桌宠 (DshCompanion.cs)   ── 状态 → 动画 + 气泡 + 三把锁
```

好处是**显示端可以随便换**：只要读同一个状态文件，你也可以用悬浮窗、状态栏、脚本甚至别的桌宠来展示 DSH 状态，不必改插件。

---

## 快速开始

### 环境要求

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（构建桌宠）
- [DeepSeek API Key](https://platform.deepseek.com/)（AI 聊天与余额显示需要）
- 可选：[DeepSeek Harness](https://api-docs.deepseek.com/) + Node.js ≥ 22.19（状态联动需要）

### 一、运行桌宠

```powershell
cd Dafeiyv
dotnet run
```

首次启动后：**右键桌宠 → ⚙️ 设置 → 填入 API Key → 保存**。

### 二、接入 DSH 状态（可选）

1. 把 `dsh-pet-status` 文件夹复制到 DSH profile 的 `node_modules` 下：

```
<你的 .dsh 目录>\profiles\web\node_modules\dsh-pet-status\
```

2. 在 `<你的 .dsh 目录>\profiles\web\cordis.patch.yml` 的 `insert:` 列表追加两行：

```yaml
    - id: dsh-pet-status
      name: 'dsh-pet-status'
```

3. **完全重启 DSH**（不是刷新浏览器）

重启后状态文件会出现在 `%LOCALAPPDATA%\dsh-pet-status\status.jsonl`，桌宠会自动跟随。

更详细的插件文档（状态协议、配置项、如何自己写显示端）见 [`dsh-pet-status/README.md`](dsh-pet-status/README.md)。

---

## 配置

桌宠设置保存在 exe 同目录的 `Settings.json`，也可以通过 **右键 → ⚙️ 设置** 图形化修改：

| 字段 | 说明 |
|---|---|
| `ApiKey` | DeepSeek API Key |
| `SystemPrompt` | 自定义人设 / 说话风格（数独聊天也复用这套人格） |
| `EnableThinking` | 主聊天是否开启深度思考（关掉回复更快、更省 token） |
| `MaxHistoryRounds` | 对话记忆轮数（10 / 5 / 0） |
| `EnableMaxReplyLength` / `MaxReplyLength` | 是否限制回复字数 |
| `EnableLongTermMemory` / `MemoryEntryMaxChars` | 长期记忆开关与每条上限 |
| `PetScale` | 桌宠缩放比例（0.5 ~ 2.0） |
| `IdleActionIntervalSeconds` | 随机小动作间隔 |
| `EnableProactiveSpeak` / `ProactiveSpeakIntervalSeconds` | 主动搭话开关与间隔 |
| `EnableRoam` / `RoamIntervalSeconds` | 漫游开关与间隔 |
| `Welcome1` ~ `Welcome4` | 启动欢迎语 |

---

## 项目结构

```
.
├── Dafeiyv/                     # 桌宠本体（WPF）
│   ├── MainWindow.xaml(.cs)     # 主窗口、气泡、托盘、右键菜单、DSH 状态接线
│   ├── PetBehavior.cs           # 动画 / 睡眠 / 定时器 / DSH 工作模式
│   ├── PhysicsHelper.cs         # 物理效果
│   ├── DeepSeekService.cs       # DeepSeek 调用、情绪解析、余额查询
│   ├── MemoryStore.cs           # 长期记忆文档
│   ├── SudokuWindow.xaml(.cs)   # 数独界面与 AI 陪玩
│   ├── SudokuGame.cs            # 数独引擎（出题/校验/提示/备注）
│   ├── DshCompanion.cs          # 读 DSH 状态文件 → 驱动桌宠
│   ├── SettingsWindow.xaml(.cs) # 设置界面
│   ├── HistoryWindow.xaml(.cs)  # 对话记录
│   ├── IconLoader.cs            # 从 .ico 解析托盘图标
│   ├── AutoStartHelper.cs       # 开机自启
│   ├── Images/                  # 情绪表情动画
│   ├── Idle/                    # 随机小动作动画
│   ├── Interact/                # 互动动作动画
│   └── ico/dafeiyv.ico          # 程序与托盘图标
│
└── dsh-pet-status/              # DSH 状态插件
    ├── index.js                 # 插件本体
    ├── package.json
    ├── test.mjs                 # 离线自测
    └── README.md                # 状态协议与接入文档
```

---

## 常见问题

**Q：必须接 DSH 才能用吗？**
不必。桌宠可以完全独立运行，AI 聊天、数独、互动都不依赖 DSH。状态插件是可选的加分项。

**Q：桌宠没反应 / 一直不显示工作状态？**
按顺序检查：① 状态插件是否装好并**完全重启**过 DSH；② `%LOCALAPPDATA%\dsh-pet-status\status.jsonl` 是否存在；③ 桌宠启动日志里状态是否为 `Disconnected`（表示读不到文件）。

**Q：为什么工具报错时桌宠不进入错误状态？**
工具级别的失败不会打扰你（否则失败命令会刷屏），只有**整轮任务**异常结束才进入 `ERROR`。

**Q：状态文件会不会越来越大？**
插件在文件超过 4 MB 时会自动清空重写；桌宠按增量读取，遇到清空会自动从头读，不会出错。

**Q：AI 回复很慢？**
模型默认开启深度思考。可在设置中关闭「深度思考」，回复会明显变快、也更省 token。

---

## ⚠️ 注意

- **不要提交 `Settings.json` 和 `Memory.txt`**：前者含你的 API Key，后者是你的个人聊天记忆。建议加入 `.gitignore`。
- **动画素材版权**：`Images/`、`Idle/`、`Interact/` 下的 GIF 为收集/自制素材，若二次分发请自行确认授权。**代码与素材的授权可能不同**，转发仓库时请注意。
- 本项目仅用于学习交流，与 DeepSeek 官方无隶属关系。

---

## 许可

代码部分见仓库内 `LICENSE`（如未附带，请自行补充）。动画素材版权归各自原作者所有。
