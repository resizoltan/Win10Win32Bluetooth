using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.IO.Ports;
using System.IO;


namespace ConsoleApplication1
{
    class Program
    {
        BluetoothLEDevice device = null;
        GattCharacteristic bleChannel = null;
        string bleChannelServiceUuidString = "0000ffe0-0000-1000-8000-00805f9b34fb";
        string bleChannelCharacteristicUuidString = "0000ffe1-0000-1000-8000-00805f9b34fb";

        DataWriter bleWriter = new DataWriter();

        static SerialPort serialPort = new SerialPort();
        static SerialPort ftdi = new SerialPort();
        enum Target
        {
            Bootloader,
            Console,
            Matlab,
            Unknown
        }
        
        Target curTarget = Target.Bootloader;
        const int receive_buf_size = 100;
        byte[] receiveBuffer = new byte[receive_buf_size];

        const byte ack = 100; //100 is acknowledgement byte


        static void Main(string[] args)
        {
            InitSerialPort();
            // Start the program
            var program = new Program();
            
        }

        public Program()
        {
            // Create Bluetooth Listener
            var watcher = new BluetoothLEAdvertisementWatcher();

            watcher.ScanningMode = BluetoothLEScanningMode.Active;

            // Only activate the watcher when we're recieving values >= -80
            watcher.SignalStrengthFilter.InRangeThresholdInDBm = -50;

            // Stop watching if the value drops below -90 (user walked away)
            watcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -70;

            // Only search for advertisements which satisfy the following filters
            watcher.AdvertisementFilter.Advertisement.LocalName = "JDY-10-V2.4";

            // Register callback for when we see an advertisements
            watcher.Received += OnAdvertisementReceived;

            // Wait 5 seconds to make sure the device is really out of range
            watcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(5000);
            watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(2000);

            // Starting watching for advertisements
            watcher.Start();

            //StartReadingFTDI();
            StartReadingSerial();


            // Communicate
            string message;
            do
            {
                message = Console.ReadLine();
                //SendMessage(message);
            } while (message != "exit");
            device.Dispose();
            serialPort.Close();
            //ftdi.Close();
        }

        private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            // Tell the user we see an advertisement and print some properties
            Console.WriteLine(String.Format("Advertisement:"));
            Console.WriteLine(String.Format("  BT_ADDR: {0}", eventArgs.BluetoothAddress));
            Console.WriteLine(String.Format("  FR_NAME: {0}", eventArgs.Advertisement.LocalName));
            Console.WriteLine();

            watcher.Stop();

            device = await BluetoothLEDevice.FromBluetoothAddressAsync(eventArgs.BluetoothAddress);

