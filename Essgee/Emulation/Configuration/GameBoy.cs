﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using Essgee.Emulation.Cartridges.Nintendo;
using Essgee.Emulation.ExtDevices.Nintendo;
using Essgee.Utilities;

namespace Essgee.Emulation.Configuration
{
	[ElementPriority(5)]
	public class GameBoy : IConfiguration
	{
		[CheckBoxControl("General", "Use Bootstrap ROM")]
		public bool UseBootstrap { get; set; }
		[IsBootstrapRomPath]
		[FileBrowserControl("General", "Bootstrap Path", "Game Boy Bootstrap ROM (*.gb;*.bin;*.zip)|*.gb;*.bin;*.zip")]
		public string BootstrapRom { get; set; }
		[DropDownControl("General", "Serial Device", typeof(ISerialDevice))]
		[JsonConverter(typeof(TypeNameJsonConverter), "Essgee.Emulation.ExtDevices.Nintendo")]
		public Type SerialDevice { get; set; }

		[DropDownControl("GB Camera", "Camera Source", typeof(GBCameraCartridge.ImageSources))]
		[JsonConverter(typeof(StringEnumConverter))]
		public GBCameraCartridge.ImageSources CameraSource { get; set; }
		[FileBrowserControl("GB Camera", "Camera Image", "Image Files (*.png;*.bmp)|*.png;*.bmp")]
		public string CameraImageFile { get; set; }

		[DropDownControl("Controls", "Up", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsUp { get; set; }
		[DropDownControl("Controls", "Down", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsDown { get; set; }
		[DropDownControl("Controls", "Left", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsLeft { get; set; }
		[DropDownControl("Controls", "Right", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsRight { get; set; }
		[DropDownControl("Controls", "A", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsA { get; set; }
		[DropDownControl("Controls", "B", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsB { get; set; }
		[DropDownControl("Controls", "Select", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsSelect { get; set; }
		[DropDownControl("Controls", "Start", typeof(Keys), Keys.F11)]
		[JsonConverter(typeof(StringEnumConverter))]
		public Keys ControlsStart { get; set; }

		public GameBoy()
		{
			UseBootstrap = false;
			BootstrapRom = string.Empty;

			SerialDevice = typeof(DummyDevice);
			CameraSource = GBCameraCartridge.ImageSources.Noise;
			CameraImageFile = string.Empty;

			ControlsUp = Keys.Up;
			ControlsDown = Keys.Down;
			ControlsLeft = Keys.Left;
			ControlsRight = Keys.Right;
			ControlsA = Keys.S;
			ControlsB = Keys.A;
			ControlsSelect = Keys.Space;
			ControlsStart = Keys.Enter;
		}
	}
}
