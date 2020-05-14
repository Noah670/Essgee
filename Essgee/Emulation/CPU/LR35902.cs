﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Essgee.Exceptions;
using Essgee.Utilities;

using static Essgee.Emulation.Utilities;

namespace Essgee.Emulation.CPU
{
	public partial class LR35902 : ICPU
	{
		[Flags]
		enum Flags : byte
		{
			UnusedBit0 = (1 << 0),          /* (0) */
			UnusedBit1 = (1 << 1),          /* (0) */
			UnusedBit2 = (1 << 2),          /* (0) */
			UnusedBit3 = (1 << 3),          /* (0) */
			Carry = (1 << 4),               /* C */
			HalfCarry = (1 << 5),           /* H */
			Subtract = (1 << 6),            /* N */
			Zero = (1 << 7)                 /* Z */
		}

		[Flags]
		enum InterruptTypes : byte
		{
			VBlank = (1 << 0),
			LCDCStatus = (1 << 1),
			TimerOverflow = (1 << 2),
			SerialIO = (1 << 3),
			Keypad = (1 << 4)
		}

		public delegate byte MemoryReadDelegate(ushort address);
		public delegate void MemoryWriteDelegate(ushort address, byte value);

		delegate void SimpleOpcodeDelegate(LR35902 c);

		MemoryReadDelegate memoryReadDelegate;
		MemoryWriteDelegate memoryWriteDelegate;

		[StateRequired]
		protected Register af, bc, de, hl;
		[StateRequired]
		protected ushort sp, pc;

		[StateRequired]
		protected bool ime, eiDelay, halt;

		[StateRequired]
		protected byte op;

		[StateRequired]
		InterruptState intState;

		[StateRequired]
		int currentCycles;

		public LR35902(MemoryReadDelegate memoryRead, MemoryWriteDelegate memoryWrite)
		{
			af = bc = de = hl = new Register();

			memoryReadDelegate = memoryRead;
			memoryWriteDelegate = memoryWrite;
		}

		public virtual void Startup()
		{
			Reset();

			if (memoryReadDelegate == null) throw new EmulationException("LR35902: Memory read method is null");
			if (memoryWriteDelegate == null) throw new EmulationException("LR35902: Memory write method is null");
		}

		public virtual void Shutdown()
		{
			//
		}

		public virtual void Reset()
		{
			af.Word = bc.Word = de.Word = hl.Word = 0;
			pc = 0;
			sp = 0;

			ime = eiDelay = halt = false;

			intState = InterruptState.Clear;

			currentCycles = 0;
		}

		public int Step()
		{
			currentCycles = 0;

			/* Handle delayed interrupt enable */
			if (eiDelay)
			{
				eiDelay = false;
				ime = true;
			}
			else
			{
				/* Check interrupt line */
				if (intState == InterruptState.Assert)
				{
					ServiceInterrupt();
				}
			}

			if (Program.AppEnvironment.EnableSuperSlowCPULogger)
			{
				string disasm = string.Format($"{pc:X4} | {ReadMemory8(pc):X2} | AF:{af.Word:X4} BC:{bc.Word:X4} DE:{de.Word:X4} HL:{hl.Word:X4} SP:{sp:X4}\n");
				System.IO.File.AppendAllText(@"D:\Temp\Essgee\log-lr35902.txt", disasm);
			}

			/* Fetch and execute opcode */
			op = ReadMemory8(pc++);
			switch (op)
			{
				case 0xCB: ExecuteOpCB(); break;
				default: ExecuteOpcodeNoPrefix(op); break;
			}

			return currentCycles;
		}

		#region Opcode Execution and Cycle Management

		private void ExecuteOpcodeNoPrefix(byte op)
		{
			opcodesNoPrefix[op](this);
			currentCycles += CycleCounts.NoPrefix[op];
		}

		private void ExecuteOpCB()
		{
			byte cbOp = ReadMemory8(pc++);
			opcodesPrefixCB[cbOp](this);
			currentCycles += CycleCounts.PrefixCB[cbOp];
		}

		#endregion

		#region Helpers (Flags, etc.)

		public void SetStackPointer(ushort value)
		{
			sp = value;
		}

		public void SetProgramCounter(ushort value)
		{
			pc = value;
		}

