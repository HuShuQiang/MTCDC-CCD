using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM.Core;

namespace 美团充电仓CCD
{
    public delegate void MainViewBack();
    public partial class MainForm : Form
    {
        public MainView mainView = null;
        public SetupView setupView = null;
        public MainForm()
        {
            InitializeComponent();
            Text = "美团充电仓CCD"+Assembly.GetExecutingAssembly().GetName().Version;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            MianView_Btn.PerformClick();
        }

        public void MainView_Btn_Click(object sender, EventArgs e)
        {
            ShowPanel.Controls.Clear();
            mainView = new MainView();
            ShowPanel.Controls.Add(mainView);
            mainView.Dock = DockStyle.Fill;
            mainView.Show();
        }

        public async void SetupView_Btn_Click(object sender, EventArgs e)
        {
            mainView.StopButton();
            ShowPanel.Controls.Clear();
            setupView = new SetupView();
            setupView.mainviewback += new MainViewBack(Back);
            ShowPanel.Controls.Add(setupView);
            setupView.Dock = DockStyle.Fill;
            setupView.Show();
        }

        private void Back()
        {
            MianView_Btn.PerformClick();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            VmSolution.Instance?.Dispose();
            if (MainView.controlserial != null)
            {
                if (MainView.controlserial.IsOpen)
                {
                    MainView.controlserial?.Close();
                    MainView.controlserial?.Dispose();
                }
            }

            if (MainView.scanserial != null)
            {
                if (MainView.scanserial.IsOpen)
                {
                    MainView.scanserial?.Close();
                    MainView.scanserial?.Dispose();
                }
            }

        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;              // 阻止自动关闭
                AppShutdown.RequestShutdown(); // 走统一入口
                return;
            }
            // 系统关机 / 任务管理器 / 进程结束 → 直接放行
            base.OnFormClosing(e);
        }
        // ESC 键关闭
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                AppShutdown.RequestShutdown();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

}
