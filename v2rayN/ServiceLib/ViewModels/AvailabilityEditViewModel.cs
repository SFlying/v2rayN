using System.Reactive;
using ReactiveUI;
using ServiceLib.Models;

namespace ServiceLib.ViewModels;

public class AvailabilityEditViewModel : MyReactiveObject
{
    [Reactive]
    public AvailabilityTarget SelectedSource { get; set; }

    public ReactiveCommand<Unit, Unit> SaveCmd { get; }

    public AvailabilityEditViewModel(AvailabilityTarget item, Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _updateView = updateView;

        // 这里的 item 是从父窗口传过来的副本或新实例
        SelectedSource = item;

        // 设置默认 User-Agent (如果用户没填的话)
        if (SelectedSource.UserAgent.IsNullOrEmpty())
        {
            SelectedSource.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        }

        // 保存命令逻辑
        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await SaveData())
            {
                // 通知 View 关闭窗口
                await _updateView?.Invoke(EViewAction.CloseWindow, null);
            }
        });
    }

    private async Task<bool> SaveData()
    {
        // 基础校验
        if (SelectedSource.DestinationName.IsNullOrEmpty())
        {
            // 这里可以加一个 NoticeManager 提示，或者简单的返回 false
            return false;
        }

        if (SelectedSource.TestUrl.IsNullOrEmpty() || !SelectedSource.TestUrl.StartsWith("http"))
        {
            return false;
        }

        // 验证 SuccessKeywords 是否为空，虽然不是强制，但建议用户填写
        if (SelectedSource.SuccessKeywords.IsNullOrEmpty())
        {
            // 可以设置默认关键词，比如 "200"
        }

        return await Task.FromResult(true);
    }
}
