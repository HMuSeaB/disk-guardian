using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using DiskGuardian.Core.Engine;
using Microsoft.UI.Xaml;

namespace DiskGuardian.UI.ViewModels;

public sealed class CleanerSubItemRow : INotifyPropertyChanged
{
    public SubPathItem Model { get; }
    public string Name => Model.Name;
    public string Path => Model.Path;
    public long SizeBytes => Model.SizeBytes;
    public string SizeHuman => CleanerEngine.HumanSize(SizeBytes);
    public int FilesCount => Model.FilesCount;

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                Model.IsSelected = value;
                OnPropertyChanged();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public CleanerSubItemRow(SubPathItem model)
    {
        Model = model;
        _isSelected = model.IsSelected;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class CleanerRuleRow : INotifyPropertyChanged
{
    public RuleItem Rule { get; }

    public string Id => Rule.Id;
    public string Name => Rule.Name;
    public int Level => Rule.Level;
    public string LevelDisplay => $"Level {Level}";
    public string Note => Rule.Note;
    public string Rebuild => Rule.Rebuild;
    public string SizeHint => !string.IsNullOrEmpty(Rule.SizeHint) ? $"预估: {Rule.SizeHint}" : "";
    public bool IsDiscover => Rule.Discover != null;
    public string TypeBadge => IsDiscover ? "D盘深层发现 (~30s)" : "快速缓存";

    public ObservableCollection<CleanerSubItemRow> SubItemRows { get; } = new();
    public bool HasSubItems => SubItemRows.Count > 1;
    public Visibility SubItemsVisibility => HasSubItems ? Visibility.Visible : Visibility.Collapsed;

    private bool _isExpanded = false;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpandIconGlyph));
                OnPropertyChanged(nameof(ExpandActionText));
                OnPropertyChanged(nameof(ExpandedVisibility));
            }
        }
    }

    public Visibility ExpandedVisibility => (HasSubItems && _isExpanded) ? Visibility.Visible : Visibility.Collapsed;

    public string ExpandIconGlyph => _isExpanded ? "\uE70E" : "\uE70D"; // ChevronDown : ChevronRight
    public string ExpandActionText => _isExpanded ? "收起明细" : $"展开各项目明细 ({SubItemRows.Count} 处)";

    public string SubItemsSummary => HasSubItems
        ? $"已勾选 {SubItemRows.Count(s => s.IsSelected)} / {SubItemRows.Count} 处项目"
        : string.Empty;

    private bool _isSyncingSelection = false;

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();

                if (!_isSyncingSelection && SubItemRows.Count > 0)
                {
                    _isSyncingSelection = true;
                    foreach (var sub in SubItemRows)
                    {
                        sub.IsSelected = value;
                    }
                    _isSyncingSelection = false;
                    RecalculateSelectedSize();
                }

                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _isScanned = false;
    public bool IsScanned
    {
        get => _isScanned;
        private set { _isScanned = value; OnPropertyChanged(); }
    }

    private long _sizeBytes = 0;
    public long SizeBytes
    {
        get => _sizeBytes;
        private set { _sizeBytes = value; OnPropertyChanged(); }
    }

    private string _sizeDisplay = "待扫描";
    public string SizeDisplay
    {
        get => _sizeDisplay;
        private set { _sizeDisplay = value; OnPropertyChanged(); }
    }

    private string _status = "pending"; // pending, scanning, ready, clean, blocked, need-admin
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public string StatusDisplay => Status switch
    {
        "pending" => "未扫描",
        "scanning" => "扫描中...",
        "ready" => "可清理",
        "clean" => "已是干净",
        "blocked" => "安全拦截",
        "need-admin" => "需管理员权限",
        _ => Status
    };

    public bool IsReady => Status == "ready";

    public PlanRow? ScannedResult { get; private set; }

    public event EventHandler? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public CleanerRuleRow(RuleItem rule, bool isSelectedDefault = true)
    {
        Rule = rule;
        _isSelected = isSelectedDefault;
        _sizeDisplay = "待扫描";
        _status = "pending";
    }

    public void MarkScanning()
    {
        Status = "scanning";
        SizeDisplay = "计算中...";
    }

    public void ApplyResult(PlanRow result)
    {
        ScannedResult = result;
        IsScanned = true;
        Status = result.Status;

        // 加载子项目明细
        SubItemRows.Clear();
        if (result.SubItems != null && result.SubItems.Count > 0)
        {
            foreach (var sub in result.SubItems)
            {
                var subRow = new CleanerSubItemRow(sub);
                subRow.SelectionChanged += (s, e) => OnSubItemSelectionChanged();
                SubItemRows.Add(subRow);
            }
        }

        RecalculateSelectedSize();

        OnPropertyChanged(nameof(HasSubItems));
        OnPropertyChanged(nameof(SubItemsVisibility));
        OnPropertyChanged(nameof(ExpandedVisibility));
        OnPropertyChanged(nameof(SubItemsSummary));
        OnPropertyChanged(nameof(ExpandActionText));

        // 默认若有多个项目，自动展开让用户一目了然
        if (HasSubItems && result.Status == "ready")
        {
            IsExpanded = true;
        }

        if (result.Status == "ready" && result.SizeBytes > 0)
        {
            _isSyncingSelection = true;
            IsSelected = true;
            _isSyncingSelection = false;
        }
        else if (result.Status == "clean" || result.Status == "blocked")
        {
            _isSyncingSelection = true;
            IsSelected = false;
            _isSyncingSelection = false;
        }
    }

    private void OnSubItemSelectionChanged()
    {
        if (_isSyncingSelection) return;

        RecalculateSelectedSize();

        int selectedSubCount = SubItemRows.Count(s => s.IsSelected);
        _isSyncingSelection = true;
        IsSelected = selectedSubCount > 0;
        _isSyncingSelection = false;

        OnPropertyChanged(nameof(SubItemsSummary));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecalculateSelectedSize()
    {
        if (SubItemRows.Count > 0)
        {
            SizeBytes = SubItemRows.Where(s => s.IsSelected).Sum(s => s.SizeBytes);
        }
        else if (ScannedResult != null)
        {
            SizeBytes = IsSelected ? ScannedResult.SizeBytes : 0;
        }

        SizeDisplay = CleanerEngine.HumanSize(SizeBytes);
    }

    public void ResetToPending()
    {
        ScannedResult = null;
        IsScanned = false;
        SizeBytes = 0;
        SizeDisplay = "待扫描";
        Status = "pending";
        SubItemRows.Clear();
        IsExpanded = false;
        OnPropertyChanged(nameof(HasSubItems));
        OnPropertyChanged(nameof(SubItemsVisibility));
        OnPropertyChanged(nameof(ExpandedVisibility));
        OnPropertyChanged(nameof(SubItemsSummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