		private void SetFlag(Flags flags)
		{
			af.Low |= (byte)flags;
		}

		private void ClearFlag(Flags flags)
		{
			af.Low &= (byte)~flags;
		}

		private void SetClearFlagConditional(Flags flags, bool condition)
		{
			if (condition)
				af.Low |= (byte)flags;
			else
				af.Low &= (byte)~flags;
		}

		private bool IsFlagSet(Flags flags)
		{
			return (((Flags)af.Low & flags) == flags);
		}

		#endregion

		#region Interrupt, Halt and Stop State Handling

		public void SetInterruptLine(InterruptType type, InterruptState state)
		{
			switch (type)
			{
				case InterruptType.Maskable:
					intState = state;
					break;

				default: throw new EmulationException("LR35902: Unknown interrupt type");
			}
		}

		private void ServiceInterrupt()
		{
			if (!ime) return;

			LeaveHaltState();
			ime = false;

			InterruptTypes regIE = (InterruptTypes)ReadMemory8(0xFFFF);
			InterruptTypes regIF = (InterruptTypes)ReadMemory8(0xFF0F);

			// TODO make more elegant?
			if (((regIE & InterruptTypes.VBlank) == InterruptTypes.VBlank) && ((regIF & InterruptTypes.VBlank) == InterruptTypes.VBlank))
			{
				intState = InterruptState.Clear;
				Restart(0x0040);
				WriteMemory8(0xFF0F, (byte)(regIF & ~InterruptTypes.VBlank));
			}
			else if (((regIE & InterruptTypes.LCDCStatus) == InterruptTypes.LCDCStatus) && ((regIF & InterruptTypes.LCDCStatus) == InterruptTypes.LCDCStatus))
			{
				intState = InterruptState.Clear;
				Restart(0x0048);
				WriteMemory8(0xFF0F, (byte)(regIF & ~InterruptTypes.LCDCStatus));
			}
			else if (((regIE & InterruptTypes.TimerOverflow) == InterruptTypes.TimerOverflow) && ((regIF & InterruptTypes.TimerOverflow) == InterruptTypes.TimerOverflow))
			{
				intState = InterruptState.Clear;
				Restart(0x0050);
				WriteMemory8(0xFF0F, (byte)(regIF & ~InterruptTypes.TimerOverflow));
			}
			else if (((regIE & InterruptTypes.SerialIO) == InterruptTypes.SerialIO) && ((regIF & InterruptTypes.SerialIO) == InterruptTypes.SerialIO))
			{
				intState = InterruptState.Clear;
				Restart(0x0058);
				WriteMemory8(0xFF0F, (byte)(regIF & ~InterruptTypes.SerialIO));
			}
			else if (((regIE & InterruptTypes.Keypad) == InterruptTypes.Keypad) && ((regIF & InterruptTypes.Keypad) == InterruptTypes.Keypad))
			{
				intState = InterruptState.Clear;
				Restart(0x0060);
				WriteMemory8(0xFF0F, (byte)(regIF & ~InterruptTypes.Keypad));
			}

			currentCycles += 20;
		}

		private void EnterHaltState()
		{
			halt = true;
			pc--;
		}

		private void LeaveHaltState()
		{
			if (halt)
			{
				halt = false;
				pc++;
			}
		}

		#endregion

		#region Memory Access Functions

		private byte ReadMemory8(ushort address)
		{
			return memoryReadDelegate(address);
		}

		private void WriteMemory8(ushort address, byte value)
		{
			memoryWriteDelegate(address, value);
		}

		private ushort ReadMemory16(ushort address)
		{
			return (ushort)((memoryReadDelegate((ushort)(address + 1)) << 8) | memoryReadDelegate(address));
		}

		private void WriteMemory16(ushort address, ushort value)
		{
			memoryWriteDelegate(address, (byte)(value & 0xFF));
			memoryWriteDelegate((ushort)(address + 1), (byte)(value >> 8));
		}

		#endregion

		#region Opcodes: 8-Bit Load Group

		protected void LoadRegisterFromMemory8(ref byte register, ushort address, bool specialRegs)
		{
			LoadRegister8(ref register, ReadMemory8(address), specialRegs);
		}

		protected void LoadRegisterImmediate8(ref byte register, bool specialRegs)
		{
			LoadRegister8(ref register, ReadMemory8(pc++), specialRegs);
		}

