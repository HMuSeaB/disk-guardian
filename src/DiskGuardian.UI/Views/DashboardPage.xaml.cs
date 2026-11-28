using System;
using System.IO;
using System.Threading.Tasks;
using DiskGuardian.Core.Diagnostics;
using DiskGuardian.Core.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DiskGuardian.UI.Views;

public partial class DashboardPage : Page
{
    private string? _lastBluetoothAdapterId;
    private System.Collections.Generic.List<PnpErrorDeviceItem> _lastSafeErrorDevices = new();

    public DashboardPage()
    {
        this.InitializeComponent();
        RefreshDrives();
        _ = RefreshProxyStatusAsync();
        _ = RefreshExplorerStatusAsync();
        _ = RefreshBrowserStatusAsync();
        _ = RefreshDeviceStatusAsync();
        _ = RefreshPowerStatusAsync();
    }

    private void RefreshDrives()
    {
        try
        {
            var driveC = new DriveInfo("C");
            if (driveC.IsReady)
            {
                long free = driveC.AvailableFreeSpace;
                long total = driveC.TotalSize;
                int pct = (int)((total - free) * 100 / total);
                CProgress.Value = pct;
                CText.Text = $"可用: {free / (1024 * 1024 * 1024.0):F2} GiB / {total / (1024 * 1024 * 1024.0):F2} GiB (已用 {pct}%)";
            }

            var driveD = new DriveInfo("D");
            if (driveD.IsReady)
            {
                long free = driveD.AvailableFreeSpace;
                long total = driveD.TotalSize;
                int pct = (int)((total - free) * 100 / total);
                DProgress.Value = pct;
                DText.Text = $"可用: {free / (1024 * 1024 * 1024.0):F2} GiB / {total / (1024 * 1024 * 1024.0):F2} GiB (已用 {pct}%)";
            }
        }
        catch { }
    }

    private async Task RefreshProxyStatusAsync()
    {
        try
        {
            var report = await Task.Run(() => ProxyDoctor.Diagnose());

            ProxySummaryText.Text = report.Summary;

            // 1. 顶部指示灯与 Badge
            if (report.HasAnomaly)
            {
                ProxyStatusIcon.Glyph = "\uE7BA"; // Warning icon
                ProxyStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                ProxyBadgeText.Text = "幽灵残留告警";
            }
            else
            {
                ProxyStatusIcon.Glyph = "\uE73E"; // Checkmark icon
                ProxyStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                ProxyBadgeText.Text = report.ProxyEnable ? "活跃代理" : "健康直连";
            }

            // 2. 四宫格指标 (Metric Tiles)
            // Tile 1: 工作模式
            ProxyModeValueText.Text = report.ProxyTypeDisplay;
            ProxyModeSubText.Text = report.ProxyEnable ? "系统级代理已开启" : (report.InconsistentFlags ? "底层残留手动标记" : "网络流量直接通信");

            // Tile 2: 目标服务器与端口
            if (!string.IsNullOrEmpty(report.ProxyServer))
            {
                ProxyEndpointValueText.Text = report.ProxyServer;
                ProxyEndpointSubText.Text = report.ProxyPort.HasValue && ProxyDoctor.IsLoopbackHost(report.ProxyHost) ? "本机回环代理" : "外部代理目标";
            }
            else
            {
                ProxyEndpointValueText.Text = "直连互联网";
                ProxyEndpointSubText.Text = "无需代理转发";
            }

            // Tile 3: 本地端口连通性
            ProxyPortHealthValueText.Text = report.PortStatusDisplay;
            ProxyPortHealthSubText.Text = report.PortLatencyMs.HasValue ? $"TCP 探测耗时: {report.PortLatencyMs}ms" : "无本地代理监听需求";

            // Tile 4: WinINET 标志位
            ProxyFlagsValueText.Text = report.BinaryFlagHex;
            ProxyFlagsSubText.Text = report.BinaryFlagDescription;

            // 3. 折叠面板取证细节
            DetailProxyEnableText.Text = report.ProxyEnable ? "1 (已开启)" : "0 (已关闭)";
            DetailProxyOverrideText.Text = string.IsNullOrEmpty(report.ProxyOverride) ? "(未设置绕过)" : report.ProxyOverride;
            DetailPacText.Text = string.IsNullOrEmpty(report.AutoConfigUrl) ? "(未配置 PAC 脚本)" : report.AutoConfigUrl;
            DetailEnvProxyText.Text = report.EnvStatusDisplay;
            DetailGitProxyText.Text = report.GitStatusDisplay;

            if (report.WorkBuddyDetected)
            {
                DetailAppHardeningLabel.Text = "WorkBuddy 隔离防线:";
                DetailAppHardeningText.Text = report.WorkBuddyStatusDisplay;
            }
            else
            {
                DetailAppHardeningLabel.Text = "已识别应用安全隔离:";
                DetailAppHardeningText.Text = report.AppHardeningDisplay;
            }

            if (report.IdeList.Count > 0)
            {
                var ideLines = report.IdeList.Select(i => $"• [{i.Name}] {i.StatusDisplay}\n  路径: {i.SettingsPath}");
                DetailIdeListText.Text = string.Join("\n", ideLines);
            }
            else
            {
                DetailIdeListText.Text = "本机未检测到 VS Code / Cursor / Trae 等 Agent IDE 默认配置目录。";
            }

            if (report.RunningProxyClients.Count > 0)
            {
                DetailRunningClientsText.Text = "• " + string.Join("\n• ", report.RunningProxyClients);
            }
            else
            {
                DetailRunningClientsText.Text = "当前无后台常驻的代理客户端进程。";
            }

            if (report.Details.Count > 0)
            {
                ProxyDetailLogsText.Text = "• " + string.Join("\n• ", report.Details);
            }
            else
            {
                ProxyDetailLogsText.Text = "WinINET 注册表标志位一致，无死端口，环境变量与 Git 配置纯净。";
            }
        }
        catch (Exception ex)
        {
            ProxySummaryText.Text = $"体检异常: {ex.Message}";
        }
    }

