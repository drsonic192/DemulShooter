using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using DsCore;
using DsCore.Config;
using DsCore.MameOutput;
using DsCore.Memory;
using DsCore.RawInput;

namespace DemulShooter.Games
{
    public class Game_LindberghRambo : Game
    {
        private const String GAMEDATA_FOLDER = @"MemoryData\lindbergh\rambo";

        //Inputs
        private InjectionStruct _JvsRawAxes_InjectionStruct = new InjectionStruct(0x083D696A, 7);
        private InjectionStruct _AdjustedAxes_InjectionStruct = new InjectionStruct(0x08075288, 6);
        private InjectionStruct _Buttons_InjectionStruct = new InjectionStruct(0x083D6729, 7);

        //Outputs
        private UInt32 _JvsMgrPtr_Address = 0x085DD158;
        private UInt32 _CreditsMgrPtr_Address = 0x085DC2B0;
        private UInt32 _PlayerMgrPtr_Address = 0x85CE9B0;

        //Show crosshair flag
        private UInt32 _DrawSightFlag_Address = 0x85A3BE8;

        //Custom Data
        private UInt32 _JvsRawAxes_CaveAddress;
        private UInt32 _AdjustedAxes_CaveAddress;
        private UInt32 _Buttons_CaveAddress;

        private UInt32 _RomLoaded_Check_Address = 0x08073BC7;

        /// <summary>
        /// Constructor
        /// </summary>
        public Game_LindberghRambo(String RomName)
            : base(RomName, "linuxloader")
        {
            _KnownMd5Prints.Add("ramboD.elf (SBQL)", "cad71f7fa562b285feee195ee1ff32bf");
            _KnownMd5Prints.Add("ramboM.elf (SBQL)", "e3ce5cd7aa18be0c0a3b9d6a7928b122");
            _KnownMd5Prints.Add("ramboM.elf (SBSS)", "4590a750488712001d230daf69dc7fab");

            _tProcess.Start();
            Logger.WriteLog("Waiting for Lindbergh " + _RomName + " game to hook.....");
        }

