using System;
using System.Windows;
using System.Globalization;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using SchoolFeeSystem.Infrastructure.Services;
using SchoolFeeSystem.Presentation.ViewModels;
using SchoolFeeSystem.Presentation.Views;

namespace SchoolFeeSystem.Presentation
{
    public partial class App : System.Windows.Application
    {
        private ServiceProvider _serviceProvider;

        // Allow access to the current App instance and Services from anywhere
        public new static App Current => (App)Application.Current;
        public IServiceProvider Services => _serviceProvider;

        public App()
        {
            // 1. FORCE INDIAN CURRENCY (₹) GLOBALLY
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-IN");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-IN");

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // --- Database ---
            services.AddDbContext<AppDbContext>();

            // --- Services (Logic) ---
            services.AddTransient<IAuthService, AuthService>();
            services.AddTransient<IStudentService, StudentService>();
            services.AddTransient<IFeeService, FeeService>();
            services.AddTransient<IFeeCollectionService, FeeCollectionService>();
            services.AddTransient<IReportService, ReportService>();
            services.AddTransient<IPayrollService, PayrollService>(); // <--- THIS WAS MISSING!

            // --- ViewModels (The Connectors) ---
            services.AddTransient<LoginViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<MainSelectionViewModel>();
            services.AddTransient<PayrollDashboardViewModel>();

            services.AddTransient<StudentViewModel>();
            services.AddTransient<FeeViewModel>();
            services.AddTransient<FeeCollectionViewModel>();
            services.AddTransient<ReportsViewModel>();
            services.AddTransient<ClassViewModel>();
            services.AddTransient<HelpViewModel>();

            // --- Views (The Screens) ---
            services.AddSingleton<MainWindow>(); // <--- This registers the Main Shell

            services.AddTransient<LoginView>();
            services.AddTransient<MainSelectionView>();
            services.AddTransient<DashboardView>();
            services.AddTransient<PayrollDashboardView>();

            services.AddTransient<StudentView>();
            services.AddTransient<FeeView>();
            services.AddTransient<FeeCollectionView>();
            services.AddTransient<ReportsView>();
            services.AddTransient<ClassView>();
            services.AddTransient<HelpView>();
            services.AddTransient<StaffDirectoryViewModel>();
            services.AddTransient<StaffDirectoryView>();
            services.AddTransient<AddStaffViewModel>();
            services.AddTransient<AddStaffView>();

            // Use Singleton for details so we can pass data to it easily
            services.AddSingleton<StaffDetailsViewModel>();
            services.AddSingleton<StaffDetailsView>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Get the MainWindow (The Shell)
            // If this fails, it means 'services.AddSingleton<MainWindow>()' above didn't run.
            var mainWindow = Services.GetRequiredService<MainWindow>();

            // 2. Get the Login View (The Content)
            var loginView = Services.GetRequiredService<LoginView>();

            // 3. Put Login View INSIDE Main Window
            mainWindow.Content = loginView;

            // 4. Set Initial Size for Login
            mainWindow.Width = 450;
            mainWindow.Height = 550;
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            mainWindow.Title = "Login";

            // 5. Show the Main Window
            mainWindow.Show();
        }
    }
}