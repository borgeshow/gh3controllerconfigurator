using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using x360ce.Plugins;

namespace x360ce.Plugins.GH3
{
	#region Choices

	/// <summary>A key of the keyboard, by its Windows virtual-key code.</summary>
	/// <remarks>
	/// A list of its own rather than <see cref="Keys"/>: that enum gives one value several names (Enter and Return) and
	/// carries modifier flags, which a drop-down list cannot offer one by one.
	/// </remarks>
	public enum GH3Key
	{
		[Description("(nenhuma)")] None = 0,
		A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47, H = 0x48, I = 0x49, J = 0x4A, K = 0x4B,
		L = 0x4C, M = 0x4D, N = 0x4E, O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55, V = 0x56,
		W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,
		[Description("0")] D0 = 0x30, [Description("1")] D1 = 0x31, [Description("2")] D2 = 0x32, [Description("3")] D3 = 0x33,
		[Description("4")] D4 = 0x34, [Description("5")] D5 = 0x35, [Description("6")] D6 = 0x36, [Description("7")] D7 = 0x37,
		[Description("8")] D8 = 0x38, [Description("9")] D9 = 0x39,
		F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74, F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79,
		F11 = 0x7A, F12 = 0x7B,
		[Description("Espaço")] Space = 0x20,
		[Description("Enter")] Enter = 0x0D,
		[Description("Tab")] Tab = 0x09,
		[Description("Backspace")] Backspace = 0x08,
		[Description("Shift esquerdo")] LeftShift = 0xA0,
		[Description("Shift direito")] RightShift = 0xA1,
		[Description("Ctrl esquerdo")] LeftCtrl = 0xA2,
		[Description("Ctrl direito")] RightCtrl = 0xA3,
		[Description("Alt esquerdo")] LeftAlt = 0xA4,
		[Description("Alt direito")] RightAlt = 0xA5,
		[Description("Seta para cima")] Up = 0x26,
		[Description("Seta para baixo")] Down = 0x28,
		[Description("Seta para a esquerda")] Left = 0x25,
		[Description("Seta para a direita")] Right = 0x27,
		[Description("Insert")] Insert = 0x2D,
		[Description("Delete")] Delete = 0x2E,
		[Description("Home")] Home = 0x24,
		[Description("End")] End = 0x23,
		[Description("Page Up")] PageUp = 0x21,
		[Description("Page Down")] PageDown = 0x22,
		[Description("Numérico 0")] NumPad0 = 0x60, [Description("Numérico 1")] NumPad1 = 0x61,
		[Description("Numérico 2")] NumPad2 = 0x62, [Description("Numérico 3")] NumPad3 = 0x63,
		[Description("Numérico 4")] NumPad4 = 0x64, [Description("Numérico 5")] NumPad5 = 0x65,
		[Description("Numérico 6")] NumPad6 = 0x66, [Description("Numérico 7")] NumPad7 = 0x67,
		[Description("Numérico 8")] NumPad8 = 0x68, [Description("Numérico 9")] NumPad9 = 0x69,
		[Description("Numérico +")] NumPadAdd = 0x6B, [Description("Numérico -")] NumPadSubtract = 0x6D,
		[Description("Numérico *")] NumPadMultiply = 0x6A, [Description("Numérico /")] NumPadDivide = 0x6F,
		[Description("Numérico ,")] NumPadDecimal = 0x6E,
		[Description(";")] Semicolon = 0xBA, [Description("=")] Plus = 0xBB, [Description(",")] Comma = 0xBC,
		[Description("-")] Minus = 0xBD, [Description(".")] Period = 0xBE, [Description("/")] Slash = 0xBF,
		[Description("'")] Quote = 0xDE, [Description("[")] OpenBracket = 0xDB, [Description("]")] CloseBracket = 0xDD,
		[Description("\\")] Backslash = 0xDC,
	}

	/// <summary>Which way the generated strum goes.</summary>
	public enum StrumDirection
	{
		[Description("Sempre para baixo")] Down,
		[Description("Sempre para cima")] Up,
		[Description("Alternar baixo e cima")] Alternate,
	}

