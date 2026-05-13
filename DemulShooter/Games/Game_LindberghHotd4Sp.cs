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

namespace DemulShooter
{
    class Game_LindberghHotd4Sp : Game
    {
        private const String GAMEDATA_FOLDER = @"MemoryData\lindbergh\hotd4sp";

        //Inputs       
        private InjectionStruct _Axes_InjectionStruct = new InjectionStruct(0x08366CDC, 7);
        private InjectionStruct _AdjustedAxes_InjectionStruct = new InjectionStruct(0x08179DAC, 6);
        private InjectionStruct _Buttons_InjectionStruct = new InjectionStruct(0x08366AF5, 7);

        //Outputs Address
        private UInt32 _Outputs_Address = 0x084C35CB;
        private UInt32 _Outputs2_Address = 0x0A69F8BC;
        private UInt32 _GameInfos_Address = 0x0A69F92C;
        private UInt32 _Credits_Address = 0x0A6AEFA4;     //also 0x0A6C2E40 

        //Custom Data
        private UInt32 _JvsRawAxes_CaveAddress;
        private UInt32 _AdjustedAxes_CaveAddress;
        private UInt32 _Buttons_CaveAddress;

        //Rom loaded + Rom version check
        private UInt32 _RomLoaded_Check_Address = 0x080505A2;

        private int _LastLife = 0;
        private int _Life = 0;

        /// <summary>
        /// Constructor
        /// </summary>
        public Game_LindberghHotd4Sp(String RomName)
            : base(RomName, "linuxloader")
        {
            _KnownMd5Prints.Add("House of The Dead 4 Special (SBLS) (Rev.B)", "74baffc7429973f3df8902a31c5164a0");

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
                                if (!FindGameWindow_Contains("TeknoBudgie"))
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
                            if (buffer.SequenceEqual(new byte[] { 0x0D, 0x80, 0x00 }))
                            {
                                Logger.WriteLog("House Of The Dead 4 Special - Rev. B binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["House of The Dead 4 Special (SBLS) (Rev.B)"];
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

            SetHack_Axes();

            //Hacking into the GetAdjustPos() disable gun firing or misplace axis somewhere ?
            //For now the default calibration is good enough to not use this, and the game still has bugs....be back at it later
            //SetHack_AdjustedAxes();

            SetHack_Buttons();

            Logger.WriteLog("Inputs Memory Hack complete !");
            Logger.WriteLog("-");
        }

        /// <summary>
        /// At the end of amJvspAckAnalogInput(), replacing Axis value before memory copy
        /// </summary>
        private void SetHack_Axes()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess_MemoryBaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);

            //lea ecx,[eax-B]
            CaveMemory.Write_StrBytes("8D 48 F5");
            //movzx ecx,word ptr [ecx+_Axes_CaveAddress]
            CaveMemory.Write_StrBytes("0F B6 89");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_JvsRawAxes_CaveAddress));
            //lea eax,[eax+esi+00000102]
            CaveMemory.Write_StrBytes("8D 84 30 02 01 00 00");
            //mov [eax],cx
            CaveMemory.Write_StrBytes("66 89 08");

