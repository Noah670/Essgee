﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Essgee.Emulation.Configuration;
using Essgee.Emulation.CPU;
using Essgee.Emulation.Video;
using Essgee.Emulation.Audio;
using Essgee.Emulation.Cartridges;
using Essgee.Emulation.Cartridges.Sega;
using Essgee.Emulation.Peripherals;
using Essgee.EventArguments;
using Essgee.Exceptions;
using Essgee.Utilities;

namespace Essgee.Emulation.Machines
{
	/* TODO: verify everything, the SC-3000 isn't that well-documented...
	 * 
	 * Tape cassette notes: PPI port B bit 7 == input from cassette, port C bit 4 == output to cassette
	 */

	[MachineIndex(1)]
	public class SC3000 : IMachine
	{
		const double masterClockNtsc = 10738635;
		const double masterClockPal = 10640684;
		const double refreshRateNtsc = 59.922743;
		const double refreshRatePal = 49.701459;

		const int ramSize = 1 * 2048;

		double masterClock;
		double vdpClock, psgClock;

		public event EventHandler<SendLogMessageEventArgs> SendLogMessage;
		protected virtual void OnSendLogMessage(SendLogMessageEventArgs e) { SendLogMessage?.Invoke(this, e); }

		public event EventHandler<EventArgs> EmulationReset;
		protected virtual void OnEmulationReset(EventArgs e) { EmulationReset?.Invoke(this, e); }

		public event EventHandler<RenderScreenEventArgs> RenderScreen
		{
			add { vdp.RenderScreen += value; }
			remove { vdp.RenderScreen -= value; }
		}

		public event EventHandler<SizeScreenEventArgs> SizeScreen
		{
			add { vdp.SizeScreen += value; }
			remove { vdp.SizeScreen -= value; }
		}

		public event EventHandler<ChangeViewportEventArgs> ChangeViewport;
		protected virtual void OnChangeViewport(ChangeViewportEventArgs e) { ChangeViewport?.Invoke(this, e); }

		public event EventHandler<PollInputEventArgs> PollInput;
		protected virtual void OnPollInput(PollInputEventArgs e) { PollInput?.Invoke(this, e); }

		public event EventHandler<EnqueueSamplesEventArgs> EnqueueSamples
		{
			add { psg.EnqueueSamples += value; }
			remove { psg.EnqueueSamples -= value; }
		}

		public event EventHandler<SaveExtraDataEventArgs> SaveExtraData;
		protected virtual void OnSaveExtraData(SaveExtraDataEventArgs e) { SaveExtraData?.Invoke(this, e); }

		public event EventHandler<EventArgs> EnableRumble { add { } remove { } }

		public string ManufacturerName => "Sega";
		public string ModelName => "SC-3000";
		public string DatFilename => "Sega - SG-1000.dat";      // TODO: SC-3000 .dat does not exist?
		public (string Extension, string Description) FileFilter => (".sc", "SC-3000 ROMs");
		public bool HasBootstrap => false;
		public double RefreshRate { get; private set; }
		public double PixelAspectRatio => 8.0 / 7.0;
		public (string Name, string Description)[] RuntimeOptions => vdp.RuntimeOptions.Concat(psg.RuntimeOptions).ToArray();

		ICartridge cartridge;
		byte[] wram;
		Z80A cpu;
		TMS99xxA vdp;
		SN76489 psg;
		Intel8255 ppi;
		bool[,] keyboard;

		[Flags]
		enum PortAInputs : byte
		{
			P1Up = (1 << 0),
			P1Down = (1 << 1),
			P1Left = (1 << 2),
			P1Right = (1 << 3),
			P1Button1 = (1 << 4),
			P1Button2 = (1 << 5),
			P2Up = (1 << 6),
			P2Down = (1 << 7),
		}

		[Flags]
		enum PortBInputs : byte
		{
			P2Left = (1 << 0),
			P2Right = (1 << 1),
			P2Button1 = (1 << 2),
			P2Button2 = (1 << 3),
		}

		PortAInputs portAInputsPressed;
		PortBInputs portBInputsPressed;

		bool resetButtonPressed, resetButtonToggle;

		bool keyboardMode;
		bool changeInputButtonPressed;

		enum TapeUpdateModes
		{
			Reading,
			Writing
		}