	/// <summary>How long the first note of a chord waits for the others before the strum.</summary>
	public enum ChordTolerance
	{
		[Description("Mais rápido")] Faster,
		[Description("Equilibrado")] Balanced,
		[Description("Mais tolerante a acordes")] Tolerant,
	}

	/// <summary>The strum the controller reads now.</summary>
	public enum StrumOutput
	{
		None = 0,
		Down = 1,
		Up = 2,
	}

	#endregion

	#region Auto strum

	/// <summary>Turns the frets held into frets and strums, so a note is played by pressing its key alone.</summary>
	/// <remarks>
	/// Pure logic, with no Windows call and no clock of its own, so it is tested by giving it times and keys. It allocates
	/// nothing after it is made, because it runs on x360ce's input thread, up to a thousand times a second.
	///
	/// A fret newly pressed opens a chord window. Frets pressed before the window closes join the same chord. When the
	/// window closes one strum pulse is sent, and the chord's frets are held until the pulse ends, even if a key was
	/// tapped and let go inside the window, so the game reads the frets and the strum together. A held key never strums
	/// again; a released key never strums. Only a fret newly pressed asks for a new strum.
	///
	/// In one direction, a strum asked for while a pulse is still out ends that pulse, waits the gap so the game can see
	/// the strum let go, and pulses again. Alternating goes straight to the other direction instead, with no gap.
	/// </remarks>
	public sealed class AutoStrum
	{
		/// <summary>The bit of each fret in a fret mask, in the order of the game: green, red, yellow, blue, orange.</summary>
		public const int Green = 1, Red = 2, Yellow = 4, Blue = 8, Orange = 16;

		/// <summary>Every fret.</summary>
		public const int AllFrets = 31;

		/// <summary>Whether strums are made. Off, the frets follow the keys and no strum is sent.</summary>
		public bool Enabled = true;

		/// <summary>Which way the strum goes.</summary>
		public StrumDirection Direction = StrumDirection.Down;

		/// <summary>How long, in milliseconds, the first fret of a chord waits for the others; 0 strums at once.</summary>
		public double ChordWindowMs = 10;

		/// <summary>How long, in milliseconds, a strum is held.</summary>
		public double PulseMs = 30;

		/// <summary>How long, in milliseconds, the strum rests between two pulses in the same direction.</summary>
		public double GapMs = 20;

		/// <summary>The frets the controller holds now.</summary>
		public int Frets { get; private set; }

		/// <summary>The strum the controller holds now.</summary>
		public StrumOutput Strum { get; private set; }

		/// <summary>How many strums were sent since it was made or reset.</summary>
		public int StrumCount { get; private set; }

		int _previous;
		// Frets pressed since the last strum, waiting for it.
		int _chord;
		// Frets of the strum being held, kept until it ends.
		int _latched;
		bool _windowOpen;
		double _windowEnd;
		bool _pending;
		bool _pulsing;
		double _pulseEnd;
		double _gapEnd = double.MinValue;
		StrumOutput _last = StrumOutput.None;

		/// <summary>Takes the frets pressed now and works out what the controller holds.</summary>
		/// <param name="nowMs">The time now, in milliseconds, from any start; it must not go back.</param>
		/// <param name="pressed">The frets whose keys are down now, as a mask of <see cref="Green"/> to <see cref="Orange"/>.</param>
		public void Update(double nowMs, int pressed)
		{
			pressed &= AllFrets;
			var newPresses = pressed & ~_previous;
			_previous = pressed;
			if (!Enabled)
			{
				ClearStrum();
				Frets = pressed;
				return;
			}
			if (newPresses != 0)
			{
				_chord |= newPresses;
				if (!_windowOpen)
				{
					_windowOpen = true;
					_windowEnd = nowMs + Math.Max(0, ChordWindowMs);
				}
			}
			if (_windowOpen && nowMs >= _windowEnd)
			{
				_windowOpen = false;
				_pending = true;
			}
			if (_pulsing && nowMs >= _pulseEnd)
				EndPulse(nowMs);
			if (_pending)
			{
				if (_pulsing)
				{
					if (Direction == StrumDirection.Alternate)
						StartPulse(nowMs);
					else
						EndPulse(nowMs);
				}
				// Alternating never needs the gap: the next pulse goes the other way.
				if (!_pulsing && (Direction == StrumDirection.Alternate || nowMs >= _gapEnd))
					StartPulse(nowMs);
			}
			Frets = pressed | _chord | _latched;
		}