            //Inject it
            CaveMemory.InjectToAddress(_Axes_InjectionStruct, "Axes");
        }

        /// <summary>
        /// Replacing GetAdjstGunPos() content
        /// </summary>
        private void SetHack_AdjustedAxes()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess_MemoryBaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);

            //mov ebx,eax
            CaveMemory.Write_StrBytes("8B D8");
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
            CaveMemory.Write_StrBytes("74 0F");
            //mov [ebx],000000FF
            CaveMemory.Write_StrBytes("C7 03 FF 00 00 00");
            //mov [edx],000000FF
            CaveMemory.Write_StrBytes("C7 02 FF 00 00 00");
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
            //mov [ebx],00000000
            CaveMemory.Write_StrBytes("C7 03 00 00 00 00");
            //mov [edx],00000000
            CaveMemory.Write_StrBytes("C7 02 00 00 00 00");
            //mov [ebx],al
            CaveMemory.Write_StrBytes("88 03");
            //shr eax, 8
            CaveMemory.Write_StrBytes("C1 E8 08");
            //mov [edx],al
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

            //cmp ebx,05
            CaveMemory.Write_StrBytes("83 FB 05");
            //je originalcode
            CaveMemory.Write_StrBytes("74 28");
            //and byte ptr [edx+ebx+00000102],
            CaveMemory.Write_StrBytes("80 A4 1A 02 01 00 00 FC");
            //and byte ptr [edx+ebx+00000103],7F
            CaveMemory.Write_StrBytes("80 A4 1A 03 01 00 00 7F");
            //movzx ecx,word ptr [ebx+_Buttons_CaveAddress]
            CaveMemory.Write_StrBytes("0F B7 8B");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_Buttons_CaveAddress));
            //or [edx+ebx+00000102],cl
            CaveMemory.Write_StrBytes("08 8C 1A 02 01 00 00");
            //shr ecx,08
            CaveMemory.Write_StrBytes("C1 E9 08");
            //or [edx+ebx+00000103],cl
            CaveMemory.Write_StrBytes("08 8C 1A 03 01 00 00");
            //originalcode:
            //lea eax,[edx+ebx+00000102]
            CaveMemory.Write_StrBytes("8D 84 1A 02 01 00 00");

            //Inject it
            CaveMemory.InjectToAddress(_Buttons_InjectionStruct, "Buttons");
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
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 6, 0x01);
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
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 8, 0x01);
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
            //Gun motor : Is activated permanently while trigger is pressed
            _Outputs = new List<GameOutput>();
            _Outputs.Add(new GameOutput(OutputId.P1_LmpStart));
            _Outputs.Add(new GameOutput(OutputId.P1_GunMotor));
            _Outputs.Add(new GameOutput(OutputId.P2_GunMotor));
            _Outputs.Add(new GameOutput(OutputId.P1_AirFront));
            _Outputs.Add(new GameOutput(OutputId.P1_AirRear));
            _Outputs.Add(new GameOutput(OutputId.P2_AirFront));
            _Outputs.Add(new GameOutput(OutputId.P2_AirRear));
            _Outputs.Add(new GameOutput(OutputId.LmpStop));
            _Outputs.Add(new GameOutput(OutputId.LmpAction));
            _Outputs.Add(new GameOutput(OutputId.LmpReset));
            _Outputs.Add(new GameOutput(OutputId.LmpError));
            _Outputs.Add(new GameOutput(OutputId.LmpSafety));
            _Outputs.Add(new GameOutput(OutputId.LmpFloor));
            _Outputs.Add(new GameOutput(OutputId.LmpSpot));
            _Outputs.Add(new GameOutput(OutputId.P1_Ammo));
            _Outputs.Add(new GameOutput(OutputId.P2_Ammo));
            _Outputs.Add(new AsyncGameOutput(OutputId.P1_CtmRecoil, Configurator.GetInstance().OutputCustomRecoilOnDelay, Configurator.GetInstance().OutputCustomRecoilOffDelay, 0));
            _Outputs.Add(new AsyncGameOutput(OutputId.P2_CtmRecoil, Configurator.GetInstance().OutputCustomRecoilOnDelay, Configurator.GetInstance().OutputCustomRecoilOffDelay, 0));
            _Outputs.Add(new GameOutput(OutputId.P1_Life));
            _Outputs.Add(new AsyncGameOutput(OutputId.P1_Damaged, Configurator.GetInstance().OutputCustomDamagedDelay, 100, 0));
            _Outputs.Add(new GameOutput(OutputId.Credits));
        }

        /// <summary>
        /// Update all Outputs values before sending them to MameHooker
        /// </summary>
        public override void UpdateOutputValues()
        {
            //Original Outputs
            int P1_Motor_Status = ReadByte(_Outputs_Address) >> 4 & 0x01;
            int P2_Motor_Status = ReadByte(_Outputs_Address) >> 5 & 0x01;

            //SetOutputValue(OutputId.P1_LmpStart, ReadByte(_Outputs_Address) >> 7 & 0x01);
            SetOutputValue(OutputId.P1_GunMotor, P1_Motor_Status);
            SetOutputValue(OutputId.P2_GunMotor, P2_Motor_Status);
            SetOutputValue(OutputId.P1_AirFront, ReadByte(_Outputs_Address) & 0x01);
            SetOutputValue(OutputId.P2_AirFront, ReadByte(_Outputs_Address) >> 1 & 0x01);
            SetOutputValue(OutputId.P1_AirRear, ReadByte(_Outputs_Address) >> 2 & 0x01);
            SetOutputValue(OutputId.P2_AirRear, ReadByte(_Outputs_Address) >> 3 & 0x01);
            SetOutputValue(OutputId.LmpStop, ReadByte(_Outputs_Address + 2) >> 3 & 0x01);
            SetOutputValue(OutputId.LmpReset, ReadByte(_Outputs_Address + 2) & 0x01);
            SetOutputValue(OutputId.LmpError, ReadByte(_Outputs_Address + 2) >> 1 & 0x01);
            SetOutputValue(OutputId.LmpSafety, ReadByte(_Outputs_Address + 2) >> 2 & 0x01);
            SetOutputValue(OutputId.LmpFloor, ReadByte(_Outputs_Address + 2) >> 4 & 0x01);
            SetOutputValue(OutputId.LmpSpot, ReadByte(_Outputs_Address + 2) >> 5 & 0x01);
            //Billboard : 0x80=RED, 0x81=GREEN, 0x82=BLUE
            int BillBoardOn = ReadByte(_Outputs_Address + 1) >> 7 & 0x01;
            if (BillBoardOn != 0)
                SetOutputValue(OutputId.LmpBillboard, ReadByte(_Outputs_Address + 1));
            else
                SetOutputValue(OutputId.LmpBillboard, 0);

            //Start and Action Buttons are on another memory location
            Byte bOutputs2 = ReadByte(ReadPtr(_Outputs2_Address));
            SetOutputValue(OutputId.LmpAction, bOutputs2 >> 4 & 0x01);
            SetOutputValue(OutputId.P1_LmpStart, bOutputs2 >> 7 & 0x01);

            //Custom Outputs
            _Life = 0;
            _P1_Ammo = 0;
            _P2_Ammo = 0;
            int P1_Clip = 0;
            int P2_Clip = 0;
            if (ReadPtr(_GameInfos_Address) != 0)
            {
                _Life = ReadByte(ReadPtr(_GameInfos_Address) + 0x40);
                //[Damaged] custom Output                
                if (_Life < _LastLife)
                    SetOutputValue(OutputId.P1_Damaged, 1);

                _P1_Ammo = ReadByte(ReadPtrChain(_GameInfos_Address, new UInt32[] { 0x34 }) + 0x2D4);

                //No attract mode so we can just reuse the original recoil flag
                SetOutputValue(OutputId.P1_CtmRecoil, P1_Motor_Status);

                _P2_Ammo = ReadByte(ReadPtrChain(_GameInfos_Address, new UInt32[] { 0x38 }) + 0x2D4);

                //No attract mode so we can just reuse the original recoil flag
                SetOutputValue(OutputId.P2_CtmRecoil, P2_Motor_Status);
            }

            _LastLife = _Life;
            _P1_LastAmmo = _P1_Ammo;
            _P2_LastAmmo = _P2_Ammo;

            SetOutputValue(OutputId.P1_Ammo, _P1_Ammo);
            SetOutputValue(OutputId.P2_Ammo, _P2_Ammo);
            SetOutputValue(OutputId.P1_Clip, P1_Clip);
            SetOutputValue(OutputId.P2_Clip, P2_Clip);
            SetOutputValue(OutputId.P1_Life, _Life);
            SetOutputValue(OutputId.Credits, ReadByte(_Credits_Address));
        }

        #endregion
    }
}
