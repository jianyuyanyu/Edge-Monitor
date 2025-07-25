using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Drawing;

namespace EdgeMonitor.Services
{
    public interface ITrayService
    {
        /// <summary>
        /// 初始化托盘图标
        /// </summary>
        void InitializeTray();

        /// <summary>
        /// 显示托盘图标
        /// </summary>
        void ShowTray();

        /// <summary>
        /// 隐藏托盘图标
        /// </summary>
        void HideTray();

        /// <summary>
        /// 显示托盘通知
        /// </summary>
        void ShowNotification(string title, string message);

        /// <summary>
        /// 释放托盘资源
        /// </summary>
        void Dispose();
    }

    public class TrayService : ITrayService, IDisposable
    {
        private readonly ILogger<TrayService> _logger;
        private TaskbarIcon? _taskbarIcon;
        private bool _disposed = false;

        public TrayService(ILogger<TrayService> logger)
        {
            _logger = logger;
        }

        public void InitializeTray()
        {
            try
            {
                _taskbarIcon = new TaskbarIcon();
                
                // 设置托盘图标 - 使用应用程序图标
                try
                {
                    // 方法1：尝试从应用程序主程序集获取图标
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var iconStream = assembly.GetManifestResourceStream("EdgeMonitor.icon.ico");
                    
                    if (iconStream != null)
                    {
                        _taskbarIcon.Icon = new System.Drawing.Icon(iconStream);
                        _logger.LogInformation("托盘图标从嵌入资源加载成功");
                    }
                    else
                    {
                        // 方法2：尝试从应用程序当前目录加载
                        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                        if (File.Exists(iconPath))
                        {
                            _taskbarIcon.Icon = new System.Drawing.Icon(iconPath);
                            _logger.LogInformation($"托盘图标从文件路径加载成功: {iconPath}");
                        }
                        else
                        {
                            // 方法3：尝试使用应用程序的图标
                            var appIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                            if (appIcon != null)
                            {
                                _taskbarIcon.Icon = appIcon;
                                _logger.LogInformation("托盘图标从应用程序图标加载成功");
                            }
                            else
                            {
                                // 方法4：使用系统默认应用程序图标
                                _taskbarIcon.Icon = SystemIcons.Application;
                                _logger.LogWarning("使用系统默认图标，未找到自定义图标文件");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"加载托盘图标失败: {ex.Message}，使用系统默认图标");
                    try
                    {
                        _taskbarIcon.Icon = SystemIcons.Application;
                    }
                    catch
                    {
                        _logger.LogError("连系统默认图标都无法加载");
                    }
                }
                
                _taskbarIcon.ToolTipText = "Edge Monitor - 后台监控中";
                
                // 双击托盘图标显示主窗口
                _taskbarIcon.TrayMouseDoubleClick += (s, e) =>
                {
                    ShowMainWindow();
                };
                
                // 单击托盘图标也显示主窗口
                _taskbarIcon.TrayLeftMouseUp += (s, e) =>
                {
                    ShowMainWindow();
                };

                // 创建右键菜单
                var contextMenu = new System.Windows.Controls.ContextMenu();
                
                var showMenuItem = new System.Windows.Controls.MenuItem
                {
                    Header = "显示主窗口"
                };
                showMenuItem.Click += (s, e) => ShowMainWindow();
                
                var exitMenuItem = new System.Windows.Controls.MenuItem
                {
                    Header = "退出程序"
                };
                exitMenuItem.Click += (s, e) => ExitApplication();
                
                contextMenu.Items.Add(showMenuItem);
                contextMenu.Items.Add(new System.Windows.Controls.Separator());
                contextMenu.Items.Add(exitMenuItem);
                
                _taskbarIcon.ContextMenu = contextMenu;
                
                _logger.LogInformation("托盘图标已初始化");
            }
            catch (Exception ex)
            {
                _logger.LogError($"初始化托盘图标失败: {ex.Message}");
            }
        }

        public void ShowTray()
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.Visibility = Visibility.Visible;
                _logger.LogInformation("托盘图标已显示");
            }
        }

        public void HideTray()
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.Visibility = Visibility.Hidden;
                _logger.LogInformation("托盘图标已隐藏");
            }
        }

        public void ShowNotification(string title, string message)
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
                _logger.LogInformation($"显示托盘通知: {title} - {message}");
            }
        }

        private void ShowMainWindow()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
                _logger.LogInformation("主窗口已从托盘恢复");
            }
        }

        private void ExitApplication()
        {
            _logger.LogInformation("从托盘退出应用程序");
            Application.Current?.Shutdown();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _taskbarIcon?.Dispose();
                _disposed = true;
                _logger.LogInformation("托盘服务已释放");
            }
        }
    }
}
