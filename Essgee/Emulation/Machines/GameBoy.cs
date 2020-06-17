﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

using Essgee.Emulation.Configuration;
using Essgee.Emulation.CPU;
using Essgee.Emulation.Video.Nintendo;
using Essgee.Emulation.Audio;
using Essgee.Emulation.Cartridges.Nintendo;
using Essgee.Emulation.ExtDevices.Nintendo;
using Essgee.EventArguments;
using Essgee.Exceptions;
using Essgee.Utilities;

namespace Essgee.Emulation.Machines
{
	[MachineIndex(5)]
	public class GameBoy : IMachine
	{
		const double masterClock = 4194304;
		const double refreshRate = 59.727500569606;

		const int wramSize = 8 * 1024;
		const int hramSize = 0x7F;

		const int serialCycleCount = 512;

		public event EventHandler<SendLogMessageEventArgs> SendLogMessage;
		protected virtual void OnSendLogMessage(SendLogMessageEventArgs e) { SendLogMessage?.Invoke(this, e); }

		public event EventHandler<EventArgs> EmulationReset;
		protected virtual void OnEmulationReset(EventArgs e) { EmulationReset?.Invoke(this, e); }

		public event EventHandler<RenderScreenEventArgs> RenderScreen
		{
			add { video.RenderScreen += value; }
			remove { video.RenderScreen -= value; }
		}

		public event EventHandler<SizeScreenEventArgs> SizeScreen
		{
			add { video.SizeScreen += value; }
			remove { video.SizeScreen -= value; }
		}

		public event EventHandler<ChangeViewportEventArgs> ChangeViewport;
		protected virtual void OnChangeViewport(ChangeViewportEventArgs e) { ChangeViewport?.Invoke(this, e); }

		public event EventHandler<PollInputEventArgs> PollInput;
		protected virtual void OnPollInput(PollInputEventArgs e) { PollInput?.Invoke(this, e); }

		public event EventHandler<EnqueueSamplesEventArgs> EnqueueSamples
		{
			add { audio.EnqueueSamples += value; }
			remove { audio.EnqueueSamples -= value; }
		}

		public event EventHandler<SaveExtraDataEventArgs> SaveExtraData;
		protected virtual void OnSaveExtraData(SaveExtraDataEventArgs e) { SaveExtraData?.Invoke(this, e); }

		public event EventHandler<EventArgs> EnableRumble;
		protected virtual void OnEnableRumble(EventArgs e) { EnableRumble?.Invoke(this, EventArgs.Empty); }

		public string ManufacturerName => "Nintendo";
		public string ModelName => "Game Boy";
		public string DatFilename => "Nintendo - Game Boy.dat";
		public (string Extension, string Description) FileFilter => (".gb", "Game Boy ROMs");
		public bool HasBootstrap => true;
		public double RefreshRate => refreshRate;
		public double PixelAspectRatio => 1.0;

		byte[] bootstrap;
		IGameBoyCartridge cartridge;
		byte[] wram, hram;
		byte ie;
		SM83 cpu;
		DMGVideo video;
		DMGAudio audio;
		ISerialDevice serialDevice;

		// FF00 - P1/JOYP
		byte joypadRegister;

		// FF01 - SB
		byte serialData;
		// FF02 - SC
		bool serialUseInternalClock, serialTransferInProgress;

		// FF04 - DIV
		byte divider;
		// FF05 - TIMA
		byte timerCounter;
		//
		ushort clockCycleCount;

		// FF06 - TMA
		byte timerModulo;

		// FF07 - TAC
		bool timerRunning;
		byte timerInputClock;
		//
		bool timerOverflow, timerLoading;

		// FF0F - IF
		bool irqVBlank, irqLCDCStatus, irqTimerOverflow, irqSerialIO, irqKeypad;

		// FF50
		bool bootstrapDisabled;

		[Flags]
		enum JoypadInputs : byte
		{
			Right = (1 << 0),
			Left = (1 << 1),
			Up = (1 << 2),
			Down = (1 << 3),
			A = (1 << 4),
			B = (1 << 5),
			Select = (1 << 6),
			Start = (1 << 7)
		}

		JoypadInputs inputsPressed;

		int serialBitsCounter, serialCycles;

		int currentMasterClockCyclesInFrame, totalMasterClockCyclesInFrame;

		Configuration.GameBoy configuration;

		public GameBoy() { }

