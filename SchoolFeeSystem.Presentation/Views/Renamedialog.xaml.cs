using System.Windows;
using System.Windows.Input;

namespace SchoolFeeSystem.Presentation.Views
{
    /// <summary>
    /// A reusable single-field input dialog used for:
    ///   • Renaming a course
    ///   • Editing a student's name, father's name, phone, or category
    ///
    /// Usage:
    ///   var dlg = new RenameDialog(currentValue, "Edit Student Name");
    ///   dlg.Owner = Application.Current.MainWindow;
    ///   if (dlg.ShowDialog() == true)
    ///       string newValue = dlg.NewName;
    /// </summary>
    public partial class RenameDialog : Window
    {
        /// <summary>
        /// The value typed by the admin. Only valid when DialogResult == true.
        /// </summary>
        public string NewName { get; private set; }

        public RenameDialog(string currentValue = "", string label = "Enter new name:")
        {
            InitializeComponent();

            LabelText.Text = label;
            NameBox.Text = currentValue ?? string.Empty;

            // Pre-select all text so the admin can type straight away
            Loaded += (_, __) =>
            {
                NameBox.SelectAll();
                NameBox.Focus();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string value = NameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                NameBox.BorderBrush = System.Windows.Media.Brushes.Red;
                NameBox.Focus();
                return;
            }
            NewName = value;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // Allow Enter to confirm and Escape to cancel
        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Ok_Click(sender, e);
            if (e.Key == Key.Escape) Cancel_Click(sender, e);
        }
    }
}