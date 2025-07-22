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

                // 检查管理员权限
                var privilegeService = _host.Services.GetRequiredService<IPrivilegeService>();
                if (!privilegeService.IsRunningAsAdministrator())
                {
                    privilegeService.ShowAdministratorRequiredMessage();
                    Shutdown();
                    return;
                }

                // 获取主窗口并显示
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();

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