        /// <summary>
        /// Timer event when looking for Process (auto-Hook and auto-close)
        /// </summary>
        protected override void tProcess_Elapsed(Object Sender, EventArgs e)
        {
            if (!_ProcessHooked)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(_Target_Process_Name);
                    if (processes.Length > 0)
                    {
                        _TargetProcess = processes[0];
                        _ProcessHandle = _TargetProcess.Handle;
                        _TargetProcess_MemoryBaseAddress = _TargetProcess.MainModule.BaseAddress;

                        if (_TargetProcess_MemoryBaseAddress != IntPtr.Zero)
                        {
                            if (_Target_Process_Name.ToLower().Contains("budgieloader"))
                            {
                                if (!FindGameWindow_Contains("RAMBO"))
                                {
                                    Logger.WriteLog("Game Window not found, waiting...");
                                    return;
                                }
                            }
                            else if (_Target_Process_Name.ToLower().Contains("linuxloader"))
                            {
                                if (!FindGameWindow_Contains("FPS"))
                                {
                                    Logger.WriteLog("Game Window not found, waiting...");
                                    return;
                                }
                            }

                            //To make sure LinuxLoader has loaded the rom entirely, we're looking for some random instruction to be present in memory before starting                            
                            //And this instruction is also helping us detecting whether the game file is Rev.A or Rev.B or Rev.C binary, to call the corresponding hack
                            byte[] buffer = ReadBytes(_RomLoaded_Check_Address, 3);
                            if (buffer.SequenceEqual(new byte[] { 0xE8, 0x76, 0x01 }))
                            {
                                Logger.WriteLog("ramboD.elf (SBQL) binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["ramboD.elf (SBQL)"];
                            }
                            else if (buffer.SequenceEqual(new byte[] { 0x89, 0x1C, 0x24 }))
                            {
                                Logger.WriteLog("ramboM.elf (SBQL) binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["ramboM.elf (SBQL)"];
                            }
                            else if (buffer.SequenceEqual(new byte[] { 0xE9, 0x86, 0xEF }))
                            {
                                Logger.WriteLog("ramboM.elf (SBSS) binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["ramboM.elf (SBSS)"];
                            }
                            else
                            {
                                Logger.WriteLog("Game not Loaded, waiting...");
                                return;
                            }

                            Logger.WriteLog("Attached to Process " + _Target_Process_Name + ".exe, ProcessHandle = " + _ProcessHandle);
                            Logger.WriteLog(_Target_Process_Name + ".exe = 0x" + _TargetProcess_MemoryBaseAddress.ToString("X8"));
                            ReadGameDataFromMd5Hash(GAMEDATA_FOLDER);
                            Apply_MemoryHacks();
                            _ProcessHooked = true;
                            RaiseGameHookedEvent();
                        }
                    }
                }
                catch
                {
                    Logger.WriteLog("Error trying to hook " + _Target_Process_Name + ".exe");
                }
            }
            else
            {
                Process[] processes = Process.GetProcessesByName(_Target_Process_Name);
                if (processes.Length <= 0)
                {
                    _ProcessHooked = false;
                    _TargetProcess = null;
                    _ProcessHandle = IntPtr.Zero;
                    _TargetProcess_MemoryBaseAddress = IntPtr.Zero;
                    Logger.WriteLog(_Target_Process_Name + ".exe closed");
                    Application.Exit();
                }
            }
        }

        #region Screen

        /// <summary>
        /// Convert client area pointer location to Game speciffic data for memory injection
        /// </summary>
        public override bool GameScale(PlayerSettings PlayerData)
        {
            if (_ProcessHandle != IntPtr.Zero)
            {
                try
                {
                    double TotalResX = _ClientRect.Right - _ClientRect.Left;
                    double TotalResY = _ClientRect.Bottom - _ClientRect.Top;
                    Logger.WriteLog("Game Window Rect (Px) = [ " + TotalResX + "x" + TotalResY + " ]");

                    //X and Y axis => 0x00 - 0xFF                    
                    double dMaxX = 255.0;
                    double dMaxY = 255.0;

                    PlayerData.RIController.Computed_X = Convert.ToInt16(Math.Round(dMaxX * PlayerData.RIController.Computed_X / TotalResX));
                    PlayerData.RIController.Computed_Y = Convert.ToInt16(Math.Round(dMaxY * PlayerData.RIController.Computed_Y / TotalResY));
                    if (PlayerData.RIController.Computed_X < 0)
                        PlayerData.RIController.Computed_X = 0;
                    if (PlayerData.RIController.Computed_Y < 0)
                        PlayerData.RIController.Computed_Y = 0;
                    if (PlayerData.RIController.Computed_X > (int)dMaxX)
                        PlayerData.RIController.Computed_X = (int)dMaxX;
                    if (PlayerData.RIController.Computed_Y > (int)dMaxY)
                        PlayerData.RIController.Computed_Y = (int)dMaxY;

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.WriteLog("Error scaling mouse coordonates to GameFormat : " + ex.Message.ToString());
                }
            }
            return false;
        }

        #endregion

        #region Memory Hack

        protected override void Apply_InputsMemoryHack()
        {
            Create_InputsDataBank();
            _JvsRawAxes_CaveAddress = _InputsDatabank_Address;
            _AdjustedAxes_CaveAddress = _InputsDatabank_Address + 0x10;
            _Buttons_CaveAddress = _InputsDatabank_Address + 0x20;

            SetHack_JvsRawAxes();
            SetHack_AdjustedAxes();
            SetHack_Buttons();
        }

        /// <summary>
        /// At the end of amJvspAckAnalogInput(), replacing Axis value before memory copy
        /// </summary>
        private void SetHack_JvsRawAxes()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess_MemoryBaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);

