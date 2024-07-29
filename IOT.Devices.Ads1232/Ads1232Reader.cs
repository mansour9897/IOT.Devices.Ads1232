using System.Device.Gpio;

namespace IOT.Devices.Ads1232
{
	public class Ads1232Reader
	{
		private readonly GpioController _gpioController;
		private readonly int _pinDout;
		private readonly int _pinPD_Sck;

		private readonly object _readLock;


		public Ads1232Reader(GpioController gpioController, int pinDout, int pinPD_Sck, object readLock)
		{
			_gpioController = gpioController;
			_pinDout = pinDout;
			_pinPD_Sck = pinPD_Sck;
			_readLock = readLock;

		}


		//public int Read(int numberOfReads = 3, int offsetFromZero = 0)
		//{
		//	// Make sure we've been asked to take a rational amount of samples.
		//	if (numberOfReads <= 0)
		//	{
		//		throw new ArgumentException(message: "Param value must be greater than zero!", nameof(numberOfReads));
		//	}

		//	// If we're only average across one value, just read it and return it.
		//	if (numberOfReads == 1)
		//	{
		//		return CalculateNetValue(ReadInt(), offsetFromZero);
		//	}

		//	// If we're averaging across a low amount of values, just take the
		//	// median.
		//	if (numberOfReads < 5)
		//	{
		//		return CalculateNetValue(ReadMedian(numberOfReads), offsetFromZero);
		//	}

		//	return CalculateNetValue(ReadAverage(numberOfReads), offsetFromZero);
		//}

		/// <summary>
		/// Calculate net value
		/// </summary>
		/// <param name="value">Gross value read from Hx711</param>
		/// <param name="offset">Offset value from 0</param>
		/// <returns>Return net value read</returns>
		//private static int CalculateNetValue(int value, int offset)
		//{
		//	return value - offset;
		//}


		/// <summary>
		/// The output 24 bits of data is in 2's complement format. Convert it to int.
		/// </summary>
		/// <param name="inputValue">24 bit in 2' complement format</param>
		/// <returns>Int converted</returns>
		/// 
		private int ConvertFromTwosComplement24bit(int inputValue)
		{
			// Docs says
			// "When input differential signal goes out of the 24-bit range,
			// the output data will be saturated at 800000h (MIN) or 7FFFFFh (MAX),
			// until the input signal comes back to the input range.", page 4
			// 24 bit in 2's complement only 23 are a value if
			// the number is negative. 0xFFFFFF >> 1 = 0x7FFFFF
			// Mask to take true value
			const int MaxValue = 0x7FFFFF;
			// Mask to take sign bit
			const int BitSign = 0x800000;
			return -(inputValue & BitSign) + (inputValue & MaxValue);
		}

		/// <summary>
		/// Check if data is ready
		/// </summary>
		private bool IsOutputDataReady()
		{
			// Doc says "When output data is not ready for retrieval, digital output
			// pin DOUT is high.
			// When DOUT goes to low, it indicates data is ready for retrieval", page 4
			
			var valueRead = _gpioController.Read(_pinDout);
			return valueRead != PinValue.High;
		}

		/// <summary>
		/// A avarage-based read method, might help when getting random value spikes
		/// </summary>
		/// <param name="numberOfReads">Number of readings to take from which to average, to get a more accurate value.</param>
		private int ReadAverage(int numberOfReads)
		{
			// If we're taking a lot of samples, we'll collect them in a list, remove
			// the outliers, then take the mean of the remaining set.
			var valueList = new List<int>(numberOfReads);

			for (int x = 0; x < numberOfReads; x++)
			{
				valueList.Add(ReadInt());
			}

			valueList.Sort();

			// We'll be trimming 20% of outlier samples from top and bottom of collected set.
			int trimAmount = Convert.ToInt32(Math.Round(valueList.Count * 0.2));

			// Trim the edge case values.
			valueList = valueList.Skip(trimAmount).Take(valueList.Count - (trimAmount * 2)).ToList();

			// Return the mean of remaining samples.
			return Convert.ToInt32(Math.Round(valueList.Average()));
		}

		/// <summary>
		/// A median-based read method, might help when getting random value spikes for unknown or CPU-related reasons
		/// </summary>
		/// <param name="numberOfReads">Number of readings to take from which to average, to get a more accurate value.</param>
		/// <returns>Return a int value read</returns>
		private int ReadMedian(int numberOfReads)
		{
			var valueList = new List<int>(numberOfReads);

			for (int x = 0; x < numberOfReads; x++)
			{
				valueList.Add(ReadInt());
			}

			valueList.Sort();

			// If times is odd we can just take the centre value.
			if ((numberOfReads & 0x1) == 0x1)
			{
				return valueList[valueList.Count / 2];
			}
			else
			{
				// If times is even we have to take the arithmetic mean of
				// the two middle values.
				var midpoint = valueList.Count / 2;
				return (valueList[midpoint] + valueList[midpoint + 1]) / 2;
			}
		}

		/// <summary>
		/// Read a weight value from Ads1232
		/// </summary>
		/// <returns>Return a signed int value read</returns>
		public int ReadInt()
		{
			// Get a sample from the Hx711 in the form of raw bytes.
			var dataBytes = ReadRawBytes();

			// Join the raw bytes into a single 24bit 2s complement value.
			int twosComplementValue = (dataBytes[0] << 16)
				| (dataBytes[1] << 8)
				| dataBytes[2];

			// Convert from 24bit twos-complement to a signed value.
			int signedIntValue = ConvertFromTwosComplement24bit(twosComplementValue);

			// Return the sample value we've read from the Hx711.
			return signedIntValue;
		}

		/// <summary>
		/// Read one value from ADS1232
		/// </summary>
		/// <returns>Return bytes read</returns>
		private byte[] ReadRawBytes()
		{
			// Wait for and get the Read Lock, incase another thread is already
			lock (_readLock)
			{
				_gpioController.Write(_pinPD_Sck, PinValue.Low);

				// Wait until ADS1232 is ready for us to read a sample.
				while (!IsOutputDataReady())
				{
					Thread.Sleep(TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond));
				}

				// Read three bytes (24bit) of data from the Hx711.
				var firstByte = ReadNextByte();
				var secondByte = ReadNextByte();
				var thirdByte = ReadNextByte();

				return (new[] { firstByte, secondByte, thirdByte });

				// Release the Read Lock, now that we've finished driving the ADS1232
			}
		}

		/// <summary>
		/// Read bits and build the byte
		/// </summary>
		/// <returns>Byte readed by ADS1232</returns>
		private byte ReadNextByte()
		{
			byte byteValue = 0;

			// Read bits and build the byte from top, or bottom, depending
			for (int x = 0; x < 8; x++)
			{
				byteValue <<= 1;
				byteValue |= ReadNextBit();
			}

			return byteValue;
		}

		/// <summary>
		/// Read next bit by send a signal to ADS1232
		/// </summary>
		/// <returns>Return bit read from ADS1232</returns>
		private byte ReadNextBit()
		{
			// Clock ADS1232 Digital Serial Clock (PD_SCK). DOUT will be
			// ready 1µs after PD_SCK rising edge, so we sample after
			// lowering PD_SCL, when we know DOUT will be stable.
			_gpioController.Write(_pinPD_Sck, PinValue.High);
			_gpioController.Write(_pinPD_Sck, PinValue.Low);
			var value = _gpioController.Read(_pinDout);

			return value == PinValue.High ? (byte)1 : (byte)0;
		}
	}
}

