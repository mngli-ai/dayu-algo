# 大肥鱼の算法大冒险 · dayu-algo

把 C++ 算法题画成节点图：拖模块、连线、双击写表达式，一条主链自上而下读下来就是 `main`。做完点「运行」，样例板和隐藏抽查都过了才算通关。

Unity 6 实现，全部界面与美术是运行时用代码装配的像素风 UI，没有预制体也没有场景美术。

![编辑器界面](docs/screenshots/01-editor.png)

## 玩法

一关给你半张图：起始模块和两块样例板已经摆好，缺的部分自己补。

| 交互 | 说明 |
| --- | --- |
| 左侧模块栏 | 按章节分组的节点目录，拖到画布上就多一个卡片 |
| 连线 | 从底部端口拖到下一个节点顶部；分支节点有 `true` / `false` 两路，循环节点有 `body` 和 `done` |
| 双击卡片 | 弹出浮窗编辑参数，表达式（`sum + a[i]`、`s[i]`、`c == '('`）都写在这里 |
| 运行 / 单步 / 断点 / 跟踪 | 顶栏按钮，配合变量面板看每一步的值 |
| 样例对比板 | 自动拿运行结果和期望输出比对，不一致就标红 |
| 提交 | 除可见样例外还会跑隐藏抽查用例，全过才解锁下一关 |

容器有专门的可视化：数组是列车车厢，栈是一口竖直的筒，队列是排队通道；压入弹出会跟着执行链播动画。

![关卡运行](docs/screenshots/02-run.png)

## 关卡

共 13 关，分两个章节。

- **序章 · 从零开始** —— 第一块模块、读进来
- **序章 · 变量与运算** —— 两数之和
- **序章 · 条件分支** —— 走哪条路
- **序章 · 循环** —— 重复做、倒着加
- **序章 · 数组** —— 存一存、数组求和、数组最大值
- **第一章 · 栈与队列** —— 倒着出来、谁还在最上面、排队先出、括号匹配

题目正文、故事、数据范围、提示、标准答案图和测试用例全部写在 `Assets/Scripts/Core/Levels.cs`，加关卡就是加一个 `new Level{...}`。

![开始菜单](docs/screenshots/03-menu.png)

## 代码结构

| 路径 | 内容 |
| --- | --- |
| `Assets/Scripts/Core/` | 图数据模型（`Graph` / `NodeTypes`）、表达式语言与类型检查（`ExprLang`）、编译器（`Compiler`）、虚拟机（`Vm`）、追踪（`Trace`）、C++ 代码生成（`CppGen`）、关卡（`Levels`） |
| `Assets/Scripts/GraphView/` | 画布、卡片形状绘制、撤销重做历史 |
| `Assets/Scripts/UI/` | 像素 UI 组件（按钮、输入框、位图文本、面板）、模块栏、检查器、变量浮窗、数组/栈/队列看板、顶栏、存档 |
| `Assets/Scripts/Art/` | 程序化像素素材、模块图标、精灵动画 |
| `Assets/Scripts/Text/` | 位图字形图集 |
| `Assets/Editor/` | 自测入口（`Cli.RunSelfTests`）、命令行打包（`BuildCli.BuildWindows`）、16 个测试类 |

约 1.9 万行 C#。

## 构建与运行

需要 Unity **6000.3.25f1**（Build Target: Windows x64）。

编辑器里跑：Unity Hub 添加本目录，打开 `Assets/Scenes/Main.unity`，点 Play。场景里只有一个空物体，界面由 `App` 在运行时搭出来。

命令行自测（编译每关标准答案，再用测试用例跑 VM）：

```bat
"C:\Unity\6000.3.25f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath <本项目路径> ^
  -executeMethod Dayu.EditorTools.Cli.RunSelfTests ^
  -logFile -
```

命令行打包：

```bat
"C:\Unity\6000.3.25f1\Editor\Unity.exe" -batchmode -quit ^
  -projectPath <本项目路径> ^
  -executeMethod Dayu.EditorTools.BuildCli.BuildWindows ^
  -logFile -
```

产物在 `Build/dayu.exe`。

单个测试类也能单独跑，例如 `-executeMethod Dayu.EditorTools.ExprLangTests.RunAll`，断言失败时进程退出码为 1。

## 仓库说明

不入库的目录：`Library/`、`Build/`、`Logs/`、`Evidence/`（逐轮验收截图与日志）、`UserSettings/`、`ArtInbox/` 下的第三方素材原包。`ArtInbox/SOURCES.md` 和随附的 NOTICE 保留在仓库里，作为素材授权台账。

Windows 试玩包在 Releases 里下载，解压直接运行 `dayu.exe`。

## 授权与署名

游戏主角「溟月」的形象全部来自第三方二创素材，授权为 **CC BY-NC-SA 4.0**，署名链：

1. 上善无形 —— 溟月原型（pixiv 62155430）
2. ZipZipPipe —— 女仆鲸鱼娘设计（pixiv 18604994）
3. Small-tailqwq —— 三创再设计（https://github.com/Small-tailqwq/dsh-deep-whale）

逐文件的来源仓库、许可原文位置、派生参数记录在 `ArtInbox/SOURCES.md`，游戏实际用到的产物清单在 `docs/art-assets-manifest.md`。其余道具与图标（木箱、道岔、循环工厂、数组列车、码头）为本项目自绘。

由于包含 NC-SA 素材，本仓库整体按非商业用途发布：转载、二创需署名上述作者链，并以相同方式共享。代码部分未单独声明开源许可。
