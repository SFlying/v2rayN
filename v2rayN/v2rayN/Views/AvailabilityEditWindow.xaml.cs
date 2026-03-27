namespace v2rayN.Views;

public partial class AvailabilityEditWindow
{
    public AvailabilityEditWindow(AvailabilityTarget item)
    {
        InitializeComponent();

        Owner = Application.Current.MainWindow;

        // 创建对应的 ViewModel
        ViewModel = new AvailabilityEditViewModel(item, UpdateViewHandler);

        this.WhenActivated(disposables =>
        {
            // 双向绑定各个字段
            this.Bind(ViewModel, vm => vm.SelectedSource.DestinationName, v => v.txtDestinationName.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.TestUrl, v => v.txtTestUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.SuccessKeywords, v => v.txtSuccessKeywords.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.UserAgent, v => v.txtUserAgent.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.IsEnabled, v => v.togEnable.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource.Sort, v => v.txtSort.Text).DisposeWith(disposables);

            // 绑定保存命令
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });

        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.CloseWindow:
                DialogResult = true;
                break;
        }
        return await Task.FromResult(true);
    }
}