		bool isTapePlaying;
		bool tapePlayButtonPressed;

		int currentMasterClockCyclesInFrame, totalMasterClockCyclesInFrame;

		Configuration.SC3000 configuration;

		public SC3000() { }

		public void Initialize()
		{
			cartridge = null;
			wram = new byte[ramSize];
			cpu = new Z80A(ReadMemory, WriteMemory, ReadPort, WritePort);
			vdp = new TMS99xxA();
			psg = new SN76489();
			ppi = new Intel8255();
			keyboard = new bool[12, 8];

			vdp.EndOfScanline += (s, e) =>
			{
				PollInputEventArgs pollInputEventArgs = new PollInputEventArgs();
				OnPollInput(pollInputEventArgs);
				ParseInput(pollInputEventArgs);
			};
		}

		public void SetConfiguration(IConfiguration config)
		{
			configuration = (Configuration.SC3000)config;

			ReconfigureSystem();
		}

		public object GetRuntimeOption(string name)
		{
			if (name.StartsWith("Graphics"))
				return vdp.GetRuntimeOption(name);
			else if (name.StartsWith("Audio"))
				return psg.GetRuntimeOption(name);
			else
				return null;
		}

		public void SetRuntimeOption(string name, object value)
		{
			if (name.StartsWith("Graphics"))
				vdp.SetRuntimeOption(name, value);
			else if (name.StartsWith("Audio"))
				psg.SetRuntimeOption(name, value);
		}

		private void ReconfigureSystem()
		{
			if (configuration.TVStandard == TVStandard.NTSC)
			{
				masterClock = masterClockNtsc;
				RefreshRate = refreshRateNtsc;
			}
			else
			{
				masterClock = masterClockPal;
				RefreshRate = refreshRatePal;
			}

			vdpClock = (masterClock / 1.0);
			psgClock = (masterClock / 3.0);

			vdp?.SetClockRate(vdpClock);
			vdp?.SetRefreshRate(RefreshRate);
			vdp?.SetRevision(0);

			psg?.SetSampleRate(Program.Configuration.SampleRate);
			psg?.SetOutputChannels(2);
			psg?.SetClockRate(psgClock);
			psg?.SetRefreshRate(RefreshRate);

			currentMasterClockCyclesInFrame = 0;
			totalMasterClockCyclesInFrame = (int)Math.Round(masterClock / RefreshRate);

			OnChangeViewport(new ChangeViewportEventArgs(vdp.Viewport));
		}

		public void Startup()
		{
			cpu.Startup();
			vdp.Startup();
			psg.Startup();
		}

		public void Reset()
		{
			cpu.Reset();
			cpu.SetStackPointer(0xDFF0);
			vdp.Reset();
			psg.Reset();
			ppi.Reset();

			for (int i = 0; i < keyboard.GetLength(0); i++)
				for (int j = 0; j < keyboard.GetLength(1); j++)
					keyboard[i, j] = false;

			portAInputsPressed = 0;
			portBInputsPressed = 0;

			resetButtonPressed = resetButtonToggle = false;

			keyboardMode = true;
			changeInputButtonPressed = false;

			isTapePlaying = false;
			tapePlayButtonPressed = false;

			OnEmulationReset(EventArgs.Empty);
		}

		public void Shutdown()
		{
			cpu?.Shutdown();
			vdp?.Shutdown();
			psg?.Shutdown();
		}

		public void SetState(Dictionary<string, dynamic> state)
		{
			configuration.TVStandard = state[nameof(configuration.TVStandard)];

			SaveStateHandler.PerformSetState(cartridge, state[nameof(cartridge)]);
			wram = state[nameof(wram)];
			SaveStateHandler.PerformSetState(cpu, state[nameof(cpu)]);
			SaveStateHandler.PerformSetState(vdp, state[nameof(vdp)]);
			SaveStateHandler.PerformSetState(psg, state[nameof(psg)]);
			SaveStateHandler.PerformSetState(ppi, state[nameof(ppi)]);
			keyboard = state[nameof(keyboard)];

			ReconfigureSystem();
		}

