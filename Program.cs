using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace 美团充电仓CCD
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {

            CloseProcessByName("美团充电仓CCD");
            // 先注册，再启动 UI
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            // UI 线程未捕获异常（最常见）
            Application.ThreadException += (s, e) =>
            {
                LogHelper.WriteLogs("ThreadException："+ e.Exception);
                ShowErrorAndShutdown(e.Exception);
            };
            // 非UI线程未捕获异常（async/后台线程）
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                LogHelper.WriteLogs("UnhandledException："+ ex);

                if (e.IsTerminating)
                {
                    // 进程即将被OS终止，只记录日志，来不及弹窗
                    Environment.Exit(1);
                }
            };
            // TaskScheduler 未观察的异常（async void 等）
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogHelper.WriteLogs("UnobservedTaskException："+ e.Exception);
                e.SetObserved();
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void ShowErrorAndShutdown(Exception ex)
        {
            try
            {
                var msg = $"发生未处理异常，程序将退出。\n\n{ex.Message}";
                MessageBox.Show(msg, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // 弹窗失败也要继续退出
            }
            finally
            {
                AppShutdown.RequestShutdown();
            }
        }

        public static void CloseProcessByName(string processName)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                if (process.Id == Process.GetCurrentProcess().Id) continue;

                // 先尝试发送关闭信号（让程序有机会优雅退出）
                if (!process.CloseMainWindow())
                {
                    // 没窗口或拒绝关闭，再强制杀
                    process.Kill();
                }
                else
                {
                    // 给了5秒优雅退出时间
                    if (!process.WaitForExit(5000))
                        process.Kill();
                }
            }
        }
    }
}