		public void Initialize()
		{
			bootstrap = null;
			cartridge = null;

			wram = new byte[wramSize];
			hram = new byte[hramSize];
			cpu = new SM83(ReadMemory, WriteMemory);
			video = new DMGVideo(ReadMemory, cpu.RequestInterrupt);
			audio = new DMGAudio();

			video.EndOfScanline += (s, e) =>
			{
				PollInputEventArgs pollInputEventArgs = new PollInputEventArgs();
				OnPollInput(pollInputEventArgs);
				ParseInput(pollInputEventArgs);
			};
		}

		public void SetConfiguration(IConfiguration config)
		{
			configuration = (Configuration.GameBoy)config;

			ReconfigureSystem();
		}

		public void SetRuntimeOption(string name, object value)
		{
			//TODO layer toggling etc
		}

		private void ReconfigureSystem()
		{
			/* Video */
			video?.SetClockRate(masterClock);
			video?.SetRefreshRate(refreshRate);
			video?.SetRevision(0);

			/* Audio */
			audio?.SetSampleRate(Program.Configuration.SampleRate);
			audio?.SetOutputChannels(2);
			audio?.SetClockRate(masterClock);
			audio?.SetRefreshRate(refreshRate);

			/* Cartridge */
			if (cartridge is GBCameraCartridge camCartridge)
				camCartridge.SetImageSource(configuration.CameraSource, configuration.CameraImageFile);

			/* Serial */
			if (serialDevice != null)
			{
				serialDevice.SaveExtraData -= SaveExtraData;
				serialDevice.Shutdown();
			}

			serialDevice = (ISerialDevice)Activator.CreateInstance(configuration.SerialDevice);
			serialDevice.SaveExtraData += SaveExtraData;
			serialDevice.Initialize();

			/* Misc timing */
			currentMasterClockCyclesInFrame = 0;
			totalMasterClockCyclesInFrame = (int)Math.Round(masterClock / refreshRate);

			/* Announce viewport */
			OnChangeViewport(new ChangeViewportEventArgs(video.Viewport));
		}

		private void LoadBootstrap()
		{
			if (configuration.UseBootstrap)
			{
				var (type, bootstrapRomData) = CartridgeLoader.Load(configuration.BootstrapRom, "Game Boy Bootstrap");
				bootstrap = new byte[bootstrapRomData.Length];
				Buffer.BlockCopy(bootstrapRomData, 0, bootstrap, 0, bootstrap.Length);
			}
		}

		public void Startup()
		{
			LoadBootstrap();

			cpu.Startup();
			video.Startup();
			audio.Startup();
		}

		public void Reset()
		{
			cpu.Reset();
			video.Reset();
			audio.Reset();

			if (configuration.UseBootstrap)
			{
				cpu.SetProgramCounter(0x0000);
				cpu.SetStackPointer(0x0000);
			}
			else
			{
				cpu.SetProgramCounter(0x0100);
				cpu.SetStackPointer(0xFFFE);
				cpu.SetRegisterAF(0x01B0);
				cpu.SetRegisterBC(0x0013);
				cpu.SetRegisterDE(0x00D8);
				cpu.SetRegisterHL(0x014D);

				video.WritePort(0x40, 0x91);
				video.WritePort(0x42, 0x00);
				video.WritePort(0x43, 0x00);
				video.WritePort(0x45, 0x00);
				video.WritePort(0x47, 0xFC);
				video.WritePort(0x48, 0xFF);
				video.WritePort(0x49, 0xFF);
				video.WritePort(0x4A, 0x00);
				video.WritePort(0x4B, 0x00);
			}

			joypadRegister = 0x0F;

			serialData = 0xFF;
			serialUseInternalClock = serialTransferInProgress = false;

			timerCounter = 0;
			clockCycleCount = 0;

			timerModulo = 0;

			timerRunning = false;
			timerInputClock = 0;

			timerOverflow = timerLoading = false;

			irqVBlank = irqLCDCStatus = irqTimerOverflow = irqSerialIO = irqKeypad = false;

			bootstrapDisabled = !configuration.UseBootstrap;

			inputsPressed = 0;

			serialBitsCounter = serialCycles = 0;

			OnEmulationReset(EventArgs.Empty);
		}

