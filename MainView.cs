using GlobalVariableModuleCs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM.Core;
using Timer = System.Windows.Forms.Timer;

namespace 美团充电仓CCD
{
    public partial class MainView : UserControl
    {
        /// <summary>
        /// 停止按钮点击事件
        /// </summary>
        public event EventHandler StopClicked;
        /// <summary>
        /// 是否为运行状态
        /// </summary>
        private bool IsRun = false;
        /// <summary>
        /// 是否为运行结束状态
        /// </summary>
        private bool IsRunEnd = false;
        /// <summary>
        /// 是否检测到感应器触发
        /// </summary>
        private bool StateOK = false;
        /// <summary>
        /// 感应器上一次的状态
        /// </summary>
        private bool LastState = false;
        /// <summary>
        /// 扫码是否成功
        /// </summary>
        private bool ScanOK = false;
        /// <summary>
        /// CCD测试是否成功
        /// </summary>
        private bool CCDOK = false;
        /// <summary>
        /// 上传工站是否成功
        /// </summary>
        private bool UploadOK = false;
        /// <summary>
        /// 包装对号是否成功
        /// </summary>
        private bool PackOK = false;
        /// <summary>
        /// VM程序对象
        /// </summary>
        private VmProcedure process = null;
        /// <summary>
        /// 控制盒串口对象
        /// </summary>
        public static SerialPort controlserial = null;
        /// <summary>
        /// 控制盒串口操作锁
        /// </summary>
        private static readonly SemaphoreSlim controlLock = new SemaphoreSlim(1, 1);
        /// <summary>
        /// 控制盒串口是否连接
        /// </summary>
        private bool _isConnected = false;
        /// <summary>
        /// 扫码器串口对象
        /// </summary>
        public static SerialPort scanserial = null;
        /// <summary>
        /// 扫码器串口操作锁
        /// </summary>
        private static readonly SemaphoreSlim scanLock = new SemaphoreSlim(1, 1);
        /// <summary>
        /// 定时器，用于检测感应器状态
        /// </summary>
        private Timer CheckStateTimer = null;
        /// <summary>
        /// 定时器，用于触发扫码器扫描
        /// </summary>
        private Timer ScanTimer = null;
        /// <summary>
        /// 控制盒指令集（查询感应器状态）
        /// </summary>
        private string CheckStateCMD = "01 04 00 00 00 01 31 CA";
        /// <summary>
        /// 控制盒指令集（感应器状态为ON）
        /// </summary>
        private string CheckStateON = "01 04 02 00 01 78 F0";
        /// <summary>
        /// 控制盒指令集（感应器状态为OFF）
        /// </summary>
        private string CheckStateOFF = "01 04 02 00 00 B9 30";
        /// <summary>
        /// 控制盒指令集（LED绿灯亮)
        /// </summary>
        private string LEDGreenON = "01 06 00 00 00 01 48 0A";
        /// <summary>
        /// 控制盒指令集（LED绿灯灭)
        /// </summary>
        private string LEDGreenOFF = "01 06 00 00 00 00 89 CA";
        /// <summary>
        /// 控制盒指令集（LED红灯亮)
        /// </summary>
        private string LEDRedON = "01 06 00 01 00 01 19 CA";
        /// <summary>
        /// 控制盒指令集（LED红灯灭)
        /// </summary>
        private string LEDRedOFF = "01 06 00 01 00 00 D8 0A";
        /// <summary>
        /// 控制盒指令集（蜂鸣器响)
        /// </summary>
        private string BeeON = "01 06 00 02 00 01 E9 CA";
        /// <summary>
        /// 控制盒指令集（蜂鸣器停)
        /// </summary>
        private string BeeOFF = "01 06 00 02 00 00 28 0A";
        /// <summary>
        /// 扫码器指令集（触发扫码)
        /// </summary>
        private string ScanCMD = "scan";
        /// <summary>
        /// 数据字段
        /// </summary>
        private string sn = string.Empty;
        private string qr = string.Empty;
        private string product = string.Empty;
        private string cmiitid = string.Empty;
        private string input = string.Empty;
        private string output = string.Empty;
        private string totaloutput = string.Empty;
        private ConcurrentQueue<string> orderqueue = new ConcurrentQueue<string>();

        /// <summary>
        /// 初始化MainView组件
        /// </summary>
        public MainView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 停止按钮点击事件触发，停止运行
        /// </summary>
        public void StopButton()
        {
            if (IsRun)
            {
                Run_Btn.PerformClick();
            }

        }

