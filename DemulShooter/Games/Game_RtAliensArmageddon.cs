using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using DsCore;
using DsCore.Config;
using DsCore.MameOutput;
using DsCore.Memory;

namespace DemulShooter
{
    class Game_RtAliensArmageddon : Game
    {
        //Rom loaded + Rom version check
        private UInt32 _RomLoaded_check_Instruction_v390 = 0x08088211;

        private InjectionStruct _P1_NoCrosshair_InjectionStruct = new InjectionStruct(0x0807109C, 6);
        private InjectionStruct _P2_NoCrosshair_InjectionStruct = new InjectionStruct(0x0807111A, 6);

        //Outputs Address
        private UInt32 _P1_Struct_Address = 0x08DAB920;
        private UInt32 _P2_Struct_Address = 0x08DABAF0;
        private UInt32 _Lamp_Address = 0x8ECEC5C;
        private UInt32 _RecoilStatus_CaveAddress;
        private InjectionStruct _Recoil_InjectionStruct = new InjectionStruct(0x080883B5, 11);

        private int _P1_Last_Weapon = 0;
        private int _P2_Last_Weapon = 0;

        /// <summary>
        /// Constructor
        /// </summary>
        public Game_RtAliensArmageddon(String RomName)
            : base(RomName, "BudgieLoader")
        {
            _KnownMd5Prints.Add("Aliens Armageddon - 03.90 USA", "fe95d8a34331b95d14f788220e6b8fed");

            _tProcess.Start();
            Logger.WriteLog("Waiting for Raw Thrill " + _RomName + " game to hook.....");
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
                            //To make sure BurgieLoader has loaded the rom entirely, we're looking for some random instruction to be present in memory before starting                            
                            byte[] buffer = ReadBytes(_RomLoaded_check_Instruction_v390, 3);
                            if (buffer[0] == 0x83 && buffer[1] == 0xEA && buffer[2] == 0x01)
                            {
                                _GameWindowHandle = _TargetProcess.MainWindowHandle;
                                Logger.WriteLog("Aliens Arageddon - 03.90 USA binary detected");
                                _TargetProcess_Md5Hash = _KnownMd5Prints["Aliens Armageddon - 03.90 USA"];
                                Logger.WriteLog("Attached to Process " + _Target_Process_Name + ".exe, ProcessHandle = " + _ProcessHandle);
                                Logger.WriteLog(_Target_Process_Name + ".exe = 0x" + _TargetProcess_MemoryBaseAddress.ToString("X8"));
                                Apply_MemoryHacks();
                                _ProcessHooked = true;
                                RaiseGameHookedEvent();
                            }
                            else
                            {
                                Logger.WriteLog("Game not Loaded, waiting...");
                            }
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

        #region Memory Hack

        /// <summary>
        /// At that place, game acknoledged a gun0 or gun1 fired
        /// [ESP+4D] has a pointer to the struct containing the weapon's name, so it's possible to exclude flamethrower from custom recoil
        /// Names can be assault_rifle, shotgun, minigun, turret, fthrower, sniper, gunpod_2, launcher
        /// </summary>
        protected override void Apply_OutputsMemoryHack()
        {
            Create_OutputsDataBank();
            _RecoilStatus_CaveAddress = _OutputsDatabank_Address;

            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess.MainModule.BaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);

            //mov eax,[esp+000000D4]
            CaveMemory.Write_StrBytes("8B 84 24 D4 00 00 00");
            //add eax,04
            CaveMemory.Write_StrBytes("83 C0 04");
            //cmp [eax],72687466
            CaveMemory.Write_StrBytes("81 38 66 74 68 72"); //HEX equivalent of "fthr to exclude flamethrower (quicket than strcmp)
            //je originalcode
            CaveMemory.Write_StrBytes("74 07");
            //mov byte ptr [ebx+_RecoilStatus_CaveAddress],01
            CaveMemory.Write_StrBytes("C6 83");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_RecoilStatus_CaveAddress));   
            CaveMemory.Write_StrBytes("01");
            //Originalcode:
            //mov [esp+04],edi
            CaveMemory.Write_StrBytes("89 7C 24 04");
            //mov [esp],00000000
            CaveMemory.Write_StrBytes("C7 04 24 00 00 00 00");

