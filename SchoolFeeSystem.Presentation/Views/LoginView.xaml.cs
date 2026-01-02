using SchoolFeeSystem.Presentation.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class LoginView : UserControl
    {
        private readonly LoginViewModel _viewModel;

        // Constructor injection
        public LoginView(LoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Pass the password securely from the View to the ViewModel
            _viewModel.Login(txtPassword.Password);
        }
    }
}