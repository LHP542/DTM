using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DTM.ViewModels.TreeNodes;

public abstract partial class NodeViewModelBase : ViewModelBase
{
    [ObservableProperty] private string _header = string.Empty;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<NodeViewModelBase> Children { get; } = new();

    protected virtual void OnExpanded() { }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) OnExpanded();
    }
}
