using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ED63Trans.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Close();
    }

    private void TextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender!;
        tb.CaretIndex = int.MaxValue;
    }
}