		public Dictionary<string, dynamic> GetState()
		{
			return new Dictionary<string, dynamic>
			{
				[nameof(configuration.TVStandard)] = configuration.TVStandard,

				[nameof(cartridge)] = SaveStateHandler.PerformGetState(cartridge),
				[nameof(wram)] = wram,
				[nameof(cpu)] = SaveStateHandler.PerformGetState(cpu),
				[nameof(vdp)] = SaveStateHandler.PerformGetState(vdp),
				[nameof(psg)] = SaveStateHandler.PerformGetState(psg),
				[nameof(ppi)] = SaveStateHandler.PerformGetState(ppi),
				[nameof(keyboard)] = keyboard
			};
		}

		public Dictionary<string, dynamic> GetDebugInformation()
		{
			var dict = new Dictionary<string, dynamic>
			{
				{ "CyclesInFrame", currentMasterClockCyclesInFrame },
			};

			return dict;
		}

		public void Load(byte[] romData, byte[] ramData, Type mapperType)
		{
			if (mapperType == null)
				mapperType = typeof(SegaSGCartridge);

			cartridge = (ICartridge)Activator.CreateInstance(mapperType, new object[] { romData.Length, ramData.Length });
			cartridge.LoadRom(romData);
			cartridge.LoadRam(ramData);
		}

		public byte[] GetCartridgeRam()
		{
			return cartridge.GetRamData();
		}

		public bool IsCartridgeRamSaveNeeded()
		{
			return cartridge.IsRamSaveNeeded();
		}

		public virtual void RunFrame()
		{
			while (currentMasterClockCyclesInFrame < totalMasterClockCyclesInFrame)
				RunStep();

			currentMasterClockCyclesInFrame -= totalMasterClockCyclesInFrame;
		}

		public void RunStep()
		{
			double currentCpuClockCycles = 0.0;
			currentCpuClockCycles += cpu.Step();

			double currentMasterClockCycles = (currentCpuClockCycles * 3.0);

			vdp.Step((int)Math.Round(currentMasterClockCycles));

			if (resetButtonPressed)
			{
				resetButtonPressed = false;
				cpu.SetInterruptLine(InterruptType.NonMaskable, InterruptState.Assert);
			}

			cpu.SetInterruptLine(InterruptType.Maskable, vdp.InterruptLine);

			psg.Step((int)Math.Round(currentCpuClockCycles));

			cartridge?.Step((int)Math.Round(currentCpuClockCycles));

			currentMasterClockCyclesInFrame += (int)Math.Round(currentMasterClockCycles);
		}

