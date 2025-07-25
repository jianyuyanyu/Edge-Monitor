using System.ComponentModel;
using System.Windows;
using EdgeMonitor.Services;
using EdgeMonitor.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EdgeMonitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ITrayService _trayService;
        private readonly IDialogService _dialogService;
        private readonly IConfiguration _configuration;
        private readonly IConfigurationService _configService;
        private readonly ILogger<MainWindow> _logger;
        private bool _isReallyClosing = false;

        public MainWindow(MainViewModel viewModel, ITrayService trayService, IDialogService dialogService, IConfiguration configuration, IConfigurationService configService, ILogger<MainWindow> logger)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _trayService = trayService;
            _dialogService = dialogService;
            _configuration = configuration;
            _configService = configService;
            _logger = logger;
            
            DataContext = _viewModel;
            
            // 处理窗口关闭事件
            this.Closing += MainWindow_Closing;
            
            _logger.LogInformation("主窗口已初始化，托盘功能已启用");
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // 如果已经决定真正关闭，不再询问
            if (_isReallyClosing)
            {
                _trayService.Dispose();
                return;
            }

            // 取消关闭事件
            e.Cancel = true;

            // 检查是否记住了用户选择
            var rememberChoice = _configService.GetValue<bool>("UI:RememberCloseChoice");
            var savedChoice = _configService.GetValue<string?>("UI:CloseToTray");

            CloseOption option;

            if (rememberChoice && !string.IsNullOrEmpty(savedChoice))
            {
                // 使用保存的选择
                option = savedChoice.ToLower() switch
                {
                    "true" => CloseOption.MinimizeToTray,
                    "false" => CloseOption.Exit,
                    _ => CloseOption.MinimizeToTray // 默认值
                };
                _logger.LogInformation($"使用保存的关闭选择: {option}");
            }
            else
            {
                // 显示选择对话框
                var result = ShowCloseOptionsDialog();
                option = result.Option;

                // 如果用户选择记住选择，保存到配置
                if (result.RememberChoice && option != CloseOption.Cancel)
                {
                    await SaveCloseChoiceAsync(option);
                }
            }
            
            switch (option)
            {
                case CloseOption.MinimizeToTray:
                    MinimizeToTray();
                    break;
                case CloseOption.Exit:
                    ReallyClose();
                    break;
                case CloseOption.Cancel:
                    // 什么都不做，继续显示窗口
                    break;
            }
        }

        private (CloseOption Option, bool RememberChoice) ShowCloseOptionsDialog()
        {
            var dialog = new CloseOptionsDialog();
            dialog.Owner = this;
            var result = dialog.ShowDialog();
            
            if (result == true)
            {
                return (dialog.SelectedOption, dialog.RememberChoice);
            }
            
            return (CloseOption.Cancel, false);
        }

        private async Task SaveCloseChoiceAsync(CloseOption option)
        {
            try
            {
                var closeToTray = option == CloseOption.MinimizeToTray ? "true" : "false";
                
                _configService.SetValue("UI:CloseToTray", closeToTray);
                _configService.SetValue("UI:RememberCloseChoice", true);
                
                await _configService.SaveAsync();
                
                _logger.LogInformation($"保存关闭选择: {option}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"保存关闭选择失败: {ex.Message}");
            }
        }

        private void SaveCloseChoice(CloseOption option)
        {
            try
            {
                // 注意：这里只是演示，实际应用中需要更复杂的配置保存机制
                // 因为 IConfiguration 通常是只读的
                var closeToTray = option == CloseOption.MinimizeToTray ? "true" : "false";
                _logger.LogInformation($"保存关闭选择: {option}");
                
                // 这里可以实现配置文件的实际写入逻辑
                // 或者使用用户配置存储（Registry、用户文件夹等）
            }
            catch (Exception ex)
            {
                _logger.LogError($"保存关闭选择失败: {ex.Message}");
            }
        }

        private void MinimizeToTray()
        {
            this.Hide();
            _trayService.ShowTray();
            _trayService.ShowNotification("Edge Monitor", "程序已最小化到托盘，双击托盘图标可恢复窗口");
            _logger.LogInformation("程序已最小化到托盘");
        }

        private void ReallyClose()
        {
            _isReallyClosing = true;
            _trayService.Dispose();
            _logger.LogInformation("程序正在退出");
            Application.Current.Shutdown();
        }
    }

    public enum CloseOption
    {
        Cancel,
        MinimizeToTray,
        Exit
    }

    public enum CloseAction
    {
        Ask,
        MinimizeToTray,
        Exit
    }
}
