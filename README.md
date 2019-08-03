# Essgee
Essgee is an emulator for various 8-bit consoles and handhelds by Sega, supporting the SG-1000, SC-3000 (partially), Mark III/Master System and Game Gear. It is written in C# and uses .NET Framework v4.7.1, [OpenTK](https://www.nuget.org/packages/OpenTK) and [OpenTK.GLControl](https://www.nuget.org/packages/OpenTK.GLControl) for graphics and sound output, as well as [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) for JSON parsing.

It also improves just enough on [its](https://github.com/xdanieldzd/MasterFudge) [predecessors](https://github.com/xdanieldzd/MasterFudgeMk2) to no longer be called "fudged", hence its new, pun-y name.

## Status

### CPUs
* __Zilog Z80__: All opcodes implemented, documented and undocumented; the two undocumented flags are (possibly?) not fully supported yet; disassembly features incomplete

### VDPs
* __Texas Instruments TMS9918A__: Scanline-based, not fully accurate and possibly with some bugs here and there; still missing the multicolor graphics mode
  * __Sega 315-5124__ and __315-5246__: Mark III/Master System and Master System II VDPs, TMS9918A with additional graphics mode, line interrupts, etc.; also not fully accurate, also currently emulating a bit of a hybrid of both
  * __Sega 315-5378__: Game Gear VDP based on Master System II VDP, with higher color depth, etc.; also not fully accurate

### PSGs
* __Texas Instruments SN76489__: Fully emulated, accuracy is probably not very high, but still sounds decent enough
  * __Sega 315-5246__: Master System II PSG (integrated into VDP chip), SN76489 with minor differences in noise channel; same issues as SN76489
  * __Sega 315-5378__: Game Gear PSG (integrated into VDP) based on Master System II PSG, with stereo output extension; same issues as other PSGs

### Support Chips
* __Intel 8255__: Peripheral interface chip used in SG-1000 and SC-3000; not fully tested nor accurate, enough for controller and keyboard support where applicable

### Media
* Support for various cartridge types, ex. standard Sega mapper, Codemasters mapper and various Korean mappers

### Input Devices
* __SG-1000__: Standard controllers
* __SC-3000__: Standard controllers and integrated keyboard
  * Switch between controller and keyboard input using the Change Input Mode key, defaults to F1
  * Keyboard layout is (currently?) not user-configurable
* __Mark III/Master System__: Standard controllers and/or Light Phaser
  * Light Phaser support is still somewhat rudimentary
* __Game Gear__: Integrated controls

## Notes
* Overall accuracy of the emulation is nowhere near exact, but it is certainly accurate enough to play many games quite well
* Sound output _might_ stutter from time to time, the corresponding sound management code isn't too great
* The framerate limiter and FPS counter are somewhat inaccurate and might contribute to the aforementioned sound stuttering issues

## Screenshots
* __Girl's Garden__ (SG-1000):<br><br>
 ![Screenshot 1](https://raw.githubusercontent.com/xdanieldzd/Essgee/master/Screenshots/SG1000-Garden.png)<br><br>
* __Sega SC-3000 BASIC Level 3__ (SC-3000):<br><br>
 ![Screenshot 2](https://raw.githubusercontent.com/xdanieldzd/Essgee/master/Screenshots/SC3000-BasicLv3.png)<br><br>
* __Sonic the Hedgehog__ (Master System):<br><br>
 ![Screenshot 3](https://raw.githubusercontent.com/xdanieldzd/Essgee/master/Screenshots/SMS-Sonic1.png)<br><br>
* __GG Aleste II / Power Strike II__ (Game Gear):<br><br>
 ![Screenshot 4](https://raw.githubusercontent.com/xdanieldzd/Essgee/master/Screenshots/GG-AlesteII.png)<br><br>

## Acknowledgements & Attribution
* Essgee uses [DejaVu](https://dejavu-fonts.github.io) Sans Condensed as its OSD font; see the [DejaVu Fonts License](https://dejavu-fonts.github.io/License.html) for applicable information.
* The XML data files in `Assets\No-Intro` were created by the [No-Intro](http://www.no-intro.org) project; see the [DAT-o-MATIC website](https://datomatic.no-intro.org) for official downloads.
* The file `EssgeeIcon.ico` is derived from "[Sg1000.jpg](https://segaretro.org/File:Sg1000.jpg)" on [Sega Retro](https://segaretro.org), in revision from 16 March 2011 by [Black Squirrel](https://segaretro.org/User:Black_Squirrel), and used under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The image was edited to remove the controller and text, then resized for use as the application icon.
