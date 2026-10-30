using System.Windows;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.ViewModels;

namespace FloatingDeskAssistant.UI.Windows;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 32);
        MaxWidth = Math.Max(240, SystemParameters.WorkArea.Width - 32);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        WindowCaptureProtection.Attach(this, cornerRadius: 16, roundOpaqueWindow: true);
        DataContext = _viewModel = viewModel;
        _viewModel.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => { UserIdTextBox.Focus(); UserIdTextBox.SelectAll(); };
        Closed += (_, _) => { _viewModel.CloseRequested -= OnCloseRequested; _viewModel.Dispose(); };
    }

    private void OnCloseRequested(bool success) => DialogResult = success;
}