        /// <summary>
        /// MainView加载事件，初始化设置和定时器
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainView_Load(object sender, EventArgs e)
        {
            try
            {
                Model.Text = Properties.Settings.Default.Model;
                Ukey.Text = Properties.Settings.Default.Ukey;
                Station.Text = Properties.Settings.Default.Station;
                Fixid.Text = Properties.Settings.Default.Fixid;
                ischecklasttested.Text = Properties.Settings.Default.ischecklasttested.ToString();
                ischecktested.Text = Properties.Settings.Default.ischecktested.ToString();
                BoxScan.Text = Properties.Settings.Default.BoxScan;
                DevScan.Text = Properties.Settings.Default.DevScan;
                Order.Text = Properties.Settings.Default.Order;
                packing_check_recovery.Text = Properties.Settings.Default.packing_check_recovery.ToString();
                packing_check_boardBind.Text = Properties.Settings.Default.packing_check_boardBind.ToString();
                Product.Text = Properties.Settings.Default.Product;
                CMIITID.Text = Properties.Settings.Default.CMIITID;
                Input.Text = Properties.Settings.Default.Input;
                Output.Text = Properties.Settings.Default.Output;
                TotalOutput.Text = Properties.Settings.Default.TotalOutput;

                string vmfilepath = Properties.Settings.Default.FilePath;
                if (!string.IsNullOrEmpty(vmfilepath))
                {
                    VmSolution.Load(vmfilepath, "");
                }
                process = (VmProcedure)VmSolution.Instance["CCD"];

                CheckStateTimer = new Timer();
                CheckStateTimer.Interval = int.Parse(Properties.Settings.Default.StateDelay);
                CheckStateTimer.Tick += CheckState;

                ScanTimer = new Timer();
                ScanTimer.Interval = int.Parse(Properties.Settings.Default.ScanDelay);
                ScanTimer.Tick += ScanAction;
            }
            catch (Exception ex)
            {
                ShowLogs(ex.Message,0);
            }


        }

        /// <summary>
        /// 控制盒串口接收数据事件，处理感应器状态变化
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlReceived(object sender, EventArgs e)
        {
            int count = controlserial.BytesToRead;
            byte[] buffer = new byte[count];
            if (count > 0)
            {
                controlserial.Read(buffer, 0, count);
                string hex = BytesToHex(buffer);
                if (hex == CheckStateON)
                {
                    StateOK = true;
                    CheckAction();
                }
                else if (hex == CheckStateOFF)
                {
                    StateOK = false;
                    CheckAction();
                }

            }

        }

        /// <summary>
        /// 扫码器串口接收数据事件，处理扫码结果
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScanReceived(object sender, EventArgs e)
        {
            int count = scanserial.BytesToRead;
            if (count > 0)
            {
                byte[] buffer = new byte[count];
                scanserial.Read(buffer, 0, count);
                qr = Encoding.ASCII.GetString(buffer).Replace("\r", "");
                ShowLogs("扫码结束，获取数据：" + qr);
                ScanOK = true;
                this.Invoke(new Action(() =>
                {
                    if (BoxScan.Text == "QR")
                    {
                        BoxScanValue.Text = qr;
                    }
                    else if (DevScan.Text == "QR")
                    {
                        DevScanValue.Text = qr;
                    }
                }));
                if (CCDOK)
                {
                    UploadToServer();
                }
            }
            else
            {
                this.Invoke(new Action(() =>
                {
                    if (BoxScan.Text == "QR")
                    {
                        BoxScanValue.Text = "";
                    }
                    else if (DevScan.Text == "QR")
                    {
                        DevScanValue.Text = "";
                    }
                }));
                ShowLogs("扫码结束，获取数据为空！");
            }
        }

