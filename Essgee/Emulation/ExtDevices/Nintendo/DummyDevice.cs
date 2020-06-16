﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

using Essgee.EventArguments;

namespace Essgee.Emulation.ExtDevices.Nintendo
{
	[Description("None")]
	[ElementPriority(0)]
	public class DummyDevice : ISerialDevice
	{
		public event EventHandler<SaveExtraDataEventArgs> SaveExtraData;
		protected virtual void OnSaveExtraData(SaveExtraDataEventArgs e) { SaveExtraData?.Invoke(this, e); }

		public bool ProvidesClock => false;

		public void Initialize() { }
		public void Shutdown() { }
		public byte DoSlaveTransfer(byte data) { return 0xFF; }
		public byte DoMasterTransfer(byte data) { return 0xFF; }
	}
}
