using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using SchoolFeeSystem.Infrastructure.Services;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.ViewModels;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

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
            services.AddTransient<IPayrollService, PayrollService>();
            services.AddTransient<IAttendanceService, AttendanceService>();
            services.AddScoped<ILeaveService, LeaveService>();  // ✅ FIXED: Only registered once
            services.AddScoped<OvertimeCalculationService>();

            // ✅ NEW: Payment Logging Service (for transaction audit trail)
            services.AddSingleton<PaymentLogService>();

            // ✅ EXISTING: Core Services
            services.AddSingleton<CsvDataService>();
            services.AddSingleton<PdfReportService>();

            // --- ViewModels & Views ---

            // Main Shell
            services.AddSingleton<MainWindow>();

            // Login
            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginView>();

            // Dashboard & Main Selection
            services.AddTransient<MainSelectionViewModel>();
            services.AddTransient<MainSelectionView>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<DashboardView>();
            services.AddTransient<PayrollDashboardViewModel>();
            services.AddTransient<PayrollDashboardView>();

            // Core Features (Student, Fees, Reports)
            services.AddTransient<StudentViewModel>();
            services.AddTransient<StudentView>();
            services.AddTransient<FeeViewModel>();
            services.AddTransient<FeeView>();

            // ✅ UPDATED: FeeCollectionViewModel now requires PaymentLogService
            services.AddTransient<FeeCollectionViewModel>(sp =>
                new FeeCollectionViewModel(
                    sp.GetRequiredService<CsvDataService>(),
                    sp.GetRequiredService<PaymentLogService>()
                ));
            services.AddTransient<FeeCollectionView>();

            // ✅ UPDATED: ReportsViewModel now requires PaymentLogService
            services.AddTransient<ReportsViewModel>(sp =>
                new ReportsViewModel(
                    sp.GetRequiredService<CsvDataService>(),
                    sp.GetRequiredService<PdfReportService>(),
                    sp.GetRequiredService<PaymentLogService>()
                ));
            services.AddTransient<ReportsView>();

            services.AddTransient<ClassViewModel>();
            services.AddTransient<ClassView>();
            services.AddTransient<HelpViewModel>();
            services.AddTransient<HelpView>();

            // ✅ Scholarship ViewModel & View
            services.AddTransient<ScholarshipViewModel>();
            services.AddTransient<ScholarshipView>();

            // Staff & Payroll Features
            services.AddTransient<StaffDirectoryViewModel>();
            services.AddTransient<StaffDirectoryView>();
            services.AddTransient<AddStaffViewModel>();
            services.AddTransient<AddStaffView>();

            // Staff Details (Singleton for data passing)
            services.AddSingleton<StaffDetailsViewModel>();
            services.AddSingleton<StaffDetailsView>();

            services.AddTransient<SalarySetupViewModel>();
            services.AddTransient<SalarySetupView>();

            services.AddTransient<AttendanceManagementViewModel>();
            services.AddTransient<AttendanceManagementView>();
            services.AddTransient<EditAttendanceViewModel>(); // Popup VM

            services.AddTransient<HolidayManagementViewModel>();
            services.AddTransient<HolidayManagementView>();

            services.AddTransient<ProcessPayrollViewModel>();
            services.AddTransient<ProcessPayrollView>();
            services.AddTransient<SalarySettingsViewModel>();
            services.AddTransient<SalarySettingsView>();
            services.AddTransient<PayrollReportsViewModel>();
            services.AddTransient<PayrollReportsView>();

            // Register Popups
            services.AddTransient<PayslipViewerViewModel>();
            services.AddTransient<PayslipViewerView>();
            services.AddTransient<ImportHolidaysViewModel>();
            services.AddTransient<ImportHolidaysView>();

            // Allowance Time
            services.AddTransient<AllowanceTimeViewModel>();
            services.AddTransient<AllowanceTimeView>();

            // ✅ FIXED: Leave Management (clean registration, no duplicates)
            services.AddTransient<LeaveManagementViewModel>();
            services.AddTransient<LeaveManagementView>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            var loginView = Services.GetRequiredService<LoginView>();

            mainWindow.Content = loginView;
            mainWindow.Width = 450;
            mainWindow.Height = 550;
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            mainWindow.Title = "Login";

            mainWindow.Show();
        }
    }
}