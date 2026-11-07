using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskGuardian.Core.Engine;

namespace DiskGuardian.UI.ViewModels;

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

    private string _sizeDisplay = "未扫描";
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
        _sizeDisplay = !string.IsNullOrEmpty(rule.SizeHint) ? $"待扫描 ({rule.SizeHint})" : "待扫描";
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
        SizeBytes = result.SizeBytes;
        SizeDisplay = result.SizeHuman;
        Status = result.Status;

        // 扫描完成后，如果已被清理干净或被拦截，取消勾选；如果有可清理体积则勾选
        if (result.Status == "ready" && result.SizeBytes > 0)
        {
            IsSelected = true;
        }
        else if (result.Status == "clean" || result.Status == "blocked")
        {
            IsSelected = false;
        }
    }

    public void ResetToPending()
    {
        ScannedResult = null;
        IsScanned = false;
        SizeBytes = 0;
        SizeDisplay = !string.IsNullOrEmpty(Rule.SizeHint) ? $"待扫描 ({Rule.SizeHint})" : "待扫描";
        Status = "pending";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
