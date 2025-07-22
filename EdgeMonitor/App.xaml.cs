using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Windows;
using EdgeMonitor.ViewModels;
using EdgeMonitor.Services;

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
            _host = CreateHostBuilder(e.Args).Build();

            // 启动服务
            await _host.StartAsync();

            // 检查管理员权限
            var privilegeService = _host.Services.GetRequiredService<IPrivilegeService>();
            if (!privilegeService.IsRunningAsAdministrator())
            {
                privilegeService.ShowAdministratorRequiredMessage();
                return; // 如果用户选择重新启动，程序会在 RestartAsAdministrator 中退出
            }

            // 获取主窗口并显示
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
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

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // 注册服务
                    services.AddTransient<IDataService, DataService>();
                    services.AddTransient<IDialogService, DialogService>();
                    services.AddSingleton<IPrivilegeService, PrivilegeService>();
                    services.AddSingleton<IEdgeMonitorService, EdgeMonitorService>();
                    
                    // 注册 ViewModels
                    services.AddTransient<MainViewModel>();
                    
                    // 注册 Windows
                    services.AddTransient<MainWindow>();
                    
                    // 注册日志
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.AddDebug();
                    });
                });
    }
}