		void StartPulse(double nowMs)
		{
			StrumOutput direction;
			if (Direction == StrumDirection.Up)
				direction = StrumOutput.Up;
			else if (Direction == StrumDirection.Down)
				direction = StrumOutput.Down;
			else
				direction = _last == StrumOutput.Down ? StrumOutput.Up : StrumOutput.Down;
			_last = direction;
			Strum = direction;
			_pulsing = true;
			_pulseEnd = nowMs + Math.Max(1, PulseMs);
			_pending = false;
			_latched = _chord;
			_chord = 0;
			StrumCount++;
		}

		void EndPulse(double nowMs)
		{
			_pulsing = false;
			Strum = StrumOutput.None;
			_latched = 0;
			_gapEnd = nowMs + Math.Max(0, GapMs);
		}

		void ClearStrum()
		{
			_chord = 0;
			_latched = 0;
			_windowOpen = false;
			_pending = false;
			_pulsing = false;
			Strum = StrumOutput.None;
		}

		/// <summary>Lets go of everything, as if no key had ever been pressed.</summary>
		public void Reset()
		{
			ClearStrum();
			_previous = 0;
			_gapEnd = double.MinValue;
			_last = StrumOutput.None;
			Frets = 0;
			StrumCount = 0;
		}

		/// <summary>The chord window, in milliseconds, of a tolerance.</summary>
		/// <remarks>Starting values, to be tuned in the game.</remarks>
		public static double WindowOf(ChordTolerance tolerance)
		{
			switch (tolerance)
			{
				case ChordTolerance.Faster:
					return 5;
				case ChordTolerance.Tolerant:
					return 20;
				default:
					return 10;
			}
		}
	}

	#endregion

	#region The device

	/// <summary>A Guitar Hero III guitar played with five keys, which strums by itself.</summary>
	/// <remarks>
	/// It reads the keys with GetAsyncKeyState on each read: the state of the key, not its messages, so the keyboard's own
	/// repeat changes nothing, no hook is installed and no key is taken from another program. It reads them only while
	/// Guitar Hero III is the window in front, or x360ce itself, unless told otherwise.
	///
	/// Its buttons are in the order x360ce's own mapping gives a new gamepad, so added to a controller it is already
	/// mapped as a Guitar Hero III guitar: button 0 green (A), 1 red (B), 2 blue (X), 3 yellow (Y), 4 orange (left
	/// bumper), 6 Star Power (Back), 7 Start; and the hat is the strum (D-pad up and down).
	/// </remarks>
	[Description("Guitarra do Guitar Hero III tocada com cinco teclas, com palhetada automática: basta pressionar a tecla da nota. "
		+ "Ao ser adicionada a um controle, o mapeamento automático do x360ce já a deixa no padrão do jogo: "
		+ "verde = A, vermelho = B, amarelo = Y, azul = X, laranja = LB, palhetada = D-pad para cima e para baixo, "
		+ "Star Power = Back e Start = Start. As teclas são lidas somente enquanto o Guitar Hero III (ou o próprio x360ce) "
		+ "estiver em primeiro plano; nenhuma tecla é bloqueada para outros programas.")]
	public class GH3Keyboard : IPluginDevice
	{
		/// <summary>Names this kind of device in every version: saved mappings find it by this.</summary>
		public static readonly Guid Product = new Guid("6c1f3a52-8f0e-4b8d-9a35-2f7d4c1e9b60");

		/// <summary>Where each fret's button is, in the order of <see cref="AutoStrum.Green"/> to <see cref="AutoStrum.Orange"/>.</summary>
		/// <remarks>Blue before yellow, because x360ce's own mapping makes button 2 X and button 3 Y.</remarks>
		public static readonly int[] FretButtons = { 0, 1, 3, 2, 4 };

		/// <summary>The Star Power button, which x360ce's own mapping makes Back.</summary>
		public const int StarPowerButton = 6;

		/// <summary>The Start button, which x360ce's own mapping makes Start.</summary>
		public const int StartButton = 7;

