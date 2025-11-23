using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class MoneyManagerPlugin : IActionPlugin
{
    public string Name => "money_manager";
    public string Author => "ycxom";
    public string Description
    {
        get
        {
            if (_vpetLLM == null) return "节日里给萝莉斯包红包！";
            switch (_vpetLLM.Settings.Language)
            {
                case "ja":
                    return "休日にはロリコンにお年玉をあげましょう！";
                case "zh-hans":
                    return "节日里给萝莉斯包红包！";
                case "zh-hant":
                    return "節日裡給蘿莉斯包紅包！";
                case "en":
                default:
                    return "Give red envelopes to lolisi on holidays!";
            }
        }
    }
    public string Examples => "Examples: `<|plugin_money_manager_begin|> action(add), amount(100) <|plugin_money_manager_end|>`, `<|plugin_money_manager_begin|> action(set), amount(500) <|plugin_money_manager_end|>`";
    public string Parameters => "action(string: add/sub/set), amount(double)";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        VPetLLM.Utils.Logger.Log("Money Manager Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        if (_vpetLLM == null)
        {
            return Task.FromResult("插件未初始化。");
        }
        try
        {
            var actionMatch = new Regex(@"action\((\w+)\)").Match(arguments);
            var amountMatch = new Regex(@"amount\(([\d\.]+)\)").Match(arguments);

            if (!actionMatch.Success || !amountMatch.Success)
            {
                return Task.FromResult("创建金钱操作失败：缺少 'action' 或 'amount' 参数。");
            }

            var action = actionMatch.Groups[1].Value.ToLower();
            var amount = double.Parse(amountMatch.Groups[1].Value);

            var money = _vpetLLM.MW.Core.Save.Money;
            string resultMessage;

            switch (action)
            {
                case "add":
                    var today = DateTime.Today;
                    var isHoliday = IsHoliday(today);
                    var isThursday = today.DayOfWeek == DayOfWeek.Thursday;

                    if (isHoliday)
                    {
                        if (amount > 500)
                        {
                            return Task.FromResult("节假日红包也不能太大哦，最大500元！");
                        }
                        _vpetLLM.MW.Core.Save.Money += amount;
                        resultMessage = $"今天是节假日，额外奖励！成功增加 {amount:f2} 金钱，当前总额: {_vpetLLM.MW.Core.Save.Money:f2}。";
                    }
                    else if (isThursday)
                    {
                        if (amount > 50)
                        {
                            return Task.FromResult("今天是疯狂星期四，但也不能超过50元！");
                        }
                        _vpetLLM.MW.Core.Save.Money += amount;
                        resultMessage = $"疯狂星期四V我50！成功增加 {amount:f2} 金钱，当前总额: {_vpetLLM.MW.Core.Save.Money:f2}。";
                    }
                    else
                    {
                        if (amount > 10)
                        {
                            return Task.FromResult("平时没事不要找萝莉斯要太多钱，会被讨厌的！不能超过10元！");
                        }
                        _vpetLLM.MW.Core.Save.Money += amount;
                        resultMessage = $"成功增加 {amount:f2} 金钱，当前总额: {_vpetLLM.MW.Core.Save.Money:f2}。";
                    }
                    break;
                case "sub":
                case "subtract":
                    _vpetLLM.MW.Core.Save.Money -= amount;
                    resultMessage = $"成功减少 {amount:f2} 金钱，当前总额: {_vpetLLM.MW.Core.Save.Money:f2}。";
                    break;
                case "set":
                    _vpetLLM.MW.Core.Save.Money = amount;
                    resultMessage = $"成功设置金钱为 {amount:f2}。";
                    break;
                default:
                    return Task.FromResult($"未知的操作: {action}。只支持 'add', 'sub', 'set'。");
            }

            // 调用工具栏的公共刷新方法来更新UI
            _vpetLLM.MW.Main.ToolBar.M_TimeUIHandle(_vpetLLM.MW.Main);

            return Task.FromResult(resultMessage);
        }
        catch (Exception e)
        {
            return Task.FromResult($"操作金钱失败，请检查参数: {e.Message}");
        }
    }

    private bool IsHoliday(DateTime date)
    {
        // 固定的公历假日
        var fixedHolidays = new[]
        {
            (month: 1, day: 1),   // 元旦
            (month: 5, day: 1),   // 劳动节
            (month: 10, day: 1),  // 国庆节
            (month: 10, day: 2),
            (month: 10, day: 3),
            (month: 10, day: 4),
            (month: 10, day: 5),
            (month: 10, day: 6),
            (month: 10, day: 7),
        };

        if (Array.Exists(fixedHolidays, h => h.month == date.Month && h.day == date.Day))
        {
            return true;
        }

        // 农历假日 (这里简化处理，只判断除夕、春节、端午、中秋)
        try
        {
            var chineseCalendar = new System.Globalization.ChineseLunisolarCalendar();
            var chineseMonth = chineseCalendar.GetMonth(date);
            var chineseDay = chineseCalendar.GetDayOfMonth(date);
            var isLeapMonth = chineseCalendar.IsLeapMonth(chineseCalendar.GetYear(date), chineseMonth);

            // 春节 (正月初一)
            if (!isLeapMonth && chineseMonth == 1 && chineseDay == 1) return true;
            // 端午 (五月初五)
            if (!isLeapMonth && chineseMonth == 5 && chineseDay == 5) return true;
            // 中秋 (八月十五)
            if (!isLeapMonth && chineseMonth == 8 && chineseDay == 15) return true;
        }
        catch
        {
            // 如果日历转换失败，就当不是节假日
        }
        return false;
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Money Manager Plugin Unloaded!");
    }
}