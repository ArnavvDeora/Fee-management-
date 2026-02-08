using System.Windows.Controls;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class ScholarshipView : UserControl
    {
        public ScholarshipView(ScholarshipViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}