		/// <summary>The hat's value for a strum up and a strum down; x360ce's own mapping makes the hat the D-pad.</summary>
		public const int HatUp = 0, HatDown = 18000;

		public string Name { get { return "GH3 Keyboard"; } }
		public Guid ProductGuid { get { return Product; } }
		public int AxisCount { get { return 0; } }
		public int SliderCount { get { return 0; } }
		public int PovCount { get { return 1; } }
		public int ButtonCount { get { return 8; } }

		/// <summary>Made by x360ce: reads the real keyboard and the window in front.</summary>
		public GH3Keyboard() : this(null, null, null)
		{
		}

		/// <summary>Made by a test, which says which keys are down, what time it is and whether the game is in front.</summary>
		/// <param name="isKeyDown">True while the key with this virtual-key code is down; null reads the keyboard.</param>
		/// <param name="clockMs">The time now in milliseconds; null reads a stopwatch.</param>
		/// <param name="isGameInFront">True while the keys should be read; null asks Windows which window is in front.</param>
		public GH3Keyboard(Func<int, bool> isKeyDown, Func<double> clockMs, Func<bool> isGameInFront)
		{
			_isKeyDown = isKeyDown ?? new Func<int, bool>(IsKeyDown);
			_clockMs = clockMs ?? new Func<double>(StopwatchMs);
			_isGameInFront = isGameInFront ?? new Func<bool>(IsGameInFront);
		}

		readonly Func<int, bool> _isKeyDown;
		readonly Func<double> _clockMs;
		readonly Func<bool> _isGameInFront;
		readonly AutoStrum _strum = new AutoStrum();
		readonly Stopwatch _watch = Stopwatch.StartNew();

		#region Settings

		[Category("1. Notas"), DisplayName("Verde"), Description("A tecla da nota verde.")]
		public GH3Key GreenKey { get; set; } = GH3Key.A;

		[Category("1. Notas"), DisplayName("Vermelha"), Description("A tecla da nota vermelha.")]
		public GH3Key RedKey { get; set; } = GH3Key.S;

		[Category("1. Notas"), DisplayName("Amarela"), Description("A tecla da nota amarela.")]
		public GH3Key YellowKey { get; set; } = GH3Key.D;

		[Category("1. Notas"), DisplayName("Azul"), Description("A tecla da nota azul.")]
		public GH3Key BlueKey { get; set; } = GH3Key.F;

		[Category("1. Notas"), DisplayName("Laranja"), Description("A tecla da nota laranja.")]
		public GH3Key OrangeKey { get; set; } = GH3Key.G;

		[Category("2. Palhetada"), DisplayName("Palhetada automática")]
		[Description("Palheta sozinho sempre que uma nota ou um acorde novo é pressionado. Desligada, as notas só são seguradas, e a palhetada vem das teclas de palhetada.")]
		public bool AutoStrumEnabled { get; set; } = true;

		[Category("2. Palhetada"), DisplayName("Direção")]
		[Description("Sempre para baixo, sempre para cima, ou alternando. Alternar permite notas mais rápidas, mas precisa ser confirmado no jogo.")]
		public StrumDirection Direction { get; set; } = StrumDirection.Down;

		[Category("2. Palhetada"), DisplayName("Acordes")]
		[Description("Quanto a palhetada espera as outras notas de um acorde: mais rápido 5 ms, equilibrado 10 ms, mais tolerante 20 ms.")]
		public ChordTolerance Chords { get; set; } = ChordTolerance.Balanced;

		[Category("2. Palhetada"), DisplayName("Duração da palhetada (ms)")]
		[Description("Por quanto tempo a palhetada fica pressionada. O jogo precisa vê-la em pelo menos um quadro: a 60 quadros por segundo, um quadro dura 17 ms.")]
		[Range(5, 100)]
		public int PulseMs { get; set; } = 30;

		[Category("2. Palhetada"), DisplayName("Intervalo entre palhetadas (ms)")]
		[Description("Por quanto tempo a palhetada fica solta entre duas palhetadas na mesma direção, para o jogo ver que ela foi solta.")]
		[Range(0, 100)]
		public int GapMs { get; set; } = 20;

		[Category("3. Outras teclas"), DisplayName("Star Power"), Description("A tecla do Star Power (Back).")]
		public GH3Key StarPowerKey { get; set; } = GH3Key.None;

