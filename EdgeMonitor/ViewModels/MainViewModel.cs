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
        
        private string _statusMessage = "就绪";
        private string _monitorData = "";
        private string _logData = "";
        private int _monitorInterval = 5;
        private bool _autoSaveEnabled = true;
        private bool _notificationsEnabled = true;
        private DateTime _currentTime = DateTime.Now;
        private string _windowTitle = "Edge Monitor";
        private bool _isMonitoring = false;
        private System.Windows.Threading.DispatcherTimer? _monitorTimer;
        private bool _isCurrentlyMonitoring = false; // 防止重叠检查
        
        public MainViewModel(
            ILogger<MainViewModel> logger,
            IDataService dataService,
            IDialogService dialogService,
            IPrivilegeService privilegeService,
            IEdgeMonitorService edgeMonitorService)
        {
            _logger = logger;
            _dataService = dataService;
            _dialogService = dialogService;
            _privilegeService = privilegeService;
            _edgeMonitorService = edgeMonitorService;
            
            InitializeCommands();
            StartTimeUpdater();
            UpdateWindowTitle();
        }

        #region Properties

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string MonitorData
        {
            get => _monitorData;
            set => SetProperty(ref _monitorData, value);
        }

        public string LogData
        {
            get => _logData;
            set => SetProperty(ref _logData, value);
        }

        public int MonitorInterval
        {
            get => _monitorInterval;
            set => SetProperty(ref _monitorInterval, value);
        }

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => SetProperty(ref _autoSaveEnabled, value);
        }

        public bool NotificationsEnabled
        {
            get => _notificationsEnabled;
            set => SetProperty(ref _notificationsEnabled, value);
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

        #endregion

        #region Commands

        public ICommand AboutCommand { get; private set; } = null!;
        public ICommand StartMonitoringCommand { get; private set; } = null!;
        public ICommand StopMonitoringCommand { get; private set; } = null!;
        public ICommand ClearLogsCommand { get; private set; } = null!;
        public ICommand CheckAdminCommand { get; private set; } = null!;
        public ICommand TestEdgeDetectionCommand { get; private set; } = null!;
        public ICommand ForceKillEdgeCommand { get; private set; } = null!;

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

        private void ExecuteStartMonitoring()
        {
            if (IsMonitoring)
            {
                _logger.LogWarning("监控已在运行中");
                return;
            }

            _logger.LogInformation("开始Edge监控");
            IsMonitoring = true;
            StatusMessage = "Edge监控已启动";
            
            var message = $"[{DateTime.Now:HH:mm:ss}] Edge监控已启动 - 检查间隔: {MonitorInterval}秒\n";
            MonitorData += message;
            LogData += $"[{DateTime.Now:HH:mm:ss}] INFO: Edge监控服务已启动\n";

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

        private void ExecuteStopMonitoring()
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
            
            var message = $"[{DateTime.Now:HH:mm:ss}] Edge监控已停止\n";
            MonitorData += message;
            LogData += $"[{DateTime.Now:HH:mm:ss}] INFO: Edge监控服务已停止\n";
        }

        private void ExecuteClearLogs()
        {
            _logger.LogInformation("清除日志");
            StatusMessage = "日志已清除";
            MonitorData = "";
            LogData = "";
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
                               $"==================\n\n";
                
                LogData += $"[{DateTime.Now:HH:mm:ss}] {testResult}";
                
                foreach (var process in edgeProcesses)
                {
                    var processInfo = $"进程 {process.ProcessId}: {process.ProcessName}, " +
                                    $"CPU: {process.CpuUsage:F1}%, 内存: {process.MemoryUsageMB}MB, " +
                                    $"窗口数: {process.WindowCount}\n";
                    LogData += $"[{DateTime.Now:HH:mm:ss}] {processInfo}";
                }
                
                StatusMessage = $"测试完成 - 进程:{edgeProcesses.Length}, CPU:{totalCpu:F1}%, 内存:{totalMemory}MB";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Edge检测测试失败: {ex.Message}");
                LogData += $"[{DateTime.Now:HH:mm:ss}] 测试失败: {ex.Message}\n";
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
                
                var message = $"[{DateTime.Now:HH:mm:ss}] 手动强制终止Edge进程完成\n";
                MonitorData += message;
                LogData += message;
                StatusMessage = "Edge进程已被手动终止";
                
                _dialogService.ShowMessage("操作完成", "所有Edge进程已被强制终止");
            }
            catch (Exception ex)
            {
                _logger.LogError($"强制终止Edge进程失败: {ex.Message}");
                LogData += $"[{DateTime.Now:HH:mm:ss}] 强制终止失败: {ex.Message}\n";
                StatusMessage = "强制终止失败";
                _dialogService.ShowMessage("操作失败", $"强制终止Edge进程失败: {ex.Message}");
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
                    var message = $"[{DateTime.Now:HH:mm:ss}] 未检测到Edge进程\n";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MonitorData += message;
                        StatusMessage = "未检测到Edge进程";
                    });
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
                    var message = $"[{DateTime.Now:HH:mm:ss}] {statusInfo}\n";
                    MonitorData += message;
                    StatusMessage = statusInfo;
                });

                // 记录详细信息
                foreach (var process in edgeProcesses)
                {
                    var processDetail = $"[{DateTime.Now:HH:mm:ss}] 进程详情: PID={process.ProcessId}, " +
                                      $"CPU={process.CpuUsage:F1}%, 内存={process.MemoryUsageMB}MB, " +
                                      $"窗口数={process.WindowCount}\n";
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LogData += processDetail;
                    });
                    
                    _logger.LogDebug($"进程 {process.ProcessId}: CPU={process.CpuUsage:F1}%, 内存={process.MemoryUsageMB}MB, 窗口={process.WindowCount}");
                }

                // 判断是否需要终止Edge
                _logger.LogInformation($"判断条件: 后台运行={isRunningInBackground}, 异常使用={hasAbnormalUsage}");
                if (isRunningInBackground && hasAbnormalUsage)
                {
                    _logger.LogWarning($"满足终止条件! Edge异常检测 - CPU: {totalCpu:F1}%, 内存: {totalMemory}MB, 后台运行: {isRunningInBackground}");
                    
                    var killMessage = $"[{DateTime.Now:HH:mm:ss}] 检测到Edge在后台运行且资源占用异常，正在终止...\n";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MonitorData += killMessage;
                        LogData += $"[{DateTime.Now:HH:mm:ss}] WARNING: Edge异常检测 - CPU: {totalCpu:F1}%, 内存: {totalMemory}MB\n";
                    });

                    // 执行终止操作
                    var killStartTime = DateTime.Now;
                    await _edgeMonitorService.KillAllEdgeProcessesAsync();
                    var killEndTime = DateTime.Now;
                    _logger.LogInformation($"[{killEndTime:HH:mm:ss.fff}] Edge进程终止耗时: {(killEndTime - killStartTime).TotalMilliseconds}ms");
                    
                    var killedMessage = $"[{DateTime.Now:HH:mm:ss}] Edge进程已被终止\n";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MonitorData += killedMessage;
                        LogData += $"[{DateTime.Now:HH:mm:ss}] INFO: Edge进程已被终止\n";
                        StatusMessage = "Edge进程已被终止";
                    });

                    // 静默终止，不显示通知
                    _logger.LogInformation($"Edge进程已静默终止 - CPU: {totalCpu:F1}%, 内存: {totalMemory}MB");
                }
                else
                {
                    _logger.LogInformation("不满足终止条件，继续监控");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LogData += $"[{DateTime.Now:HH:mm:ss}] INFO: 检查完成 - 不满足终止条件\n";
                    });
                }
                
                var endTime = DateTime.Now;
                _logger.LogInformation($"[{endTime:HH:mm:ss.fff}] Edge监控检查完成，总耗时: {(endTime - startTime).TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                var endTime = DateTime.Now;
                _logger.LogError($"[{endTime:HH:mm:ss.fff}] Edge监控过程中发生错误(耗时: {(endTime - startTime).TotalMilliseconds}ms): {ex.Message}");
                _logger.LogError($"错误堆栈: {ex.StackTrace}");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var errorMessage = $"[{DateTime.Now:HH:mm:ss}] 监控错误: {ex.Message}\n";
                    LogData += errorMessage;
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
