using System.Device.Gpio;

namespace IOT.Devices.Ads1232
{
	public class ADS1232
	{
		#region Variables & Const
		private readonly GpioController _gpioController;
		private int _gain;
		private const double VoltPerCount = 2.9802325940409414817025043609744e-7;
		private readonly Ads1232Reader _reader;
		private readonly object _readLock;
		#endregion

		#region Constractor	
		public ADS1232(int pinData, int pinSck, int pinGain0, int pinGain1, int pinSpeed, int pinPowerDown, int pinAddress, GpioController gpioController)
		{
			_gpioController = gpioController;

			DataPin = pinData;
			SckPin = pinSck;
			Gain0Pin = pinGain0;
			Gain1Pin = pinGain1;
			SpeedPin = pinSpeed;
			PowerDownPin = pinPowerDown;
			AddressPin = pinAddress;

			_gpioController.OpenPin(dataPin, PinMode.Input);
			_gpioController.OpenPin(sckPin, PinMode.Output);
			_gpioController.OpenPin(gain0Pin, PinMode.Output);
			_gpioController.OpenPin(gain1Pin, PinMode.Output);
			_gpioController.OpenPin(speedPin, PinMode.Output);
			_gpioController.OpenPin(PowerDownPin, PinMode.Output);
			_gpioController.OpenPin(addressPin, PinMode.Output);

			_gpioController.Write(sckPin, PinValue.High);
			_gpioController.Write(speedPin, PinValue.Low);
			PowerReset();
			_readLock = new object();
			_reader = new Ads1232Reader(_gpioController, dataPin, sckPin, _readLock);
		}
		#endregion

		#region Methods
		//public void InitialIO(int pinData, int pinSck, int pinGain0, int pinGain1, int pinSpeed, int pinPowerDown, int pinAddress)
		//{
		//	try
		//	{
		//		DataPin = pinData;
		//		SckPin = pinSck;
		//		Gain0Pin = pinGain0;
		//		Gain1Pin = pinGain1;
		//		SpeedPin = pinSpeed;
		//		PowerDownPin = pinPowerDown;
		//		AddressPin = pinAddress;
		//	}
		//	catch (Exception ex)
		//	{
		//		Console.WriteLine($"Ads1232 init error : {ex.Message}");
		//	}
		//	finally
		//	{
		//		SetupGpio();
		//	}
		//}
		private void SetupGpio()
		{
			Console.WriteLine("GPIO setup...");
			try
			{
				
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Setup GPIO Error : {ex.Message}");
			}
			finally
			{
				PowerReset();
			}
		}
		private void PowerReset()
		{
			Console.WriteLine("Power Reset...");
			_gpioController.Write(powerDownPin, PinValue.Low);
			Task.Delay(1000);
			_gpioController.Write(powerDownPin, PinValue.High);
		}

		public double ReadVoltage(int channel)
		{
			int rawValue = _reader.ReadInt();// await ReadRawValueAsync(channel);
			return (double)(rawValue * VoltPerCount / _gain);
		}
		private async Task<Int32> ReadRawValueAsync(int channel)
		{

			_gpioController.Write(addressPin, ((channel == 1) ? PinValue.High : PinValue.Low));

			int readyCount = 0;
			//await SingleClkAsync();
			_gpioController.Write(sckPin, PinValue.Low);
			while (_gpioController.Read(dataPin) == PinValue.Low) ;
			while (_gpioController.Read(dataPin) == PinValue.High) ;

			Int32 rawVal = await ReadAdcDataAsync();
			return rawVal;
		}
		private async Task<Int32> ReadAdcDataAsync()
		{
			UInt32 data = 0;
			for (int i = 0; i < 24; i++)
			{
				await SingleClkAsync();
				data = data << 1;
				data |= (byte)_gpioController.Read(dataPin);
			}

			//Console.WriteLine($"RAW DATA : {data}\t{data:x}\t{data:b}");
			_gpioController.Write(sckPin, PinValue.High);
			Int32 signedData = 0;
			if ((data & 0x800000) == 0x800000)
			{
				//Console.WriteLine($"NOT DATA     : {~data}\t{~data:x}\t{~data:b}");
				//Console.WriteLine($"NOT DATA++   : {~data + 1}\t{~data + 1:x}\t{~data + 1:b}");
				//Console.WriteLine($"NOT DATA++ & : {((~data) + 1) & 0x7fffff}\t{((~data) + 1) & 0x7fffff:x}\t{((~data) + 1) & 0x7fffff:b}");
				signedData = (Int32)((((~data) + 1) & 0x7fffff) * -1);
			}
			else
			{
				// else do not do anything the value is positive number
				signedData = (Int32)data;
			}
			//Console.WriteLine($"Signed DATA : {signedData}\t{signedData:x}\t{signedData:b}");
			return signedData;

		}
		private async Task SingleClkAsync()
		{
			_gpioController.Write(sckPin, PinValue.High);
			await Task.Delay(1);
			_gpioController.Write(sckPin, PinValue.Low);
			await Task.Delay(1);
		}
		private bool ValidatePinNumber(int pinNumber)
		{
			if (pinNumber < 0 || pinNumber >= _gpioController.PinCount)
			{
				throw new ArgumentOutOfRangeException(nameof(pinNumber), $"The given GPIO Controller has no pin number {pinNumber}");
			}

			return true;
		}

		public void SetGain(int gain)
		{
			if (gain == 1 || gain == 2 || gain == 64 || gain == 128)
			{
				_gain = gain;
				ConfigureGain();
			}
		}

		private void ConfigureGain()
		{
			switch (_gain)
			{
				case 1:
					{
						_gpioController.Write(gain0Pin, PinValue.Low);
						_gpioController.Write(gain1Pin, PinValue.Low);
						break;
					}
				case 2:
					{
						_gpioController.Write(gain0Pin, PinValue.High);
						_gpioController.Write(gain1Pin, PinValue.Low);
						break;
					}
				case 64:
					{
						_gpioController.Write(gain0Pin, PinValue.Low);
						_gpioController.Write(gain1Pin, PinValue.High);
						break;
					}
				case 128:
					{
						_gpioController.Write(gain0Pin, PinValue.High);
						_gpioController.Write(gain1Pin, PinValue.High);
						break;
					}
				default:
					break;
			}
		}
		#endregion

		#region Properties
		private int dataPin;

		public int DataPin
		{
			get { return dataPin; }
			private set
			{
				if (ValidatePinNumber(value))
					dataPin = value;
			}
		}

		private int sckPin;

		public int SckPin
		{
			get { return sckPin; }
			private set
			{
				if (ValidatePinNumber(value))
					sckPin = value;
			}
		}

		private int gain0Pin;

		public int Gain0Pin
		{
			get { return gain0Pin; }
			set
			{

				if (ValidatePinNumber(value))
					gain0Pin = value;
			}
		}

		private int gain1Pin;

		public int Gain1Pin
		{
			get { return gain1Pin; }
			set
			{

				if (ValidatePinNumber(value))
					gain1Pin = value;
			}
		}

		private int speedPin;

		public int SpeedPin
		{
			get { return speedPin; }
			set
			{
				if (ValidatePinNumber(value))
					speedPin = value;
			}
		}


		private int powerDownPin;

		public int PowerDownPin
		{
			get { return powerDownPin; }
			set
			{

				if (ValidatePinNumber(value))
					powerDownPin = value;
			}
		}

		private int addressPin;

		public int AddressPin
		{
			get { return addressPin; }
			set { addressPin = value; }
		}

		#endregion
	}
}