		[Category("3. Outras teclas"), DisplayName("Start"), Description("A tecla do Start, que pausa o jogo.")]
		public GH3Key StartKey { get; set; } = GH3Key.None;

		[Category("3. Outras teclas"), DisplayName("Palhetada para cima"), Description("Uma palhetada para cima feita à mão; também move os menus para cima.")]
		public GH3Key StrumUpKey { get; set; } = GH3Key.None;

		[Category("3. Outras teclas"), DisplayName("Palhetada para baixo"), Description("Uma palhetada para baixo feita à mão; também move os menus para baixo.")]
		public GH3Key StrumDownKey { get; set; } = GH3Key.None;

		[Category("4. Jogo"), DisplayName("Somente com o jogo em primeiro plano")]
		[Description("Lê as teclas somente enquanto o jogo estiver em primeiro plano. Desligado, as teclas viram notas em qualquer programa.")]
		public bool OnlyWhenGameInFront { get; set; } = true;

		[Category("4. Jogo"), DisplayName("Programa do jogo")]
		[Description("O nome do arquivo do jogo, como aparece no Gerenciador de Tarefas.")]
		public string GameExecutable { get; set; } = "GH3.exe";

		[Category("4. Jogo"), DisplayName("Também com o x360ce em primeiro plano")]
		[Description("Lê as teclas também enquanto o x360ce estiver em primeiro plano, para testar o mapeamento sem abrir o jogo.")]
		public bool AlsoWhenX360ceInFront { get; set; } = true;

		[DisplayName("Ativo"), Description("Se as teclas estão sendo lidas agora.")]
		public bool Active { get { return _active; } }

		[DisplayName("Palhetadas"), Description("Quantas palhetadas foram geradas desde que o dispositivo foi adicionado.")]
		public int Strums { get { return _strums; } }

		[DisplayName("Leituras por segundo"), Description("Quantas vezes o x360ce leu o dispositivo no último segundo.")]
		public int ReadsPerSecond { get { return _readsPerSecond; } }

		[DisplayName("Aviso"), Description("Um problema na configuração das teclas, ou vazio.")]
		public string Warning { get { return DuplicateWarning(); } }

		#endregion

		#region What the panel reads

		volatile bool _active;
		volatile int _strums;
		volatile int _readsPerSecond;
		volatile int _frets;
		volatile int _strumShown;
		int _reads;
		long _readsSince;

		/// <summary>The frets the controller holds now, as a mask of <see cref="AutoStrum.Green"/> to <see cref="AutoStrum.Orange"/>.</summary>
		[Browsable(false)]
		public int Frets { get { return _frets; } }

		/// <summary>The strum the controller holds now.</summary>
		[Browsable(false)]
		public StrumOutput StrumNow { get { return (StrumOutput)_strumShown; } }

		/// <summary>The key of each fret, green to orange.</summary>
		[Browsable(false)]
		public GH3Key[] FretKeys { get { return new[] { GreenKey, RedKey, YellowKey, BlueKey, OrangeKey }; } }

		#endregion

