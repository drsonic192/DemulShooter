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
    class Game_LindberghLgj : Game
    {
        private const String GAMEDATA_FOLDER = @"MemoryData\lindbergh\lgj";

        //Inputs
        private InjectionStruct _JvsRawAxes_InjectionStruct = new InjectionStruct(0x084E8CDC, 7);
        private InjectionStruct _Buttons_InjectionStruct = new InjectionStruct(0x084E8AF5, 7);

        //Game is using complex calibration procedure, overriding float values after this :
        private NopStruct _Nop_AdjustedAxis_X = new NopStruct(0x080A9AD2, 8);
        private NopStruct _Nop_AdjustedAxis_Y = new NopStruct(0x080A97FE, 8);
        //INPUT_STRUCT offset in game
        private UInt32 _Player1_InputPtr_Address = 0x087D3BB0;
        private UInt32 _Player2_InputPtr_Address = 0x087D3BAC;
        private const UInt32 INPUT_X_OFFSET = 0x134;
        private const UInt32 INPUT_Y_OFFSET = 0x138;

        private UInt32 _NoCrosshair_Patch_Address = 0x080B0D06;

        //Outputs
        private UInt32 _JvsOutput_Address = 0x087D186D;
        private UInt32 _Credits_Address = 0x08C08420;
        private UInt32 _PlayersStatus_Address = 0x87D3DC0;
        private InjectionStruct _Recoil_InjectionStruct = new InjectionStruct(0x080AFA1C, 6);

        //Custom Data
        private UInt32 _RawJvsAxes_CaveAddress;
        private UInt32 _Buttons_CaveAddress;
        private UInt32 _CustomRecoil_CaveAddress = 0;


        //Check instruction for game loaded
        private UInt32 _RomLoaded_Check_Address = 0x0807925B;

        /// <summary>
        /// Constructor
        /// </summary>
        public Game_LindberghLgj(String RomName)
            : base(RomName, "linuxloader")
        {
            _KnownMd5Prints.Add("Let's Go Jungle (SBLC)", "37b0239d4fbc6256c65dd70beb087a34");
            _KnownMd5Prints.Add("Let's Go Jungle (SBLC) (Rev.A)", "875486650a16abce5b1901fd319001c0");

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
                            if (buffer.SequenceEqual(new byte[] { 0x8B, 0x1D, 0xB0 }))
                            {
                                Logger.WriteLog("Let's Go Jungle! binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["Let's Go Jungle (SBLC)"];
                            }
                            else if (buffer.SequenceEqual(new byte[] { 0x00, 0x00, 0x8B }))
                            {
                                Logger.WriteLog("Let's Go Jungle! - Rev .A binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["Let's Go Jungle (SBLC) (Rev.A)"];
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
                catch (Exception Ex)
                {
                    Logger.WriteLog("Error trying to hook " + _Target_Process_Name + ".exe");
                    Logger.WriteLog(Ex.Message.ToString());
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

                    PlayerData.RIController.Computed_X = Convert.ToInt16(Math.Round(dMaxX - dMaxX * PlayerData.RIController.Computed_X / TotalResX));
                    PlayerData.RIController.Computed_Y = Convert.ToInt16(Math.Round(dMaxY - dMaxY * PlayerData.RIController.Computed_Y / TotalResY));
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
            _RawJvsAxes_CaveAddress = _InputsDatabank_Address;
            _Buttons_CaveAddress = _InputsDatabank_Address + 0x10;

            SetHack_Axes();
            SetHack_Buttons();

            // Noping X,Y axis values update in the following procedure, after adjustment is made :
            // acpPlayer::input()
            SetNops(0, _Nop_AdjustedAxis_X);
            SetNops(0, _Nop_AdjustedAxis_Y);
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
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_RawJvsAxes_CaveAddress));
            //lea eax,[eax+esi+00000102]
            CaveMemory.Write_StrBytes("8D 84 30 02 01 00 00");
            //mov [eax],cx
            CaveMemory.Write_StrBytes("66 89 08");

            //Inject it
            CaveMemory.InjectToAddress(_JvsRawAxes_InjectionStruct, "Axes");
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

        //Original game is simply setting a Motor to vibrate, so simply using this data to create or pulsed custom recoil will not be synchronized with bullets shot
        //as the pulses lenght and spaceing will depend on DemulShooter output pulse config data.
        //To synch recoil pulse with projectiles, this hack allows to intercept the code increasing the "PlayerShotCounter" variable
        protected override void Apply_OutputsMemoryHack()
        {
            //Create Databak to store our value
            Create_OutputsDataBank();
            _CustomRecoil_CaveAddress = _OutputsDatabank_Address;

            SetHack_Recoil();

            Logger.WriteLog("Outputs Memory Hack complete !");
            Logger.WriteLog("-");
        }

        /// <summary>
        /// Intercepting the bullet count increase call to create our own recoil
        /// </summary>
        private void SetHack_Recoil()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess.MainModule.BaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);
            List<Byte> Buffer = new List<Byte>();

            //lea edx,[edi+01]
            CaveMemory.Write_StrBytes("8D 57 01");
            //mov [esi+14],edi
            CaveMemory.Write_StrBytes("89 7E 14");
            //push eax
            CaveMemory.Write_StrBytes("50");
            //mov    eax,DWORD PTR [ebx+0x118] 
            CaveMemory.Write_StrBytes("8B 83 18 01 00 00");
            //mov byte ptr [eax+_CustomRecoil_CaveAddress],01
            CaveMemory.Write_StrBytes("C6 80");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_CustomRecoil_CaveAddress));
            CaveMemory.Write_StrBytes("01");
            //pop eax
            CaveMemory.Write_StrBytes("58");            

            //Inject it
            CaveMemory.InjectToAddress(_Recoil_InjectionStruct, "Recoil");
        }

        /// <summary>
        /// acpPlayer::calc() calls AbkTrunk::StartBranch() a 2 different places, depending on the crosshair displayed (no shoot / shoot)
        /// Changing the last parameter from 1 to 0 when the needed cursor is "shoot" will mask it
        /// </summary>
        protected override void Apply_NoCrosshairMemoryHack()
        {
            WriteByte(_NoCrosshair_Patch_Address, 0x00);
        }

        #endregion

        #region Inputs

        /// <summary>
        /// Writing Axis and Buttons data in memory
        /// </summary>
        public override void SendInput(PlayerSettings PlayerData)
        {
            float X_Value = 1.0f -  (2.0f * (float)PlayerData.RIController.Computed_X / 255.0f);
            float Y_Value = (2.0f * (float)PlayerData.RIController.Computed_Y / 255.0f) - 1.0f;

            if (PlayerData.ID == 1)
            {
                WriteByte(_RawJvsAxes_CaveAddress, (byte)PlayerData.RIController.Computed_Y);
                WriteByte(_RawJvsAxes_CaveAddress + 0x02, (byte)PlayerData.RIController.Computed_X);

                WriteBytes(ReadPtr(_Player1_InputPtr_Address) + INPUT_X_OFFSET, BitConverter.GetBytes(X_Value));
                WriteBytes(ReadPtr(_Player1_InputPtr_Address) + INPUT_Y_OFFSET, BitConverter.GetBytes(Y_Value));

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 6, 0x02);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 6, 0xFD);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 6, 0x02);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 6, 0xFD);
            }
            else if (PlayerData.ID == 2)
            {
                WriteByte(_RawJvsAxes_CaveAddress + 0x04, (byte)PlayerData.RIController.Computed_Y);
                WriteByte(_RawJvsAxes_CaveAddress + 0x06, (byte)PlayerData.RIController.Computed_X);

                WriteBytes(ReadPtr(_Player2_InputPtr_Address) + INPUT_X_OFFSET, BitConverter.GetBytes(X_Value));
                WriteBytes(ReadPtr(_Player2_InputPtr_Address) + INPUT_Y_OFFSET, BitConverter.GetBytes(Y_Value));

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 8, 0x02);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.OnScreenTriggerUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 8, 0xFD);

                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionDown) != 0)
                    Apply_OR_ByteMask(_Buttons_CaveAddress + 8, 0x02);
                if ((PlayerData.RIController.Computed_Buttons & RawInputcontrollerButtonEvent.ActionUp) != 0)
                    Apply_AND_ByteMask(_Buttons_CaveAddress + 8, 0xFD);
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
            _Outputs.Add(new GameOutput(OutputId.P2_LmpStart));
            _Outputs.Add(new GameOutput(OutputId.LmpRoom));
            _Outputs.Add(new GameOutput(OutputId.LmpCoin));
            _Outputs.Add(new GameOutput(OutputId.P1_GunMotor));
            _Outputs.Add(new GameOutput(OutputId.P2_GunMotor));
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
            SetOutputValue(OutputId.P1_LmpStart, ReadByte(_JvsOutput_Address) >> 7 & 0x01);
            SetOutputValue(OutputId.P2_LmpStart, ReadByte(_JvsOutput_Address) >> 4 & 0x01);
            SetOutputValue(OutputId.LmpRoom, ReadByte(_JvsOutput_Address) >> 5 & 0x01);
            SetOutputValue(OutputId.LmpCoin, ReadByte(_JvsOutput_Address) >> 2 & 0x01);
            SetOutputValue(OutputId.P1_GunMotor, ReadByte(_JvsOutput_Address) >> 3 & 0x01);
            SetOutputValue(OutputId.P2_GunMotor, ReadByte(_JvsOutput_Address) >> 6 & 0x01);

            //Custom Outputs
            UInt32 P1_StructAddress = ReadPtr(_Player1_InputPtr_Address);
            UInt32 P2_StructAddress = ReadPtr(_Player2_InputPtr_Address);

            _P1_Life = 0;
            _P2_Life = 0;
            //Checking this byte (player playing ?) otherwise the game is still enabing rumble during Attract Mode
            if (ReadByte(_PlayersStatus_Address) == 1)
            {
                if (P1_StructAddress != 0)
                {
                    _P1_Life = (int)BitConverter.ToSingle(ReadBytes(P1_StructAddress + 0x4BC, 4), 0);
                    //[Damaged] custom Output                
                    if (_P1_Life < _P1_LastLife)
                        SetOutputValue(OutputId.P1_Damaged, 1);

                    if (ReadByte(_CustomRecoil_CaveAddress) == 1 && (ReadByte(_JvsOutput_Address) >> 3 & 0x01) == 1)
                    {
                        SetOutputValue(OutputId.P1_CtmRecoil, 1);
                        WriteByte(_CustomRecoil_CaveAddress, 0);
                    }

                }
            }

            //Checking this byte (player playing ?) otherwise the game is still enabing rumble during Attract Mode
            if (ReadByte(_PlayersStatus_Address + 4) == 1)
            {
                if (P2_StructAddress != 0)
                {
                    _P2_Life = (int)BitConverter.ToSingle(ReadBytes(P2_StructAddress + 0x4BC, 4), 0);
                    //[Damaged] custom Output                
                    if (_P2_Life < _P2_LastLife)
                        SetOutputValue(OutputId.P2_Damaged, 1);

                    if (ReadByte(_CustomRecoil_CaveAddress + 1) == 1 && (ReadByte(_JvsOutput_Address) >> 6 & 0x01) == 1)
                    {
                        SetOutputValue(OutputId.P2_CtmRecoil, 1);
                        WriteByte(_CustomRecoil_CaveAddress + 1, 0);
                    }
                }
            }

            _P1_LastLife = _P1_Life;
            _P2_LastLife = _P2_Life;
            SetOutputValue(OutputId.P1_Life, _P1_Life);
            SetOutputValue(OutputId.P2_Life, _P2_Life);
            SetOutputValue(OutputId.Credits, ReadByte(_Credits_Address));
        }

        #endregion
    }
}