            //lea ecx,[ebx-B]
            CaveMemory.Write_StrBytes("8D 4B F5");
            //movzx ecx,word ptr [ecx+_Axes_CaveAddress]
            CaveMemory.Write_StrBytes("0F B6 89");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_JvsRawAxes_CaveAddress));
            //lea ebx,[ebx+esi+00000102]
            CaveMemory.Write_StrBytes("8D 9C 33 02 01 00 00");
            //mov [ebx],cx
            CaveMemory.Write_StrBytes("66 89 0B");

            //Inject it
            CaveMemory.InjectToAddress(_JvsRawAxes_InjectionStruct, "Axes");
        }

        /// <summary>
        /// Replacing GetAdjstGunPos() content
        /// </summary>
        private void SetHack_AdjustedAxes()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess_MemoryBaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);

            //mov eax,[esp+18]
            CaveMemory.Write_StrBytes("8B 44 24 18");
            //shl eax,1
            CaveMemory.Write_StrBytes("D1 E0");
            //movzx eax,byte ptr [eax+_Buttons_CaveAddress + 6]
            CaveMemory.Write_StrBytes("0F B6 80");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_Buttons_CaveAddress + 6));
            //and al,1
            CaveMemory.Write_StrBytes("24 01");
            //test al,al
            CaveMemory.Write_StrBytes("84 C0");
            //je OnScreen
            CaveMemory.Write_StrBytes("74 11");
            //mov eax,[esp+04]
            CaveMemory.Write_StrBytes("8B 44 24 04");
            //mov byte ptr [eax],
            CaveMemory.Write_StrBytes("C6 00 FF");
            //mov eax,[esp+08]
            CaveMemory.Write_StrBytes("8B 44 24 08");
            //mov byte ptr [eax],
            CaveMemory.Write_StrBytes("C6 00 FF");
            //xor eax,eax
            CaveMemory.Write_StrBytes("31 C0");
            //ret
            CaveMemory.Write_StrBytes("C3");

            //OnScreen:
            //mov eax,[esp+18]
            CaveMemory.Write_StrBytes("8B 44 24 18");
            //shl eax,1
            CaveMemory.Write_StrBytes("D1 E0");
            //add eax,_AdjustedAxes_CaveAddress
            CaveMemory.Write_StrBytes("05");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_AdjustedAxes_CaveAddress));
            //movzx eax,word ptr [eax]
            CaveMemory.Write_StrBytes("0F B7 00");
            //mov edx,[esp+04]
            CaveMemory.Write_StrBytes("8B 54 24 04");
            //mov byte ptr[edx],al
            CaveMemory.Write_StrBytes("88 02");
            //mov edx,[esp+08]
            CaveMemory.Write_StrBytes("8B 54 24 08");
            //shr eax, 8
            CaveMemory.Write_StrBytes("C1 E8 08");
            //mov byte ptr[edx],al
            CaveMemory.Write_StrBytes("88 02");
            //mov eax,00000001
            CaveMemory.Write_StrBytes("B8 01 00 00 00");
            //ret
            CaveMemory.Write_StrBytes("C3");

            //Inject it
            CaveMemory.InjectToAddress(_AdjustedAxes_InjectionStruct, "Adjusted Axes");
        }

        /// <summary>
        /// At the end of amJvspAckSwInput(), removing the wanted buttons bit states from the source memory (Trigger, Reload, and Grenade)
        /// and changing the bits with custom values before memorycopy
        /// </summary>
        private void SetHack_Buttons()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess_MemoryBaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);

            //cmp esi,05
            CaveMemory.Write_StrBytes("83 FE 05");
            //je originalcode
            CaveMemory.Write_StrBytes("74 28");
            //and byte ptr [edx+esi+00000102],FC
            CaveMemory.Write_StrBytes("80 A4 32 02 01 00 00 FC");
            //and byte ptr [edx+esi+00000103],7F
            CaveMemory.Write_StrBytes("80 A4 32 03 01 00 00 7F");
            //movzx ecx,word ptr [esi+_Buttons_CaveAddress]
            CaveMemory.Write_StrBytes("0F B7 8E");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_Buttons_CaveAddress));
            //or [edx+esi+00000102],cl
            CaveMemory.Write_StrBytes("08 8C 32 02 01 00 00");
            //shr ecx,08
            CaveMemory.Write_StrBytes("C1 E9 08");
            //or [edx+esi+00000102],cl
            CaveMemory.Write_StrBytes("08 8C 32 03 01 00 00");
            //originalcode:
            //lea eax,[edx+esi+00000102]
            CaveMemory.Write_StrBytes("8D 84 32 02 01 00 00");

            //Inject it
            CaveMemory.InjectToAddress(_Buttons_InjectionStruct, "Buttons");
        }

        /// <summary>
        /// ramboD.elf has a switch to draw sight, but ramboM does not
        /// </summary>
        protected override void Apply_NoCrosshairMemoryHack()
        {
            if (_TargetProcess_Md5Hash.Equals(_KnownMd5Prints["ramboD.elf (SBQL)"]))
                WriteByte(_DrawSightFlag_Address, 0);
        }

        #endregion

        #region Inputs

        /// <summary>
        /// Writing Axis and Buttons data in memory
        /// </summary>
        public override void SendInput(PlayerSettings PlayerData)
        {
            if (PlayerData.ID == 1)
            {
                WriteByte(_JvsRawAxes_CaveAddress, (byte)PlayerData.RIController.Computed_X);
                WriteByte(_JvsRawAxes_CaveAddress + 0x02, (byte)PlayerData.RIController.Computed_Y);

                WriteByte(_AdjustedAxes_CaveAddress, (byte)PlayerData.RIController.Computed_X);
                WriteByte(_AdjustedAxes_CaveAddress + 1, (byte)PlayerData.RIController.Computed_Y);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 6, 0x02);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 6, 0xFD);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 7, 0x80);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 7, 0x7F);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OffScreenTriggerDown) != 0)
                {
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 6, 0x01);
                }
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OffScreenTriggerUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 6, 0xFE);
            }
            else if (PlayerData.ID == 2)
            {
                WriteByte(_JvsRawAxes_CaveAddress + 0x04, (byte)PlayerData.RIController.Computed_X);
                WriteByte(_JvsRawAxes_CaveAddress + 0x06, (byte)PlayerData.RIController.Computed_Y);

                WriteByte(_AdjustedAxes_CaveAddress + 2, (byte)PlayerData.RIController.Computed_X);
                WriteByte(_AdjustedAxes_CaveAddress + 3, (byte)PlayerData.RIController.Computed_Y);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 8, 0x02);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 8, 0xFD);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 9, 0x80);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 9, 0x7F);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OffScreenTriggerDown) != 0)
                {
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 8, 0x01);
                }
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OffScreenTriggerUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 8, 0xFE);
            }
        }

        #endregion

        #region Outputs

        /// <summary>
        /// Create the Output list that we will be looking for and forward to MameHooker
        /// </summary>
        protected override void CreateOutputList()
        {
            //Gun motor : Is activated for every bullet fired
            _Outputs = new List<GameOutput>();
            _Outputs.Add(new GameOutput(OutputId.P1_LmpStart));
            _Outputs.Add(new GameOutput(OutputId.P2_LmpStart));
            _Outputs.Add(new GameOutput(OutputId.P1_GunRecoil));
            _Outputs.Add(new GameOutput(OutputId.P2_GunRecoil));
            _Outputs.Add(new GameOutput(OutputId.P1_Ammo));
            _Outputs.Add(new GameOutput(OutputId.P2_Ammo));
            _Outputs.Add(new AsyncGameOutput(OutputId.P1_CtmRecoil, Configurator.GetInstance().OutputCustomRecoilOnDelay, Configurator.GetInstance().OutputCustomRecoilOffDelay, 0));
            _Outputs.Add(new AsyncGameOutput(OutputId.P2_CtmRecoil, Configurator.GetInstance().OutputCustomRecoilOnDelay, Configurator.GetInstance().OutputCustomRecoilOffDelay, 0));
            _Outputs.Add(new GameOutput(OutputId.P1_Life));
            _Outputs.Add(new GameOutput(OutputId.P2_Life));
            _Outputs.Add(new AsyncGameOutput(OutputId.P1_Damaged, Configurator.GetInstance().OutputCustomDamagedDelay, 100, 0));
            _Outputs.Add(new AsyncGameOutput(OutputId.P2_Damaged, Configurator.GetInstance().OutputCustomDamagedDelay, 100, 0));
            _Outputs.Add(new GameOutput(OutputId.Credits));
        }

        /// <summary>
        /// Update all Outputs values before sending them to MameHooker
        /// </summary>
        public override void UpdateOutputValues()
        {
            //Original Outputs
            UInt32 Outputs_Address = ReadPtr(_JvsMgrPtr_Address);
            SetOutputValue(OutputId.P1_LmpStart, ReadByte(Outputs_Address) & 0x01);
            SetOutputValue(OutputId.P2_LmpStart, ReadByte(Outputs_Address) >> 1 & 0x01);
            SetOutputValue(OutputId.P1_GunRecoil, ReadByte(Outputs_Address) >> 2 & 0x01);
            SetOutputValue(OutputId.P2_GunRecoil, ReadByte(Outputs_Address) >> 3 & 0x01);

            //Custom Outputs
            UInt32 PlayersPtr_BaseAddress = ReadPtr(_PlayerMgrPtr_Address);
            int P1_Ammo = 0;
            int P2_Ammo = 0;
            _P1_Life = 0;
            _P2_Life = 0;

            if (PlayersPtr_BaseAddress != 0)
            {
                UInt32 P1_StructAddress = ReadPtr(PlayersPtr_BaseAddress + 0x34);
                UInt32 P2_StructAddress = ReadPtr(PlayersPtr_BaseAddress + 0x38);
                //PlayerMode:
                //0: Standby
                //3: Game
                //4: Hold
                //A, C: Continue
                int P1_Status = ReadByte(P1_StructAddress + 0x38);
                int P2_Status = ReadByte(P2_StructAddress + 0x38);
                if (P1_Status == 3 || P1_Status == 4)
                {
                    _P1_Life = ReadByte(P1_StructAddress + 0x3C);
                    P1_Ammo = ReadByte(P1_StructAddress + 0x400);

                    //[Damaged] custom Output                
                    if (_P1_Life < _P1_LastLife)
                        SetOutputValue(OutputId.P1_Damaged, 1);
                }

                if (P2_Status == 3 || P2_Status == 4)
                {
                    _P2_Life = ReadByte(P2_StructAddress + 0x3C);
                    P2_Ammo = ReadByte(P2_StructAddress + 0x400);

                    //[Damaged] custom Output                
                    if (_P2_Life < _P2_LastLife)
                        SetOutputValue(OutputId.P2_Damaged, 1);
                }
            }
            _P1_LastLife = _P1_Life;
            _P2_LastLife = _P2_Life;

            SetOutputValue(OutputId.P1_Ammo, P1_Ammo);
            SetOutputValue(OutputId.P2_Ammo, P2_Ammo);
            //Custom recoil will be recoil just like the original one
            SetOutputValue(OutputId.P1_CtmRecoil, ReadByte(Outputs_Address) >> 2 & 0x01);
            SetOutputValue(OutputId.P2_CtmRecoil, ReadByte(Outputs_Address) >> 3 & 0x01);
            SetOutputValue(OutputId.P1_Life, _P1_Life);
            SetOutputValue(OutputId.P2_Life, _P2_Life);

            UInt32 CreditsMgr = ReadPtr(_CreditsMgrPtr_Address);
            SetOutputValue(OutputId.Credits, (int)(ReadByte(CreditsMgr + 0x38)));
        }

        #endregion
    }
}
