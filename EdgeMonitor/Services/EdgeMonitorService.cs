using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace EdgeMonitor.Services
{
    public interface IEdgeMonitorService
    {
        Task<EdgeProcessInfo[]> GetEdgeProcessesAsync();
        Task<bool> HasVisibleWindowsAsync();
        Task KillAllEdgeProcessesAsync();
        bool IsEdgeRunningInBackground(EdgeProcessInfo[] processes);
        bool HasAbnormalResourceUsage(EdgeProcessInfo[] processes, double cpuThreshold = 30.0, long memoryThreshold = 2048);
    }

    public class EdgeProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public double CpuUsage { get; set; }
        public long MemoryUsageMB { get; set; }
        public int WindowCount { get; set; }
        public string CommandLine { get; set; } = "";
        public DateTime StartTime { get; set; }
    }

    public class EdgeMonitorService : IEdgeMonitorService
    {
        private readonly ILogger<EdgeMonitorService> _logger;
        private readonly Dictionary<int, double> _previousCpuTimes = new();
        private readonly Dictionary<int, DateTime> _previousSampleTimes = new();

        // Windows API 声明
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public EdgeMonitorService(ILogger<EdgeMonitorService> logger)
        {
            _logger = logger;
        }

        public async Task<EdgeProcessInfo[]> GetEdgeProcessesAsync()
        {
            return await Task.Run(() =>
            {
                var edgeProcesses = new List<EdgeProcessInfo>();
                
                // 获取所有Edge相关进程
                var processNames = new[] { "msedge", "MicrosoftEdge", "MicrosoftEdgeCP", "MicrosoftEdgeSH" };
                
                _logger.LogInformation($"开始搜索Edge进程，进程名: {string.Join(", ", processNames)}");
                
                foreach (var processName in processNames)
                {
                    var processes = Process.GetProcessesByName(processName);
                    _logger.LogInformation($"找到 {processes.Length} 个 {processName} 进程");
                    
                    foreach (var process in processes)
                    {
                        try
                        {
                            var processInfo = new EdgeProcessInfo
                            {
                                ProcessId = process.Id,
                                ProcessName = process.ProcessName,
                                MemoryUsageMB = process.WorkingSet64 / (1024 * 1024),
                                StartTime = process.StartTime,
                                WindowCount = GetWindowCountForProcess(process.Id)
                            };

                            // 计算CPU使用率
                            processInfo.CpuUsage = CalculateCpuUsage(process);

                            // 获取命令行参数（如果可能）
                            try
                            {
                                processInfo.CommandLine = GetCommandLine(process);
                            }
                            catch
                            {
                                processInfo.CommandLine = "无法获取";
                            }

                            edgeProcesses.Add(processInfo);
                            _logger.LogDebug($"添加进程: PID={processInfo.ProcessId}, 内存={processInfo.MemoryUsageMB}MB, 窗口数={processInfo.WindowCount}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"无法获取进程 {process.ProcessName} (PID: {process.Id}) 的信息: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }

                _logger.LogInformation($"总共找到 {edgeProcesses.Count} 个Edge进程");
                return edgeProcesses.ToArray();
            });
        }

        public async Task<bool> HasVisibleWindowsAsync()
        {
            return await Task.Run(() =>
            {
                var edgeProcesses = Process.GetProcessesByName("msedge");
                var hasVisibleWindows = false;

                var processIds = edgeProcesses.Select(p => (uint)p.Id).ToHashSet();

                EnumWindows((hWnd, lParam) =>
                {
                    if (IsWindowVisible(hWnd))
                    {
                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (processIds.Contains(processId))
                        {
                            // 检查窗口是否有标题（通常表示是真正的用户界面窗口）
                            int length = GetWindowTextLength(hWnd);
                            if (length > 0)
                            {
                                var title = new System.Text.StringBuilder(length + 1);
                                GetWindowText(hWnd, title, title.Capacity);
                                
                                // 过滤掉一些系统窗口或隐藏窗口
                                var titleText = title.ToString();
                                if (!string.IsNullOrWhiteSpace(titleText) && 
                                    !titleText.Contains("Microsoft Edge WebView2") &&
                                    !titleText.Contains("DevTools"))
                                {
                                    hasVisibleWindows = true;
                                    return false; // 停止枚举
                                }
                            }
                        }
                    }
                    return true; // 继续枚举
                }, IntPtr.Zero);

                foreach (var process in edgeProcesses)
                {
                    process.Dispose();
                }

                return hasVisibleWindows;
            });
        }

        public async Task KillAllEdgeProcessesAsync()
        {
            await Task.Run(() =>
            {
                var processNames = new[] { "msedge", "MicrosoftEdge", "MicrosoftEdgeCP", "MicrosoftEdgeSH" };
                int killedCount = 0;

                foreach (var processName in processNames)
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            _logger.LogInformation($"正在终止 Edge 进程: {process.ProcessName} (PID: {process.Id})");
                            process.Kill();
                            process.WaitForExit(5000); // 等待最多5秒
                            killedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"无法终止进程 {process.ProcessName} (PID: {process.Id}): {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }

                _logger.LogInformation($"已终止 {killedCount} 个 Edge 进程");
            });
        }

        public bool IsEdgeRunningInBackground(EdgeProcessInfo[] processes)
        {
            return processes.Any() && processes.All(p => p.WindowCount == 0);
        }

        public bool HasAbnormalResourceUsage(EdgeProcessInfo[] processes, double cpuThreshold = 30.0, long memoryThreshold = 2048)
        {
            if (!processes.Any()) return false;

            var totalCpu = processes.Sum(p => p.CpuUsage);
            var totalMemory = processes.Sum(p => p.MemoryUsageMB);

            return totalCpu > cpuThreshold || totalMemory > memoryThreshold;
        }

        private int GetWindowCountForProcess(int processId)
        {
            int windowCount = 0;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint winProcessId);
                if (winProcessId == processId && IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        windowCount++;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return windowCount;
        }

        private double CalculateCpuUsage(Process process)
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var currentCpuTime = process.TotalProcessorTime.TotalMilliseconds;

                if (_previousCpuTimes.ContainsKey(process.Id) && _previousSampleTimes.ContainsKey(process.Id))
                {
                    var previousCpuTime = _previousCpuTimes[process.Id];
                    var previousSampleTime = _previousSampleTimes[process.Id];

                    var cpuTimeDelta = currentCpuTime - previousCpuTime;
                    var realTimeDelta = (currentTime - previousSampleTime).TotalMilliseconds;

                    if (realTimeDelta > 0)
                    {
                        var cpuUsage = (cpuTimeDelta / realTimeDelta) * 100.0 / Environment.ProcessorCount;
                        
                        _previousCpuTimes[process.Id] = currentCpuTime;
                        _previousSampleTimes[process.Id] = currentTime;
                        
                        return Math.Max(0, Math.Min(100, cpuUsage));
                    }
                }

                // 第一次检测时，记录初始值并返回一个基于当前CPU时间的估算值
                _previousCpuTimes[process.Id] = currentCpuTime;
                _previousSampleTimes[process.Id] = currentTime;
                
                // 对于新进程，尝试使用进程启动时间来估算CPU使用率
                var processAge = (DateTime.Now - process.StartTime).TotalMilliseconds;
                if (processAge > 1000) // 如果进程运行超过1秒
                {
                    var estimatedCpu = (currentCpuTime / processAge) * 100.0 / Environment.ProcessorCount;
                    return Math.Max(0, Math.Min(100, estimatedCpu));
                }
                
                return 0.0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"计算进程 {process.Id} CPU使用率失败: {ex.Message}");
                return 0.0;
            }
        }

        private string GetCommandLine(Process process)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                {
                    using (var objects = searcher.Get())
                    {
                        var result = objects.Cast<System.Management.ManagementObject>()
                                           .SingleOrDefault()?["CommandLine"]?.ToString();
                        return result ?? "";
                    }
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