		protected void LoadRegister8(ref byte register, byte value, bool specialRegs)
		{
			register = value;
		}

		protected void LoadMemory8(ushort address, byte value)
		{
			WriteMemory8(address, value);
		}

		#endregion

		#region Opcodes: 16-Bit Load Group

		protected void LoadRegisterImmediate16(ref ushort register)
		{
			LoadRegister16(ref register, ReadMemory16(pc));
			pc += 2;
		}

		protected void LoadRegister16(ref ushort register, ushort value)
		{
			register = value;
		}

		protected void LoadMemory16(ushort address, ushort value)
		{
			WriteMemory16(address, value);
		}

		protected void Push(Register register)
		{
			WriteMemory8(--sp, register.High);
			WriteMemory8(--sp, register.Low);
		}

		protected void Pop(ref Register register)
		{
			register.Low = ReadMemory8(sp++);
			register.High = ReadMemory8(sp++);
		}

		#endregion

		#region Opcodes: 8-Bit Arithmetic Group

		protected void Add8(byte operand, bool withCarry)
		{
			int operandWithCarry = (operand + (withCarry && IsFlagSet(Flags.Carry) ? 1 : 0));
			int result = (af.High + operandWithCarry);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			ClearFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, (((af.High ^ result ^ operand) & 0x10) != 0));
			SetClearFlagConditional(Flags.Carry, (result > 0xFF));

			af.High = (byte)result;
		}

		protected void Subtract8(byte operand, bool withCarry)
		{
			int operandWithCarry = (operand + (withCarry && IsFlagSet(Flags.Carry) ? 1 : 0));
			int result = (af.High - operandWithCarry);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			SetFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, (((af.High ^ result ^ operand) & 0x10) != 0));
			SetClearFlagConditional(Flags.Carry, (af.High < operandWithCarry));

