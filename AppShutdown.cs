using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace 美团充电仓CCD
{
    static class AppShutdown
    {
        public static async void RequestShutdown()
        {
            // 确保在 UI 线程上执行
            if (Application.OpenForms.Count > 0)
            {
                Application.OpenForms[0].Invoke((Action)delegate { ShutdownCore(); });
            }
            else
            {
                ShutdownCore();
            }
        }
        private static void ShutdownCore(int exitCode = 0)
        {
            try
            {
                //// 1. 关闭数据库连接
                //DbContext.Close();
                // 2. 保存配置
                Properties.Settings.Default.Save();
                //// 3. 释放托盘图标等资源
                //TrayIcon?.Dispose();
                //// 4. 停止后台服务
                //BackgroundService.Stop();
                //// 5. 刷日志
                //Logger.Flush();
                // 6. 真正退出
                Application.Exit();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogs($"Shutdown failed: {ex}");
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }
    }
}