		public void Read(PluginState state)
		{
			CountRead();
			var now = _clockMs();
			_strum.Enabled = AutoStrumEnabled;
			_strum.Direction = Direction;
			_strum.ChordWindowMs = AutoStrum.WindowOf(Chords);
			_strum.PulseMs = PulseMs;
			_strum.GapMs = GapMs;
			var active = !OnlyWhenGameInFront || _isGameInFront();
			if (active != _active)
			{
				_active = active;
				// Nothing is left held when the game goes to the back, and nothing pressed before counts as new after.
				if (!active)
					_strum.Reset();
			}
			var pressed = 0;
			var starPower = false;
			var start = false;
			var manualUp = false;
			var manualDown = false;
			if (active)
			{
				if (Down(GreenKey)) pressed |= AutoStrum.Green;
				if (Down(RedKey)) pressed |= AutoStrum.Red;
				if (Down(YellowKey)) pressed |= AutoStrum.Yellow;
				if (Down(BlueKey)) pressed |= AutoStrum.Blue;
				if (Down(OrangeKey)) pressed |= AutoStrum.Orange;
				starPower = Down(StarPowerKey);
				start = Down(StartKey);
				manualUp = Down(StrumUpKey);
				manualDown = Down(StrumDownKey);
				_strum.Update(now, pressed);
			}
			var frets = active ? _strum.Frets : 0;
			for (var i = 0; i < FretButtons.Length; i++)
				state.Buttons[FretButtons[i]] = (frets & (1 << i)) != 0;
			state.Buttons[5] = false;
			state.Buttons[StarPowerButton] = starPower;
			state.Buttons[StartButton] = start;
			var strum = active ? _strum.Strum : StrumOutput.None;
			// A strum made by hand goes out as it is; the automatic one only when no key asks for a direction.
			if (manualUp && !manualDown)
				strum = StrumOutput.Up;
			else if (manualDown && !manualUp)
				strum = StrumOutput.Down;
			state.Povs[0] = strum == StrumOutput.Up ? HatUp : strum == StrumOutput.Down ? HatDown : PluginState.PovReleased;
			_frets = frets;
			_strumShown = (int)strum;
			_strums = _strum.StrumCount;
		}

		bool Down(GH3Key key)
		{
			return key != GH3Key.None && _isKeyDown((int)key);
		}

		void CountRead()
		{
			var elapsed = _watch.ElapsedMilliseconds;
			_reads++;
			if (elapsed - _readsSince >= 1000)
			{
				_readsPerSecond = _reads;
				_reads = 0;
				_readsSince = elapsed;
			}
		}

		/// <summary>Names a key used for two things, or returns empty.</summary>
		string DuplicateWarning()
		{
			var keys = new[] { GreenKey, RedKey, YellowKey, BlueKey, OrangeKey, StarPowerKey, StartKey, StrumUpKey, StrumDownKey };
			for (var i = 0; i < keys.Length; i++)
			{
				if (keys[i] == GH3Key.None)
					continue;
				for (var j = i + 1; j < keys.Length; j++)
					if (keys[j] == keys[i])
						return "A tecla " + KeyName(keys[i]) + " está em duas funções.";
			}
			return "";
		}

		/// <summary>A key's name as a person reads it.</summary>
		public static string KeyName(GH3Key key)
		{
			var field = typeof(GH3Key).GetField(key.ToString());
			var description = field == null ? null : (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
			return description == null ? key.ToString() : description.Description;
		}

		#region Windows

		[DllImport("user32.dll")]
		static extern short GetAsyncKeyState(int vKey);

		[DllImport("user32.dll")]
		static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

		[DllImport("kernel32.dll", SetLastError = true)]
		static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

		[DllImport("kernel32.dll")]
		static extern bool CloseHandle(IntPtr handle);

		const uint ProcessQueryLimitedInformation = 0x1000;

		static bool IsKeyDown(int virtualKey)
		{
			// The high bit is the key as it is now. The low bit, pressed since the last call, is shared by every program
			// that asks, so it is not read.
			return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
		}

		double StopwatchMs()
		{
			return _watch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
		}

		static readonly uint OwnProcessId = (uint)Process.GetCurrentProcess().Id;
		readonly StringBuilder _imageName = new StringBuilder(1024);
		IntPtr _lastWindow;
		bool _lastIsGame;
		bool _lastIsOwn;
		string _lastExecutable;

		/// <summary>True while the game, or x360ce if allowed, is the window in front.</summary>
		/// <remarks>
		/// The window in front is asked on each read, which is cheap. Its program is looked up only when the window or the
		/// program's name to look for changes, so a read makes nothing while the same window stays in front.
		/// </remarks>
		bool IsGameInFront()
		{
			var window = GetForegroundWindow();
			var executable = GameExecutable;
			if (window != _lastWindow || !ReferenceEquals(executable, _lastExecutable))
			{
				_lastWindow = window;
				_lastExecutable = executable;
				uint processId = 0;
				if (window != IntPtr.Zero)
					GetWindowThreadProcessId(window, out processId);
				_lastIsOwn = processId == OwnProcessId;
				_lastIsGame = !_lastIsOwn && processId != 0 && ProcessIs(processId, executable);
			}
			return _lastIsGame || (_lastIsOwn && AlsoWhenX360ceInFront);
		}

		bool ProcessIs(uint processId, string executable)
		{
			if (string.IsNullOrWhiteSpace(executable))
				return false;
			var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
			if (handle == IntPtr.Zero)
				return false;
			try
			{
				_imageName.Length = 0;
				var size = _imageName.Capacity;
				if (!QueryFullProcessImageName(handle, 0, _imageName, ref size))
					return false;
				var path = _imageName.ToString();
				var name = path.Substring(path.LastIndexOf('\\') + 1);
				return string.Equals(name, executable.Trim(), StringComparison.OrdinalIgnoreCase);
			}
			finally
			{
				CloseHandle(handle);
			}
		}

		#endregion
	}

