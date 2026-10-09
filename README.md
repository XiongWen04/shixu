# 拾序 · Shixu

<img src="src/AppLogo.png" alt="拾序的小猫头鹰图标" width="96">

拾序是一款面向个人使用的 Windows 原生待办软件。用月历安排事情，用桌面小组件查看今天的任务；所有数据保存在本机，无需登录。

当前版本：**v1.3.2**。基于 **C# / WPF / .NET Framework 4.8**，便携运行，无第三方运行依赖。

## 功能

- 添加、编辑、删除待办，标记完成或恢复未完成，撤销最近一次删除。
- 可选日期、24 小时时间、地点和备注。
- 今天、全部及月历视图；点击日期查看和添加当天任务。
- 桌面小组件显示今天未完成及逾期事项，可直接完成或添加任务。
- 毛玻璃风格背景，顶部拖动自由摆放，右下角调整大小，记住位置。
- 蓝白、暖色和深色三套皮肤；标题栏同步配色。
- 小组件开关按钮显示当前操作，并随开关状态切换高亮。
- 自动保存、上一份有效备份、导入导出，以及自选数据保存目录。

时间用于安排和排序，暂不提供提醒通知、重复任务、账号、云同步或多人协作。

## 运行

推荐 Windows 11 64 位；Windows 10 64 位需已安装 .NET Framework 4.8。原生标题栏自定义配色依赖 Windows 11 的相关系统接口。

便携版解压后运行 `拾序.exe`，并保留旁边的 `拾序.exe.config`。关闭主窗口只会隐藏主窗口；彻底退出请在托盘菜单中选择“退出”。

如果仓库发布了便携包，可从 GitHub Releases 下载；源码仓库本身不包含编译后的程序。

详细操作见 [使用说明](docs/使用说明.md)。

## 从源码构建

在 Windows 上执行，构建前确认存在系统编译器：

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

在仓库根目录打开 Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1
```

输出位于 `dist\拾序\`，包含 `拾序.exe` 和配置文件。无需安装 Node.js、Python 或 NuGet 包。

构建并执行核心检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1 -TestRoot .\test-results
```

核心检查覆盖日期与时分校验、旧数据兼容、导入导出、原子保存、备份恢复、文件锁和迁移失败保护。

界面检查需交互式 Windows 桌面，会短暂打开测试窗口，仅向指定测试目录写入示例任务：

```powershell
$testPath = Join-Path $PWD 'test-results\ui'
New-Item -ItemType Directory -Path $testPath -Force | Out-Null
$run = Start-Process -FilePath '.\dist\拾序\拾序.exe' -ArgumentList @('--ui-test', ('"' + $testPath + '"')) -PassThru
$run.WaitForExit()
Get-Content -LiteralPath (Join-Path $testPath 'ui-report.txt')
```

界面检查应使用新的空测试目录，避免把已有测试任务再次加载。不要将测试目录指向实际待办数据目录。自动界面检查不能替代不同 Windows 环境中的实际使用验证。

## 数据与备份

默认数据位于程序旁的 `Data\tasks.json`；设置保存在 `preferences.json`。在设置中可以选择其他数据目录，迁移时保留原数据。

保存使用临时文件及原子替换，并保留 `tasks.json.bak`。读取失败不会把损坏文件当成空清单覆盖。导入会校验文件，并在替换前要求确认。

支持读取旧格式 v1 和当前格式 v2，修改后保存为 v2。已有 v2 数据应使用当前或更新版本管理。

个人数据、设置、备份、编译结果和开发临时目录均由 `.gitignore` 排除。**不要把整个运行目录或个人数据手动拖入 GitHub 上传页面。**

## 目录

```text
.
├── README.md
├── .gitignore
├── .gitattributes
├── src/                # 源码、图标、样式、构建与检查代码
├── docs/               # 使用说明
├── dist/               # 构建输出，忽略
├── test-results/       # 检查结果，忽略
├── outputs/            # 本机程序与交付文件，忽略
└── work/               # 本机历史备份与临时内容，忽略
```

`outputs/` 和 `work/` 是本机保留目录，不属于公开源码。新克隆的仓库不会包含它们。

## 实现说明与限制

- `Core.cs`：数据结构、校验、JSON 持久化、备份与迁移。
- `App.cs`：应用生命周期、共享状态、主题与托盘。
- `MainWindow.cs`、`Ui.cs`：主界面、待办编辑与设置。
- `WidgetWindow.cs`、`WidgetSurface.cs`：桌面小组件、拖动与壁纸模糊背景。
- `NativeDesktop.cs`：桌面所有者绑定、窗口层级及标题栏配色。
- `CoreTests.cs`、`UiSmoke.cs`：核心与界面检查。

小组件保持非置顶，并允许自由摆放；与桌面图标重叠时可能遮挡图标，不自动避让。毛玻璃使用模糊桌面壁纸与半透明色层，目前按“填充”布局映射；其他布局、多屏不同壁纸可能出现背景对齐差异。

桌面接入依赖 Explorer 窗口结构，未来系统更新或第三方桌面工具可能影响行为。未逐一验证所有 Windows 版本、多屏/DPI 组合或 Explorer 重启场景。

## 图标与许可

小猫头鹰图标为本项目经 AI 辅助生成并选定的视觉资源，源图及多尺寸 ICO 位于 `src/`。

目前尚未指定开源许可证。公开上传代码不等于授予开源使用、修改或分发许可；如果希望开放这些权限，请由项目维护者选择并添加相应 `LICENSE`。
