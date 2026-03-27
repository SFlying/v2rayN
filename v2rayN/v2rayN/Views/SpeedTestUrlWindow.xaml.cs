namespace v2rayN.Views;

public partial class SpeedTestUrlWindow
{
    public SpeedTestUrlWindow()
    {
        InitializeComponent();

        Owner = Application.Current.MainWindow;

        // 初始化对应的 ViewModel，并传入 UI 回调处理函数
        ViewModel = new AvailabilityTargetViewModel(UpdateViewHandler);

        Closing += SpeedTestUrlWindow_Closing;
        lstSpeedTestUrls.MouseDoubleClick += LstSpeedTestUrls_MouseDoubleClick;
        lstSpeedTestUrls.SelectionChanged += LstSpeedTestUrls_SelectionChanged;
        menuClose.Click += menuClose_Click;

        // ReactiveUI 绑定逻辑
        this.WhenActivated(disposables =>
        {
            // 将 ViewModel 中的测速列表绑定到 DataGrid 的 ItemsSource
            this.OneWayBind(ViewModel, vm => vm.TargetItems, v => v.lstSpeedTestUrls.ItemsSource)
                .DisposeWith(disposables);

            // 绑定当前选中的单个项
            this.Bind(ViewModel, vm => vm.SelectedSource, v => v.lstSpeedTestUrls.SelectedItem)
                .DisposeWith(disposables);

            // 绑定增、删、改命令（包含工具栏和右键菜单）
            this.BindCommand(ViewModel, vm => vm.AddCmd, v => v.menuAdd).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DeleteCmd, v => v.menuDelete).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditCmd, v => v.menuEdit).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.AddCmd, v => v.menuAdd2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DeleteCmd, v => v.menuDelete2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditCmd, v => v.menuEdit2).DisposeWith(disposables);
        });

        // 应用深色主题边框样式
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    /// <summary>
    /// 处理 ViewModel 触发的 UI 操作请求
    /// </summary>
    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.CloseWindow:
                DialogResult = true;
                break;

            case EViewAction.ShowYesNo:
                // 使用原有的多语言资源或自定义提示信息
                if (UI.ShowYesNo("确定要删除选中的检测站点吗？") == MessageBoxResult.No)
                {
                    return false;
                }
                break;

            case EViewAction.AvailabilityEditWindow: // 你需要在 EViewAction 枚举中增加此项
                if (obj is not AvailabilityTarget target) // 确保 obj 是我们定义的模型
                    return false;

                // 弹出编辑窗口，构造函数接收的是模型数据
                var win = new AvailabilityEditWindow(target);
                return win.ShowDialog() ?? false;
        }
        return await Task.FromResult(true);
    }

    private void SpeedTestUrlWindow_Closing(object? sender, CancelEventArgs e)
    {
        // 如果 ViewModel 标记为已修改，关闭时返回 True 以触发主界面更新
        if (ViewModel?.IsModified == true)
        {
            DialogResult = true;
        }
    }

    private void LstSpeedTestUrls_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 双击执行编辑逻辑
        ViewModel?.EditAsync();
    }

    private void LstSpeedTestUrls_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // 更新选中的多行项
        if (ViewModel != null)
        {
            ViewModel.SelectedSources = lstSpeedTestUrls.SelectedItems.Cast<AvailabilityTarget>().ToList();
        }
    }

    private void menuClose_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.IsModified == true)
        {
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }
}
