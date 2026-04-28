using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace DriveInsight.Views;

internal static class OwnerOverlayDialog
{
    public static async Task<TResult?> ShowAsync<TResult>(Window owner, Window dialog)
    {
        SyncToOwner(owner, dialog);

        owner.PositionChanged += MoveWithOwner;
        owner.PropertyChanged += ResizeWithOwner;
        try
        {
            return await dialog.ShowDialog<TResult>(owner);
        }
        finally
        {
            owner.PositionChanged -= MoveWithOwner;
            owner.PropertyChanged -= ResizeWithOwner;
        }

        void MoveWithOwner(object? sender, PixelPointEventArgs e)
        {
            dialog.Position = owner.Position;
        }

        void ResizeWithOwner(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.BoundsProperty)
            {
                SyncToOwner(owner, dialog);
            }
        }
    }

    private static void SyncToOwner(Window owner, Window dialog)
    {
        dialog.Position = owner.Position;
        dialog.Width = owner.Bounds.Width;
        dialog.Height = owner.Bounds.Height;
    }
}