		private void ParseInput(PollInputEventArgs eventArgs)
		{
			/* Get variables */
			var keysDown = eventArgs.Keyboard;

			/* Handle reset button */
			var resetPressed = keysDown.Contains(configuration.InputReset);
			var resetButtonHeld = (resetButtonToggle && resetPressed);
			if (resetPressed)
			{
				if (!resetButtonHeld) resetButtonPressed = true;
				resetButtonToggle = true;
			}
			else if (resetButtonToggle)
				resetButtonToggle = false;

			/* Toggle input mode (keyboard or controllers) */
			if (keysDown.Contains(configuration.InputChangeMode) && !changeInputButtonPressed)
			{
				keyboardMode = !keyboardMode;
				var modeString = (keyboardMode ? "keyboard" : "controller");
				SendLogMessage(this, new SendLogMessageEventArgs($"Selected {modeString} mode."));
			}
			changeInputButtonPressed = keysDown.Contains(configuration.InputChangeMode);

			/* Toggle tape playback */
			if (keysDown.Contains(configuration.InputPlayTape) && !tapePlayButtonPressed)
			{
				isTapePlaying = !isTapePlaying;
				var playString = (isTapePlaying ? "playing" : "stopped");
				SendLogMessage(this, new SendLogMessageEventArgs($"Tape is {playString}."));
			}
			tapePlayButtonPressed = keysDown.Contains(configuration.InputPlayTape);

			if (keyboardMode)
			{
				/* Handle keyboard */

				// TODO: Replace hardcoded English layout w/ user-configurable settings
				keyboard[0, 0] = keysDown.Contains(System.Windows.Forms.Keys.D1);
				keyboard[0, 1] = keysDown.Contains(System.Windows.Forms.Keys.D2);
				keyboard[0, 2] = keysDown.Contains(System.Windows.Forms.Keys.D3);
				keyboard[0, 3] = keysDown.Contains(System.Windows.Forms.Keys.D4);
				keyboard[0, 4] = keysDown.Contains(System.Windows.Forms.Keys.D5);
				keyboard[0, 5] = keysDown.Contains(System.Windows.Forms.Keys.D6);
				keyboard[0, 6] = keysDown.Contains(System.Windows.Forms.Keys.D7);

				keyboard[1, 0] = keysDown.Contains(System.Windows.Forms.Keys.Q);
				keyboard[1, 1] = keysDown.Contains(System.Windows.Forms.Keys.W);
				keyboard[1, 2] = keysDown.Contains(System.Windows.Forms.Keys.E);
				keyboard[1, 3] = keysDown.Contains(System.Windows.Forms.Keys.R);
				keyboard[1, 4] = keysDown.Contains(System.Windows.Forms.Keys.T);
				keyboard[1, 5] = keysDown.Contains(System.Windows.Forms.Keys.Y);
				keyboard[1, 6] = keysDown.Contains(System.Windows.Forms.Keys.U);

				keyboard[2, 0] = keysDown.Contains(System.Windows.Forms.Keys.A);
				keyboard[2, 1] = keysDown.Contains(System.Windows.Forms.Keys.S);
				keyboard[2, 2] = keysDown.Contains(System.Windows.Forms.Keys.D);
				keyboard[2, 3] = keysDown.Contains(System.Windows.Forms.Keys.F);
				keyboard[2, 4] = keysDown.Contains(System.Windows.Forms.Keys.G);
				keyboard[2, 5] = keysDown.Contains(System.Windows.Forms.Keys.H);
				keyboard[2, 6] = keysDown.Contains(System.Windows.Forms.Keys.J);

				keyboard[3, 0] = keysDown.Contains(System.Windows.Forms.Keys.Z);
				keyboard[3, 1] = keysDown.Contains(System.Windows.Forms.Keys.X);
				keyboard[3, 2] = keysDown.Contains(System.Windows.Forms.Keys.C);
				keyboard[3, 3] = keysDown.Contains(System.Windows.Forms.Keys.V);
				keyboard[3, 4] = keysDown.Contains(System.Windows.Forms.Keys.B);
				keyboard[3, 5] = keysDown.Contains(System.Windows.Forms.Keys.N);
				keyboard[3, 6] = keysDown.Contains(System.Windows.Forms.Keys.M);

				keyboard[4, 0] = keysDown.Contains(System.Windows.Forms.Keys.None);             // Alphanumerics, Eng Dier's
				keyboard[4, 1] = keysDown.Contains(System.Windows.Forms.Keys.Space);
				keyboard[4, 2] = keysDown.Contains(System.Windows.Forms.Keys.Home);             // Clr, Home
				keyboard[4, 3] = keysDown.Contains(System.Windows.Forms.Keys.Back);             // Del, Ins
				keyboard[4, 4] = keysDown.Contains(System.Windows.Forms.Keys.None);             // Not on English keyboard?
				keyboard[4, 5] = keysDown.Contains(System.Windows.Forms.Keys.None);             // ""
				keyboard[4, 6] = keysDown.Contains(System.Windows.Forms.Keys.None);             // ""

				keyboard[5, 0] = keysDown.Contains(System.Windows.Forms.Keys.Oemcomma);
				keyboard[5, 1] = keysDown.Contains(System.Windows.Forms.Keys.OemPeriod);
				keyboard[5, 2] = keysDown.Contains(System.Windows.Forms.Keys.OemQuestion);      // Forward slash
				keyboard[5, 3] = keysDown.Contains(System.Windows.Forms.Keys.None);             // Pi
				keyboard[5, 4] = keysDown.Contains(System.Windows.Forms.Keys.Down);
				keyboard[5, 5] = keysDown.Contains(System.Windows.Forms.Keys.Left);
				keyboard[5, 6] = keysDown.Contains(System.Windows.Forms.Keys.Right);

				keyboard[6, 0] = keysDown.Contains(System.Windows.Forms.Keys.K);
				keyboard[6, 1] = keysDown.Contains(System.Windows.Forms.Keys.L);
				keyboard[6, 2] = keysDown.Contains(System.Windows.Forms.Keys.Oemplus);          // Semicolon
				keyboard[6, 3] = keysDown.Contains(System.Windows.Forms.Keys.OemSemicolon);     // Colon
				keyboard[6, 4] = keysDown.Contains(System.Windows.Forms.Keys.OemCloseBrackets);
				keyboard[6, 5] = keysDown.Contains(System.Windows.Forms.Keys.Enter);
				keyboard[6, 6] = keysDown.Contains(System.Windows.Forms.Keys.Up);

				keyboard[7, 0] = keysDown.Contains(System.Windows.Forms.Keys.I);
				keyboard[7, 1] = keysDown.Contains(System.Windows.Forms.Keys.O);
				keyboard[7, 2] = keysDown.Contains(System.Windows.Forms.Keys.P);
				keyboard[7, 3] = keysDown.Contains(System.Windows.Forms.Keys.PageUp);           // @
				keyboard[7, 4] = keysDown.Contains(System.Windows.Forms.Keys.OemOpenBrackets);
				keyboard[7, 5] = keysDown.Contains(System.Windows.Forms.Keys.None);             // Not on English keyboard?
				keyboard[7, 6] = keysDown.Contains(System.Windows.Forms.Keys.None);             // ""

				keyboard[8, 0] = keysDown.Contains(System.Windows.Forms.Keys.D8);
				keyboard[8, 1] = keysDown.Contains(System.Windows.Forms.Keys.D9);
				keyboard[8, 2] = keysDown.Contains(System.Windows.Forms.Keys.D0);
				keyboard[8, 3] = keysDown.Contains(System.Windows.Forms.Keys.OemMinus);
				keyboard[8, 4] = keysDown.Contains(System.Windows.Forms.Keys.Oemtilde);         // ^, ~
				keyboard[8, 5] = keysDown.Contains(System.Windows.Forms.Keys.OemPipe);          // Yen, Pipe, Pound?
				keyboard[8, 6] = keysDown.Contains(System.Windows.Forms.Keys.PageDown);         // Break

				keyboard[9, 6] = keysDown.Contains(System.Windows.Forms.Keys.RControlKey);      // Graph

				keyboard[10, 6] = keysDown.Contains(System.Windows.Forms.Keys.LControlKey);     // Ctrl

				keyboard[11, 5] = keysDown.Contains(System.Windows.Forms.Keys.Tab);             // Func
				keyboard[11, 6] = keysDown.Contains(System.Windows.Forms.Keys.ShiftKey);        // Shift
			}
			else
			{
				/* Handle controllers */
				portAInputsPressed = 0;
				portBInputsPressed = 0;

				/* Keyboard */
				if (keysDown.Contains(configuration.Joypad1Up)) portAInputsPressed |= PortAInputs.P1Up;
				if (keysDown.Contains(configuration.Joypad1Down)) portAInputsPressed |= PortAInputs.P1Down;
				if (keysDown.Contains(configuration.Joypad1Left)) portAInputsPressed |= PortAInputs.P1Left;
				if (keysDown.Contains(configuration.Joypad1Right)) portAInputsPressed |= PortAInputs.P1Right;
				if (keysDown.Contains(configuration.Joypad1Button1)) portAInputsPressed |= PortAInputs.P1Button1;
				if (keysDown.Contains(configuration.Joypad1Button2)) portAInputsPressed |= PortAInputs.P1Button2;
				if (keysDown.Contains(configuration.Joypad2Up)) portAInputsPressed |= PortAInputs.P2Up;
				if (keysDown.Contains(configuration.Joypad2Down)) portAInputsPressed |= PortAInputs.P2Down;
				if (keysDown.Contains(configuration.Joypad2Left)) portBInputsPressed |= PortBInputs.P2Left;
				if (keysDown.Contains(configuration.Joypad2Right)) portBInputsPressed |= PortBInputs.P2Right;
				if (keysDown.Contains(configuration.Joypad2Button1)) portBInputsPressed |= PortBInputs.P2Button1;
				if (keysDown.Contains(configuration.Joypad2Button2)) portBInputsPressed |= PortBInputs.P2Button2;

				/* XInput controller */
				if (eventArgs.ControllerState.IsAnyUpDirectionPressed() && !eventArgs.ControllerState.IsAnyDownDirectionPressed()) portAInputsPressed |= PortAInputs.P1Up;
				if (eventArgs.ControllerState.IsAnyDownDirectionPressed() && !eventArgs.ControllerState.IsAnyUpDirectionPressed()) portAInputsPressed |= PortAInputs.P1Down;
				if (eventArgs.ControllerState.IsAnyLeftDirectionPressed() && !eventArgs.ControllerState.IsAnyRightDirectionPressed()) portAInputsPressed |= PortAInputs.P1Left;
				if (eventArgs.ControllerState.IsAnyRightDirectionPressed() && !eventArgs.ControllerState.IsAnyLeftDirectionPressed()) portAInputsPressed |= PortAInputs.P1Right;
				if (eventArgs.ControllerState.IsAPressed()) portAInputsPressed |= PortAInputs.P1Button1;
				if (eventArgs.ControllerState.IsXPressed() || eventArgs.ControllerState.IsBPressed()) portAInputsPressed |= PortAInputs.P1Button2;
			}
		}

