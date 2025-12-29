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

        // 1. Allow access to the current App instance and Services from anywhere
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

            // 2. Existing startup logic
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Inside ConfigureServices method...

            services.AddTransient<StudentViewModel>();
            services.AddTransient<StudentView>(); // The new UserControl
            // --- Database ---
            services.AddDbContext<AppDbContext>();

            // --- Services ---
            services.AddTransient<IAuthService, AuthService>();
            services.AddTransient<IStudentService, StudentService>(); // Added for Student Management

            // --- ViewModels ---
            services.AddTransient<LoginViewModel>();
            services.AddTransient<DashboardViewModel>(); // Added for Dashboard Logic

            // --- Views ---
            services.AddTransient<LoginView>();
            services.AddTransient<DashboardView>();      // Added for the Dashboard Window
            services.AddTransient<IFeeService, FeeService>();
            services.AddTransient<FeeViewModel>();
            services.AddTransient<FeeView>();
            services.AddTransient<IFeeCollectionService, FeeCollectionService>();
            services.AddTransient<FeeCollectionViewModel>();
            services.AddTransient<FeeCollectionView>();
            services.AddTransient<IReportService, ReportService>();
            services.AddTransient<ReportsViewModel>();
            services.AddTransient<ReportsView>();
            services.AddTransient<ClassViewModel>();
            services.AddTransient<ClassView>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize DB and Seed Admin User
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                DbInitializer.Initialize(context);
            }

            // Show Login Window
            var loginWindow = _serviceProvider.GetRequiredService<LoginView>();
            loginWindow.Show();
        }
    }
}