            if(device != null)
            {
                Console.WriteLine();
                Console.WriteLine("Successfully connected to device\nAvailable services: " + device.GattServices.Count);
                
                foreach(GattDeviceService service in device.GattServices)
                {
                    Console.WriteLine("Uuid: " + service.Uuid);
                }

                Console.WriteLine("Searching for service: {" + bleChannelServiceUuidString + "}");

                GattDeviceService bleChannelService = null;
                try
                {
                    bleChannelService = device.GattServices.Single(service => service.Uuid == new Guid(bleChannelServiceUuidString));
                }
                catch(Exception e)
                {
                    Console.WriteLine("More than one service found");
                    device.Dispose();
                    return;
                }
                if(bleChannelService == null)
                {
                    Console.WriteLine("Service not found");
                    device.Dispose();
                    return;
                }

                Console.WriteLine("Available characteristics: " + bleChannelService.GetAllCharacteristics().Count);

                foreach (GattCharacteristic characteristic in bleChannelService.GetAllCharacteristics())
                {
                    Console.WriteLine("Uuid: " + characteristic.Uuid);
                }

                try
                {
                    bleChannel = bleChannelService.GetAllCharacteristics().Single(
                        characteristic => characteristic.Uuid == new Guid(bleChannelCharacteristicUuidString));
                }
                catch (Exception e)
                {
                    Console.WriteLine("More than one characteristic found");
                    device.Dispose();
                    return;
                }
                if (bleChannel == null)
                {
                    Console.WriteLine("Characteristic not found");
                    device.Dispose();
                    return;
                }

                GattCommunicationStatus status = await bleChannel.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if(status == GattCommunicationStatus.Success)
                {
                    bleChannel.ValueChanged += BleChannel_ValueChanged;
                    Console.WriteLine("Subscribed to notifications");
                }
                else
                {
                    Console.WriteLine("Couldn't subscribe to notifications");
                }

               

            }
        }
        bool echoing = true;
        private async void BleChannel_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            DataReader bleReader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] inBytes = new byte[bleReader.UnconsumedBufferLength];
            bleReader.ReadBytes(inBytes);
            //serialPort.Write(inBytes, 0, inBytes.Length);
            //int pckLength = -1;
            //int index = 0;
            int echoLength = -1;

            byte[] xcpPacket = null;
            //Console.WriteLine("STM32 msg size: " + inBytes.Length + " bytes");
            //Console.Write(inBytes, 0, inBytes.Length);
            byte[] msg = new byte[inBytes.Length - 1];
            Array.Copy(inBytes, 1, msg, 0, inBytes.Length - 1);
            switch (curTarget)
            {
                case Target.Bootloader:
                    if (echoing)
                    {
                        /*int echoIndex = 0;
                        if (echoLength == -1)
                        {
                            echoIndex = 1;
                            echoLength = inBytes[0];
                            Console.Write("Echo: \t\t" + (echoLength) + ":\t");
                        }
                        for (; echoIndex <= echoLength; echoIndex++)
                        {
                            //Console.Write(inBytes[echoIndex].ToString("X"));
                            if(echoIndex == inBytes.Length)
                            {
                                echoLength = echoLength - inBytes.Length + 1;
                                break;
                            }
                            Console.Write(inBytes[echoIndex].ToString("X"));
                        }
                        Console.WriteLine("");*/
                        echoLength = inBytes[0];
                        /*Console.Write("Echo: \t\t" + (echoLength) + ":\t");
                        for (int echoIndex = 1; echoIndex <= echoLength; echoIndex++)
                        {
                            //Console.Write(inBytes[echoIndex].ToString("X"));
                            if (echoIndex == inBytes.Length)
                            {
                                echoLength = echoLength - inBytes.Length + 1;
                                break;
                            }
                            Console.Write(inBytes[echoIndex].ToString("X") + "-");
                        }
                        Console.WriteLine("");*/
                        if (inBytes.Length == echoLength + 1)
                        {
                            //Console.WriteLine("No echo now");
                            echoing = false;
                            break;
                        }
                    }               
                    xcpPacket = new byte[inBytes.Length - echoLength - 1];
                    Array.Copy(inBytes, echoLength + 1, xcpPacket, 0, inBytes.Length - echoLength - 1);
                    SendToSerial(xcpPacket);
                    /*Console.Write("Bootloader says: ");
                    foreach(byte b in xcpPacket)
                    {
                        Console.Write(b.ToString("X"));
                    }
                    Console.WriteLine("");*/
                    echoing = true;
                    break;
                case Target.Unknown:
                    curTarget = (Target)inBytes[0];
                    sendAck();

                    break;
                case Target.Console:
                    for (int i = 0; i < inBytes.Length; i++)
                    {
                       
                        if(inBytes[i] == 23) // end of transition block
                        {
                            curTarget = Target.Unknown;
                            sendAck();
                            break;
                        }
                        else if (inBytes[i] == '\0')
                        {
                            sendAck();
                            break;
                        }
                        else
                        {
                            Console.Write(Convert.ToChar(inBytes[i]));
                        }
                    }
                    break;
                case Target.Matlab:
                    if (inBytes.Last() == 23)
                    { // end of transition block, will be implemented some other way
                        curTarget = Target.Unknown;
                        SendToSerial(msg);// send truncated msg
                    }
                    else
                    {
                        SendToSerial(inBytes);// send original msg
                    }
                    sendAck();

                    break;
            }


            //for (int i = 0; i < inBytes.Length; i++)
            //{
            //    if (inBytes[i] == '\0')
            //    {
            //        break;
            //    }
            //    else
            //    {
            //        receiveBuffer[i] = inBytes[i];
            //    }
            //}

            //Console.WriteLine("");
        }

        private async void sendAck()
        {
            bleWriter.WriteByte(ack);
            await bleChannel.WriteValueAsync(bleWriter.DetachBuffer());
        }
        volatile static bool sending = false;
        private async void SendToSTM32(byte[] message)
        {
            //ftdi.Write(message, 0, message.Length);
            //Console.WriteLine("Message: 0x" + message[0].ToString("X") + ", size: " + message.Length);
            Thread.Sleep(2);
            //await Task.Delay(500);
            //while (sending) ;
           // sending = true;
            bleWriter.WriteBytes(message);
            await bleChannel.WriteValueAsync(bleWriter.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
           // sending = false;
            //Console.WriteLine("Serial says: " + Convert.ToString(message[0], 16) + "," + Convert.ToString(message[0],2));
            //bleWriter.WriteBytes(message);
            //Console.WriteLine("DetachBuffer: " + bleWriter.DetachBuffer().Length);
            //Console.WriteLine("Size of message: " + message.Length);
            //Console.Write("CubeMX says: ");
            //foreach(byte b in message)
            //{
            //    Console.Write(Convert.ToString(b, 16) + " - " + Convert.ToString(b, 2) + ", ");
            //}
            //Console.WriteLine();

        }

        private void SendToSerial(byte[] message)
        {
            
            serialPort.Write(message, 0, message.Length);

        }

        private static void InitSerialPort()
        {
            serialPort.PortName = "COM8";
            serialPort.BaudRate = 9600;
            serialPort.ReadBufferSize = 100;
            serialPort.Parity = Parity.Even;
            serialPort.Open();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            //ftdi.PortName = "COM6";
            //ftdi.BaudRate = 115200;
            //ftdi.ReadBufferSize = 2;
            //ftdi.Parity = Parity.Even;
            //ftdi.Open();

        }

        private void StartReadingSerial()
        {
            byte[] buffer = new byte[1];
            byte[] packetData = null;
            Action kickoffRead = null;
            int pckLength = -1;
            int index = 0;
            bool echoing = true;
            int echoLength = -1;
            int echoIndex = 0;
            kickoffRead = async delegate
            {
                int actualLength = await serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                byte[] received = new byte[actualLength];
                System.Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                try
                {
                    if (actualLength > 0)
                    {
                        if (pckLength == -1)
                        {
                            pckLength = received[0];
                            packetData = new byte[pckLength];
                            if (pckLength == 0)
                            {
                                pckLength = -1;
                                index = 0;
                                //echoing = true;
                            }
                        }
                        else
                        {
                            packetData[index] = received[0];
                            index++;
                            //SendToSTM32(buffer);

                            if (index == pckLength)
                            {

                                //Console.Write("Microboot says: " + pckLength + ":\t");
                                /*foreach (byte b in packetData)
                                {
                                    //Console.Write(b.ToString("X") + "-");
                                }
                               // Console.WriteLine("");*/
                                //echoing = true;
                                pckLength = -1;
                                index = 0;
                            }
                        }
                        SendToSTM32(buffer);

                    }
                }
                catch (IOException exc)
                {
                    Console.WriteLine("Exception raised during reading");
                }
                kickoffRead();
            };
            kickoffRead();

        }

        private void StartReadingFTDI()
        {
            byte[] buffer = new byte[1];
            Action kickoffRead = null;
            kickoffRead = async delegate
            {
                int actualLength = await ftdi.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                try
                {
                    //byte[] received = new byte[actualLength];
                    //System.Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                    SendToSerial(buffer);
                    Console.WriteLine("STM32 says: " + Convert.ToString(buffer[0], 16));
                }
                catch (IOException exc)
                {
                    Console.WriteLine("Exception raised during reading");
                }
                kickoffRead();
            };
            kickoffRead();

        }

    }
}
