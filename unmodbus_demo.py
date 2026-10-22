"""
unmodbus Modbus RTU 串口通信示例
串口: COM1
"""

import serial
import time
from unmodbus import ModbusClient


def main():
    # 串口配置
    SERIAL_PORT = "COM1"
    BAUD_RATE = 9600
    DATA_BITS = 8
    STOP_BITS = 1
    PARITY = "N"  # N=None, E=Even, O=Odd

    # 创建串口连接
    ser = serial.Serial(
        port=SERIAL_PORT,
        baudrate=BAUD_RATE,
        bytesize=DATA_BITS,
        stopbits=STOP_BITS,
        parity=PARITY,
        timeout=1.0
    )

    # 创建 Modbus RTU 客户端
    client = ModbusClient(ser)

    print(f"已连接到 {SERIAL_PORT}, 波特率 {BAUD_RATE}")

    # 示例 1: 读取保持寄存器 (功能码 0x03)
    # 从地址 0 开始读取 10 个寄存器
    print("\n--- 读取保持寄存器 (FC=03) ---")
    try:
        holding_registers = client.read_holding_registers(slave_id=1, address=0, count=10)
        print(f"保持寄存器 [0-9]: {holding_registers}")
    except Exception as e:
        print(f"读取失败: {e}")

    # 示例 2: 读取输入寄存器 (功能码 0x04)
    # 从地址 0 开始读取 5 个寄存器
    print("\n--- 读取输入寄存器 (FC=04) ---")
    try:
        input_registers = client.read_input_registers(slave_id=1, address=0, count=5)
        print(f"输入寄存器 [0-4]: {input_registers}")
    except Exception as e:
        print(f"读取失败: {e}")

    # 示例 3: 读取线圈 (功能码 0x01)
    # 从地址 0 开始读取 16 个线圈
    print("\n--- 读取线圈 (FC=01) ---")
    try:
        coils = client.read_coils(slave_id=1, address=0, count=16)
        print(f"线圈 [0-15]: {coils}")
    except Exception as e:
        print(f"读取失败: {e}")

    # 示例 4: 写入单个保持寄存器 (功能码 0x06)
    # 写入地址 0，值 1234
    print("\n--- 写入单个寄存器 (FC=06) ---")
    try:
        client.write_single_register(slave_id=1, address=0, value=1234)
        print("写入成功: 地址=0, 值=1234")
    except Exception as e:
        print(f"写入失败: {e}")

    # 示例 5: 写入多个保持寄存器 (功能码 0x10)
    # 从地址 10 开始写入 [100, 200, 300]
    print("\n--- 写入多个寄存器 (FC=10) ---")
    try:
        client.write_multiple_registers(slave_id=1, address=10, values=[100, 200, 300])
        print("写入成功: 地址=10, 值=[100, 200, 300]")
    except Exception as e:
        print(f"写入失败: {e}")

    # 示例 6: 轮询示例 - 定期读取数据
    print("\n--- 轮询示例 (5秒) ---")
    for i in range(5):
        try:
            data = client.read_holding_registers(slave_id=1, address=0, count=1)
            print(f"轮询 {i+1}: 寄存器[0] = {data[0] if data else '无数据'}")
        except Exception as e:
            print(f"轮询 {i+1} 失败: {e}")
        time.sleep(1)

    # 关闭串口
    ser.close()
    print("\n连接已关闭")


if __name__ == "__main__":
    main()
