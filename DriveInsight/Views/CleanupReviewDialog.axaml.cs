using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using DriveInsight.Services;
using DriveInsight.Utilities;
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
        SelectedSizeText.Text = $"{StorageFormatter.Format(selectedBytes)} selected";
        RemoveButton.IsEnabled = selected.Count > 0;
    }
}
