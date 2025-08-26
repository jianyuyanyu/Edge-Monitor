using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;

namespace EdgeMonitor.Services
{
    /// <summary>
    /// Windows系统启动管理服务实现
    /// </summary>
    public class StartupService : IStartupService
    {
        private readonly ILogger<StartupService> _logger;
        private const string TASK_NAME = "EdgeMonitor";
        private const string TRAY_TASK_NAME = "EdgeMonitorTrayMonitor";
        private const string REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "EdgeMonitor";
        private const string TRAY_APP_NAME = "EdgeMonitorTrayMonitor";
        
        private static string ExecutablePath => Process.GetCurrentProcess().MainModule?.FileName ?? "";

        public StartupService(ILogger<StartupService> logger)
        {
            _logger = logger;
        }

        public bool IsStartupEnabled()
        {
            try
            {
                // 首先检查任务计划程序
                if (IsScheduledTask())
                    return true;

                // 然后检查注册表启动项
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, false);
                return key?.GetValue(APP_NAME) != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查开机自启动状态时发生错误");
                return false;
            }
        }

        public bool IsAdminStartupEnabled()
        {
            return IsScheduledTask();
        }

        public bool IsTrayMonitorStartupEnabled()
        {
            try
            {
                // 检查托盘监测的任务计划程序
                if (IsTrayMonitorScheduledTask())
                    return true;

                // 检查托盘监测的注册表启动项
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, false);
                return key?.GetValue(TRAY_APP_NAME) != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查托盘监测开机自启动状态时发生错误");
                return false;
            }
        }

        private bool IsScheduledTask()
        {
            try
            {
                using var taskService = new TaskService();
                return taskService.RootFolder.AllTasks.Any(t => t.Name == TASK_NAME);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查任务计划程序启动状态时发生错误");
                return false;
            }
        }

        private bool IsTrayMonitorScheduledTask()
        {
            try
            {
                using var taskService = new TaskService();
                return taskService.RootFolder.AllTasks.Any(t => t.Name == TRAY_TASK_NAME);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查托盘监测任务计划程序启动状态时发生错误");
                return false;
            }
        }

        public async Task<bool> EnableStartupAsync(bool runAsAdmin = false, bool hideToTray = false)
        {
            try
            {
                // 先清除现有的启动配置
                await DisableStartupAsync();

                if (runAsAdmin)
                {
                    return CreateScheduledTask(hideToTray);
                }
                else
                {
                    return CreateRegistryStartup(hideToTray);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启用开机自启动时发生错误");
                return false;
            }
        }

        public async Task<bool> EnableTrayMonitorStartupAsync()
        {
            try
            {
                // 先清除现有的启动配置
                await DisableStartupAsync();

                // 创建带有托盘监测参数的启动项（使用管理员权限以确保功能正常）
                return CreateTrayMonitorScheduledTask();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启用托盘监测开机自启动时发生错误");
                return false;
            }
        }

        public Task<bool> DisableStartupAsync()
        {
            var success = true;

            // 删除任务计划程序任务
            try
            {
                if (IsScheduledTask())
                {
                    RemoveScheduledTask();
                    _logger.LogInformation("已删除任务计划程序启动项");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除任务计划程序启动项时发生错误");
                success = false;
            }

            // 删除托盘监测任务计划程序任务
            try
            {
                if (IsTrayMonitorScheduledTask())
                {
                    RemoveTrayMonitorScheduledTask();
                    _logger.LogInformation("已删除托盘监测任务计划程序启动项");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除托盘监测任务计划程序启动项时发生错误");
                success = false;
            }

            // 删除注册表启动项
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true);
                if (key?.GetValue(APP_NAME) != null)
                {
                    key.DeleteValue(APP_NAME, false);
                    _logger.LogInformation("已删除注册表启动项");
                }
                if (key?.GetValue(TRAY_APP_NAME) != null)
                {
                    key.DeleteValue(TRAY_APP_NAME, false);
                    _logger.LogInformation("已删除托盘监测注册表启动项");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除注册表启动项时发生错误");
                success = false;
            }

            return System.Threading.Tasks.Task.FromResult(success);
        }

        private bool CreateRegistryStartup(bool hideToTray = false)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true);
                if (key != null)
                {
                    var arguments = hideToTray ? " --startup-hide-tray" : "";
                    key.SetValue(APP_NAME, $"\"{ExecutablePath}\"{arguments}");
                    _logger.LogInformation($"Registry startup entry created: {ExecutablePath}{arguments}");
                    return true;
                }
                else
                {
                    _logger.LogError("Unable to open registry startup key");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create registry startup entry");
                return false;
            }
        }

        private bool CreateScheduledTask(bool hideToTray = false)
        {
            try
            {
                if (string.IsNullOrEmpty(ExecutablePath))
                {
                    _logger.LogError("Unable to get application executable path");
                    return false;
                }

                using var taskDefinition = TaskService.Instance.NewTask();
                
                taskDefinition.RegistrationInfo.Description = "Edge Monitor Auto Startup";
                
                // Trigger on user logon
                var logonTrigger = new LogonTrigger 
                { 
                    UserId = WindowsIdentity.GetCurrent().Name, 
                    Delay = TimeSpan.FromSeconds(3) 
                };
                taskDefinition.Triggers.Add(logonTrigger);
                
                // Execute action
                var arguments = hideToTray ? "--startup-hide-tray" : "";
                taskDefinition.Actions.Add(ExecutablePath, arguments);

                // Run with highest privileges (admin rights)
                taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
                taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;

                // Set other options
                taskDefinition.Settings.StopIfGoingOnBatteries = false;
                taskDefinition.Settings.DisallowStartIfOnBatteries = false;
                taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                taskDefinition.Settings.AllowDemandStart = true;
                taskDefinition.Settings.StartWhenAvailable = true;

                // Register task
                TaskService.Instance.RootFolder.RegisterTaskDefinition(TASK_NAME, taskDefinition);
                
                _logger.LogInformation($"Scheduled task startup entry created: {ExecutablePath} {arguments}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create scheduled task startup entry");
                return false;
            }
        }

        private bool RemoveScheduledTask()
        {
            try
            {
                using var taskService = new TaskService();
                var task = taskService.RootFolder.AllTasks.FirstOrDefault(t => t.Name == TASK_NAME);
                if (task != null)
                {
                    taskService.RootFolder.DeleteTask(TASK_NAME);
                    _logger.LogInformation($"已删除任务计划程序启动项: {TASK_NAME}");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除任务计划程序启动项时发生错误");
                return false;
            }
        }

        private bool CreateTrayMonitorScheduledTask()
        {
            try
            {
                if (string.IsNullOrEmpty(ExecutablePath))
                {
                    _logger.LogError("无法获取应用程序可执行文件路径");
                    return false;
                }

                using var taskDefinition = TaskService.Instance.NewTask();
                
                taskDefinition.RegistrationInfo.Description = "Edge Monitor 托盘自动监测";
                
                // 用户登录时触发
                var logonTrigger = new LogonTrigger 
                { 
                    UserId = WindowsIdentity.GetCurrent().Name, 
                    Delay = TimeSpan.FromSeconds(5) // 稍微延迟启动
                };
                taskDefinition.Triggers.Add(logonTrigger);
                
                // 执行操作，添加 --tray-monitor 参数
                taskDefinition.Actions.Add(ExecutablePath, "--tray-monitor");

                // 设置为最高权限运行（管理员权限）
                taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
                taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;

                // 设置其他选项
                taskDefinition.Settings.StopIfGoingOnBatteries = false;
                taskDefinition.Settings.DisallowStartIfOnBatteries = false;
                taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                taskDefinition.Settings.AllowDemandStart = true;
                taskDefinition.Settings.StartWhenAvailable = true;
                taskDefinition.Settings.Hidden = true; // 隐藏运行

                // 注册任务
                TaskService.Instance.RootFolder.RegisterTaskDefinition(TRAY_TASK_NAME, taskDefinition);
                
                _logger.LogInformation($"已创建托盘监测任务计划程序启动项: {ExecutablePath} --tray-monitor");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建托盘监测任务计划程序启动项时发生错误");
                return false;
            }
        }

        private bool RemoveTrayMonitorScheduledTask()
        {
            try
            {
                using var taskService = new TaskService();
                var task = taskService.RootFolder.AllTasks.FirstOrDefault(t => t.Name == TRAY_TASK_NAME);
                if (task != null)
                {
                    taskService.RootFolder.DeleteTask(TRAY_TASK_NAME);
                    _logger.LogInformation($"已删除托盘监测任务计划程序启动项: {TRAY_TASK_NAME}");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除托盘监测任务计划程序启动项时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 检查启动任务状态并在需要时重新安排
        /// </summary>
        public void StartupCheck()
        {
            try
            {
                using var taskService = new TaskService();
                var task = taskService.RootFolder.AllTasks.FirstOrDefault(t => t.Name == TASK_NAME);
                if (task != null)
                {
                    try
                    {
                        var action = task.Definition.Actions.FirstOrDefault()?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(action) && 
                            !ExecutablePath.Equals(action.Trim('"'), StringComparison.OrdinalIgnoreCase) && 
                            !File.Exists(action.Trim('"')))
                        {
                            _logger.LogInformation($"启动任务路径已过期: {action}，重新创建: {ExecutablePath}");
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                await DisableStartupAsync();
                                await EnableStartupAsync(true);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "检查启动任务时发生错误");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动检查时发生错误");
            }
        }
    }
}