		private void UpdateInput()
		{
			byte portA = 0xFF, portB = 0xFF;
			byte row = (byte)(ppi.PortCOutput & 0x07);

			if (row == 0x07)
			{
				/* Controller ports */
				portA &= (byte)~portAInputsPressed;
				portB &= (byte)~portBInputsPressed;
			}
			else
			{
				/* Keyboard matrix */
				for (int i = 0; i < 8; i++)
					if (keyboard[i, row]) portA &= (byte)~(1 << i);

				for (int i = 0; i < 4; i++)
					if (keyboard[8 + i, row]) portB &= (byte)~(1 << i);
			}
			ppi.PortAInput = portA;
			ppi.PortBInput = (byte)((ppi.PortBInput & 0xF0) | (portB & 0x0F));
		}

		private void UpdateTape(TapeUpdateModes updateMode)
		{
			if (!isTapePlaying) return;

			// TODO: errr, try to actually emulate this? so far just seems to write repeating bit patterns, no ex. recognizable basic program data...

			switch (updateMode)
			{
				case TapeUpdateModes.Reading:
					var read = ((ppi.PortBInput >> 7) & 0b1);   // TODO: correct?

					//
					break;

				case TapeUpdateModes.Writing:
					var write = ((ppi.PortCOutput >> 4) & 0b1); // TODO: correct?

					//
					break;
			}
		}

