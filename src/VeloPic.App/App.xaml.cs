using Microsoft.UI.Xaml;

namespace VeloPic.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        AppLogger.Info("App 构造开始");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                AppLogger.Error("AppDomain 未处理异常", exception);
            }
            else
            {
                AppLogger.Info("AppDomain 未处理异常：" + e.ExceptionObject);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("Task 未观察异常", e.Exception);
        };
        UnhandledException += (_, e) =>
        {
            AppLogger.Error("WinUI 未处理异常", e.Exception);
        };
        InitializeComponent();
        AppLogger.Info("App 构造完成");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppLogger.Info("OnLaunched 开始");
            _window = new MainWindow();
            AppLogger.Info("MainWindow 创建完成");
            _window.Activate();
            AppLogger.Info("MainWindow Activate 完成");
        }
        catch (Exception ex)
        {
            AppLogger.Error("OnLaunched 失败", ex);
            throw;
        }
    }
}
