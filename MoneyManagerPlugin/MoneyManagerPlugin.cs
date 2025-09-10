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
            if (_vpetLLM == null) return "控制桌宠的金钱。";
            switch (_vpetLLM.Settings.Language)
            {
                case "ja":
                    return "卓球の金を管理します。";
                case "zh-hans":
                    return "控制桌宠的金钱。";
                case "zh-hant":
                    return "控制桌寵的金錢。";
                case "en":
                default:
                    return "Control the pet's money.";
            }
        }
    }
    public string Examples => "Example: `[:plugin(money_manager(action(add), amount(100)))]` or `[:plugin(money_manager(action(set), amount(500)))]`";
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
                    _vpetLLM.MW.Core.Save.Money += amount;
                    resultMessage = $"成功增加 {amount:f2} 金钱，当前总额: {_vpetLLM.MW.Core.Save.Money:f2}。";
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

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Money Manager Plugin Unloaded!");
    }
}