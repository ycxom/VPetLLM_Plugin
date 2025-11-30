using System;
using System.Text;
using WeatherPlugin.Models;

namespace WeatherPlugin.Utils
{
    /// <summary>
    /// 天气数据格式化工具
    /// </summary>
    public static class WeatherFormatter
    {
        /// <summary>
        /// 格式化当前天气为可读字符串
        /// </summary>
        public static string FormatCurrentWeather(CurrentWeather weather)
        {
            if (weather == null)
                return "无法获取天气数据";

            var sb = new StringBuilder();
            sb.AppendLine($"📍 {weather.Province} {weather.City}");
            sb.AppendLine($"🌡️ 温度: {weather.TemperatureFloat}°C");
            sb.AppendLine($"🌤️ 天气: {weather.Weather}");
            sb.AppendLine($"💧 湿度: {weather.HumidityFloat}%");
            sb.AppendLine($"🌬️ 风向: {weather.WindDirectionStr} {weather.WindPower}级");
            sb.AppendLine($"⏰ 更新时间: {weather.ReportTime:yyyy-MM-dd HH:mm}");

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 格式化天气预报为可读字符串
        /// </summary>
        public static string FormatForecast(Forecast forecast)
        {
            if (forecast == null)
                return "无法获取天气预报数据";

            var sb = new StringBuilder();
            sb.AppendLine($"📍 {forecast.Province} {forecast.City} 天气预报");
            sb.AppendLine($"⏰ 更新时间: {forecast.ReportTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            foreach (var cast in forecast.Casts)
            {
                sb.AppendLine($"📅 {cast.Date:MM月dd日}");
                sb.AppendLine($"   白天: {cast.DayWeather} {cast.DayTempFloat}°C {cast.DayWind}风{cast.DayPower}级");
                sb.AppendLine($"   夜间: {cast.NightWeather} {cast.NightTempFloat}°C {cast.NightWind}风{cast.NightPower}级");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 格式化简短的当前天气信息
        /// </summary>
        public static string FormatCurrentWeatherShort(CurrentWeather weather)
        {
            if (weather == null)
                return "无法获取天气数据";

            return $"{weather.Province}{weather.City}: {weather.Weather}, {weather.TemperatureFloat}°C, 湿度{weather.HumidityFloat}%, {weather.WindDirectionStr}风{weather.WindPower}级 (更新于{weather.ReportTime:HH:mm})";
        }

        /// <summary>
        /// 格式化简短的天气预报信息
        /// </summary>
        public static string FormatForecastShort(Forecast forecast)
        {
            if (forecast == null || forecast.Casts.Count == 0)
                return "无法获取天气预报数据";

            var sb = new StringBuilder();
            sb.Append($"{forecast.Province}{forecast.City}未来天气: ");

            for (int i = 0; i < Math.Min(forecast.Casts.Count, 4); i++)
            {
                var cast = forecast.Casts[i];
                var dayName = i == 0 ? "今天" : i == 1 ? "明天" : i == 2 ? "后天" : $"{cast.Date:MM/dd}";
                sb.Append($"{dayName}:{cast.DayWeather}/{cast.NightWeather} {cast.NightTempFloat}~{cast.DayTempFloat}°C; ");
            }

            return sb.ToString().TrimEnd(' ', ';');
        }
    }
}