		public void Shutdown()
		{
			if (serialDevice != null)
			{
				serialDevice.SaveExtraData -= SaveExtraData;
				serialDevice.Shutdown();
			}

			if (cartridge is MBC5Cartridge mbc5Cartridge)
				mbc5Cartridge.EnableRumble -= EnableRumble;

			cpu?.Shutdown();
			video?.Shutdown();
			audio?.Shutdown();
		}

		public void SetState(Dictionary<string, dynamic> state)
		{
			throw new NotImplementedException();
		}

		public Dictionary<string, dynamic> GetState()
		{
			throw new NotImplementedException();
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
			cartridge = SpecializedLoader.CreateCartridgeInstance(romData, ramData, mapperType);

			if (cartridge is GBCameraCartridge camCartridge)
				camCartridge.SetImageSource(configuration.CameraSource, configuration.CameraImageFile);

			if (cartridge is MBC5Cartridge mbc5Cartridge)
				mbc5Cartridge.EnableRumble += EnableRumble;
		}

		public byte[] GetCartridgeRam()
		{
			return cartridge?.GetRamData();
		}

		public bool IsCartridgeRamSaveNeeded()
		{
			if (cartridge == null) return false;
			return cartridge.IsRamSaveNeeded();
		}

		public virtual void RunFrame()
		{
			while (currentMasterClockCyclesInFrame < totalMasterClockCyclesInFrame)
				RunStep();

			currentMasterClockCyclesInFrame = 0;
		}

		public void RunStep()
		{
			var clockCyclesInStep = cpu.Step();

			for (var s = 0; s < clockCyclesInStep / 4; s++)
			{
				HandleTimerOverflow();
				UpdateCycleCounter((ushort)(clockCycleCount + 4));

				HandleSerialIO(4);

				video.Step(4);
				audio.Step(4);
				cartridge?.Step(4);

				currentMasterClockCyclesInFrame += 4;
			}
		}

		private void IncrementTimer()
		{
			timerCounter++;
			if (timerCounter == 0) timerOverflow = true;
		}

		private bool GetTimerBit(byte value, ushort cycles)
		{
			switch (value & 0b11)
			{
				case 0: return (cycles & (1 << 9)) != 0;
				case 1: return (cycles & (1 << 3)) != 0;
				case 2: return (cycles & (1 << 5)) != 0;
				case 3: return (cycles & (1 << 7)) != 0;
				default: throw new EmulationException("Unhandled timer state");
			}
		}

		private void UpdateCycleCounter(ushort value)
		{
			if (timerRunning)
			{
				if (!GetTimerBit(timerInputClock, value) && GetTimerBit(timerInputClock, clockCycleCount))
					IncrementTimer();
			}

			clockCycleCount = value;
			divider = (byte)(clockCycleCount >> 8);
		}

		private void HandleTimerOverflow()
		{
			timerLoading = false;

			if (timerOverflow)
			{
				cpu.RequestInterrupt(SM83.InterruptSource.TimerOverflow);
				timerOverflow = false;

				timerCounter = timerModulo;
				timerLoading = true;
			}
		}

		private void HandleSerialIO(int clockCyclesInStep)
		{
			if (serialTransferInProgress)
			{
				if (serialUseInternalClock)
				{
					/* If using internal clock... */

					serialCycles += clockCyclesInStep;
					if (serialCycles >= serialCycleCount)
					{
						serialBitsCounter++;
						if (serialBitsCounter == 8)
						{
							serialData = serialDevice.DoSlaveTransfer(serialData);

							cpu.RequestInterrupt(SM83.InterruptSource.SerialIO);
							serialTransferInProgress = false;
							serialBitsCounter = 0;
						}
						serialCycles -= serialCycleCount;
					}
				}
				else if (serialDevice.ProvidesClock)
				{
					/* If other devices provides clock... */

					serialCycles += clockCyclesInStep;
					if (serialCycles >= serialCycleCount)
					{
						serialBitsCounter++;
						if (serialBitsCounter == 8)
						{
							serialData = serialDevice.DoMasterTransfer(serialData);

							cpu.RequestInterrupt(SM83.InterruptSource.SerialIO);
							serialTransferInProgress = false;
							serialBitsCounter = 0;
						}
						serialCycles -= serialCycleCount;
					}
				}
			}
		}

