using System.Threading.Tasks;
using WeatherPlugin.Models;

namespace WeatherPlugin.Providers
{
    /// <summary>
    /// 天气 API 提供商接口
    /// </summary>
    public interface IWeatherApiProvider
    {
        /// <summary>
        /// 提供商名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 是否需要 API 密钥
        /// </summary>
        bool RequiresApiKey { get; }

        /// <summary>
        /// 设置 API 密钥
        /// </summary>
        /// <param name="apiKey">API 密钥</param>
        void SetApiKey(string apiKey);

        /// <summary>
        /// 设置请求超时时间
        /// </summary>
        /// <param name="timeoutSeconds">超时时间（秒）</param>
        void SetTimeout(int timeoutSeconds);

        /// <summary>
        /// 获取当前天气
        /// </summary>
        /// <param name="adcode">行政区划代码</param>
        /// <returns>当前天气数据</returns>
        Task<CurrentWeather?> GetCurrentWeatherAsync(int adcode);

        /// <summary>
        /// 获取天气预报
        /// </summary>
        /// <param name="adcode">行政区划代码</param>
        /// <returns>天气预报数据</returns>
        Task<Forecast?> GetForecastAsync(int adcode);
    }
}
