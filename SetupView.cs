using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace 美团充电仓CCD
{
    public partial class SetupView : UserControl
    {
        public event MainViewBack mainviewback;
        private Hashtable ModelTable = new Hashtable { { "TS6J-Test", 900000325 }, { "T7J-Test", 900000353 } };
        public SetupView()
        {
            InitializeComponent();
        }

        private List<string> GetSerialPort()
        {
            List<string> ports = SerialPort.GetPortNames().ToList();
            ports.Sort();
            return ports;
        }

        private List<string> GetUkeyList()
        {
            List<string> list = new List<string>();
            try
            {
                string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-devInfos/model/listallukeyitems&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={ModelTable[ModelList.Text]}";
                string jsonStr = WebHelper.Post(url);
                JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
                JArray ukey = (JArray)JsonConvert.DeserializeObject(data["data"]["items"].ToString());
                foreach (var item in ukey)
                {
                    list.Add(item["name"].ToString());
                }
            }
            catch (Exception ex)
            {
                Task.Run(() => LogHelper.WriteLogs(ex.Message));
            }

            return list;
        }

        private List<string> GetStation()
        {
            List<string> list = new List<string>();
            try
            {
                string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-testFlowInfos/testflowitem/listmodelitems&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={ModelTable[ModelList.Text]}";
                string jsonStr = WebHelper.Post(url);
                JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
                JArray ukey = (JArray)JsonConvert.DeserializeObject(data["data"]["items"].ToString());
                foreach (var item in ukey)
                {
                    list.Add(item["name"].ToString());
                }
            }
            catch (Exception ex)
            {
                Task.Run(() => LogHelper.WriteLogs(ex.Message));
            }

            return list;
        }

        private List<string> GetScanField()
        {
            List<string> list = new List<string>();
            try
            {
                string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-devInfos/model/listactiveitems&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={ModelTable[ModelList.Text]}";
                string jsonStr = WebHelper.Post(url);
                JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
                JArray ukey = (JArray)JsonConvert.DeserializeObject(data["data"]["items"].ToString());
                foreach (var item in ukey)
                {
                    list.Add(item["name"].ToString());
                }
            }
            catch (Exception ex)
            {
                Task.Run(() => LogHelper.WriteLogs(ex.Message));
            }

            return list;
        }

        private List<string> GetOrder()
        {
            List<string> list = new List<string>();
            try
            {
                list.Add("");
                string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-devInfos/model/model2orders&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={ModelTable[ModelList.Text]}";
                string jsonStr = WebHelper.Post(url);
                JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
                JArray ukey = (JArray)JsonConvert.DeserializeObject(data["data"]["items"].ToString());
                foreach (var item in ukey)
                {
                    list.Add(item["order_name"].ToString());
                }
            }
            catch (Exception ex)
            {
                Task.Run(() => LogHelper.WriteLogs(ex.Message));
            }

            return list;
        }

        private bool SetPacking()
        {
            bool res = false;
            try
            {
                string url = $"http://192.168.8.8/testbackground/web/index.php?r=/admin-packInfos/pack/setconfig&access-token=k_Cfne5tZGexkDO1XqDGKHYU2mZi_vpI&model_uuid={ModelTable[ModelList.Text]}";
                string json = $"{{\\\"packing_checked_items_box\\\": \\\"{BoxScanList.Text}\\\",\\\"packing_checked_items_dev\\\": \\\"{DevScanList.Text}\\\",\\\"packing_checked_items_backup\\\": \\\"{DevScanList.Text}\\\",\\\"packing_ordeChecked\\\": \\\"{OrderList.Text}\\\",\\\"packing_check_caseCode\\\": false,\\\"packing_check_recovery\\\": {packing_check_recovery.Checked},\\\"packing_check_boardBind\\\": {packing_check_boardBind.Checked},\\\"packing_check_reserveSn\\\": false}}";
                string jsonStr = WebHelper.Post(url);
                JObject data = JsonConvert.DeserializeObject<JObject>(jsonStr);
                if (data["code"].ToString() == "20000")
                {
                    res = true;
                }
            }
            catch (Exception ex)
            {
                Task.Run(() => LogHelper.WriteLogs(ex.Message));
            }

            return res;
        }

        private void SetupView_Load(object sender, EventArgs e)
        {
            ModelList.Text = Properties.Settings.Default.Model;
            ControlSerialList.DataSource = GetSerialPort();
            ScanSerialList.DataSource = GetSerialPort();
            ControlSerialList.Text = Properties.Settings.Default.ControlSerialPort;
            ScanSerialList.Text = Properties.Settings.Default.ScanSerialPort;

            //UkeyList.DataSource = GetUkeyList();
            //UkeyList.Text = Properties.Settings.Default.Ukey;
            //StationList.DataSource = GetStation();
            //StationList.Text = Properties.Settings.Default.Station;
            Fixid.Text = Properties.Settings.Default.Fixid;
            ischecklasttested.Checked = Properties.Settings.Default.ischecklasttested;
            ischecktested.Checked = Properties.Settings.Default.ischecktested;
            OrderList.DataSource = GetOrder();
            OrderList.DataSource = null;  // 先清空
            OrderList.Items.Clear();
            OrderList.Items.AddRange(GetOrder().ToArray());
            OrderList.AutoCompleteCustomSource.AddRange(GetOrder().ToArray());
            OrderList.AutoCompleteSource = AutoCompleteSource.CustomSource;
            OrderList.Text = Properties.Settings.Default.Order;
            OrderList.Font = new Font("微软雅黑", 15, FontStyle.Regular);
            //BoxScanList.DataSource = GetScanField();
            //BoxScanList.Text = Properties.Settings.Default.BoxScan;
            //DevScanList.DataSource = GetScanField();
            //DevScanList.Text = Properties.Settings.Default.DevScan;
            packing_check_boardBind.Checked = Properties.Settings.Default.packing_check_boardBind;
            packing_check_recovery.Checked = Properties.Settings.Default.packing_check_recovery;
            ProductList.Text = Properties.Settings.Default.Product;
            CMIITIDList.Text = Properties.Settings.Default.CMIITID;
            InputList.Text = Properties.Settings.Default.Input;
            OutputList.Text = Properties.Settings.Default.Output;
            TotalOutputList.Text = Properties.Settings.Default.TotalOutput;
            FilePath.Text = Properties.Settings.Default.FilePath;
            StateDelay.Text = Properties.Settings.Default.StateDelay;
            ScanDelay.Text = Properties.Settings.Default.ScanDelay;
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Model = ModelList.Text;
            Properties.Settings.Default.Model_uuid = ModelTable[ModelList.Text].ToString();
            Properties.Settings.Default.Ukey = UkeyList.Text;
            Properties.Settings.Default.Station = StationList.Text;
            Properties.Settings.Default.Fixid = Fixid.Text;
            Properties.Settings.Default.ischecklasttested = ischecklasttested.Checked;
            Properties.Settings.Default.ischecktested = ischecktested.Checked;
            Properties.Settings.Default.Order = OrderList.Text;
            Properties.Settings.Default.BoxScan = BoxScanList.Text;
            Properties.Settings.Default.DevScan = DevScanList.Text;
            Properties.Settings.Default.packing_check_boardBind = packing_check_boardBind.Checked;
            Properties.Settings.Default.packing_check_recovery = packing_check_recovery.Checked;
            Properties.Settings.Default.Product = ProductList.Text;
            Properties.Settings.Default.CMIITID = CMIITIDList.Text;
            Properties.Settings.Default.Input = InputList.Text;
            Properties.Settings.Default.Output = OutputList.Text;
            Properties.Settings.Default.TotalOutput = TotalOutputList.Text;
            Properties.Settings.Default.FilePath = FilePath.Text;
            Properties.Settings.Default.ControlSerialPort = ControlSerialList.Text;
            Properties.Settings.Default.ScanSerialPort = ScanSerialList.Text;
            if (string.IsNullOrEmpty(StateDelay.Text))
            {
                StateDelay.Text = "50";
            }
            if (string.IsNullOrEmpty(ScanDelay.Text))
            {
                ScanDelay.Text = "1000";
            }
            Properties.Settings.Default.StateDelay = StateDelay.Text;
            Properties.Settings.Default.ScanDelay = ScanDelay.Text;
            Properties.Settings.Default.Save();
            SetPacking();
            mainviewback();
        }

        private void SelectFilePath_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Filter = "VM文件|*.sol";
            if (openFile.ShowDialog() == DialogResult.OK)
            {
                FilePath.Text = openFile.FileName;
            }
            else
            {
                FilePath.Text = "";
            }
        }

        private void OnlyNumber_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != '\b')
            {
                e.Handled = true;
            }
        }
    }
}
