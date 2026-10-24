# 蜘蛛纸牌（含作弊功能）

Windows 桌面版蜘蛛纸牌，使用 C# + WPF（.NET 10）实现。在完整规则玩法的基础上内置两个作弊功能，并针对可玩性做了大量体验优化。

> [!WARNING]
> 本项目是许愿式 vibe coding 的典型 slop：人类只负责许愿（"加个深色模式"、"闪退了，修"），所有代码均由 AI 生成，本人从未通读。风险自负，质量随缘。

## 功能特性

### 基础玩法
- 104 张牌（2 副），支持单花色 / 双花色 / 四花色三种难度
- 拖拽移动整段同花色降序序列，双击自动寻找最佳落点
- 凑齐同花色 K→A 自动收入完成区，完成 8 组获胜
- 计分、步数、用时统计，支持多级撤销
- 提示系统：枚举当前所有可行走法并按优劣评分，连续按键在多个走法间轮换，蓝光脉冲高亮起点与终点
- 音效反馈（翻牌 / 落牌 / 发牌 / 收牌 / 胜利等）
- 深色 / 浅色双主题，启动时跟随系统，随时一键切换（牌面配色同步变化）
- 发牌动画、收牌入完成区的飞行动画（发牌直接凑齐序列时先发牌后收牌，两段衔接）
- 窗口任意缩放自动重排，牌堆拥挤时角标自动缩小并紧贴上缘
- 窗口内半透明浮层提示，无原生弹窗打断

### 作弊功能
| 功能 | 说明 |
|---|---|
| 翻牌 | 将所有暗牌直接翻开，可用 Ctrl+Z 撤销 |
| 强制移牌 | 任意明牌序列可移到任意列，不限同花降序与点数；发牌不再受空列限制 |

## 快捷键

| 按键 | 功能 |
|---|---|
| `Ctrl+Z` | 撤销 |
| `H` | 提示（连续按切换多个走法） |
| `D` | 发牌 |
| `P` | 作弊：翻开所有暗牌 |
| `F` | 作弊：强制移牌开关 |
| `M` | 音效开关 |
| `T` | 深色模式开关 |

## 构建与运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（Windows）。

```bash
# 直接运行
dotnet run

# 调试构建
dotnet build
```

### 发布单文件 exe

自包含单文件发布，目标机器无需安装 .NET：

```bash
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false `
  -o dist
```

产物为 `dist\SpiderSolitaireWithCheat.exe`（约 62 MB，已压缩）。

> 注：WPF 不支持 IL 裁剪（PublishTrimmed），XAML 反射机制裁剪后无法正常运行，因此通过压缩单文件控制体积。若目标机器已安装 .NET Desktop Runtime，可用 `--self-contained false` 将体积降到约 1–2 MB。

## 项目结构

```
├── App.xaml / App.xaml.cs          # 应用入口
├── MainWindow.xaml / .cs           # 主窗口、交互与动画编排
├── Controls/
│   ├── CardControl.cs              # 单张牌控件（纯代码绘制牌面，无图片资源）
│   └── StockPileControl.cs         # 发牌堆控件
├── Models/
│   ├── Card.cs                     # 牌
│   ├── GameState.cs                # 游戏状态（规则校验、发牌、收牌、克隆支持撤销）
│   └── MoveFinder.cs               # 走法搜索与评分（提示 / 双击自动移动）
└── Services/
    └── SoundFx.cs                  # 音效服务
```

## 技术说明

- 牌面全部由 WPF 矢量绘制（Viewbox + Border + TextBlock），无位图资源，缩放不失真
- 游戏逻辑与 UI 完全分离：`GameState` 为纯逻辑状态机，通过克隆实现撤销快照
- 动画基于 `RenderTransform` + 窗口坐标差值计算，跨行飞牌位置精确
- 主题切换采用未冻结共享画刷（改 `Color` 即全局生效），104 张牌零重建刷新
