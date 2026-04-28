using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using DriveInsight.Services;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class CleanupReviewDialog : Window
{
    public CleanupReviewDialog()
    {
        InitializeComponent();
    }

    public CleanupReviewDialog(string driveName, IReadOnlyList<CleanupCandidate> candidates)
        : this()
    {
        Candidates = new ObservableCollection<CleanupCandidateViewModel>(
            candidates.Select(candidate => new CleanupCandidateViewModel(candidate)));

        Title = $"Review cleanup for {driveName}";
        TitleText.Text = $"Review cleanup for drive {driveName}";
        CountText.Text = $"{Candidates.Count} possible cleanup items found";
        DataContext = this;

        foreach (var candidate in Candidates)
        {
            candidate.PropertyChanged += CandidatePropertyChanged;
        }

        CancelButton.Click += (_, _) => Close(new List<CleanupCandidate>());
        RemoveButton.Click += (_, _) => Close(Candidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Candidate)
            .ToList());

        UpdateSelectedSize();
    }

    public ObservableCollection<CleanupCandidateViewModel> Candidates { get; } = [];

    private void CandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupCandidateViewModel.IsSelected))
        {
            UpdateSelectedSize();
        }
    }

    private void UpdateSelectedSize()
    {
        var selected = Candidates.Where(candidate => candidate.IsSelected).ToList();
        var selectedBytes = selected.Sum(candidate => candidate.Candidate.SizeBytes);
        SelectedSizeText.Text = $"{FormatStorage(selectedBytes, 1)} selected";
        RemoveButton.IsEnabled = selected.Count > 0;
    }

    private static string FormatStorage(long bytes, int decimals)
    {
        const double scale = 1024d;
        if (bytes < scale)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB", "PB" };
        var value = bytes / scale;
        var unitIndex = 0;

        while (value >= scale && unitIndex < units.Length - 1)
        {
            value /= scale;
            unitIndex++;
        }

        return $"{value.ToString($"F{decimals}")} {units[unitIndex]}";
    }
}
