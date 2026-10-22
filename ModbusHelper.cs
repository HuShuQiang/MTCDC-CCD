using Modbus.Device;
using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;

namespace 美团充电仓CCD
{
    /// <summary>
    /// Modbus数据转换工具
    /// </summary>
    public static class ModbusUtility
    {
        /// <summary>
        /// int32转2个ushort寄存器 (低字在前)
        /// </summary>
        public static ushort[] GetInt32Registers(int value)
        {
            uint unsigned = (uint)value;
            return new ushort[]
            {
                (ushort)(unsigned & 0xFFFF),           // 低字
                (ushort)((unsigned >> 16) & 0xFFFF)    // 高字
            };
        }

        /// <summary>
        /// uint32转2个ushort寄存器 (低字在前)
        /// </summary>
        public static ushort[] GetUInt32Registers(uint value)
        {
            return new ushort[]
            {
                (ushort)(value & 0xFFFF),
                (ushort)((value >> 16) & 0xFFFF)
            };
        }

        /// <summary>
        /// 2个ushort寄存器转int32 (低字在前)
        /// </summary>
        public static int GetInt32(ushort low, ushort high)
        {
            uint unsigned = ((uint)high << 16) | low;
            return (int)unsigned;
        }

        /// <summary>
        /// 2个ushort寄存器转uint32 (低字在前)
        /// </summary>
        public static uint GetUInt32(ushort low, ushort high)
        {
            return ((uint)high << 16) | low;
        }

