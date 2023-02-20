using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    class SolarArray
    {
        private string GroupName;
        private List<IMySolarPanel> Panels;
        private IMyMotorStator Rotor;
        private IMyBlockGroup Group;
        private IMyGridTerminalSystem Grid;
        private bool Valid = false;
        private Action<string> Echo;
        private float LastCurrentPower, MaxPowerAngle, MaxPower;
        private bool MaxPowerFound;
        private enum State
        {
            Stopped,
            Reset,
            SearchingForMax,
            GoingTowardsMax,
            Idle
        };

        private State CurrentState;

        public SolarArray(Program ParentProgram, string NewGroupName)
        {
            GroupName = NewGroupName;
            Grid = ParentProgram.GridTerminalSystem;
            Echo = ParentProgram.Echo;
            Group = Grid.GetBlockGroupWithName(GroupName);
            if (Group == null)
            {
                Echo("Group not found!");
                return;
            }

            Panels = new List<IMySolarPanel>();
            Group.GetBlocksOfType<IMySolarPanel>(Panels);
            if (Panels.Count == 0)
            {
                Echo("Panels not found");
                return;
            }
            Echo("Found " + Panels.Count + " panels");

            List<IMyMotorStator> RotorList = new List<IMyMotorStator>();
            Group.GetBlocksOfType<IMyMotorStator>(RotorList);
            if (RotorList.Count == 0)
            {
                Echo("Rotor not found");
                return;
            }
            Echo("Found rotor");
            Rotor = RotorList[0];
            Valid = true;

            Echo("Init ok");
            ChangeState(State.SearchingForMax);
            ShowStatus();
        }

        void ChangeState(State NewState)
        {
            switch (NewState)
            {
                case State.Stopped:
                    {
                        Rotor.RotorLock = true;
                        break;
                    }
                case State.Reset:
                    {
                        if (Rotor.Angle < Math.PI)
                            Rotor.TargetVelocityRPM = 1;
                        else
                            Rotor.TargetVelocityRPM = -1;
                        Rotor.RotorLock = false;
                        Rotor.UpperLimitRad = 0;
                        Rotor.LowerLimitRad = 0;
                        break;
                    }
                case State.SearchingForMax:
                    {
                        //Rotor.TargetVelocityRPM = 1;
                        Rotor.UpperLimitRad = float.MaxValue;
                        Rotor.LowerLimitRad = float.MinValue;
                        MaxPowerFound = false;
                        MaxPower = 0;
                        MaxPowerAngle = 0;
                        LastCurrentPower = 0;
                        Rotor.RotorLock = false;
                        break;
                    }
                case State.GoingTowardsMax:
                    {
                        Rotor.TargetVelocityRPM *= -1;
                        Rotor.LowerLimitRad = Rotor.UpperLimitRad = MaxPowerAngle;
                        break;
                    }
                case State.Idle:
                    {
                        Rotor.RotorLock = true;
                        break;
                    }
            }
            CurrentState = NewState;
        }

        public float GetSolarPanelPowerOutput(IMySolarPanel Panel)
        {
            var SolarReadout = Panel.DetailedInfo.Split('\n');
            for (int InfoIndex = 0; InfoIndex < SolarReadout.Length; ++InfoIndex)
            {
                var NameValue = SolarReadout[InfoIndex].Split(':');
                if (NameValue.Length != 2)
                {
                    continue;
                }

                string Name = NameValue[0].Trim();
                string Value = NameValue[1].Trim();

                if (Name == "Current Output")
                {
                    var NumberSplit = Value.Split(' ');
                    return Convert.ToSingle(NumberSplit[0]);
                }
            }
            Echo("Panel output not found!");
            return -1;
        }
        public float GetCurrentArrayPower()
        {
            float Power = 0;
            for (int SolarPanelIndex = 0; SolarPanelIndex < Panels.Count; ++SolarPanelIndex)
            {
                var SolarReadout = Panels[SolarPanelIndex].DetailedInfo.Split('\n');
                float PanelOutput = GetSolarPanelPowerOutput(Panels[SolarPanelIndex]);
                if (PanelOutput >= 0)
                {
                    Power += PanelOutput;
                }
                else
                {
                    ChangeState(State.Stopped);
                    return 0;
                }
            }
            return Power;
        }
        public void Update()
        {
            float CurrentAngle = Rotor.Angle;
            float CurrentPower = GetCurrentArrayPower();
            switch (CurrentState)
            {
                case State.Reset:
                    {
                        if (CurrentAngle == 0)
                        {
                            ChangeState(State.SearchingForMax);
                        }
                        break;
                    }
                case State.SearchingForMax: // Rotate in one direction to search for the max power output
                    {
                        if (CurrentPower > MaxPower)
                        {
                            MaxPowerFound = true;
                        }
                        if (CurrentPower < LastCurrentPower)
                        {
                            if (MaxPowerFound) // I've already passed the max power, so revert rotation direction
                            {
                                ChangeState(State.GoingTowardsMax);
                            }
                            else
                            {
                                Rotor.TargetVelocityRPM *= -1;
                            }
                            break;
                        }
                        break;
                    }
                case State.GoingTowardsMax:
                    {
                        if (Rotor.Angle == MaxPowerAngle)
                        {
                            MaxPower = CurrentPower;
                            ChangeState(State.Idle);
                        }
                        break;
                    }
                case State.Idle:
                    {
                        if (CurrentPower < MaxPower * 0.8) // If it's time to search for max power again...
                        {
                            ChangeState(State.SearchingForMax);
                        }
                        break;
                    }
            }
            LastCurrentPower = CurrentPower;

            if (CurrentPower > MaxPower)
            {
                MaxPower = CurrentPower;
                MaxPowerAngle = CurrentAngle;
            }
        }
        public void ShowStatus()
        {
            Echo("Name : " + GroupName);
            Echo("Current state : " + CurrentState.ToString());
            Echo("Current power : " + LastCurrentPower);
            Echo("Best power : " + MaxPower);
            Echo("Rotor angle : " + Rotor.Angle);
            Echo("Best rotor angle : " + MaxPowerAngle);
            Echo("Rotor speed : " + Rotor.TargetVelocityRPM);
        }

        public bool IsValid()
        {
            return Valid;
        }
    }

    partial class Program : MyGridProgram
    {
        List<SolarArray> SolarArrays;
        public Program()
        {
            SolarArrays = new List<SolarArray>();
            SolarArrays.Add(new SolarArray(this, "Solar Panel Array 1"));
            if (SolarArrays[0].IsValid() == false)
                return;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            for (int SolarArrayIndex = 0; SolarArrayIndex < SolarArrays.Count; ++SolarArrayIndex)
            {
                SolarArrays[SolarArrayIndex].Update();
                SolarArrays[SolarArrayIndex].ShowStatus();
            }
        }
    }
}
