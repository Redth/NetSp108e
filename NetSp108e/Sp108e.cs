using System;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace NetSp108e
{
	public class Sp108eAnimationMode
	{
		string value;

		internal Sp108eAnimationMode(string v)
			=> value = v;

		public override string ToString()
			=> value;

		public override bool Equals(object obj)
			=> value.Equals(obj?.ToString(), StringComparison.OrdinalIgnoreCase);

		public override int GetHashCode()
			=> value.ToLowerInvariant().GetHashCode();

		public static Sp108eAnimationMode Null = new Sp108eAnimationMode("00");

		public static Sp108eAnimationMode Meteor = new Sp108eAnimationMode("CD");
		public static Sp108eAnimationMode Breathing = new Sp108eAnimationMode("CE");
		public static Sp108eAnimationMode Wave = new Sp108eAnimationMode("D1");
		public static Sp108eAnimationMode Catchup = new Sp108eAnimationMode("D4");
		public static Sp108eAnimationMode Static = new Sp108eAnimationMode("D3");
		public static Sp108eAnimationMode Stack = new Sp108eAnimationMode("CF");
		public static Sp108eAnimationMode Flash = new Sp108eAnimationMode("D2");
		public static Sp108eAnimationMode Flow = new Sp108eAnimationMode("D0");
	}

	public class Sp108eStatus
	{
		public bool On { get; private set; }
		public Sp108eAnimationMode AnimationMode { get; private set; }

		public int Speed { get; private set; }

		public int Brightness { get; private set; }

		public string ColorOrder { get; private set; }
		public int LedsPerSegment { get; private set; }
		public int NumberOfSegments { get; private set; }
		public string Color { get; private set; }
		public string IcType { get; private set; }
		public int RecordedPatterns { get; private set; }
		public int WhiteBrightness { get; private set; }

		internal static Sp108eStatus FromResponse(string r)
			=> new Sp108eStatus
			{
				On = r.Substring(2, 2) == "01",
				AnimationMode = new Sp108eAnimationMode(r.Substring(4, 2)),
				Speed = Convert.ToInt32(r.Substring(6, 2), 16),
				Brightness = Convert.ToInt32(r.Substring(8, 2), 16),
				ColorOrder = r.Substring(10, 2),
				LedsPerSegment = Convert.ToInt32(r.Substring(12, 4), 16),
				NumberOfSegments = Convert.ToInt32(r.Substring(16, 4), 16),
				Color = r.Substring(20, 6),
				IcType = r.Substring(26, 2),
				RecordedPatterns = Convert.ToInt32(r.Substring(28, 2), 16),
				WhiteBrightness = Convert.ToInt32(r.Substring(30, 2), 16),
			};
	}
	public class Sp108e
	{
		const string CMD_GET_NAME = "77";
		const string CMD_GET_STATUS = "10";
		const string CMD_PREFIX = "38";
		const string CMD_SUFFIX = "83";
		const string CMD_TOGGLE = "aa";
		const string CMD_SET_ANIMATION_MODE = "2c";
		const string CMD_SET_BRIGHTNESS = "2a"; // 00-FF
		const string CMD_SET_SPEED = "03"; // 00-FF
		const string CMD_SET_COLOR = "22"; // 000000-FFFFFF
		const string CMD_SET_DREAM_MODE = "2C"; // 1-180
		const string CMD_SET_DREAM_MODE_AUTO = "06";

		const string NO_PARAMETER = "000000";

		public Sp108e(string host, int port = 8189)
		{
			Host = host;
			Port = port;
		}

		public string Host { get; }
		public int Port { get; }

		public Task ToggleOnOff()
			=> Send(CMD_TOGGLE, NO_PARAMETER, 17);

		public async Task Off()
		{
			var status = await GetStatus();

			if (status.On)
				await ToggleOnOff();
		}

		public async Task On()
		{
			var status = await GetStatus();
			if (!status.On)
				await ToggleOnOff();
		}

		async Task<Sp108eStatus> GetStatus()
		{
			var r = await Send(CMD_GET_STATUS, NO_PARAMETER, 17);

			if (string.IsNullOrEmpty(r) || r.Length < 32)
				return null;

			return Sp108eStatus.FromResponse(r);
		}

		public Task SetBrightness(int brightness)
		{
			if (brightness < 0)
				brightness = 0;
			else if (brightness > 255)
				brightness = 255;

			return Send(CMD_SET_BRIGHTNESS, IntToHex(brightness));
		}

		public async Task SetColor(string hexColor)
		{
			var status = await GetStatus();

			if (status.AnimationMode == Sp108eAnimationMode.Null)
				await Send(CMD_SET_ANIMATION_MODE, Sp108eAnimationMode.Static.ToString());

			await Send(CMD_SET_COLOR, hexColor);
		}

		public Task SetAnimationMode(Sp108eAnimationMode mode)
			=> Send(CMD_SET_ANIMATION_MODE, mode.ToString());

		// 0-255
		public Task SetAnimationSpeed(int speed)
		{
			if (speed < 0)
				speed = 0;
			if (speed > 255)
				speed = 255;

			return Send(CMD_SET_SPEED, IntToHex(speed));
		}

		// 1-180
		public Task SetDreamMode(int mode)
		{
			if (mode < 1)
				mode = 1;
			if (mode > 180)
				mode = 180;

			return Send(CMD_SET_DREAM_MODE, IntToHex(mode - 1));
		}

		public Task SetDreamModeAuto()
			=> Send(CMD_SET_DREAM_MODE_AUTO);

		string IntToHex(int v)
			=> Convert.ToString(v, 16).PadLeft(2, '0');

		static byte[] HexStringToByteArray(string hex)
			=> Enumerable.Range(0, hex.Length)
								.Where(x => x % 2 == 0)
								.Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
								.ToArray();

		static string ByteArrayToHexString(byte[] data)
			=> BitConverter.ToString(data).Replace("-", string.Empty);

		async Task<string> Send(string cmd, string parameter = NO_PARAMETER, int responseLength = 0)
		{
			string response = null;

			var tcp = new TcpClient();
			await tcp.ConnectAsync(Host, Port);

			var hex = CMD_PREFIX + parameter.PadRight(6, '0') + cmd + CMD_SUFFIX;
			var rawHex = HexStringToByteArray(hex);

			tcp.Client.Send(rawHex);

			if (responseLength > 0)
			{
				var rxbuffer = new byte[responseLength];
				var rxd = tcp.Client.Receive(rxbuffer, responseLength, SocketFlags.None);

				if (rxd == responseLength)
					response = ByteArrayToHexString(rxbuffer);
			}

			try
			{
				tcp.Close();
				tcp.Dispose();
			}
			catch { }

			return response;
		}
	}
}