		private void ParseInput(PollInputEventArgs eventArgs)
		{
			inputsPressed = 0;

			if ((eventArgs.Keyboard.Contains(configuration.ControlsRight) && !eventArgs.Keyboard.Contains(configuration.ControlsLeft)) ||
				eventArgs.ControllerState.IsAnyRightDirectionPressed() && !eventArgs.ControllerState.IsAnyLeftDirectionPressed())
				inputsPressed |= JoypadInputs.Right;

			if ((eventArgs.Keyboard.Contains(configuration.ControlsLeft) && !eventArgs.Keyboard.Contains(configuration.ControlsRight)) ||
				eventArgs.ControllerState.IsAnyLeftDirectionPressed() && !eventArgs.ControllerState.IsAnyRightDirectionPressed())
				inputsPressed |= JoypadInputs.Left;

			if ((eventArgs.Keyboard.Contains(configuration.ControlsUp) && !eventArgs.Keyboard.Contains(configuration.ControlsDown)) ||
				eventArgs.ControllerState.IsAnyUpDirectionPressed() && !eventArgs.ControllerState.IsAnyDownDirectionPressed())
				inputsPressed |= JoypadInputs.Up;

			if ((eventArgs.Keyboard.Contains(configuration.ControlsDown) && !eventArgs.Keyboard.Contains(configuration.ControlsUp)) ||
				eventArgs.ControllerState.IsAnyDownDirectionPressed() && !eventArgs.ControllerState.IsAnyUpDirectionPressed())
				inputsPressed |= JoypadInputs.Down;

			if (eventArgs.Keyboard.Contains(configuration.ControlsA) || eventArgs.ControllerState.IsAPressed()) inputsPressed |= JoypadInputs.A;
			if (eventArgs.Keyboard.Contains(configuration.ControlsB) || eventArgs.ControllerState.IsXPressed() || eventArgs.ControllerState.IsBPressed()) inputsPressed |= JoypadInputs.B;
			if (eventArgs.Keyboard.Contains(configuration.ControlsSelect) || eventArgs.ControllerState.IsBackPressed()) inputsPressed |= JoypadInputs.Select;
			if (eventArgs.Keyboard.Contains(configuration.ControlsStart) || eventArgs.ControllerState.IsStartPressed()) inputsPressed |= JoypadInputs.Start;
		}

		private byte ReadMemory(ushort address)
		{
			if (address >= 0x0000 && address <= 0x7FFF)
			{
				if (configuration.UseBootstrap && address < 0x0100 && !bootstrapDisabled)
					return bootstrap[address & 0x00FF];
				else
					return (cartridge != null ? cartridge.Read(address) : (byte)0xFF);
			}
			else if (address >= 0x8000 && address <= 0x9FFF)
			{
				return video.ReadVram(address);
			}
			else if (address >= 0xA000 && address <= 0xBFFF)
			{
				return (cartridge != null ? cartridge.Read(address) : (byte)0xFF);
			}
			else if (address >= 0xC000 && address <= 0xFDFF)
			{
				return wram[address & (wramSize - 1)];
			}
			else if (address >= 0xFE00 && address <= 0xFE9F)
			{
				return video.ReadOam(address);
			}
			else if (address >= 0xFF00 && address <= 0xFF7F)
			{
				return ReadIo(address);
			}
			else if (address >= 0xFF80 && address <= 0xFFFE)
			{
				return hram[address - 0xFF80];
			}
			else if (address == 0xFFFF)
			{
				return ie;
			}

			/* Cannot read from address, return 0 */
			return 0x00;
		}

		private byte ReadIo(ushort address)
		{
			if ((address & 0xFFF0) == 0xFF40)
				return video.ReadPort((byte)(address & 0xFF));
			else if ((address & 0xFFF0) == 0xFF10 || (address & 0xFFF0) == 0xFF20 || (address & 0xFFF0) == 0xFF30)
				return audio.ReadPort((byte)(address & 0xFF));
			else
			{
				switch (address)
				{
					case 0xFF00:
						// P1/JOYP
						return joypadRegister;

					case 0xFF01:
						// SB
						return serialData;

					case 0xFF02:
						// SC
						return (byte)(
							0x7E |
							(serialUseInternalClock ? (1 << 0) : 0) |
							(serialTransferInProgress ? (1 << 7) : 0));

					case 0xFF04:
						// DIV
						return divider;

					case 0xFF05:
						// TIMA
						return timerCounter;

					case 0xFF06:
						// TMA
						return timerModulo;

					case 0xFF07:
						// TAC
						return (byte)(
							0xF8 |
							(timerRunning ? (1 << 2) : 0) |
							(timerInputClock & 0b11));

					case 0xFF0F:
						// IF
						return (byte)(
							0xE0 |
							(irqVBlank ? (1 << 0) : 0) |
							(irqLCDCStatus ? (1 << 1) : 0) |
							(irqTimerOverflow ? (1 << 2) : 0) |
							(irqSerialIO ? (1 << 3) : 0) |
							(irqKeypad ? (1 << 4) : 0));

					case 0xFF50:
						// Bootstrap disable
						return (byte)(
							0xFE |
							(bootstrapDisabled ? (1 << 0) : 0));

					default:
						return 0xFF;// throw new NotImplementedException();
				}
			}
		}

