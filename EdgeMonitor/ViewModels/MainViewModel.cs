using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using EdgeMonitor.Services;
using EdgeMonitor.Commands;

namespace EdgeMonitor.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly IDataService _dataService;
        private readonly IDialogService _dialogService;
        private readonly IPrivilegeService _privilegeService;
        private readonly IEdgeMonitorService _edgeMonitorService;
        private readonly ILogService _logService;
        private readonly IConfigurationService _configService;
        
        private string _statusMessage = "就绪";
        private int _monitorInterval = 5;
        private bool _autoSaveEnabled = true;
        private DateTime _currentTime = DateTime.Now;
        private string _windowTitle = "Edge Monitor";
        private bool _isMonitoring = false;
        private System.Windows.Threading.DispatcherTimer? _monitorTimer;
        private bool _isCurrentlyMonitoring = false; // 防止重叠检查
        private CloseAction _closeAction = CloseAction.Ask;
        
        public MainViewModel(
            ILogger<MainViewModel> logger,
            IDataService dataService,
            IDialogService dialogService,
            IPrivilegeService privilegeService,
            IEdgeMonitorService edgeMonitorService,
            ILogService logService,
            IConfigurationService configService)
        {
            _logger = logger;
            _dataService = dataService;
            _dialogService = dialogService;
            _privilegeService = privilegeService;
            _edgeMonitorService = edgeMonitorService;
            _logService = logService;
            _configService = configService;
            
            // 订阅日志集合变化事件
            _logService.LogEntries.CollectionChanged += (s, e) => NotifyLogPropertiesChanged();
            _logService.MonitorEntries.CollectionChanged += (s, e) => NotifyLogPropertiesChanged();
            
            InitializeCommands();
            StartTimeUpdater();
            UpdateWindowTitle();
            LoadCloseActionSettings();
            
            // 启动时清理过期日志文件
            _ = Task.Run(async () => await _logService.CleanupOldLogFilesAsync());
        }

        #region Properties

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string MonitorData
        {
            get => string.Join("\n", _logService.MonitorEntries);
        }

        public string LogData
        {
            get => string.Join("\n", _logService.LogEntries);
        }

        /// <summary>
        /// 通知日志属性已更改
        /// </summary>
        private void NotifyLogPropertiesChanged()
        {
            OnPropertyChanged(nameof(MonitorData));
            OnPropertyChanged(nameof(LogData));
        }

        public int MonitorInterval
        {
            get => _monitorInterval;
            set 
            {
                // 验证输入值，确保在合理范围内
                var newValue = Math.Max(1, Math.Min(3600, value)); // 限制在1-3600秒之间
                if (SetProperty(ref _monitorInterval, newValue))
                {
                    // 如果监控正在运行，更新定时器间隔
                    if (IsMonitoring && _monitorTimer != null)
                    {
                        _monitorTimer.Interval = TimeSpan.FromSeconds(_monitorInterval);
                        _logger.LogInformation($"监控间隔已更新为: {_monitorInterval}秒");
                    }
                }
            }
        }

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => SetProperty(ref _autoSaveEnabled, value);
        }

        public DateTime CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(ref _isMonitoring, value);
        }

        public CloseAction CloseAction
        {
            get => _closeAction;
            set => SetProperty(ref _closeAction, value);
        }

        #endregion

        #region Commands

        public ICommand AboutCommand { get; private set; } = null!;
        public ICommand StartMonitoringCommand { get; private set; } = null!;
        public ICommand StopMonitoringCommand { get; private set; } = null!;
        public ICommand ClearLogsCommand { get; private set; } = null!;
        public ICommand CheckAdminCommand { get; private set; } = null!;
        public ICommand TestEdgeDetectionCommand { get; private set; } = null!;
        public ICommand ForceKillEdgeCommand { get; private set; } = null!;
        public ICommand ViewLogStatsCommand { get; private set; } = null!;
        public ICommand OpenLogFolderCommand { get; private set; } = null!;
        public ICommand ResetCloseChoiceCommand { get; private set; } = null!;

        #endregion

        private void InitializeCommands()
        {
            AboutCommand = new RelayCommand(ExecuteAbout);
            StartMonitoringCommand = new RelayCommand(ExecuteStartMonitoring);
            StopMonitoringCommand = new RelayCommand(ExecuteStopMonitoring);
            ClearLogsCommand = new RelayCommand(ExecuteClearLogs);
            CheckAdminCommand = new RelayCommand(ExecuteCheckAdmin);
            TestEdgeDetectionCommand = new RelayCommand(ExecuteTestEdgeDetection);
            ForceKillEdgeCommand = new RelayCommand(ExecuteForceKillEdge);
            ViewLogStatsCommand = new RelayCommand(ExecuteViewLogStats);
            OpenLogFolderCommand = new RelayCommand(ExecuteOpenLogFolder);
            ResetCloseChoiceCommand = new RelayCommand(ExecuteResetCloseChoice);
        }

        private void StartTimeUpdater()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) => CurrentTime = DateTime.Now;
            timer.Start();
        }

        private void UpdateWindowTitle()
        {
            var baseTitle = "Edge Monitor";
            if (_privilegeService.IsRunningAsAdministrator())
            {
                WindowTitle = $"{baseTitle} - 管理员";
                _logger.LogInformation("应用程序正在以管理员权限运行");
            }
            else
            {
                WindowTitle = baseTitle;
                _logger.LogWarning("应用程序未以管理员权限运行");
            }
        }

        #region Command Implementations

        private void ExecuteAbout()
        {
            _logger.LogInformation("显示关于对话框");
            var aboutWindow = new EdgeMonitor.Views.AboutWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            aboutWindow.ShowDialog();
        }

        private async void ExecuteStartMonitoring()
        {
            if (IsMonitoring)
            {
                _logger.LogWarning("监控已在运行中");
                return;
            }

            _logger.LogInformation("开始Edge监控");
            IsMonitoring = true;
            StatusMessage = "Edge监控已启动";
            
            var message = $"Edge监控已启动 - 检查间隔: {MonitorInterval}秒";
            await _logService.AddMonitorEntryAsync(message);
            await _logService.AddLogEntryAsync("INFO: Edge监控服务已启动");

            // 启动监控定时器
            _monitorTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(MonitorInterval)
            };
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            // 立即执行一次检查（同步方式，避免定时器冲突）
            _logger.LogInformation("立即执行首次Edge检查");
            _ = Task.Run(async () => 
            {
                await Task.Delay(1000); // 短暂延迟让界面更新
                await PerformEdgeMonitoringAsync();
            });
        }

        private async void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            // 防止重叠检查
            if (_isCurrentlyMonitoring)
            {
                _logger.LogWarning("上一次监控检查尚未完成，跳过本次检查");
                return;
            }

            _logger.LogInformation("定时器触发Edge监控检查");
            await PerformEdgeMonitoringAsync();
        }

        private async void ExecuteStopMonitoring()
        {
            if (!IsMonitoring)
            {
                _logger.LogWarning("监控未在运行");
                return;
            }

            _logger.LogInformation("停止Edge监控");
            IsMonitoring = false;
            StatusMessage = "Edge监控已停止";
            
            _monitorTimer?.Stop();
            _monitorTimer = null;
            
            var message = "Edge监控已停止";
            await _logService.AddMonitorEntryAsync(message);
            await _logService.AddLogEntryAsync("INFO: Edge监控服务已停止");
        }

        private void ExecuteClearLogs()
        {
            _logger.LogInformation("清除内存日志");
            StatusMessage = "内存日志已清除";
            _logService.ClearMemoryLogs();
        }

        private void ExecuteCheckAdmin()
        {
            var isAdmin = _privilegeService.IsRunningAsAdministrator();
            var status = isAdmin ? "是" : "否";
            _dialogService.ShowMessage("管理员权限检查", $"当前是否以管理员身份运行: {status}");
            
            if (!isAdmin)
            {
                var result = _dialogService.ShowConfirmation("权限提升", "是否要以管理员身份重新启动程序？");
                if (result)
                {
                    _privilegeService.RestartAsAdministrator();
                }
            }
        }

        private async void ExecuteTestEdgeDetection()
        {
            _logger.LogInformation("手动执行Edge检测测试");
            StatusMessage = "正在执行Edge检测测试...";
            
            try
            {
                var edgeProcesses = await _edgeMonitorService.GetEdgeProcessesAsync();
                var hasVisibleWindows = await _edgeMonitorService.HasVisibleWindowsAsync();
                var hasAbnormalUsage = _edgeMonitorService.HasAbnormalResourceUsage(edgeProcesses, 30.0, 2048);
                
                var totalCpu = edgeProcesses.Sum(p => p.CpuUsage);
                var totalMemory = edgeProcesses.Sum(p => p.MemoryUsageMB);
                
                var testResult = $"=== Edge检测测试结果 ===\n" +
                               $"进程数量: {edgeProcesses.Length}\n" +
                               $"总CPU使用: {totalCpu:F1}%\n" +
                               $"总内存使用: {totalMemory}MB\n" +
                               $"有可见窗口: {hasVisibleWindows}\n" +
                               $"资源异常: {hasAbnormalUsage}\n" +
                               $"满足终止条件: {!hasVisibleWindows && hasAbnormalUsage}\n" +
                               $"==================";
                
                await _logService.AddLogEntryAsync(testResult);
                
                foreach (var process in edgeProcesses)
                {
                    var processInfo = $"进程 {process.ProcessId}: {process.ProcessName}, " +
                                    $"CPU: {process.CpuUsage:F1}%, 内存: {process.MemoryUsageMB}MB, " +
                                    $"窗口数: {process.WindowCount}";
                    await _logService.AddLogEntryAsync(processInfo);
                }
                
                StatusMessage = $"测试完成 - 进程:{edgeProcesses.Length}, CPU:{totalCpu:F1}%, 内存:{totalMemory}MB";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Edge检测测试失败: {ex.Message}");
                await _logService.AddLogEntryAsync($"测试失败: {ex.Message}");
                StatusMessage = "测试失败";
            }
        }

        private async void ExecuteForceKillEdge()
        {
            _logger.LogInformation("手动强制终止Edge进程");
            
            var result = _dialogService.ShowConfirmation("强制终止Edge", "确定要强制终止所有Edge进程吗？");
            if (!result) return;
            
            try
            {
                StatusMessage = "正在强制终止Edge进程...";
                await _edgeMonitorService.KillAllEdgeProcessesAsync();
                
                var message = "手动强制终止Edge进程完成";
                await _logService.AddMonitorEntryAsync(message);
                await _logService.AddLogEntryAsync(message);
                StatusMessage = "Edge进程已被手动终止";
                
                _dialogService.ShowMessage("操作完成", "所有Edge进程已被强制终止");
            }
            catch (Exception ex)
            {
                _logger.LogError($"强制终止Edge进程失败: {ex.Message}");
                await _logService.AddLogEntryAsync($"强制终止失败: {ex.Message}");
                StatusMessage = "强制终止失败";
                _dialogService.ShowMessage("操作失败", $"强制终止Edge进程失败: {ex.Message}");
            }
        }

        private async void ExecuteViewLogStats()
        {
            try
            {
                var stats = await _logService.GetLogStatisticsAsync();
                var message = $"日志统计信息:\n" +
                             $"日志文件数量: {stats.TotalFiles}\n" +
                             $"总文件大小: {stats.TotalSizeMB:F2} MB\n" +
                             $"内存中日志条数: {stats.MemoryLogCount}\n" +
                             $"内存中监控数据条数: {stats.MemoryMonitorCount}\n" +
                             $"日志目录: {stats.LogDirectory}";
                
                _dialogService.ShowMessage("日志统计", message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"获取日志统计失败: {ex.Message}");
                _dialogService.ShowMessage("错误", $"获取日志统计失败: {ex.Message}");
            }
        }

        private void ExecuteOpenLogFolder()
        {
            try
            {
                var logPath = _logService.GetLogFilePath();
                System.Diagnostics.Process.Start("explorer.exe", logPath);
                _logger.LogInformation($"打开日志文件夹: {logPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"打开日志文件夹失败: {ex.Message}");
                _dialogService.ShowMessage("错误", $"打开日志文件夹失败: {ex.Message}");
            }
        }

        private async void ExecuteResetCloseChoice()
        {
            try
            {
                _configService.SetValue("UI:RememberCloseChoice", false);
                _configService.SetValue("UI:CloseToTray", "");
                await _configService.SaveAsync();
                
                CloseAction = CloseAction.Ask;
                
                _logger.LogInformation("关闭选择已重置");
                _dialogService.ShowMessage("成功", "关闭选择已重置，下次关闭时将重新询问。");
            }
            catch (Exception ex)
            {
                _logger.LogError($"重置关闭选择失败: {ex.Message}");
                _dialogService.ShowMessage("错误", $"重置关闭选择失败: {ex.Message}");
            }
        }

        private void LoadCloseActionSettings()
        {
            try
            {
                var rememberChoice = _configService.GetValue<bool>("UI:RememberCloseChoice");
                var savedChoice = _configService.GetValue<string>("UI:CloseToTray");

                if (rememberChoice && !string.IsNullOrEmpty(savedChoice))
                {
                    CloseAction = savedChoice.ToLower() switch
                    {
                        "true" => CloseAction.MinimizeToTray,
                        "false" => CloseAction.Exit,
                        _ => CloseAction.Ask
                    };
                }
                else
                {
                    CloseAction = CloseAction.Ask;
                }

                _logger.LogInformation($"加载关闭行为设置: {CloseAction}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"加载关闭行为设置失败: {ex.Message}");
                CloseAction = CloseAction.Ask;
            }
        }

        private async Task PerformEdgeMonitoringAsync()
        {
            // 设置监控标志，防止重叠
            if (_isCurrentlyMonitoring)
            {
                _logger.LogWarning("监控检查已在进行中，忽略重复调用");
                return;
            }

            _isCurrentlyMonitoring = true;
            var startTime = DateTime.Now;
            
            try
            {
                _logger.LogInformation($"[{startTime:HH:mm:ss.fff}] 开始执行Edge监控检查");
                
                // 获取Edge进程信息
                var processStartTime = DateTime.Now;
                var edgeProcesses = await _edgeMonitorService.GetEdgeProcessesAsync();
                var processEndTime = DateTime.Now;
                _logger.LogInformation($"[{processEndTime:HH:mm:ss.fff}] 获取Edge进程耗时: {(processEndTime - processStartTime).TotalMilliseconds}ms，检测到 {edgeProcesses.Length} 个Edge进程");
                
                if (!edgeProcesses.Any())
                {
                    var message = "未检测到Edge进程";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "未检测到Edge进程";
                    });
                    await _logService.AddMonitorEntryAsync(message);
                    _logger.LogInformation("未检测到Edge进程，跳过检查");
                    return;
                }

                // 检查是否有可见窗口
                var windowStartTime = DateTime.Now;
                var hasVisibleWindows = await _edgeMonitorService.HasVisibleWindowsAsync();
                var windowEndTime = DateTime.Now;
                var isRunningInBackground = !hasVisibleWindows && edgeProcesses.Any();
                _logger.LogInformation($"[{windowEndTime:HH:mm:ss.fff}] 窗口检查耗时: {(windowEndTime - windowStartTime).TotalMilliseconds}ms，结果: 可见窗口={hasVisibleWindows}, 后台运行={isRunningInBackground}");
                
                // 检查资源使用情况
                var resourceStartTime = DateTime.Now;
                var hasAbnormalUsage = _edgeMonitorService.HasAbnormalResourceUsage(edgeProcesses, 30.0, 2048);
                var resourceEndTime = DateTime.Now;
                
                var totalCpu = edgeProcesses.Sum(p => p.CpuUsage);
                var totalMemory = edgeProcesses.Sum(p => p.MemoryUsageMB);
                _logger.LogInformation($"[{resourceEndTime:HH:mm:ss.fff}] 资源检查耗时: {(resourceEndTime - resourceStartTime).TotalMilliseconds}ms，使用情况: CPU={totalCpu:F1}%, 内存={totalMemory}MB, 异常={hasAbnormalUsage}");
                
                var statusInfo = $"Edge进程: {edgeProcesses.Length}个 | CPU: {totalCpu:F1}% | 内存: {totalMemory}MB | 窗口: {(hasVisibleWindows ? "有" : "无")}";
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = statusInfo;
                });

                await _logService.AddMonitorEntryAsync(statusInfo);

                // 记录详细信息
                foreach (var process in edgeProcesses)
                {
                    var processDetail = $"进程详情: PID={process.ProcessId}, " +
                                      $"CPU={process.CpuUsage:F1}%, 内存={process.MemoryUsageMB}MB, " +
                                      $"窗口数={process.WindowCount}";
                    
                    await _logService.AddLogEntryAsync(processDetail);
                    
                    _logger.LogDebug($"进程 {process.ProcessId}: CPU={process.CpuUsage:F1}%, 内存={process.MemoryUsageMB}MB, 窗口={process.WindowCount}");
                }

                // 判断是否需要终止Edge
                _logger.LogInformation($"判断条件: 后台运行={isRunningInBackground}, 异常使用={hasAbnormalUsage}");
                if (isRunningInBackground && hasAbnormalUsage)
                {
                    _logger.LogWarning($"满足终止条件! Edge异常检测 - CPU: {totalCpu:F1}%, 内存: {totalMemory}MB, 后台运行: {isRunningInBackground}");
                    
                    var killMessage = "检测到Edge在后台运行且资源占用异常，正在终止...";
                    await _logService.AddMonitorEntryAsync(killMessage);
                    await _logService.AddLogEntryAsync($"WARNING: Edge异常检测 - CPU: {totalCpu:F1}%, 内存: {totalMemory}MB");

                    // 执行终止操作
                    var killStartTime = DateTime.Now;
                    await _edgeMonitorService.KillAllEdgeProcessesAsync();
                    var killEndTime = DateTime.Now;
                    _logger.LogInformation($"[{killEndTime:HH:mm:ss.fff}] Edge进程终止耗时: {(killEndTime - killStartTime).TotalMilliseconds}ms");
                    
                    var killedMessage = "Edge进程已被终止";
                    await _logService.AddMonitorEntryAsync(killedMessage);
                    await _logService.AddLogEntryAsync("INFO: Edge进程已被终止");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Edge进程已被终止";
                    });

                    // 静默终止，不显示通知
                    _logger.LogInformation($"Edge进程已静默终止 - CPU: {totalCpu:F1}%, 内存: {totalMemory}MB");
                }
                else
                {
                    _logger.LogInformation("不满足终止条件，继续监控");
                    await _logService.AddLogEntryAsync("INFO: 检查完成 - 不满足终止条件");
                }
                
                var endTime = DateTime.Now;
                _logger.LogInformation($"[{endTime:HH:mm:ss.fff}] Edge监控检查完成，总耗时: {(endTime - startTime).TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                var endTime = DateTime.Now;
                _logger.LogError($"[{endTime:HH:mm:ss.fff}] Edge监控过程中发生错误(耗时: {(endTime - startTime).TotalMilliseconds}ms): {ex.Message}");
                _logger.LogError($"错误堆栈: {ex.StackTrace}");
                
                var errorMessage = $"监控错误: {ex.Message}";
                await _logService.AddLogEntryAsync(errorMessage);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "监控出现错误";
                });
            }
            finally
            {
                // 重置监控标志
                _isCurrentlyMonitoring = false;
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
