# README 截图的生成方式

截图全部由游戏自带的 `-shot` 批处理模式拍摄，不用手工截屏，改完 UI 重跑一遍就能更新。

```bat
:: 主图：序章「两数之和」带标准答案并自动运行一遍，截图时正好停在 6/6 步、样例板判 AC
Build\dayu.exe -level L1 -solution -demo ^
  -shot  docs/screenshots/01-level.png -shotres 1440x900 -shotat 8

:: 关卡选择：13 关列表与每关进度
Build\dayu.exe -levelselect ^
  -shot  docs/screenshots/02-levels.png -shotres 1440x900 -shotat 6

:: 主菜单：标题、溟月立绘、章节进度
Build\dayu.exe -menu ^
  -shot  docs/screenshots/03-menu.png -shotres 1440x900 -shotat 6
```

约定：

- **分辨率固定 1440x900**。这是界面设计基准分辨率，顶栏 13 个按钮和右侧题栏正好铺满；换成 1600x1000 会把右侧面板和顶栏按钮挤出画面，拉高到 1600x1400 也不会自动重排。
- `-solution` 只把标准答案铺到画布用于截图，不写玩家存档。
- `-demo` 会在启动 1.2 秒后自动点一次「运行」，配合 `-shotat` 把截图卡在跑完的那一步。
- `-shotat` 是截图前的等待秒数，改完启动流程要重新试一次，等短了会截到相机补间中间态。
- 拍完进程自己退出，不留后台窗口。

需要真实鼠标键盘交互才能到的状态（拖拽、框选、滚轮）用 `tools/real_input.py` 加 `tools/scenarios/*.json` 驱动，截图落在 `Temp/shots/`。