    private async void HardenIdeProxy_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "加固主流 Agent IDE 代理防线",
            Content = "将为您自动探测本机已安装的 VS Code, Cursor, Trae, WorkBuddy, Windsurf 等 IDE，安全注入：\n\n1. http.proxySupport: \"on\"\n2. http.proxy: 绑定当前活跃代理 (如 127.0.0.1:7890)\n\n解决自定义海外模型（如 DeepSeek、Claude、OpenAI、TokenHarbor 等）因缺少代理设置而走直连断网的问题。\n原配置文件将自动生成 .bak 备份。\n\n是否立即加固？",
            PrimaryButtonText = "立即加固",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var (count, logs) = await Task.Run(() => ProxyDoctor.HardenAllIdeProxies());
        var tip = new ContentDialog
        {
            Title = "加固完成",
            Content = $"已成功加固 {count} 款 Agent IDE：\n\n✓ " + string.Join("\n✓ ", logs) + "\n\n重启对应 IDE 后，自定义模型即可顺畅连通！",
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await tip.ShowAsync();
        await RefreshProxyStatusAsync();
    }

    private async void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        var (ok, path, err) = ShortcutManager.CreateDesktopShortcut();
        if (ok)
        {
            var tip = new ContentDialog
            {
                Title = "桌面快捷方式创建成功",
                Content = $"已成功在您的 Windows 桌面上生成规范快捷方式：\n\n{path}\n\n您现在可以随时从桌面双击直接启动 DiskGuardian。",
                CloseButtonText = "好的",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        else
        {
            var tip = new ContentDialog
            {
                Title = "创建快捷方式失败",
                Content = $"未能生成桌面快捷方式：\n{err}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
    }

    private async void RefreshProxy_Click(object sender, RoutedEventArgs e)
    {
        await RefreshProxyStatusAsync();
    }

    private async void FixProxy_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "确认安全修复系统代理",
            Content = "将为您执行以下安全修复步骤：\n\n1. 自动备份当前注册表原值至 out/ 目录（可随时还原）；\n2. 清洗 DefaultConnectionSettings 二进制标志位为纯直连；\n3. 通过 WinINet API 向操作系统广播刷新（绝不杀 explorer.exe）；\n4. 清理残留的用户环境变量并在识别到下游应用时加固防线。\n\n是否立即执行？",
            PrimaryButtonText = "确认修复",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var repairResult = await Task.Run(() => ProxyDoctor.Repair(hardenWorkBuddy: true));
        if (repairResult.Succeeded)
        {
            var actionsStr = string.Join("\n✓ ", repairResult.ActionsTaken);
            var tip = new ContentDialog
            {
                Title = "修复成功",
                Content = $"已成功清洗并恢复网络配置！\n\n✓ {actionsStr}\n\n备份已保存至:\n{repairResult.BackupPath}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        else
        {
            var tip = new ContentDialog
            {
                Title = "修复遇到问题",
                Content = repairResult.ErrorMessage,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }

        await RefreshProxyStatusAsync();
    }

    private async void PureDirect_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "一键切回纯净直连模式 (适合打游戏/局域网)",
            Content = "将为您执行以下彻底脱网与直连动作：\n\n1. 安全退出后台常驻的代理客户端（如 FlClash 托盘与内核进程，完全保留您的订阅与配置文件，绝不损坏）；\n2. 强制将 Windows 系统代理 ProxyEnable 重置为 0，清空 ProxyServer；\n3. 清洗底层二进制标志位为 0x01 (直连)；\n4. WinINET 原生广播刷新 (全局即刻生效，绝不杀 explorer.exe)；\n5. 清除系统 DNS 缓存 (彻底消除代理 Fake-IP 域名残留)。\n\n是否立即执行？",
            PrimaryButtonText = "确认直连",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var repairResult = await Task.Run(() => ProxyDoctor.SwitchToPureDirect(terminateRunningClients: true));
        if (repairResult.Succeeded)
        {
            var actionsStr = string.Join("\n✓ ", repairResult.ActionsTaken);
            var tip = new ContentDialog
            {
                Title = "已恢复纯净直连模式",
                Content = $"系统代理已彻底清空，游戏可顺畅直连！\n\n✓ {actionsStr}\n\n【说明】\n您的 FlClash 配置与订阅已完整保留，下次需要时直接重新启动 FlClash 即可。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        else
        {
            var tip = new ContentDialog
            {
                Title = "直连切换遇到问题",
                Content = repairResult.ErrorMessage,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }

        await RefreshProxyStatusAsync();
    }

    private async Task RefreshExplorerStatusAsync()
    {
        try
        {
            var report = await Task.Run(() => ExplorerDoctor.Diagnose());

            ExplorerSummaryText.Text = report.Summary;

            // 1. 顶部指示灯与 Badge
            if (report.HasAnomaly)
            {
                ExplorerStatusIcon.Glyph = "\uE7BA"; // Warning icon
                ExplorerStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                ExplorerBadgeText.Text = "外壳降级/阻断";
            }
            else
            {
                ExplorerStatusIcon.Glyph = "\uE73E"; // Checkmark icon
                ExplorerStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                ExplorerBadgeText.Text = "原生标签页模式";
            }

            // 2. 四宫格指标 (Metric Tiles)
            ExplorerArchValueText.Text = report.ShellArchitecture;
            ExplorerArchSubText.Text = report.IsWin11 ? "Win11 原生 XAML/WASDK" : "Win10 传统 Win32 宿主";

            ExplorerBlockedValueText.Text = report.IsModernShellBlocked ? "异常拦截" : "正常放行";
            ExplorerBlockedSubText.Text = report.BlockedLocations;

            ExplorerSeparateProcessValueText.Text = report.SeparateProcessEnabled ? "已开启 (冲突)" : "未开启 (推荐)";
            ExplorerSeparateProcessSubText.Text = report.SeparateProcessEnabled ? "多标签页宿主脱轨" : "单进程多标签共享";

            ExplorerSnippingValueText.Text = report.IsSnippingStuck ? $"⚠️ 堆积 {report.StuckSnippingCount} 个" : (report.StuckSnippingCount == 1 ? "1 进程驻留 (就绪)" : "通道通畅 (空闲)");
            ExplorerSnippingSubText.Text = report.IsSnippingStuck ? "快捷键卡死 (需重置)" : "无死锁残留";

            // 3. 折叠面板取证细节
            DetailExplorerSysVerText.Text = report.SystemVersionDisplay;
            DetailExplorerClsidText.Text = report.BlockedStatusDisplay;
            DetailExplorerSepProcText.Text = report.SeparateProcessDisplay;
            DetailExplorerIntegrityText.Text = report.IntegrityStatusDisplay;
            DetailExplorerSnippingText.Text = report.SnippingStatusDisplay;

            if (report.Details.Count > 0)
            {
                ExplorerDetailLogsText.Text = string.Join("\n", report.Details);
            }
            else
            {
                ExplorerDetailLogsText.Text = "系统外壳配置纯净，处于 Windows 11 原生多标签页模式。";
            }
        }
        catch (Exception ex)
        {
            ExplorerSummaryText.Text = $"外壳体检异常: {ex.Message}";
        }
    }

    private async void RefreshExplorer_Click(object sender, RoutedEventArgs e)
    {
        await RefreshExplorerStatusAsync();
    }

    private async void ResetSnipping_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (ok, count, msg) = await Task.Run(() => ExplorerDoctor.ResetSnippingTool());
            var tip = new ContentDialog
            {
                Title = ok ? "⚡ 截图通道重置完成" : "重置提示",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        catch (Exception ex)
        {
            var tip = new ContentDialog
            {
                Title = "重置遇到异常",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            await RefreshExplorerStatusAsync();
        }
    }

    private async void FixExplorer_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "一键恢复 Windows 11 原生资源管理器",
            Content = "将为您执行以下安全恢复步骤：\n\n1. 从注册表中安全移除现代命令栏 (CLSID {e2bf9676-...}) 的 Blocked 屏蔽项；\n2. 将【在单独的进程中打开文件夹窗口】重置为 0（恢复多标签页无缝支持）；\n3. 向 Windows 桌面外壳广播 WM_SETTINGCHANGE 设置刷新事件（绝不暴力杀 explorer.exe）；\n\n是否立即执行？",
            PrimaryButtonText = "确认恢复",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var repairResult = await Task.Run(() => ExplorerDoctor.Repair());
        if (repairResult.Succeeded)
        {
            var actionsStr = string.Join("\n✓ ", repairResult.ActionsTaken);
            var tip = new ContentDialog
            {
                Title = "外壳恢复成功",
                Content = $"已成功恢复 Windows 11 原生配置！\n\n✓ {actionsStr}\n\n【生效方式】\n新打开的窗口（Win + E）将即刻生效；\n若需当前已打开的窗口刷新，可在任务管理器中右键【Windows 资源管理器】点击【重新启动】。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        else
        {
            var tip = new ContentDialog
            {
                Title = "恢复遇到问题",
                Content = repairResult.ErrorMessage,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }

        await RefreshExplorerStatusAsync();
    }

    private async Task RefreshBrowserStatusAsync()
    {
        try
        {
            var report = await Task.Run(() =>
            {
                var r = BrowserDoctor.Diagnose("msedge");
                if (!r.IsRunning)
                {
                    var chromeReport = BrowserDoctor.Diagnose("chrome");
                    if (chromeReport.IsRunning) return chromeReport;
                }
                return r;
            });

            BrowserSummaryText.Text = report.SummaryMessage;

            // 1. 顶部指示灯与 Badge
            if (report.HealthRating == "critical")
            {
                BrowserStatusIcon.Glyph = "\uE7BA"; // Error / Warning
                BrowserStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                BrowserBadgeText.Text = "严重超载";
            }
            else if (report.HealthRating == "warning")
            {
                BrowserStatusIcon.Glyph = "\uE7BA"; // Warning
                BrowserStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                BrowserBadgeText.Text = "显存膨胀";
            }
            else if (report.HealthRating == "healthy")
            {
                BrowserStatusIcon.Glyph = "\uE73E"; // Checkmark
                BrowserStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                BrowserBadgeText.Text = "运行极佳";
            }
            else
            {
                BrowserStatusIcon.Glyph = "\uE774"; // Globe
                BrowserStatusIcon.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                BrowserBadgeText.Text = "未运行";
            }

            // 2. 4 宫格指标
            // Tile 1: 运行实例
            BrowserInstanceValueText.Text = report.IsRunning ? $"{report.DisplayName} ({report.TotalProcessCount} 进程)" : "未检测到运行";
            BrowserInstanceSubText.Text = report.IsRunning ? $"主进程 PID: {report.MainProcessId}" : "可直接启动浏览器使用";

            // Tile 2: 总物理内存
            BrowserMemValueText.Text = report.IsRunning ? report.TotalMemoryHuman : "--";
            BrowserMemSubText.Text = report.IsRunning ? $"活跃网页 {report.RendererCount} / 扩展 {report.ExtensionCount}" : "无常驻进程";

            // Tile 3: GPU 显存
            BrowserGpuValueText.Text = report.IsRunning ? report.GpuMemoryHuman : "--";
            BrowserGpuSubText.Text = report.IsRunning ? (report.IsGpuBloated ? "⚠️ 显存膨胀卡顿 (建议释放)" : "图形通道运行健康") : "无显存占用";

            // Tile 4: 性能健康评级
            BrowserHealthValueText.Text = report.HealthRatingDisplay;
            BrowserHealthSubText.Text = report.IsRunning ? (report.IsGpuBloated ? "显卡渲染通道负载偏高" : "内存与显存配比良好") : "就绪";

            // 3. 详细子进程与日志
            if (report.TopProcesses.Count > 0)
            {
                var lines = report.TopProcesses.Select(p =>
                    $"· PID {p.Id,-6} | {p.WorkingSetHuman,-10} | {p.TypeDisplay} {(string.IsNullOrEmpty(p.Detail) ? "" : $"[{p.Detail}]")}");
                BrowserDetailProcessesText.Text = string.Join("\n", lines);
            }
            else
            {
                BrowserDetailProcessesText.Text = "暂无活跃子进程。";
            }

            BrowserDetailLogsText.Text = report.Details.Count > 0 ? string.Join("\n", report.Details) : "无额外诊断日志";
        }
        catch (Exception ex)
        {
            BrowserSummaryText.Text = $"体检失败: {ex.Message}";
        }
    }

    private async void RefreshBrowser_Click(object sender, RoutedEventArgs e)
    {
        await RefreshBrowserStatusAsync();
    }

    private async void ReleaseGpu_Click(object sender, RoutedEventArgs e)
    {
        ReleaseGpuButton.IsEnabled = false;
        try
        {
            var (ok, msg, freed) = await Task.Run(() => BrowserDoctor.ReleaseGpuMemory("msedge"));
            var tip = new ContentDialog
            {
                Title = ok ? "⚡ 显存释放成功" : "显存释放提示",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        catch (Exception ex)
        {
            var tip = new ContentDialog
            {
                Title = "释放显存遇到异常",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            ReleaseGpuButton.IsEnabled = true;
            await RefreshBrowserStatusAsync();
        }
    }

    private async void TrimBrowser_Click(object sender, RoutedEventArgs e)
    {
        TrimBrowserButton.IsEnabled = false;
        try
        {
            var (ok, msg, freed) = await Task.Run(() => BrowserDoctor.TrimMemory("msedge"));
            var tip = new ContentDialog
            {
                Title = ok ? "🧹 内存修剪完成" : "内存修剪提示",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        catch (Exception ex)
        {
            var tip = new ContentDialog
            {
                Title = "内存修剪遇到异常",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            TrimBrowserButton.IsEnabled = true;
            await RefreshBrowserStatusAsync();
        }
    }

    private async Task RefreshDeviceStatusAsync()
    {
        try
        {
            var report = await Task.Run(() => DeviceDoctor.Diagnose());
            _lastBluetoothAdapterId = report.BluetoothAdapterId;
            _lastSafeErrorDevices = report.ErrorDevices.FindAll(d => d.IsSafeToDisable);

            DeviceSummaryText.Text = report.SummaryMessage;

            if (report.HealthRating == "storm")
            {
                DeviceStatusIcon.Glyph = "\uE7BA";
                DeviceStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                DeviceBadgeText.Text = "重连风暴告警";
            }
            else if (report.HealthRating == "warning")
            {
                DeviceStatusIcon.Glyph = "\uE7BA";
                DeviceStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                DeviceBadgeText.Text = "存在异常";
            }
            else
            {
                DeviceStatusIcon.Glyph = "\uE73E";
                DeviceStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                DeviceBadgeText.Text = "稳定运行";
            }

            DeviceStormValueText.Text = report.TotalStormEvents > 0 ? $"{report.TotalStormEvents} 次" : "0 次";
            DeviceStormSubText.Text = report.BthStormCount > 0
                ? $"BTHUSB={report.BthStormCount}, USB={report.UsbResetCount}"
                : "近 15 分钟无掉线枚举";

            DeviceBthValueText.Text = report.BluetoothAdapterHealthy ? "正常在线" : "状态异常";
            DeviceBthSubText.Text = report.BluetoothAdapterName;

            int bloated = report.InputHosts.FindAll(h => h.IsBloated).Count;
            DeviceInputValueText.Text = bloated > 0 ? $"{bloated} 个膨胀" : $"{report.InputHosts.Count} 个正常";
            DeviceInputSubText.Text = bloated > 0 ? "建议刷新菜单与输入法" : "Win 键与输入响应良好";

            DeviceHealthValueText.Text = report.HealthRatingDisplay;
            DeviceHealthSubText.Text = report.ErrorDevices.Count > 0
                ? (report.SafeToDisableErrorCount > 0
                    ? $"{report.ErrorDevices.Count} 个异常 (可清理 {report.SafeToDisableErrorCount} 项)"
                    : $"{report.ErrorDevices.Count} 个 PnP 错误设备")
                : "PnP 设备树正常";

            DisableErrorDevicesButton.IsEnabled = _lastSafeErrorDevices.Count > 0;
            DisableBthButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastBluetoothAdapterId) &&
                                         (report.BthStormCount >= 3 || !report.BluetoothAdapterHealthy);

            if (report.InputHosts.Count > 0)
            {
                var lines = new System.Collections.Generic.List<string>();
                foreach (var h in report.InputHosts)
                {
                    lines.Add($"• {h.ProcessName} (PID {h.Pid}) | {h.WorkingSetHuman} | {h.ThreadCount} 线程{(h.IsBloated ? " [⚠️ 膨胀]" : "")}");
                }
                DeviceInputDetailsText.Text = string.Join("\n", lines);
            }
            else
            {
                DeviceInputDetailsText.Text = "未检测到活跃的桌面输入宿主";
            }

            DeviceEventDetailsText.Text = report.Details.Count > 0
                ? string.Join("\n", report.Details)
                : "近 15 分钟无硬件错误或 PnP 重枚举事件。";
        }
        catch (Exception ex)
        {
            DeviceSummaryText.Text = $"硬件与输入体检异常: {ex.Message}";
        }
    }

    private async void RefreshDevice_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDeviceStatusAsync();
    }

    private async void ResetInputHosts_Click(object sender, RoutedEventArgs e)
    {
        ResetInputButton.IsEnabled = false;
        try
        {
            var (ok, count, msg) = await Task.Run(() => DeviceDoctor.ResetInputHosts());
            var tip = new ContentDialog
            {
                Title = ok ? "🔄 桌面输入与菜单已刷新" : "刷新提示",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            ResetInputButton.IsEnabled = true;
            await Task.Delay(600);
            await RefreshDeviceStatusAsync();
        }
    }

    private async void DisableStormBluetooth_Click(object sender, RoutedEventArgs e)
    {
        DisableBthButton.IsEnabled = false;
        try
        {
            var id = _lastBluetoothAdapterId;
            var (ok, msg) = await Task.Run(() => DeviceDoctor.DisableStormBluetooth(id));
            var tip = new ContentDialog
            {
                Title = ok ? "⚡ 蓝牙风暴已止损" : "止损失败",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            await RefreshDeviceStatusAsync();
        }
    }

    private async void DisableErrorDevices_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSafeErrorDevices.Count == 0) return;

        var listText = string.Join("\n", _lastSafeErrorDevices.Select(d => $"• {d.Name} ({d.DeviceId})"));
        var confirm = new ContentDialog
        {
            Title = $"确认禁用 {_lastSafeErrorDevices.Count} 个重复异常虚拟设备？",
            Content = $"以下设备存在同名正常实例在工作，当前冗余节点报 Code 10 错误：\n\n{listText}\n\n将通过 pnputil 安全禁用这些冗余实例（需要管理员权限，随时可在设备管理器重新启用）。",
            PrimaryButtonText = "确认禁用",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        DisableErrorDevicesButton.IsEnabled = false;
        try
        {
            var targets = _lastSafeErrorDevices.ToArray();
            var (okCount, failCount, logs) = await Task.Run(() => DeviceDoctor.DisableSafeErrorDevices(targets));
            var tip = new ContentDialog
            {
                Title = failCount == 0 ? "🧹 冗余虚拟设备已清理" : "部分设备操作需要管理员权限",
                Content = $"成功 {okCount} 项，失败 {failCount} 项：\n\n{string.Join("\n", logs)}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            await RefreshDeviceStatusAsync();
        }
    }

    private async Task RefreshPowerStatusAsync()
    {
        try
        {
            var report = await Task.Run(() => PowerDoctor.Diagnose());
            PowerSummaryText.Text = report.SummaryMessage;

            if (report.HealthRating == "aggressive" || report.HealthRating == "warning")
            {
                PowerStatusIcon.Glyph = "\uE7BA";
                PowerStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                PowerBadgeText.Text = report.HealthRating == "aggressive" ? "偏激进易高温" : "检测受限";
            }
            else
            {
                PowerStatusIcon.Glyph = "\uE73E";
                PowerStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                PowerBadgeText.Text = "调度均衡";
            }

            PowerSchemeValueText.Text = report.ActiveSchemeName;
            PowerSchemeSubText.Text = string.IsNullOrEmpty(report.ActiveSchemeGuid) ? "--" : report.ActiveSchemeGuid;

            PowerEppValueText.Text = report.EppDisplay;
            PowerEppSubText.Text = report.PerfEppAc.HasValue && report.PerfEppAc.Value <= 15
                ? "⚠️ 偏激进 (建议 40%~50%)"
                : "轻载自动降频降温";

            PowerEpp1ValueText.Text = report.Epp1Display;
            PowerEpp1SubText.Text = "能效核节能偏好";

            PowerCoreValueText.Text = report.CoreParkDisplay;
            PowerCoreSubText.Text = $"提升策略: {report.BoostModeDisplay}";

            PowerDetailLogsText.Text = report.Details.Count > 0
                ? string.Join("\n", report.Details)
                : "无额外诊断详情。";
        }
        catch (Exception ex)
        {
            PowerSummaryText.Text = $"电源调度体检异常: {ex.Message}";
        }
    }

    private async void RefreshPower_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPowerStatusAsync();
    }

    private async void OptimizePower_Click(object sender, RoutedEventArgs e)
    {
        OptimizePowerButton.IsEnabled = false;
        try
        {
            var (ok, msg) = await Task.Run(() => PowerDoctor.OptimizeGentle());
            var tip = new ContentDialog
            {
                Title = ok ? "🌡️ 电源温和降温已应用" : "调优未完成",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            OptimizePowerButton.IsEnabled = true;
            await RefreshPowerStatusAsync();
        }
    }

    private async void RestorePower_Click(object sender, RoutedEventArgs e)
    {
        RestorePowerButton.IsEnabled = false;
        try
        {
            var (ok, msg) = await Task.Run(() => PowerDoctor.RestoreDefault());
            var tip = new ContentDialog
            {
                Title = ok ? "已恢复默认电源偏好" : "恢复未完成",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        finally
        {
            RestorePowerButton.IsEnabled = true;
            await RefreshPowerStatusAsync();
        }
    }

    private async void FullScan_Click(object sender, RoutedEventArgs e)
    {
        FullScanBtn.IsEnabled = false;
        try
        {
            RefreshDrives();
            await Task.WhenAll(
                RefreshProxyStatusAsync(),
                RefreshExplorerStatusAsync(),
                RefreshBrowserStatusAsync(),
                RefreshDeviceStatusAsync(),
                RefreshPowerStatusAsync());
        }
        finally
        {
            FullScanBtn.IsEnabled = true;
        }
    }

    private async void LaunchSystemTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        string toolsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SystemTools");
        string? target = tag switch
        {
            "wechat" => Path.Combine(toolsRoot, "微信文件归集.bat"),
            "2fa" => Path.Combine(toolsRoot, "2FA-Vault", "2fa.html"),
            "wintuner" => Path.Combine(toolsRoot, "wintuner.exe"),
            "folder" => toolsRoot,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(target) || (!File.Exists(target) && !Directory.Exists(target)))
        {
            var tip = new ContentDialog
            {
                Title = "未找到工具文件",
                Content = $"路径不存在: {target}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target)
            });
        }
        catch (Exception ex)
        {
            var err = new ContentDialog
            {
                Title = "启动失败",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await err.ShowAsync();
        }
    }
}
