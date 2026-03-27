using System.Reactive;
using System.Reactive.Linq;
using DynamicData.Binding;
using ReactiveUI;
using ServiceLib.Models;

namespace ServiceLib.ViewModels;

public class AvailabilityTargetViewModel : MyReactiveObject
{
    public IObservableCollection<AvailabilityTarget> TargetItems { get; } = new ObservableCollectionExtended<AvailabilityTarget>();

    [Reactive]
    public AvailabilityTarget SelectedSource { get; set; }

    public IList<AvailabilityTarget> SelectedSources { get; set; }

    public ReactiveCommand<Unit, Unit> AddCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }
    public ReactiveCommand<Unit, Unit> EditCmd { get; }
    public bool IsModified { get; set; }

    public AvailabilityTargetViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;

        var canEditRemove = this.WhenAnyValue(
            x => x.SelectedSource,
            s => s != null && !s.Id.IsNullOrEmpty());

        AddCmd = ReactiveCommand.CreateFromTask(async () => await EditAsync(true));
        DeleteCmd = ReactiveCommand.CreateFromTask(async () => await DeleteAsync(), canEditRemove);
        EditCmd = ReactiveCommand.CreateFromTask(async () => await EditAsync(false), canEditRemove);

        _ = Init();
    }

    private async Task Init()
    {
        SelectedSource = new();
        await RefreshItems();
    }

    public async Task RefreshItems()
    {
        TargetItems.Clear();
        // 确保 Config 类里有这个 List<AvailabilityTarget>
        var items = _config.AvailabilityTargets ?? new List<AvailabilityTarget>();
        TargetItems.AddRange(items.OrderBy(x => x.Sort));
        await Task.CompletedTask;
    }

    public async Task EditAsync(bool blNew = false)
    {
        AvailabilityTarget item = blNew ? new() { DestinationName = "New", TestUrl = "https://" }
                                       : JsonUtils.DeepCopy(SelectedSource);

        if (await _updateView?.Invoke(EViewAction.AvailabilityEditWindow, item) == true)
        {
            if (_config.AvailabilityTargets == null)
                _config.AvailabilityTargets = new();

            if (blNew)
                _config.AvailabilityTargets.Add(item);
            else
            {
                var index = _config.AvailabilityTargets.FindIndex(x => x.Id == item.Id);
                if (index >= 0)
                    _config.AvailabilityTargets[index] = item;
            }

            await RefreshItems();
            IsModified = true;
            ConfigHandler.SaveConfig(_config);
        }
    }

    private async Task DeleteAsync()
    {
        if (await _updateView?.Invoke(EViewAction.ShowYesNo, null) == false)
            return;

        var targets = SelectedSources ?? new List<AvailabilityTarget> { SelectedSource };
        foreach (var it in targets.ToList())
        {
            _config.AvailabilityTargets.RemoveAll(x => x.Id == it.Id);
        }
        await RefreshItems();
        IsModified = true;
        ConfigHandler.SaveConfig(_config);
    }
}