			af.High = (byte)result;
		}

		protected void And8(byte operand)
		{
			int result = (af.High & operand);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			ClearFlag(Flags.Subtract);
			SetFlag(Flags.HalfCarry);
			ClearFlag(Flags.Carry);

			af.High = (byte)result;
		}

		protected void Or8(byte operand)
		{
			int result = (af.High | operand);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			ClearFlag(Flags.Carry);

			af.High = (byte)result;
		}

		protected void Xor8(byte operand)
		{
			int result = (af.High ^ operand);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			ClearFlag(Flags.Carry);

			af.High = (byte)result;
		}

		protected void Cp8(byte operand)
		{
			int result = (af.High - operand);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			SetFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, (((af.High ^ result ^ operand) & 0x10) != 0));
			SetClearFlagConditional(Flags.Carry, (af.High < operand));
		}

		protected void Increment8(ref byte register)
		{
			byte result = (byte)(register + 1);

			SetClearFlagConditional(Flags.Zero, (result == 0x00));
			ClearFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, ((register & 0x0F) == 0x0F));
			// C

			register = result;
		}

		protected void IncrementMemory8(ushort address)
		{
			byte value = ReadMemory8(address);
			Increment8(ref value);
			WriteMemory8(address, value);
		}

		protected void Decrement8(ref byte register)
		{
			byte result = (byte)(register - 1);

			SetClearFlagConditional(Flags.Zero, (result == 0x00));
			SetFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, ((register & 0x0F) == 0x00));
			// C

			register = result;
		}

		protected void DecrementMemory8(ushort address)
		{
			byte value = ReadMemory8(address);
			Decrement8(ref value);
			WriteMemory8(address, value);
		}

		#endregion

		#region Opcodes: General-Purpose Arithmetic and CPU Control Group

		protected void DecimalAdjustAccumulator()
		{
			/* "The Undocumented Z80 Documented" by Sean Young, chapter 4.7, http://www.z80.info/zip/z80-documented.pdf */

			byte before = af.High, diff = 0x00, result;
			bool carry = IsFlagSet(Flags.Carry), halfCarry = IsFlagSet(Flags.HalfCarry);
			byte highNibble = (byte)((before & 0xF0) >> 4), lowNibble = (byte)(before & 0x0F);

			if (carry)
			{
				diff |= 0x60;
				if ((halfCarry && lowNibble <= 0x09) || lowNibble >= 0x0A)
					diff |= 0x06;
			}
			else
			{
				if (lowNibble >= 0x0A && lowNibble <= 0x0F)
				{
					diff |= 0x06;
					if (highNibble >= 0x09 && highNibble <= 0x0F)
						diff |= 0x60;
				}
				else
				{
					if (highNibble >= 0x0A && highNibble <= 0x0F)
						diff |= 0x60;
					if (halfCarry)
						diff |= 0x06;
				}

				SetClearFlagConditional(Flags.Carry, (
					((highNibble >= 0x09 && highNibble <= 0x0F) && (lowNibble >= 0x0A && lowNibble <= 0x0F)) ||
					((highNibble >= 0x0A && highNibble <= 0x0F) && (lowNibble >= 0x00 && lowNibble <= 0x09))));
			}

			if (!IsFlagSet(Flags.Subtract))
				SetClearFlagConditional(Flags.HalfCarry, (lowNibble >= 0x0A && lowNibble <= 0x0F));
			else
				SetClearFlagConditional(Flags.HalfCarry, (halfCarry && (lowNibble >= 0x00 && lowNibble <= 0x05)));

			if (!IsFlagSet(Flags.Subtract))
				result = (byte)(before + diff);
			else
				result = (byte)(before - diff);

			SetClearFlagConditional(Flags.Zero, (result == 0x00));
			// N
			// H (set above)
			// C (set above)

			af.High = result;
		}

		protected void Negate()
		{
			int result = (0 - af.High);

			SetClearFlagConditional(Flags.Zero, ((result & 0xFF) == 0x00));
			SetFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, ((0 - (af.High & 0x0F)) < 0));
			SetClearFlagConditional(Flags.Carry, (af.High != 0x00));

			af.High = (byte)result;
		}

		#endregion

		#region Opcodes: 16-Bit Arithmetic Group

		protected void Add16(ref Register dest, ushort operand, bool withCarry)
		{
			int operandWithCarry = ((short)operand + (withCarry && IsFlagSet(Flags.Carry) ? 1 : 0));
			int result = (dest.Word + operandWithCarry);

			// Z
			ClearFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, (((dest.Word & 0x0FFF) + (operandWithCarry & 0x0FFF)) > 0x0FFF));
			SetClearFlagConditional(Flags.Carry, (((dest.Word & 0xFFFF) + (operandWithCarry & 0xFFFF)) > 0xFFFF));

			if (withCarry)
				SetClearFlagConditional(Flags.Zero, ((result & 0xFFFF) == 0x0000));

			dest.Word = (ushort)result;
		}

		protected void Subtract16(ref Register dest, ushort operand, bool withCarry)
		{
			int result = (dest.Word - operand - (withCarry && IsFlagSet(Flags.Carry) ? 1 : 0));

			SetClearFlagConditional(Flags.Zero, ((result & 0xFFFF) == 0x0000));
			SetFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, ((((dest.Word ^ result ^ operand) >> 8) & 0x10) != 0));
			SetClearFlagConditional(Flags.Carry, ((result & 0x10000) != 0));

			dest.Word = (ushort)result;
		}

		protected void Increment16(ref ushort register)
		{
			register++;
		}

		protected void Decrement16(ref ushort register)
		{
			register--;
		}

		#endregion

		#region Opcodes: Rotate and Shift Group

		protected byte RotateLeft(ushort address)
		{
			byte value = ReadMemory8(address);
			RotateLeft(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void RotateLeft(ref byte value)
		{
			bool isCarrySet = IsFlagSet(Flags.Carry);
			bool isMsbSet = IsBitSet(value, 7);
			value <<= 1;
			if (isCarrySet) SetBit(ref value, 0);

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isMsbSet);
		}

		protected byte RotateLeftCircular(ushort address)
		{
			byte value = ReadMemory8(address);
			RotateLeftCircular(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void RotateLeftCircular(ref byte value)
		{
			bool isMsbSet = IsBitSet(value, 7);
			value <<= 1;
			if (isMsbSet) SetBit(ref value, 0);

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isMsbSet);
		}

		protected byte RotateRight(ushort address)
		{
			byte value = ReadMemory8(address);
			RotateRight(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void RotateRight(ref byte value)
		{
			bool isCarrySet = IsFlagSet(Flags.Carry);
			bool isLsbSet = IsBitSet(value, 0);
			value >>= 1;
			if (isCarrySet) SetBit(ref value, 7);

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isLsbSet);
		}

		protected byte RotateRightCircular(ushort address)
		{
			byte value = ReadMemory8(address);
			RotateRightCircular(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void RotateRightCircular(ref byte value)
		{
			bool isLsbSet = IsBitSet(value, 0);
			value >>= 1;
			if (isLsbSet) SetBit(ref value, 7);

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isLsbSet);
		}

		protected void RotateLeftAccumulator()
		{
			bool isCarrySet = IsFlagSet(Flags.Carry);
			bool isMsbSet = IsBitSet(af.High, 7);
			af.High <<= 1;
			if (isCarrySet) SetBit(ref af.High, 0);

			// Z
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isMsbSet);
		}

		protected void RotateLeftAccumulatorCircular()
		{
			bool isMsbSet = IsBitSet(af.High, 7);
			af.High <<= 1;
			if (isMsbSet) SetBit(ref af.High, 0);

			// Z
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isMsbSet);
		}

		protected void RotateRightAccumulator()
		{
			bool isCarrySet = IsFlagSet(Flags.Carry);
			bool isLsbSet = IsBitSet(af.High, 0);
			af.High >>= 1;
			if (isCarrySet) SetBit(ref af.High, 7);

			// Z
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isLsbSet);
		}

		protected void RotateRightAccumulatorCircular()
		{
			bool isLsbSet = IsBitSet(af.High, 0);
			af.High >>= 1;
			if (isLsbSet) SetBit(ref af.High, 7);

			// Z
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isLsbSet);
		}

		protected void RotateRight4B()
		{
			byte hlValue = ReadMemory8(hl.Word);

			// A=WX  (HL)=YZ
			// A=WZ  (HL)=XY
			byte a1 = (byte)(af.High >> 4);     //W
			byte a2 = (byte)(af.High & 0xF);    //X
			byte hl1 = (byte)(hlValue >> 4);    //Y
			byte hl2 = (byte)(hlValue & 0xF);   //Z

			af.High = (byte)((a1 << 4) | hl2);
			hlValue = (byte)((a2 << 4) | hl1);

			WriteMemory8(hl.Word, hlValue);

			SetClearFlagConditional(Flags.Zero, (af.High == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			// C
		}

		protected void RotateLeft4B()
		{
			byte hlValue = ReadMemory8(hl.Word);

			// A=WX  (HL)=YZ
			// A=WY  (HL)=ZX
			byte a1 = (byte)(af.High >> 4);     //W
			byte a2 = (byte)(af.High & 0xF);    //X
			byte hl1 = (byte)(hlValue >> 4);    //Y
			byte hl2 = (byte)(hlValue & 0xF);   //Z

			af.High = (byte)((a1 << 4) | hl1);
			hlValue = (byte)((hl2 << 4) | a2);

			WriteMemory8(hl.Word, hlValue);

			SetClearFlagConditional(Flags.Zero, (af.High == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			// C
		}

		protected byte ShiftLeftArithmetic(ushort address)
		{
			byte value = ReadMemory8(address);
			ShiftLeftArithmetic(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void ShiftLeftArithmetic(ref byte value)
		{
			bool isMsbSet = IsBitSet(value, 7);
			value <<= 1;

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isMsbSet);
		}

		protected byte ShiftRightArithmetic(ushort address)
		{
			byte value = ReadMemory8(address);
			ShiftRightArithmetic(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void ShiftRightArithmetic(ref byte value)
		{
			bool isLsbSet = IsBitSet(value, 0);
			bool isMsbSet = IsBitSet(value, 7);
			value >>= 1;
			if (isMsbSet) SetBit(ref value, 7);

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isLsbSet);
		}

		protected byte ShiftRightLogical(ushort address)
		{
			byte value = ReadMemory8(address);
			ShiftRightLogical(ref value);
			WriteMemory8(address, value);
			return value;
		}

		protected void ShiftRightLogical(ref byte value)
		{
			bool isLsbSet = IsBitSet(value, 0);
			value >>= 1;

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			SetClearFlagConditional(Flags.Carry, isLsbSet);
		}

		#endregion

		#region Opcodes: Bit Set, Reset and Test Group

		protected byte SetBit(ushort address, int bit)
		{
			byte value = ReadMemory8(address);
			SetBit(ref value, bit);
			WriteMemory8(address, value);
			return value;
		}

		protected void SetBit(ref byte value, int bit)
		{
			value |= (byte)(1 << bit);
		}

		protected byte ResetBit(ushort address, int bit)
		{
			byte value = ReadMemory8(address);
			ResetBit(ref value, bit);
			WriteMemory8(address, value);
			return value;
		}

		protected void ResetBit(ref byte value, int bit)
		{
			value &= (byte)~(1 << bit);
		}

		protected void TestBit(ushort address, int bit)
		{
			byte value = ReadMemory8(address);

			TestBit(value, bit);
		}

		protected void TestBit(byte value, int bit)
		{
			bool isBitSet = ((value & (1 << bit)) != 0);

			SetClearFlagConditional(Flags.Zero, !isBitSet);
			ClearFlag(Flags.Subtract);
			SetFlag(Flags.HalfCarry);
			// C
		}

		#endregion

		#region Opcodes: Jump Group

		protected void Jump8()
		{
			pc += (ushort)(((sbyte)ReadMemory8(pc)) + 1);
		}

		protected void JumpConditional8(bool condition)
		{
			if (condition)
			{
				Jump8();
				currentCycles += CycleCounts.AdditionalJumpCond8Taken;
			}
			else
				pc++;
		}

		protected void JumpConditional16(bool condition)
		{
			if (condition)
				pc = ReadMemory16(pc);
			else
				pc += 2;
		}

		#endregion

		#region Opcodes: Call and Return Group

		protected void Call16()
		{
			WriteMemory8(--sp, (byte)((pc + 2) >> 8));
			WriteMemory8(--sp, (byte)((pc + 2) & 0xFF));
			pc = ReadMemory16(pc);
		}

		protected void CallConditional16(bool condition)
		{
			if (condition)
			{
				Call16();
				currentCycles += CycleCounts.AdditionalCallCondTaken;
			}
			else
				pc += 2;
		}

		protected void Return()
		{
			pc = ReadMemory16(sp);
			sp += 2;
		}

		protected void ReturnConditional(bool condition)
		{
			if (condition)
			{
				Return();
				currentCycles += CycleCounts.AdditionalRetCondTaken;
			}
		}

		protected void Restart(ushort address)
		{
			WriteMemory8(--sp, (byte)(pc >> 8));
			WriteMemory8(--sp, (byte)(pc & 0xFF));
			pc = address;
		}

		#endregion

		#region Opcodes: LR35902-specific Opcodes

		protected void Swap(ushort address)
		{
			byte value = ReadMemory8(address);
			Swap(ref value);
			WriteMemory8(address, value);
		}

		protected void Swap(ref byte value)
		{
			value = (byte)((value & 0xF0) >> 4 | (value & 0x0F) << 4);

			SetClearFlagConditional(Flags.Zero, (value == 0x00));
			ClearFlag(Flags.Subtract);
			ClearFlag(Flags.HalfCarry);
			ClearFlag(Flags.Carry);
		}

		protected void Stop()
		{
			// TODO
		}

		private void AddSPNN()
		{
			byte offset = ReadMemory8(pc++);

			ClearFlag(Flags.Zero);
			ClearFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, (((sp & 0x0F) + (offset & 0x0F)) > 0x0F));
			SetClearFlagConditional(Flags.Carry, (((sp & 0xFF) + (byte)(offset & 0xFF)) > 0xFF));

			sp = (ushort)(sp + (sbyte)offset);
		}

		private void LoadHLSPNN()
		{
			byte offset = ReadMemory8(pc++);

			ClearFlag(Flags.Zero);
			ClearFlag(Flags.Subtract);
			SetClearFlagConditional(Flags.HalfCarry, (((sp & 0x0F) + (offset & 0x0F)) > 0x0F));
			SetClearFlagConditional(Flags.Carry, (((sp & 0xFF) + (byte)(offset & 0xFF)) > 0xFF));

			hl.Word = (ushort)(sp + (sbyte)offset);
		}

		#endregion
	}
}