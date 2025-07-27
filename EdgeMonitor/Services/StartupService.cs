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
        private const string REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "EdgeMonitor";
        
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

        public async Task<bool> EnableStartupAsync(bool runAsAdmin = false)
        {
            try
            {
                // 先清除现有的启动配置
                await DisableStartupAsync();

                if (runAsAdmin)
                {
                    return CreateScheduledTask();
                }
                else
                {
                    return CreateRegistryStartup();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启用开机自启动时发生错误");
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

            // 删除注册表启动项
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true);
                if (key?.GetValue(APP_NAME) != null)
                {
                    key.DeleteValue(APP_NAME, false);
                    _logger.LogInformation("已删除注册表启动项");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除注册表启动项时发生错误");
                success = false;
            }

            return System.Threading.Tasks.Task.FromResult(success);
        }

        private bool CreateRegistryStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true);
                if (key != null)
                {
                    key.SetValue(APP_NAME, $"\"{ExecutablePath}\"");
                    _logger.LogInformation($"已设置注册表启动项: {ExecutablePath}");
                    return true;
                }
                else
                {
                    _logger.LogError("无法打开注册表启动项键");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建注册表启动项时发生错误");
                return false;
            }
        }

        private bool CreateScheduledTask()
        {
            try
            {
                if (string.IsNullOrEmpty(ExecutablePath))
                {
                    _logger.LogError("无法获取应用程序可执行文件路径");
                    return false;
                }

                using var taskDefinition = TaskService.Instance.NewTask();
                
                taskDefinition.RegistrationInfo.Description = "Edge Monitor 自动启动";
                
                // 用户登录时触发
                var logonTrigger = new LogonTrigger 
                { 
                    UserId = WindowsIdentity.GetCurrent().Name, 
                    Delay = TimeSpan.FromSeconds(3) 
                };
                taskDefinition.Triggers.Add(logonTrigger);
                
                // 执行操作
                taskDefinition.Actions.Add(ExecutablePath);

                // 设置为最高权限运行（管理员权限）
                taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
                taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;

                // 设置其他选项
                taskDefinition.Settings.StopIfGoingOnBatteries = false;
                taskDefinition.Settings.DisallowStartIfOnBatteries = false;
                taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                taskDefinition.Settings.AllowDemandStart = true;
                taskDefinition.Settings.StartWhenAvailable = true;

                // 注册任务
                TaskService.Instance.RootFolder.RegisterTaskDefinition(TASK_NAME, taskDefinition);
                
                _logger.LogInformation($"已创建任务计划程序启动项: {ExecutablePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建任务计划程序启动项时发生错误");
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