	#endregion

	#region The panel

	/// <summary>The five notes, lit while the controller holds them, with the key of each and the strum.</summary>
	/// <remarks>
	/// x360ce finds it by its constructor, which takes the device, and places it on the device's tab. It runs on the
	/// interface thread and reads only the device's volatile fields.
	/// </remarks>
	public class GH3KeyboardPanel : UserControl
	{
		static readonly Color[] NoteColors =
		{
			Color.FromArgb(0x22, 0xC5, 0x5E),
			Color.FromArgb(0xEF, 0x44, 0x44),
			Color.FromArgb(0xFA, 0xCC, 0x15),
			Color.FromArgb(0x3B, 0x82, 0xF6),
			Color.FromArgb(0xF9, 0x73, 0x16),
		};

		static readonly string[] NoteNames = { "Verde", "Vermelha", "Amarela", "Azul", "Laranja" };

		public GH3KeyboardPanel(GH3Keyboard device)
		{
			_device = device;
			Name = "GH3KeyboardPanel";
			Text = "Notas";
			AccessibleName = "Notas";
			AccessibleDescription = "As cinco notas, acesas enquanto o controle as segura, com a tecla de cada uma e a palhetada.";
			DoubleBuffered = true;
			SetMinimumSize();
			_timer = new Timer { Interval = 15 };
			_timer.Tick += (s, e) => Invalidate();
			_timer.Start();
		}

		readonly GH3Keyboard _device;
		readonly Timer _timer;

		protected override void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			SetMinimumSize();
		}

		void SetMinimumSize()
		{
			MinimumSize = new Size(Font.Height * 30, Font.Height * 7);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			var frets = _device.Frets;
			var keys = _device.FretKeys;
			var gap = Font.Height / 2;
			var arrowWidth = Font.Height * 3;
			var width = Math.Max(1, (ClientSize.Width - arrowWidth - gap * 6) / 5);
			var height = Math.Max(1, ClientSize.Height - gap * 2);
			for (var i = 0; i < 5; i++)
			{
				var rect = new Rectangle(gap + i * (width + gap), gap, width, height);
				var lit = (frets & (1 << i)) != 0;
				var color = NoteColors[i];
				using (var brush = new SolidBrush(lit ? color : Color.FromArgb(48, color)))
				using (var pen = new Pen(color, 2))
				{
					g.FillRectangle(brush, rect);
					g.DrawRectangle(pen, rect);
				}
				var text = NoteNames[i] + "\n" + GH3Keyboard.KeyName(keys[i]);
				TextRenderer.DrawText(g, text, Font, rect, lit ? Color.Black : ForeColor,
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
			}
			var arrow = new Rectangle(ClientSize.Width - arrowWidth - gap, gap, arrowWidth, height);
			var strum = _device.StrumNow;
			var arrowText = strum == StrumOutput.Up ? "▲" : strum == StrumOutput.Down ? "▼" : "·";
			using (var big = new Font(Font.FontFamily, Font.Size * 2.5f, FontStyle.Bold))
				TextRenderer.DrawText(g, arrowText, big, arrow, strum == StrumOutput.None ? SystemColors.GrayText : ForeColor,
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			if (!_device.Active)
				TextRenderer.DrawText(g, "Inativo: o jogo não está em primeiro plano", Font, new Rectangle(gap, ClientSize.Height - Font.Height - 2, ClientSize.Width, Font.Height + 2), SystemColors.GrayText);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_timer.Dispose();
			base.Dispose(disposing);
		}
	}

	#endregion
}