        /// <summary>
        /// 将十六进制字符串转换为字节数组
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        public static byte[] HexStringToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        /// <summary>
        /// 将字节数组转换为十六进制字符串
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string BytesToHex(byte[] data)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in data)
            {
                sb.AppendFormat("{0:X2} ", b);
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// 运行按钮点击事件，启动或停止程序运行
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Run_Btn_Click(object sender, EventArgs e)
        {
            try
            {
                if (!IsRun)
                {
                    IsRun = true;
                    this.Invoke(new Action(() =>
                    {
                        ShowLogs("程序开始运行...");
                        Run_Btn.BackColor = Color.GreenYellow;
                        Run_Btn.Text = "运行中";
                        process.OnWorkEndStatusCallBack += WorkEnd;
                        VMShow.Enabled = false;

                        controlserial = new SerialPort(Properties.Settings.Default.ControlSerialPort, 9600, Parity.None, 8, StopBits.One);
                        controlserial.ReadTimeout = 1000;
                        controlserial.WriteTimeout = 1000;
                        controlserial.DataReceived += ControlReceived;
                        if (!controlserial.IsOpen)
                        {
                            controlserial.Open();
                            _isConnected = true;
                        }

                        scanserial = new SerialPort(Properties.Settings.Default.ScanSerialPort, 115200, Parity.None, 8, StopBits.One);
                        scanserial.ReadTimeout = 2000;
                        scanserial.WriteTimeout = 2000;
                        scanserial.DataReceived += ScanReceived;
                        if (!scanserial.IsOpen)
                        {
                            scanserial?.Open();
                        }

                        CheckStateTimer.Start();
                        ScanTimer.Start();
                    }));
                }
                else
                {
                    IsRun = false;
                    this.Invoke(new Action(() =>
                    {
                        ShowLogs("程序停止运行...");
                        Run_Btn.BackColor = Color.OrangeRed;
                        Run_Btn.Text = "未运行";
                        process.OnWorkEndStatusCallBack -= WorkEnd;
                        VMShow.LoadFrontendSource();
                        VMShow.Enabled = true;
                        CheckStateTimer.Stop();
                        ScanTimer.Stop();
                        if (controlserial.IsOpen)
                        {
                            controlserial.Close();
                            _isConnected = false;
                        }

                        if (scanserial.IsOpen)
                        {
                            scanserial?.Close();
                            scanserial?.Dispose();
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                ShowLogs(ex.Message,0);
            }

        }

        /// <summary>
        /// CCD测试结束回调事件，处理测试结果并上传数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WorkEnd(object sender, EventArgs e)
        {
            try
            {
                this.Invoke((Action)(async () =>
                {
                    GlobalVariableModuleTool globalVar = (GlobalVariableModuleTool)VmSolution.Instance["全局变量1"];
                    sn = globalVar.GetGlobalVar("SN");
                    if (!string.IsNullOrEmpty(sn))
                    {
                        if (BoxScan.Text == "sn")
                        {
                            BoxScanValue.Text = sn;
                        }
                        else if (DevScan.Text == "sn")
                        {
                            DevScanValue.Text = sn;
                        }

                        product = globalVar.GetGlobalVar("产品型号");
                        cmiitid = globalVar.GetGlobalVar("CMIITID");
                        input = globalVar.GetGlobalVar("输入");
                        output = globalVar.GetGlobalVar("单口输出");
                        totaloutput = globalVar.GetGlobalVar("总输出");

                        string value = Ukey.Text == "QR" ? qr : sn;
                        if (Product.Text == product & CMIITID.Text == cmiitid & Input.Text == input & Output.Text == output & TotalOutput.Text == totaloutput)
                        {
                            CCDOK = true;
                            ShowLogs($"SN:{sn}，CCD测试通过！");
                            //await Task.Delay(100);
                            //UploadToServer();

                            if (ScanOK)
                            {
                                UploadToServer();
                            }

                        }
                        else
                        {
                            CCDOK = false;
                            ShowLogs($"{Ukey.Text}:{value}，CCD测试失败！");
                        }


                    }
                }));


            }
            catch (Exception ex)
            {
                ShowLogs(ex.Message,0);
            }
        }

        /// <summary>
        /// 获取美团服务器状态，判断是否可以过站
        /// </summary>
        /// <param name="sn"></param>
        /// <returns></returns>
        private bool GetMTState(string sn)
        {
            bool res = false;
            try
            {
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string uid = "ywl8beu1zw";
                string password = "czxjse66hq2z85nmj0pxszxmax5840e9";
                string combined = uid + password + timestamp;
                using (var md5 = MD5.Create())
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(combined);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }

                    string hash = sb.ToString();
                    string url = $"https://pcm-powerbank.meituan.com/pcm_api/check/result/cabinsn?cabinSn={sn}&uid={uid}&timestamp={timestamp}&sign={hash}";
                    string json = WebHelper.Get(url);
                    ShowLogs(url,0);
                    ShowLogs(json,0);
                    JObject data = JsonConvert.DeserializeObject<JObject>(json);
                    if (data["data"]["finalResult"]?.Value<int>() == 1)
                    {
                        res = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowLogs("获取美团服务器数据异常：" + ex.Message);
            }

            return res;
        }

        /// <summary>
        /// 上传过站数据到服务器
        /// </summary>
        /// <returns></returns>
        private bool UploadStation()
        {
            bool res = false;
            try
            {
                string model = Model.Text;
                string name = Ukey.Text;
                string value = Ukey.Text == "QR" ? qr : sn;
                string station = Station.Text;
                string fixid = Fixid.Text;
                string ischecktested = Properties.Settings.Default.ischecktested.ToString().ToLower();
                string ischecklasttested = Properties.Settings.Default.ischecklasttested.ToString().ToLower();
                string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-devInfos/model/teststationupload&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model={model}";
                string json = $"{{\"ukey\": \"{name}\",\"station\": \"{station}\",\"position\": \"{fixid}\",\"time\": \"\",\"ischecktested\": \"{ischecktested}\",\"ischecklasttested\": \"{ischecklasttested}\",\"itemValue\": \"{value}\"}}";
                string jsonStr = WebHelper.Post(url, json);
                ShowLogs(url,0);
                ShowLogs(json,0);
                ShowLogs(jsonStr,0);
                JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
                if (data["code"].ToString() == "20000")
                {
                    res = true;
                }
                else
                {
                    ShowLogs($"上传过站数据失败{data["message"]}");
                }
            }
            catch (Exception ex)
            {
                ShowLogs("上传过站数据异常：" + ex.Message);
            }

            return res;
        }

        /// <summary>
        /// 获取包装数据，进行包装对号
        /// </summary>
        /// <returns></returns>
        private JObject GetPackData()
        {
            string value = DevScan.Text == "QR" ? qr : sn;
            JObject Jsondata = new JObject();
            string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-packInfos/pack/packdata&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={Properties.Settings.Default.Model_uuid}";
            string json = $"{{\"itemname\": \"{DevScan.Text}\",\"itemvalue\": \"{value}\"}}";
            string jsonStr = WebHelper.Post(url, json);
            ShowLogs(url,0);
            ShowLogs(json,0);
            ShowLogs(jsonStr,0);
            JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
            if (data["code"].ToString() == "20000")
            {
                Jsondata = JsonConvert.DeserializeObject<JObject>(data["data"]["items"].ToString());
            }
            return Jsondata;
        }

        /// <summary>
        /// 包装对号，将扫码数据上传到服务器进行包装验证
        /// </summary>
        /// <returns></returns>
        private bool Packing()
        {
            bool res = false;
            string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-packInfos/pack/packing&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={Properties.Settings.Default.Model_uuid}";
            string json = $"{{\"boxsn\": \"{BoxScanValue.Text}\",\"{DevScan.Text}\": \"{DevScanValue.Text}\"}}";
            string jsonStr = WebHelper.Post(url, json);
            ShowLogs(url,0);
            ShowLogs(json,0);
            ShowLogs(jsonStr,0);
            JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
            if (data["code"].ToString() == "20000")
            {
                res = true;
            }
            else
            {
                ShowLogs($"包装失败！报错：{data["message"]}");
            }
            return res;
        }

        /// <summary>
        /// 上传数据到服务器，包括过站和包装验证
        /// </summary>
        private void UploadToServer()
        {
            try
            {
                if (!CCDOK)
                {
                    ShowLogs("CCD识别失败！");
                    ShowResult(2);
                    return;
                }

                if (string.IsNullOrEmpty(DevScanValue.Text) | string.IsNullOrEmpty(BoxScanValue.Text))
                {
                    ShowLogs("扫码数据不完整！");
                    ShowResult(2);
                    return;
                }

                string value = Ukey.Text == "QR" ? qr : sn;
                if (GetMTState(sn))
                {
                    if (UploadStation())
                    {
                        UploadOK = true;
                        ShowLogs($"{Ukey.Text}:{value}，上传工站{Station.Text}成功！");

                        JObject data = GetPackData();
                        if (data == null)
                        {
                            ShowLogs($"{Ukey.Text}:{value}，获取后台数据成功！");
                        }

                        if (qr == data["QR"].ToString())
                        {
                            if (Packing())
                            {
                                PackOK = true;
                                if (UploadOK & PackOK)
                                {
                                    ShowResult(1);
                                }
                                ShowLogs($"{Ukey.Text}:{value}，包装对号成功！");
                            }
                            else
                            {
                                PackOK = false;
                                ShowResult(2);
                                ShowLogs($"{Ukey.Text}:{value}，包装对号失败！");
                            }
                        }
                        else
                        {
                            ShowResult(2);
                            ShowLogs($"{Ukey.Text}:{value}，后台数据对比错误，包装对号失败！");
                        }
                    }
                    else
                    {
                        UploadOK = false;
                        ShowLogs($"{Ukey.Text}:{value}，上传工站{Station.Text}失败！");
                        ShowResult(2);
                    }
                }
                else
                {
                    ShowLogs($"{Ukey.Text}:{value}，核查美团后台状态异常，无法过站！");
                    ShowResult(2);
                }


            }
            catch (Exception ex)
            {
                ShowLogs("上传数据异常：" + ex.Message,0);
            }
            finally
            {
                IsRunEnd = true;
            }
        }

        /// <summary>
        /// 清空VM中的结果数据
        /// </summary>
        private void ClearVMResult()
        {
            GlobalVariableModuleTool globalVar = (GlobalVariableModuleTool)VmSolution.Instance["全局变量1"];
            globalVar.SetGlobalVar("SN", "");
            globalVar.SetGlobalVar("产品型号", "");
            globalVar.SetGlobalVar("CMIITID", "");
            globalVar.SetGlobalVar("输入", "");
            globalVar.SetGlobalVar("单口输出", "");
            globalVar.SetGlobalVar("总输出", "");
        }

        /// <summary>
        /// 清空历史数据结果，包括VM变量和UI显示
        /// </summary>
        private void ClearResult()
        {
            //ShowLogs("清空历史数据结果");
            this.Invoke(new Action(() =>
            {
                BoxScanValue.Text = "";
                DevScanValue.Text = "";
                Result.Text = "NONE";
                Result.BackColor = Color.White;
                LED.Image = Properties.Resources.RedLED;

            }));
            ClearVMResult();

            sn = string.Empty;
            qr = string.Empty;
            product = string.Empty;
            cmiitid = string.Empty;
            input = string.Empty;
            output = string.Empty;
            totaloutput = string.Empty;

            CCDOK = false;
            ScanOK = false;
            UploadOK = false;
            PackOK = false;
        }

        /// <summary>
        /// 显示测试结果在UI上，并控制LED灯和蜂鸣器的状态
        /// </summary>
        /// <param name="type"></param>
        private void ShowResult(int type)
        {
            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    switch (type)
                    {
                        case 0:
                            Result.Text = "TEST";
                            Result.BackColor = Color.Yellow;
                            break;
                        case 1:
                            Result.Text = "PASS";
                            Result.BackColor = Color.GreenYellow;
                            ShowLED(true);
                            break;
                        case 2:
                            Result.Text = "NG";
                            Result.BackColor = Color.OrangeRed;
                            ShowLED(false);
                            break;
                        case 3:
                            Result.Text = "NONE";
                            Result.BackColor = Color.White;
                            break;
                    }


                }));

            }
            catch (Exception ex)
            {
                ShowLogs("警报灯异常：" + ex.Message,0);
            }

        }

        /// <summary>
        /// 控制LED灯和蜂鸣器的状态，根据测试结果显示不同的提示
        /// </summary>
        /// <param name="state"></param>
        private async void ShowLED(bool state)
        {
            //await controlLock.WaitAsync();
            try
            {
                if (state)
                {
                    orderqueue.Enqueue(LEDGreenON);
                    //controlserial.Write(HexStringToBytes(LEDGreenON), 0, HexStringToBytes(LEDGreenON).Length);
                    //await Task.Run(() => { Thread.Sleep(50); });
                    orderqueue.Enqueue(BeeON);
                    //controlserial.Write(HexStringToBytes(BeeON), 0, HexStringToBytes(BeeON).Length);
                    await Task.Run(() => { Thread.Sleep(1000); });


                }
                else
                {

                    for (int x = 0; x < 3; x++)
                    {
                        orderqueue.Enqueue(LEDRedON);
                        orderqueue.Enqueue(BeeON);
                        //controlserial.Write(HexStringToBytes(LEDRedON), 0, HexStringToBytes(LEDRedON).Length);
                        //await Task.Run(() => { Thread.Sleep(50); });
                        //controlserial.Write(HexStringToBytes(BeeON), 0, HexStringToBytes(BeeON).Length);
                        await Task.Run(() => { Thread.Sleep(500); });
                        //controlserial.Write(HexStringToBytes(BeeOFF), 0, HexStringToBytes(BeeOFF).Length);
                        //await Task.Run(() => { Thread.Sleep(50); });
                        //controlserial.Write(HexStringToBytes(LEDRedOFF), 0, HexStringToBytes(LEDRedOFF).Length);
                        //await Task.Run(() => { Thread.Sleep(50); });
                        orderqueue.Enqueue(LEDRedOFF);
                        orderqueue.Enqueue(BeeOFF);
                        await Task.Run(() => { Thread.Sleep(100); });

                    }


                }
            }
            catch (Exception ex)
            {
                ShowLogs("ShowLED 异常：" + ex.Message,0);
            }
            finally
            {
                orderqueue.Enqueue(LEDGreenOFF);
                orderqueue.Enqueue(LEDRedOFF);
                orderqueue.Enqueue(BeeOFF);
                //controlserial.Write(HexStringToBytes(BeeOFF), 0, HexStringToBytes(BeeOFF).Length);
                //await Task.Run(() => { Thread.Sleep(50); });
                //controlserial.Write(HexStringToBytes(LEDGreenOFF), 0, HexStringToBytes(LEDGreenOFF).Length);
                //await Task.Run(() => { Thread.Sleep(50); });
                //controlserial.Write(HexStringToBytes(LEDRedOFF), 0, HexStringToBytes(LEDRedOFF).Length);
                //await Task.Run(() => { Thread.Sleep(50); });
                //controlLock.Release();
            }
        }

        /// <summary>
        /// 在日志文本框中显示日志信息，并将日志写入文件
        /// </summary>
        /// <param name="logs"></param>
        private void ShowLogs(string logs,int isshow = 1)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('\n');
            sb.Append(DateTime.Now.ToString("HH:mm:ss fff"));
            sb.Append("---");
            sb.Append(logs);
            string logText = sb.ToString();
            if (isshow==1)
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (Logs.Lines.Length >= 2000)
                    {
                        Logs.Clear();
                    }
                    Logs.AppendText(logText);
                    Logs.SelectionStart = Logs.Text.Length;
                    Logs.ScrollToCaret();
                }));
            }
            

            Task.Run(() => LogHelper.WriteLogs(logText));
        }

        /// <summary>
        /// 定时器触发事件，检查感应器状态并发送查询指令到控制盒串口
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void CheckState(object sender, EventArgs e)
        {
            if (!_isConnected)
            {
                return;
            }

            await controlLock.WaitAsync();
            try
            {
                if (orderqueue.Count > 0)
                {
                    
                    if (orderqueue.TryDequeue(out string order))
                    {
                        controlserial.Write(HexStringToBytes(order), 0, HexStringToBytes(order).Length);
                    }
                }
                else
                {
                    controlserial.Write(HexStringToBytes(CheckStateCMD), 0, HexStringToBytes(CheckStateCMD).Length);

                }
                //await Task.Run(() => { Thread.Sleep(10); });
            }
            finally
            {
                controlLock.Release();
            }
        }

        /// <summary>
        /// 定时器触发事件，检查是否需要触发扫码器扫描，如果条件满足则发送扫描指令
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScanAction(object sender, EventArgs e)
        {
            scanLock.WaitAsync();
            try
            {
                if (IsRun & StateOK & string.IsNullOrEmpty(qr))
                {
                    ShowLogs("QR数据为空，扫描器触发！",0);
                    scanserial.Write(ScanCMD);
                }

            }
            finally
            {
                scanLock.Release();
            }


        }

        private void CheckAction()
        {
            if (StateOK)
            {
                this.BeginInvoke(new Action(() =>
                {
                    LED.Image = Properties.Resources.GreenLED;
                }));

                if (!CCDOK & !process.IsRunning)
                {
                    process.Run();
                    VMShow.LoadFrontendSource();
                }
            }
            else if (!StateOK)
            {
                this.BeginInvoke(new Action(() =>
                {
                    LED.Image = Properties.Resources.RedLED;
                    VMShow.LoadFrontendSource();
                }));
            }
            if (StateOK & !LastState)
            {
                ShowLogs("感应器触发！",0);
                ClearResult();
                ScanAction(null, null);
                ShowResult(0);
                IsRunEnd = false;
            }
            else if (!StateOK & LastState)
            {
                if (!IsRunEnd)
                {
                    UploadToServer();
                }

                ClearResult();
                ShowResult(3);
            }
            LastState = StateOK;
        }

    }
}