            //Inject it
            CaveMemory.InjectToAddress(_Recoil_InjectionStruct, "Recoil");

        }

        /// <summary>
        /// To remove crosshair, we will change the cursor drawinf location to -1.0 to hide it.
        /// Small filtering added : looking at the player "playing" flag so that crosshair will still be visible on TEST menu
        /// </summary>
        protected override void Apply_NoCrosshairMemoryHack()
        {
            Apply_P1NoCrosshair_Hack();
            Apply_P2NoCrosshair_Hack();
        }

        private void Apply_P1NoCrosshair_Hack()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess.MainModule.BaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);
            //fstp dword ptr [0890B2B0]
            CaveMemory.Write_StrBytes("D9 1D B0 B2 90 08");
            //cmp byte ptr [08DAB921],01
            CaveMemory.Write_StrBytes("80 3D");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_P1_Struct_Address + 1));
            CaveMemory.Write_StrBytes("01");
            //jne exit
            CaveMemory.Write_StrBytes("75 0A");
            //mov [0890B2B0],BF800000
            CaveMemory.Write_StrBytes("C7 05 B0 B2 90 08 00 00 80 BF");

            //Inject it
            CaveMemory.InjectToAddress(_P1_NoCrosshair_InjectionStruct, "P1 NoCrosshair");
        }

        private void Apply_P2NoCrosshair_Hack()
        {
            Codecave CaveMemory = new Codecave(_TargetProcess, _TargetProcess.MainModule.BaseAddress);
            CaveMemory.Open();
            CaveMemory.Alloc(0x800);
            //fstp dword ptr [0890B2B4]
            CaveMemory.Write_StrBytes("D9 1D B4 B2 90 08");
            //cmp byte ptr [0x08DABAF1],01
            CaveMemory.Write_StrBytes("80 3D");
            CaveMemory.Write_Bytes(BitConverter.GetBytes(_P2_Struct_Address + 1));
            CaveMemory.Write_StrBytes("01");
            //jne exit
            CaveMemory.Write_StrBytes("75 0A");
            //mov [0890B2B4],BF800000
            CaveMemory.Write_StrBytes("C7 05 B4 B2 90 08 00 00 80 BF");

            //InjectIt
            CaveMemory.InjectToAddress(_P2_NoCrosshair_InjectionStruct, "P2 NoCrosshair");
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
            _Outputs.Add(new GameOutput(OutputId.P1_LmpHolder));
            _Outputs.Add(new GameOutput(OutputId.P1_LmpGun));
            _Outputs.Add(new GameOutput(OutputId.P1_LmpGunGrenadeBtn));
            _Outputs.Add(new GameOutput(OutputId.P1_LmpGunMolding));
            _Outputs.Add(new GameOutput(OutputId.P2_LmpHolder));
            _Outputs.Add(new GameOutput(OutputId.P2_LmpStart));
            _Outputs.Add(new GameOutput(OutputId.P2_LmpGun));
            _Outputs.Add(new GameOutput(OutputId.P2_LmpGunGrenadeBtn));
            _Outputs.Add(new GameOutput(OutputId.P2_LmpGunMolding));
            _Outputs.Add(new GameOutput(OutputId.LmpSpeaker));
            _Outputs.Add(new GameOutput(OutputId.LmpMarqueeBacklight));
            _Outputs.Add(new GameOutput(OutputId.LmpMarqueeUplight));
            _Outputs.Add(new GameOutput(OutputId.LmpUpperCtrlPanel));
            _Outputs.Add(new GameOutput(OutputId.LmpLowerCtrlPanel));
            _Outputs.Add(new GameOutput(OutputId.LmpBillboard));
            //In Teknoparrot, Guns hardware is not emulated, so the game is not running original gun recoil procedures 
            _Outputs.Add(new GameOutput(OutputId.P1_GunMotor));
            _Outputs.Add(new GameOutput(OutputId.P2_GunMotor));
            _Outputs.Add(new GameOutput(OutputId.P1_Ammo));
            _Outputs.Add(new GameOutput(OutputId.P2_Ammo));
            _Outputs.Add(new GameOutput(OutputId.P1_Clip));
            _Outputs.Add(new GameOutput(OutputId.P2_Clip));
            _Outputs.Add(new AsyncGameOutput(OutputId.P1_CtmRecoil, Configurator.GetInstance().OutputCustomRecoilOnDelay, Configurator.GetInstance().OutputCustomRecoilOffDelay, 0));
            _Outputs.Add(new AsyncGameOutput(OutputId.P2_CtmRecoil, Configurator.GetInstance().OutputCustomRecoilOnDelay, Configurator.GetInstance().OutputCustomRecoilOffDelay, 0));
            _Outputs.Add(new GameOutput(OutputId.P1_Life));
            _Outputs.Add(new GameOutput(OutputId.P2_Life));
            _Outputs.Add(new AsyncGameOutput(OutputId.P1_Damaged, Configurator.GetInstance().OutputCustomDamagedDelay, 100, 0));
            _Outputs.Add(new AsyncGameOutput(OutputId.P2_Damaged, Configurator.GetInstance().OutputCustomDamagedDelay, 100, 0));
            //_Outputs.Add(new GameOutput(OutputId.Credits));
        }

        /// <summary>
        /// Update all Outputs values before sending them to MameHooker
        /// </summary>
        public override void UpdateOutputValues()
        {
            //Original Outputs
            SetOutputValue(OutputId.P1_LmpStart, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x24, 4), 0));
            SetOutputValue(OutputId.P1_LmpHolder, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x3C, 4), 0));
            SetOutputValue(OutputId.P1_LmpGun, ReadByte(_Lamp_Address));
            SetOutputValue(OutputId.P1_LmpGunGrenadeBtn, ReadByte(_Lamp_Address + 0x10));
            SetOutputValue(OutputId.P1_LmpGunMolding, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x48, 4), 0));

            SetOutputValue(OutputId.P2_LmpStart, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x28, 4), 0));
            SetOutputValue(OutputId.P2_LmpHolder, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x40, 4), 0));
            SetOutputValue(OutputId.P2_LmpGun, ReadByte(_Lamp_Address + 0x0C));
            SetOutputValue(OutputId.P2_LmpGunGrenadeBtn, ReadByte(_Lamp_Address + 0x14));
            SetOutputValue(OutputId.P2_LmpGunMolding, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x4C, 4), 0));

            SetOutputValue(OutputId.LmpSpeaker, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x50, 4), 0));
            SetOutputValue(OutputId.LmpBillboard, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x38, 4), 0));
            SetOutputValue(OutputId.LmpMarqueeBacklight, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x34, 4), 0));
            SetOutputValue(OutputId.LmpMarqueeUplight, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x44, 4), 0));
            SetOutputValue(OutputId.LmpUpperCtrlPanel, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x2c, 4), 0));
            SetOutputValue(OutputId.LmpLowerCtrlPanel, BitConverter.ToInt32(ReadBytes(_Lamp_Address + 0x30, 4), 0));

            //Custom Outputs
            _P1_Life = 0;
            _P2_Life = 0;
            _P1_Ammo = 0;
            _P2_Ammo = 0;
            int P1_Clip = 0;
            int P2_Clip = 0;

            //Check if the Player is currently playing
            if (ReadByte(_P1_Struct_Address + 0x01) == 1)
            {
                _P1_Ammo = BitConverter.ToInt32(ReadBytes(_P1_Struct_Address + 0x68, 4), 0);
                if (_P1_Ammo < 0)
                    _P1_Ammo = 0;
                
                //[Clip] custom Output   
                if (_P1_Ammo > 0)
                    P1_Clip = 1;

                _P1_Life = BitConverter.ToInt32(ReadBytes(_P1_Struct_Address + 0x10, 4), 0);
                if (_P1_Life < 0)
                    _P1_Life = 0;

                //[Damaged] custom Output                
                if (_P1_Life < _P1_LastLife)
                    SetOutputValue(OutputId.P1_Damaged, 1);

                //Custom recoil by reading and resetting FLAG
                if (ReadByte(_RecoilStatus_CaveAddress) == 1)
                {
                    SetOutputValue(OutputId.P1_CtmRecoil, 1);
                    WriteByte(_RecoilStatus_CaveAddress, 0);
                }
            }

            //Check if the Player is currently playing
            if (ReadByte(_P2_Struct_Address + 0x01) == 1)
            {
                _P2_Ammo = BitConverter.ToInt32(ReadBytes(_P2_Struct_Address + 0x68, 4), 0);
                if (_P2_Ammo < 0)
                    _P2_Ammo = 0;

                //[Clip] custom Output   
                if (_P2_Ammo > 0)
                    P2_Clip = 1;

                _P2_Life = BitConverter.ToInt32(ReadBytes(_P2_Struct_Address + 0x10, 4), 0);
                if (_P2_Life < 0)
                    _P2_Life = 0;

                //[Damaged] custom Output                
                if (_P2_Life < _P2_LastLife)
                    SetOutputValue(OutputId.P2_Damaged, 1);

                //Custom recoil by reading and resetting FLAG
                if (ReadByte(_RecoilStatus_CaveAddress + 1) == 1)
                {
                    SetOutputValue(OutputId.P2_CtmRecoil, 1);
                    WriteByte(_RecoilStatus_CaveAddress + 1, 0);
                }
            }
            _P1_LastLife = _P1_Life;
            _P2_LastLife = _P2_Life;
            _P1_LastAmmo = _P1_Ammo;
            _P2_LastAmmo = _P2_Ammo;

            SetOutputValue(OutputId.P1_Ammo, _P1_Ammo);
            SetOutputValue(OutputId.P2_Ammo, _P2_Ammo);
            SetOutputValue(OutputId.P1_Clip, P1_Clip);
            SetOutputValue(OutputId.P2_Clip, P2_Clip);
            SetOutputValue(OutputId.P1_Life, _P1_Life);
            SetOutputValue(OutputId.P2_Life, _P2_Life);
            

            //Recoil : reading updated value from the game
            //SetOutputValue(OutputId.Credits, ReadByte(_Credits_Address));
        }

        #endregion

    }
}