        /// <summary>
        /// float转2个ushort寄存器 (默认ABCD，高字在前)
        /// </summary>
        public static ushort[] GetFloatRegisters(float value, bool highFirst = true)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                // ABCD: BADC需要交换
                // CDAB: DCBA需要交换
                if (highFirst)
                {
                    // ABCD -> BADC
                    return new ushort[]
                    {
                        (ushort)((bytes[1] << 8) | bytes[0]),   // 高字
                        (ushort)((bytes[3] << 8) | bytes[2])    // 低字
                    };
                }
                else
                {
                    // CDAB -> DCBA
                    return new ushort[]
                    {
                        (ushort)((bytes[2] << 8) | bytes[3]),   // 低字
                        (ushort)((bytes[0] << 8) | bytes[1])    // 高字
                    };
                }
            }
            else
            {
                // 大端机器
                if (highFirst)
                {
                    return new ushort[]
                    {
                        (ushort)((bytes[0] << 8) | bytes[1]),
                        (ushort)((bytes[2] << 8) | bytes[3])
                    };
                }
                else
                {
                    return new ushort[]
                    {
                        (ushort)((bytes[2] << 8) | bytes[3]),
                        (ushort)((bytes[0] << 8) | bytes[1])
                    };
                }
            }
        }

        /// <summary>
        /// 2个ushort寄存器转float (默认ABCD，高字在前)
        /// </summary>
        public static float GetFloat(ushort high, ushort low, bool highFirst = true)
        {
            byte[] bytes;
            if (BitConverter.IsLittleEndian)
            {
                if (highFirst)
                {
                    // ABCD <- BADC
                    bytes = new byte[]
                    {
                        (byte)(low & 0xFF), (byte)((low >> 8) & 0xFF),
                        (byte)(high & 0xFF), (byte)((high >> 8) & 0xFF)
                    };
                }
                else
                {
                    // CDAB <- DCBA
                    bytes = new byte[]
                    {
                        (byte)((high >> 8) & 0xFF), (byte)(high & 0xFF),
                        (byte)((low >> 8) & 0xFF), (byte)(low & 0xFF)
                    };
                }
            }
            else
            {
                if (highFirst)
                {
                    bytes = new byte[]
                    {
                        (byte)((high >> 8) & 0xFF), (byte)(high & 0xFF),
                        (byte)((low >> 8) & 0xFF), (byte)(low & 0xFF)
                    };
                }
                else
                {
                    bytes = new byte[]
                    {
                        (byte)(low & 0xFF), (byte)((low >> 8) & 0xFF),
                        (byte)(high & 0xFF), (byte)((high >> 8) & 0xFF)
                    };
                }
            }
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 高低字交换的float转换 (某些设备用DCBA格式)
        /// </summary>
        public static float GetFloatDCBA(ushort high, ushort low)
        {
            byte[] bytes = new byte[]
            {
                (byte)((low >> 8) & 0xFF), (byte)(low & 0xFF),
                (byte)((high >> 8) & 0xFF), (byte)(high & 0xFF)
            };
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 高低字交换的float转换 (某些设备用CDAB格式)
        /// </summary>
        public static float GetFloatCDAB(ushort high, ushort low)
        {
            byte[] bytes = new byte[]
            {
                (byte)(high & 0xFF), (byte)((high >> 8) & 0xFF),
                (byte)(low & 0xFF), (byte)((low >> 8) & 0xFF)
            };
            return BitConverter.ToSingle(bytes, 0);
        }
    }

    /// <summary>
    /// Modbus通讯模式
    /// </summary>
    public enum ModbusMode
    {
        RTU,
        TCP
    }

    /// <summary>
    /// 异步结果回调
    /// </summary>
    public delegate void ModbusAsyncResult<T>(bool success, string message, T data);

    /// <summary>
    /// 数据变化回调
    /// </summary>
    public delegate void ModbusDataChangedCallback(ushort startAddress, ushort[] values);

    /// <summary>
    /// NModbus4 串口通讯类
    /// 兼容 .NET Framework 4.0+
    /// </summary>
    public class ModbusHelper : IDisposable
    {
        #region 私有字段

        private SerialPort _serialPort;
        private TcpClient _tcpClient;
        private ModbusMaster _modbusMaster;
        private ModbusMode _mode;
        private CancellationTokenSource _asyncCts;
        private bool _disposed;

        private readonly string _portName;
        private readonly int _baudRate;
        private readonly Parity _parity;
        private readonly int _dataBits;
        private readonly StopBits _stopBits;
        private readonly byte _slaveId;
        private readonly int _timeout;

        private readonly string _ipAddress;
        private readonly int _port;

        #endregion

        #region 属性

        public bool IsConnected
        {
            get
            {
                if (_mode == ModbusMode.TCP)
                    return _tcpClient != null && _tcpClient.Connected;
                return _serialPort != null && _serialPort.IsOpen;
            }
        }

        public ModbusMode Mode
        {
            get { return _mode; }
        }

        #endregion

        #region 构造函数

        /// <summary>
        /// 串口RTU模式
        /// </summary>
        public ModbusHelper(
            string portName,
            int baudRate = 38400,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            byte slaveId = 1,
            int timeout = 1000)
        {
            _mode = ModbusMode.RTU;
            _portName = portName;
            _baudRate = baudRate;
            _parity = parity;
            _dataBits = dataBits;
            _stopBits = stopBits;
            _slaveId = slaveId;
            _timeout = timeout;
        }

        /// <summary>
        /// TCP模式
        /// </summary>
        public ModbusHelper(
            string ipAddress,
            int port,
            byte slaveId = 1,
            int timeout = 1000)
        {
            _mode = ModbusMode.TCP;
            _ipAddress = ipAddress;
            _port = port;
            _slaveId = slaveId;
            _timeout = timeout;
        }

        #endregion

        #region 连接管理

        public bool Connect()
        {
            try
            {
                Disconnect();

                if (_mode == ModbusMode.RTU)
                    return ConnectSerial();
                return ConnectTcp();
            }
            catch (Exception ex)
            {
                throw new ModbusException("连接失败", ex);
            }
        }

        public void Disconnect()
        {
            if (_asyncCts != null)
            {
                _asyncCts.Cancel();
                _asyncCts.Dispose();
                _asyncCts = null;
            }

            if (_modbusMaster != null)
            {
                _modbusMaster.Dispose();
                _modbusMaster = null;
            }

            if (_mode == ModbusMode.RTU)
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            else
            {
                if (_tcpClient != null)
                {
                    _tcpClient.Close();
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }
            }
        }

        private bool ConnectSerial()
        {
            _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits);
            _serialPort.ReadTimeout = _timeout;
            _serialPort.WriteTimeout = _timeout;
            _serialPort.Open();
            _modbusMaster = ModbusSerialMaster.CreateRtu(_serialPort);
            _modbusMaster.Transport.ReadTimeout = _timeout;
            _modbusMaster.Transport.WriteTimeout = _timeout;
            _modbusMaster.Transport.Retries = 3;
            return true;
        }

        private bool ConnectTcp()
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(_ipAddress, _port);
            _modbusMaster = ModbusIpMaster.CreateIp(_tcpClient);
            _modbusMaster.Transport.ReadTimeout = _timeout;
            _modbusMaster.Transport.WriteTimeout = _timeout;
            _modbusMaster.Transport.Retries = 3;
            return true;
        }

        #endregion

        #region 读线圈 - FC01

        public bool[] ReadCoils(ushort startAddress, ushort numberOfPoints)
        {
            CheckConnection();
            return _modbusMaster.ReadCoils(_slaveId, startAddress, numberOfPoints);
        }

        public void ReadCoilsAsync(ushort startAddress, ushort numberOfPoints, ModbusAsyncResult<bool[]> callback)
        {
            Action operation = () =>
            {
                try
                {
                    bool[] result = ReadCoils(startAddress, numberOfPoints);
                    if (callback != null)
                        callback(true, null, result);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 读离散输入 - FC02

        public bool[] ReadDiscreteInputs(ushort startAddress, ushort numberOfPoints)
        {
            CheckConnection();
            return _modbusMaster.ReadInputs(_slaveId, startAddress, numberOfPoints);
        }

        public void ReadDiscreteInputsAsync(ushort startAddress, ushort numberOfPoints, ModbusAsyncResult<bool[]> callback)
        {
            Action operation = () =>
            {
                try
                {
                    bool[] result = ReadDiscreteInputs(startAddress, numberOfPoints);
                    if (callback != null)
                        callback(true, null, result);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 读保持寄存器 - FC03

        public ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints)
        {
            CheckConnection();
            return _modbusMaster.ReadHoldingRegisters(_slaveId, startAddress, numberOfPoints);
        }

        public void ReadHoldingRegistersAsync(ushort startAddress, ushort numberOfPoints, ModbusAsyncResult<ushort[]> callback)
        {
            Action operation = () =>
            {
                try
                {
                    ushort[] result = ReadHoldingRegisters(startAddress, numberOfPoints);
                    if (callback != null)
                        callback(true, null, result);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 读输入寄存器 - FC04

        public ushort[] ReadInputRegisters(ushort startAddress, ushort numberOfPoints)
        {
            CheckConnection();
            return _modbusMaster.ReadInputRegisters(_slaveId, startAddress, numberOfPoints);
        }

        public void ReadInputRegistersAsync(ushort startAddress, ushort numberOfPoints, ModbusAsyncResult<ushort[]> callback)
        {
            Action operation = () =>
            {
                try
                {
                    ushort[] result = ReadInputRegisters(startAddress, numberOfPoints);
                    if (callback != null)
                        callback(true, null, result);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 写单个线圈 - FC05

        public void WriteSingleCoil(ushort coilAddress, bool value)
        {
            CheckConnection();
            _modbusMaster.WriteSingleCoil(_slaveId, coilAddress, value);
        }

        public void WriteSingleCoilAsync(ushort coilAddress, bool value, ModbusAsyncResult<object> callback)
        {
            Action operation = () =>
            {
                try
                {
                    WriteSingleCoil(coilAddress, value);
                    if (callback != null)
                        callback(true, null, null);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 写单个寄存器 - FC06

        public void WriteSingleRegister(ushort registerAddress, ushort value)
        {
            CheckConnection();
            _modbusMaster.WriteSingleRegister(_slaveId, registerAddress, value);
        }

        public void WriteSingleRegisterAsync(ushort registerAddress, ushort value, ModbusAsyncResult<object> callback)
        {
            Action operation = () =>
            {
                try
                {
                    WriteSingleRegister(registerAddress, value);
                    if (callback != null)
                        callback(true, null, null);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 写多个线圈 - FC15

        public void WriteMultipleCoils(ushort startAddress, bool[] data)
        {
            CheckConnection();
            _modbusMaster.WriteMultipleCoils(_slaveId, startAddress, data);
        }

        public void WriteMultipleCoilsAsync(ushort startAddress, bool[] data, ModbusAsyncResult<object> callback)
        {
            Action operation = () =>
            {
                try
                {
                    WriteMultipleCoils(startAddress, data);
                    if (callback != null)
                        callback(true, null, null);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 写多个寄存器 - FC16

        public void WriteMultipleRegisters(ushort startAddress, ushort[] data)
        {
            CheckConnection();
            _modbusMaster.WriteMultipleRegisters(_slaveId, startAddress, data);
        }

        public void WriteMultipleRegistersAsync(ushort startAddress, ushort[] data, ModbusAsyncResult<object> callback)
        {
            Action operation = () =>
            {
                try
                {
                    WriteMultipleRegisters(startAddress, data);
                    if (callback != null)
                        callback(true, null, null);
                }
                catch (Exception ex)
                {
                    if (callback != null)
                        callback(false, ex.Message, null);
                }
            };
            operation.BeginInvoke(null, null);
        }

        #endregion

        #region 异步循环读取

        public void StartAsyncReadLoop(
            ushort startAddress,
            ushort numberOfPoints,
            int intervalMs,
            ModbusDataChangedCallback callback,
            bool readHoldingRegisters = true)
        {
            _asyncCts = new CancellationTokenSource();
            CancellationToken token = _asyncCts.Token;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        ushort[] values;
                        if (readHoldingRegisters)
                            values = ReadHoldingRegisters(startAddress, numberOfPoints);
                        else
                            values = ReadInputRegisters(startAddress, numberOfPoints);

                        ModbusDataChangedCallback cb = callback;
                        if (cb != null)
                            cb(startAddress, values);
                    }
                    catch
                    {
                        Thread.Sleep(1000);
                    }

                    Thread.Sleep(intervalMs);
                }
            });
        }

        public void StopAsyncReadLoop()
        {
            if (_asyncCts != null)
                _asyncCts.Cancel();
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 读int32 (占2个寄存器)
        /// </summary>
        public int ReadInt32(ushort startAddress)
        {
            ushort[] registers = ReadHoldingRegisters(startAddress, 2);
            return ModbusUtility.GetInt32(registers[0], registers[1]);
        }

        /// <summary>
        /// 写int32 (占2个寄存器)
        /// </summary>
        public void WriteInt32(ushort startAddress, int value)
        {
            ushort[] registers = ModbusUtility.GetInt32Registers(value);
            WriteMultipleRegisters(startAddress, registers);
        }

        /// <summary>
        /// 读float (占2个寄存器)
        /// </summary>
        public float ReadFloat(ushort startAddress, bool highFirst = true)
        {
            ushort[] registers = ReadHoldingRegisters(startAddress, 2);
            return ModbusUtility.GetFloat(registers[0], registers[1], highFirst);
        }

        /// <summary>
        /// 写float (占2个寄存器)
        /// </summary>
        public void WriteFloat(ushort startAddress, float value, bool highFirst = true)
        {
            ushort[] registers = ModbusUtility.GetFloatRegisters(value, highFirst);
            WriteMultipleRegisters(startAddress, registers);
        }

        private void CheckConnection()
        {
            if (_modbusMaster == null || !IsConnected)
                throw new ModbusException("Modbus未连接");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }

        #endregion
    }

    #region 自定义异常

    public class ModbusException : Exception
    {
        public ModbusException(string message) : base(message) { }
        public ModbusException(string message, Exception inner) : base(message, inner) { }
    }

    #endregion
}