		private void WriteMemory(ushort address, byte value)
		{
			if (address >= 0x0000 && address <= 0x7FFF)
			{
				cartridge?.Write(address, value);
			}
			else if (address >= 0x8000 && address <= 0x9FFF)
			{
				video.WriteVram(address, value);
			}
			else if (address >= 0xA000 && address <= 0xBFFF)
			{
				cartridge?.Write(address, value);
			}
			else if (address >= 0xC000 && address <= 0xFDFF)
			{
				wram[address & (wramSize - 1)] = value;
			}
			else if (address >= 0xFE00 && address <= 0xFE9F)
			{
				video.WriteOam(address, value);
			}
			else if (address >= 0xFF00 && address <= 0xFF7F)
			{
				WriteIo(address, value);
			}
			else if (address >= 0xFF80 && address <= 0xFFFE)
			{
				hram[address - 0xFF80] = value;
			}
			else if (address == 0xFFFF)
			{
				ie = value;
			}
		}

		private void WriteIo(ushort address, byte value)
		{
			if ((address & 0xFFF0) == 0xFF40)
				video.WritePort((byte)(address & 0xFF), value);
			else if ((address & 0xFFF0) == 0xFF10 || (address & 0xFFF0) == 0xFF20 || (address & 0xFFF0) == 0xFF30)
				audio.WritePort((byte)(address & 0xFF), value);
			else
			{
				switch (address)
				{
					case 0xFF00:
						joypadRegister = (byte)((joypadRegister & 0xC0) | (value & 0x30));
						if ((joypadRegister & 0x30) == 0x20)
							joypadRegister |= (byte)(((byte)inputsPressed & 0x0F) ^ 0x0F);
						else if ((joypadRegister & 0x30) == 0x10)
							joypadRegister |= (byte)((((byte)inputsPressed & 0xF0) >> 4) ^ 0x0F);
						else
							joypadRegister = 0xFF;
						break;

					case 0xFF01:
						serialData = value;
						break;

					case 0xFF02:
						serialUseInternalClock = (value & (1 << 0)) != 0;
						serialTransferInProgress = (value & (1 << 7)) != 0;

						if (serialTransferInProgress) serialCycles = 0;
						serialBitsCounter = 0;
						break;

					case 0xFF04:
						UpdateCycleCounter(0);
						break;

					case 0xFF05:
						if (!timerLoading)
						{
							timerCounter = value;
							timerOverflow = false;
						}
						break;

					case 0xFF06:
						timerModulo = value;
						if (timerLoading)
							timerCounter = value;
						break;

					case 0xFF07:
						{
							var newTimerRunning = (value & (1 << 2)) != 0;
							var newTimerInputClock = (byte)(value & 0b11);

							var oldBit = timerRunning && GetTimerBit(timerInputClock, clockCycleCount);
							var newBit = newTimerRunning && GetTimerBit(newTimerInputClock, clockCycleCount);

							if (oldBit && !newBit)
								IncrementTimer();

							timerRunning = newTimerRunning;
							timerInputClock = newTimerInputClock;
						}
						break;

					case 0xFF0F:
						irqVBlank = (value & (1 << 0)) != 0;
						irqLCDCStatus = (value & (1 << 1)) != 0;
						irqTimerOverflow = (value & (1 << 2)) != 0;
						irqSerialIO = (value & (1 << 3)) != 0;
						irqKeypad = (value & (1 << 4)) != 0;
						break;

					case 0xFF50:
						if (!bootstrapDisabled)
							bootstrapDisabled = (value & (1 << 0)) != 0;
						break;
				}
			}
		}
	}
}