		/* Basic memory maps (via SC-3000 Service Manual, chp 2-8)
		 *
		 *      IIa     IIb     IIIa    IIIb
		 * 8000 --      CartRAM CartRAM CartRAM
		 * 8800 --      --      CartRAM CartRAM
		 * C000 WRAM    WRAM    WRAM    CartRAM
		 * C800 --      --      --      CartRAM
		 */

		private byte ReadMemory(ushort address)
		{
			if (cartridge != null && address >= cartridge.GetLowerBound() && address <= cartridge.GetUpperBound())
			{
				return cartridge.Read(address);
			}
			else if (address >= 0xC000 && address <= 0xFFFF)
			{
				return wram[address & (ramSize - 1)];
			}

			/* Cannot read from address, return 0 */
			return 0x00;
		}

		private void WriteMemory(ushort address, byte value)
		{
			if (cartridge != null && address >= cartridge.GetLowerBound() && address <= cartridge.GetUpperBound())
			{
				cartridge.Write(address, value);
			}
			else if (address >= 0xC000 && address <= 0xFFFF)
			{
				wram[address & (ramSize - 1)] = value;
			}
		}

		private byte ReadPort(byte port)
		{
			switch (port & 0xC0)
			{
				case 0x80:
					return vdp.ReadPort(port);

				case 0xC0:
					UpdateTape(TapeUpdateModes.Reading);
					UpdateInput();
					return ppi.ReadPort(port);

				default:
					// TODO: handle properly
					return 0x00;
			}
		}

		public void WritePort(byte port, byte value)
		{
			switch (port & 0xC0)
			{
				case 0x40:
					psg.WritePort(port, value);
					break;

				case 0x80:
					vdp.WritePort(port, value);
					break;

				case 0xC0:
					ppi.WritePort(port, value);
					UpdateTape(TapeUpdateModes.Writing);
					break;

				default:
					// TODO: handle properly
					break;
			}
		}
	}
}
