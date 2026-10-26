using Avalonia.Controls;
using Avalonia.Interactivity;
using Filee.App.Services;
using Filee.App.ViewModels;

namespace Filee.App.Views;

public partial class ImageEditWindow : Window
{
    public ImageEditWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        UiZoom.Track(this);
    }

    private void OnConvert(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImageEditViewModel vm || vm.Pictures.Count == 0) return;
        vm.Convert();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
