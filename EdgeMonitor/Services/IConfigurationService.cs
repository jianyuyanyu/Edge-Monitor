using Microsoft.Extensions.Configuration;

namespace EdgeMonitor.Services
{
    public interface IConfigurationService
    {
        /// <summary>
        /// 获取配置值
        /// </summary>
        T GetValue<T>(string key);

        /// <summary>
        /// 设置配置值
        /// </summary>
        void SetValue(string key, object value);

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// 重新加载配置
        /// </summary>
        void Reload();
    }
}
