using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

            StartReading();


            // Communicate
            string message;
            do
            {
                message = Console.ReadLine();
                SendMessage(message);
            } while (message != "exit");
            device.Dispose();
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

        private void BleChannel_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            DataReader bleReader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] inBytes = new byte[bleReader.UnconsumedBufferLength];
            bleReader.ReadBytes(inBytes);
            Console.WriteLine("Device says: " + Encoding.UTF8.GetString(inBytes));
        }

        private async void SendMessage(string message)
        {
            bleWriter.WriteBytes(Encoding.ASCII.GetBytes(message + "\n"));
            await bleChannel.WriteValueAsync(bleWriter.DetachBuffer());
        }

        private static void InitSerialPort()
        {
            serialPort.PortName = "COM6";
            serialPort.BaudRate = 115000;
            serialPort.ReadBufferSize = 100;

            serialPort.ReadTimeout = 500;
            serialPort.WriteTimeout = 500;

            serialPort.Open();

        }

        private async void StartReading()
        {
            byte[] buffer = new byte[11];
            byte[] received = null;
            Action kickoffRead = null;
            kickoffRead = delegate
            {
                serialPort.BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
                {
                    try
                    {
                        int actualLength = serialPort.BaseStream.EndRead(ar);
                        received = new byte[actualLength];
                        System.Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                        Console.WriteLine("Device says: " + Encoding.UTF8.GetString(received));
                    }
                    catch (IOException exc)
                    {
                        Console.WriteLine("Exception raised during reading");
                    }
                    kickoffRead();
                }, null);
            };
            kickoffRead();

        }

    }
}
