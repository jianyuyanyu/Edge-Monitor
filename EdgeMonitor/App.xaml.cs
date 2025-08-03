using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Windows;
using EdgeMonitor.ViewModels;
using EdgeMonitor.Services;
using System.IO;

namespace EdgeMonitor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                _host = CreateHostBuilder(e.Args).Build();

                // 启动服务
                await _host.StartAsync();

                // 检查是否是托盘监测模式
                bool isTrayMonitorMode = e.Args.Contains("--tray-monitor");

                // 检查管理员权限
                var privilegeService = _host.Services.GetRequiredService<IPrivilegeService>();
                if (!privilegeService.IsRunningAsAdministrator())
                {
                    if (!isTrayMonitorMode)
                    {
                        privilegeService.ShowAdministratorRequiredMessage();
                    }
                    Shutdown();
                    return;
                }

                // 执行启动检查
                var startupService = _host.Services.GetRequiredService<IStartupService>();
                startupService.StartupCheck();

                // 获取托盘服务
                var trayService = _host.Services.GetRequiredService<ITrayService>();
                
                // 初始化托盘服务
                trayService.InitializeTray();

                if (isTrayMonitorMode)
                {
                    // 托盘监测模式不显示主窗口自动开始监测
                    var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
                    
                    // 确保托盘显示
                    trayService.ShowTray();
                    
                    // 启动托盘监测模式
                    mainViewModel.StartTrayMonitoring();
                    
                    // 在托盘模式下，并不设置MainWindow
                }
                else
                {
                    // 正常模式显示主窗口
                    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    mainWindow.Show();
                    
                    // 设置为应用程序的主窗口
                    MainWindow = mainWindow;
                }

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用程序启动时发生严重错误: {ex.Message}\n\n{ex.ToString()}", 
                                "启动失败", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Error);
                Shutdown();
            }
        }

        private IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<IPrivilegeService, PrivilegeService>();
                    services.AddSingleton<IEdgeMonitorService, EdgeMonitorService>();
                    services.AddSingleton<IDataService, DataService>();
                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<ILogService, LogService>();
                    services.AddSingleton<ITrayService, TrayService>();
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<IStartupService, StartupService>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                });
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            base.OnExit(e);
        }
    }
}
