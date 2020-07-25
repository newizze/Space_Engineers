using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Game.EntityComponents;
using VRage.Game.Components;
using VRage.Collections;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
namespace DrillBotV2
{
    public sealed class Program : MyGridProgram
    {
        //Настройки
        double SafetyAltitude = 100;
        double DirectionX; // Координаты GPS по оси X
        double DirectionY; // Координаты GPS по оси Y
        double DirectionZ; // Координаты GPS по оси Z
        double DrillTill = 15; //Максимальная глубина бурения
        double DroneHeight = 10; //Высота дрона
        double MaxCountOfDrillPoint = 25; // Количество шахт для одного месторождения
        double StepWidth = 8; // Ширина шага бурения
        float DrillingSpeed = 0.1f;
        float MaximumTakesOffSpeed = 30; //Максимальная скорость взлета
        double AdjustKoef = 0.2; //Коэффициент тонкой подстройки скорости
        double SafePointDistance = 8; // Безопасная зона у коннектора базы
        double SpeedMultiplier = 3; // Множитель скорости при загруженном дроне
        double AdditionalAlt = 1; //Добавочная высота для коннектора при загруженном дроне
        bool WeakShip = false; //Поставить true в случае слабых двигателей


        //Глобальные переменные
        IMyShipController Kok;
        IMyCargoContainer Cargo;
        IMyShipConnector Conn;
        IMyTextPanel DOB1;
        IMyTextPanel DOB2;
        IMyTextPanel DOB3;

        Program.ThrusterMovement ThrustersController;

        Vector3D TargetMinePoint; // Вектор координат текущей шахты
        Vector3D InitialDrillPoint; //Первичная точка бурения
        Vector3D InitialDrillDirection; //Первичное направление бурения
        Vector3D CurrentPoint;
        Vector3D Waypoint;

        double CurrentAltitude;
        double DrillPoint = 0;
        double StepsPerLine = 1;
        double CurrentLine = 0;
        double SignCount = 0;
        double CargoFullness;
        double LastAltBeforeHome = 0;

        string MyGlobalStorage;
        string NameOfMiningPoint;

        enum TaskStatuses { Default, GoToPoint, Adjustment, LandingForDrill, Drilling, GoToBase, Test, Disconnect, Connect, Unloading, ToNextPoint, ToNextPointStage2 };
        enum StepDirs { Left, Right, Up, Down };

        TaskStatuses TaskStatus = TaskStatuses.Default;
        StepDirs StepDir = StepDirs.Up;

        bool IsActiveTask = false;
        bool IsInitialDrillVectorSaved = false;

        List<IMyGyro> Gyros;
        List<IMyThrust> Thrusters;

        public Program()
        {
            //Инициализация главных блоков
            Kok = GridTerminalSystem.GetBlockWithName("Kok [LCD]") as IMyShipController;
            Cargo = GridTerminalSystem.GetBlockWithName("Cargo") as IMyCargoContainer;
            Conn = GridTerminalSystem.GetBlockWithName("Conn") as IMyShipConnector;

            DOB1 = GridTerminalSystem.GetBlockWithName("DOB1") as IMyTextPanel;
            DOB2 = GridTerminalSystem.GetBlockWithName("DOB2") as IMyTextPanel;
            DOB3 = GridTerminalSystem.GetBlockWithName("DOB3") as IMyTextPanel;
            DOB1.WriteText("");
            DOB2.WriteText("");
            DOB3.WriteText("");

            Gyros = new List<IMyGyro>();
            Thrusters = new List<IMyThrust>();

            GridTerminalSystem.GetBlocksOfType<IMyGyro>(Gyros);
            GridTerminalSystem.GetBlocksOfType<IMyThrust>(Thrusters);

            //Направление движение трастеров
            ThrustersController = new Program.ThrusterMovement(0, 0, 0, 0, 0, 0, Thrusters, Kok, this);

            MyGlobalStorage = Me.CustomData;

            var Data = MyGlobalStorage.Split(';');

            if (Data.Length > 0)
            {
                Vector3D.TryParse(Data[0], out InitialDrillDirection);
            }

            if (GetCurrentPoint().Length() > 0)
            {
                CurrentPoint = GetCurrentPoint();
                CurrentLine = GetCurrentLine();
                StepsPerLine = GetStepsPerLine();
                StepDir = GetStepDir();
                SignCount = GetSignCount();
                DrillPoint = GetDrillPoint();
            }

            string DataToSave = CurrentPoint.ToString() + ";" + CurrentLine.ToString() + ";" + StepsPerLine.ToString() + ";" + StepDir.ToString() + ";" + SignCount.ToString();

            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        void TryToGetCoords()
        {
            if (DOB1.CustomData.Length > 0)
            {
                var CoordsTemp = DOB1.CustomData.Split(':');
                if (CoordsTemp.Length > 0)
                {
                    NameOfMiningPoint = CoordsTemp[1];
                    Double.TryParse(CoordsTemp[2], out DirectionX);
                    Double.TryParse(CoordsTemp[3], out DirectionY);
                    Double.TryParse(CoordsTemp[4], out DirectionZ);
                    TargetMinePoint = new Vector3D(DirectionX, DirectionY, DirectionZ);
                    Log("Coords uploaded", false, 2, 1);
                }
            } else Log("No Coords", false, 2, 1);
        }

        void Main(string args)
        {
            if (TargetMinePoint.Length() == 0) TryToGetCoords();

            //Текущая высота
            Kok.TryGetPlanetElevation(MyPlanetElevation.Surface, out CurrentAltitude);

            CargoFullness = Math.Round((float)Cargo.GetInventory().CurrentVolume / (float)Cargo.GetInventory().MaxVolume * 100);

            Log("FW Speed: " + ThrustersController.GetHorizontalSpeed().ToString(), false, 2, 3);
            Log("SD Speed: " + ThrustersController.GetSideSpeed().ToString(), true, 2, 3);

            if(TargetMinePoint.Length() > 0)
            {
                if (args == "Start")
                {
                    IsActiveTask = true;
                    TaskStatus = TaskStatuses.GoToPoint;
                }
                else if (args == "Stop")
                {   
                    IsActiveTask = false;
                    StopAllActivities();
                    TaskStatus = TaskStatuses.Default;
                }
                else if (args == "SaveConn")
                {
                    if (Conn.Status == MyShipConnectorStatus.Connected)
                    {
                        Log("STATUS: Position saved", false, 2, 1);
                        SaveConnectorPosition();

                    }
                    else Log("STATUS: Disconnected", false, 2, 1); ;
                }
                else if (args == "Test")
                {
                    IsActiveTask = true;
                    TaskStatus = TaskStatuses.Test;

                }
            }

            if (IsActiveTask)
            {
                Log("TASK: Active", false, 2, 2);
                Log("Point: " + NameOfMiningPoint, true, 2, 2);
                Start();
            }
            else
            {
                Log("TASK: Not Active", false, 2, 2);
                if(NameOfMiningPoint.Length > 0) Log("Point: " + NameOfMiningPoint, true, 2, 2);
            }
        }

        void Start()
        {

            if (Waypoint.Length() == 0)
            {
                Waypoint = CalculateWayPoint(TargetMinePoint);
            }

            double DistanceFromStartLandingPoint = Math.Round(GetDistance(InitialDrillPoint));
            double StartDrillPointAltitude = SafetyAltitude - DroneHeight;
            double DistanceToStartDrillPoint = CurrentAltitude + DrillTill;
            double MaxDepth = SafetyAltitude + DrillTill;

            if (!IsInitialDrillVectorSaved)
            {
                IsInitialDrillVectorSaved = true;
                InitialDrillPoint = Waypoint;
            }

            if (InitialDrillDirection.Length() == 0) InitialDrillDirection = Kok.WorldMatrix.Forward;

            switch (TaskStatus)
            {
                case TaskStatuses.Test:
                    {
                        Log("STATUS: Test", false, 2, 1);
                        TaskStatus = TaskStatuses.GoToBase;
                        break;
                    }
                case TaskStatuses.Default:
                    {
                        break;
                    }
                case TaskStatuses.GoToPoint:
                    {
                        Log("STATUS: To waypoint", false, 2, 1);
                        Log("Distance: " + Math.Abs(Math.Round(GetDistance(Waypoint), 2)).ToString() + "m.", true, 0, 1);

                        if (CargoFullness == 100)
                        {
                            TaskStatus = TaskStatuses.GoToBase;
                            StopAllActivities();
                        }
                        if (Conn.Status == MyShipConnectorStatus.Connected)
                        {
                            if (CargoFullness == 0)
                            {
                                StopAllActivities();
                                TaskStatus = TaskStatuses.Disconnect;
                            }
                        }

                        if (CurrentPoint.Length() > 0) Waypoint = CalculateWayPoint(CurrentPoint);
                        else Waypoint = CalculateWayPoint(TargetMinePoint);

                        GoToPoint(Waypoint);
                        ThrustersController.ThrustToDirection();
                        break;
                    }
                case TaskStatuses.Adjustment:
                    {
                        Log("STATUS: Adjustment", false, 2, 1);
                        Log("Distance: " + Math.Abs(Math.Round(GetDistance(Waypoint), 2)).ToString() + "m.", true, 0, 1);

                        AlignShipCenterByPoint(Waypoint);
                        AdjustAltitude();
                        ThrustersController.ThrustToDirection();
                        if (Kok.GetShipSpeed() < 1 && GetDistance(Waypoint) <= 1)
                        {
                            StopAllActivities();
                            TaskStatus = TaskStatuses.LandingForDrill;
                        }
                        break;
                    }
                case TaskStatuses.LandingForDrill:
                    {
                        Log("STATUS: Landing", false, 2, 1);
                        Log("Landing from: " + DistanceFromStartLandingPoint.ToString() + "m.", true, 1, 1);
                        Log("Landing to: " + StartDrillPointAltitude.ToString() + "m.", true, 1, 1);
                        
                        if (DistanceFromStartLandingPoint - LastAltBeforeHome < StartDrillPointAltitude)
                        {
                            LandingForDrill(Waypoint, DistanceToStartDrillPoint);
                            ThrustersController.ThrustToDirection();
                        } else
                        {
                            StopAllActivities();
                            TaskStatus = TaskStatuses.Drilling;
                        }
                        break;
                    }
                case TaskStatuses.Drilling:
                    {
                        Log("STATUS: Drilling", false, 2, 1);

                        Log("Distance: " + Math.Abs(Math.Round(GetDistance(Waypoint), 2)).ToString() + "m.", true, 0, 1);
                        Log("DFLP: " + DistanceFromStartLandingPoint.ToString() + "m.", true, 0, 1);

                        if (DistanceFromStartLandingPoint < MaxDepth)
                        {
                            Drill(Waypoint);
                            ThrustersController.ThrustToDirection();
                        }
                        else
                        {
                            DrillsOnOff(false);
                            StopAllActivities();
                            TaskStatus = TaskStatuses.ToNextPoint;
                        }

                        if (CargoFullness == 100)
                        {
                            LastAltBeforeHome = CurrentAltitude;
                            DrillsOnOff(false);
                            StopAllActivities();
                            TaskStatus = TaskStatuses.GoToBase;
                        }

                        break;
                    }
                case TaskStatuses.ToNextPoint:
                    {
                        Log("STATUS: Next point", false, 2, 1);
                        LastAltBeforeHome = 0;
                        if (GetDistance(InitialDrillPoint) > StartDrillPointAltitude)
                        {
                            LockYawToPoint(InitialDrillDirection);
                            AdjustAltitude(StartDrillPointAltitude, 5);
                            ThrustersController.ThrustToDirection();
                        }
                        else
                        {
                            CalculateNextPoint();
                            Waypoint = CalculateWayPoint(CurrentPoint);
                            
                            StopAllActivities();
                            TaskStatus = TaskStatuses.ToNextPointStage2;
                        }
                        break;
                    }
                case TaskStatuses.ToNextPointStage2:
                    {
                        Vector3D StartDrillPoint = CalculateWayPoint(CurrentPoint, SafetyAltitude - DroneHeight);
                        Log("STATUS: Next point 2", false, 2, 1);
                        Log("Distance: " + Math.Abs(Math.Round(GetDistance(StartDrillPoint), 2)).ToString() + "m.", true, 0, 1);

                        KeepHorizon();
                        AlignShipCenterByPoint(StartDrillPoint);
                        LockYawToPoint(InitialDrillDirection);
                        AdjustAltitude(DroneHeight);
                        
                        ThrustersController.ThrustToDirection();
                        
                        if (GetDistance(StartDrillPoint) < 3f || (GetFTD(StartDrillPoint) < 0.03 && GetLTD(StartDrillPoint) < 0.03))
                        {
                            StopAllActivities();
                            TaskStatus = TaskStatuses.Drilling;
                        }
                        
                        break;
                    }
                case TaskStatuses.GoToBase:
                    {
                        Log("STATUS: To Base", false, 2, 1);
                        Waypoint = CalculateWayPoint(GetConnectionSafePoint(), GetConnectorAlt());
                        Log("Distance: " + Math.Abs(Math.Round(GetDistance(Waypoint), 2)).ToString() + "m.", true, 0, 1);
                        
                        GoToBase();
                        ThrustersController.ThrustToDirection();

                        break;
                    }

                case TaskStatuses.Connect:
                    {
                        Log("STATUS: Landing for connection", false, 2, 1);
                        double AddAlt = 0;
                        if (CargoFullness > 80) AddAlt = AdditionalAlt;
                        AdjustAltitude(Math.Round(GetConnectorAlt() + AddAlt), 0, true);
                        
                        KeepHorizon();
                        LockYawToPoint(GetConnectionDirection());

                        if (Math.Round(CurrentAltitude) == Math.Round(GetConnectorAlt() + AddAlt))
                        {
                            AlignShipCenterByPoint(CalculateWayPoint(GetConnectorPoint(), GetConnectorAlt()), 1f);
                            Conn.Connect();
                            if (Conn.Status == MyShipConnectorStatus.Connected)
                            {   
                                StopAllActivities();
                                TaskStatus = TaskStatuses.Unloading;
                            }
                        }
                        ThrustersController.ThrustToDirection();
                        break;
                    }
                case TaskStatuses.Unloading:
                    {
                        Log("STATUS: Waiting", false, 2, 1);
                        if (CargoFullness == 0)
                        {
                            StopAllActivities();
                            TaskStatus = TaskStatuses.GoToPoint;
                        }
                        break;
                    }
                case TaskStatuses.Disconnect:
                    {
                        Log("STATUS: Disconnection", false, 2, 1);
                        
                        Vector3D SafeConnectionPoint = GetConnectionSafePoint();
                        Log("Distance: " + Math.Abs(Math.Round(GetDistance(SafeConnectionPoint), 2)).ToString() + "m.", true, 0, 1);
                        KeepHorizon();
                        AdjustAltitude(Math.Round(GetConnectorAlt() + 1), 0, true);
                        Conn.Disconnect();
                        if (GetDistance(SafeConnectionPoint) > 3)
                        {
                            ThrustersController.Backward = 1;
                            ThrustersController.BackwardSpeed = 1f;
                            ThrustersController.ThrustToDirection();
                        }
                        else
                        {
                            TaskStatus = TaskStatuses.GoToPoint;
                            StopAllActivities();
                        }
                        break;
                    }
            }
        }

        private void GoToBase()
        {
            AdjustAltitude();
            KeepHorizon();
            if (CurrentAltitude > SafetyAltitude / 2)
            {
                GoToWayPoint(Waypoint, TaskStatuses.Connect);
            }
        }
        private void Drill(Vector3D Waypoint)
        {
            DrillsOnOff(true);
            KeepHorizon();
            AlignShipCenterByPoint(Waypoint);
            LockYawToPoint(InitialDrillDirection);

            ThrustersController.Bottom = 1;
            ThrustersController.DownSpeed = DrillingSpeed;
        }
        private void CalculateNextPoint()
        {
            if (DrillPoint < MaxCountOfDrillPoint - 1)
            {
                if (SignCount == 2)
                {
                    CurrentLine = 1;
                    SignCount = 0;
                    StepsPerLine++;
                }

                CurrentLine++;

                if (CurrentLine == StepsPerLine)
                {
                    if (StepDir == StepDirs.Up) StepDir = StepDirs.Right;
                    else if (StepDir == StepDirs.Left) StepDir = StepDirs.Up;
                    else if (StepDir == StepDirs.Down) StepDir = StepDirs.Left;
                    else if (StepDir == StepDirs.Right) StepDir = StepDirs.Down;

                    CurrentLine = 0;
                    SignCount++;
                }
                //       Log(StepDir.ToString());

                if (StepDir == StepDirs.Right)
                {
                    if (CurrentPoint.Length() == 0) CurrentPoint = TargetMinePoint + Kok.WorldMatrix.Right * StepWidth;
                    else CurrentPoint += Kok.WorldMatrix.Right * StepWidth;
                }
                if (StepDir == StepDirs.Down)
                {
                    if (CurrentPoint.Length() == 0) CurrentPoint = TargetMinePoint + Kok.WorldMatrix.Backward * StepWidth;
                    else CurrentPoint += Kok.WorldMatrix.Backward * StepWidth;
                }
                if (StepDir == StepDirs.Left)
                {
                    if (CurrentPoint.Length() == 0) CurrentPoint = TargetMinePoint + Kok.WorldMatrix.Left * StepWidth;
                    else CurrentPoint += Kok.WorldMatrix.Left * StepWidth;
                }
                if (StepDir == StepDirs.Up)
                {
                    if (CurrentPoint.Length() == 0) CurrentPoint = TargetMinePoint + Kok.WorldMatrix.Forward * StepWidth;
                    else CurrentPoint += Kok.WorldMatrix.Forward * StepWidth;
                }

                DrillPoint++;
                string DataToSave = CurrentPoint.ToString() + ";" + CurrentLine.ToString() + ";" + StepsPerLine.ToString() + ";" + StepDir.ToString() + ";" + SignCount.ToString() + ";" + DrillPoint.ToString();
                Cargo.CustomData = DataToSave;
            }
        }

        void StopAllActivities()
        {
            ThrustersController.StopThrust();
            ReleaseGyros();
            DrillsOnOff(false);
        }

        void LandingForDrill(Vector3D Waypoint, double DistanceToStartDrillPoint)
        {
            double MaxSpeed;

            KeepHorizon();
            AlignShipCenterByPoint(Waypoint);
            LockYawToPoint(InitialDrillDirection);

            MaxSpeed = SpeedCorrection(DistanceToStartDrillPoint);
            ThrustersController.Bottom = 1;
            ThrustersController.DownSpeed = (float)MaxSpeed;
        }

        private bool IsDrillsOn()
        {
            List<IMyShipDrill> Drills;
            Drills = new List<IMyShipDrill>();
            GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(Drills);
            foreach (IMyShipDrill Drill in Drills)
            {
                if (Drill.Enabled) return true;
            }
            return false;
        }

        private void DrillsOnOff(bool OnOff)
        {
            List<IMyShipDrill> Drills;
            Drills = new List<IMyShipDrill>();
            GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(Drills);
            foreach (IMyShipDrill Drill in Drills)
            {
                Drill.Enabled = OnOff;
            }
        }

        void GoToPoint(Vector3D Waypoint)
        {
            AdjustAltitude();
            KeepHorizon();
            if(CurrentAltitude > SafetyAltitude / 2) GoToWayPoint(Waypoint);
        }
        private double GetDistance(Vector3D Point)
        {
            double Distance = Vector3D.Distance(Point, Kok.GetPosition());
            return Distance;
        }

        private Vector3D CalculateWayPoint(Vector3D Point, double InitialAlt = 0)
        {
            Vector3D NaturalGravity = -Vector3D.Normalize(Kok.GetNaturalGravity());
            Vector3D WayPoint = Point + NaturalGravity * (SafetyAltitude - InitialAlt);
            return WayPoint;
        }

        private void LockYawToPoint(Vector3D Point)
        {
            Vector3D DirNorm = Vector3D.Normalize(Point);
            double df = DirNorm.Dot(Kok.WorldMatrix.Forward);
            double dl = DirNorm.Dot(Kok.WorldMatrix.Left);
            float YawInput = (float)Math.Atan2(-dl, df);

            foreach (IMyGyro gyro in Gyros)
            {
                gyro.Yaw = YawInput;
            }
        }

        void GoToWayPoint(Vector3D Waypoint, TaskStatuses NextStatus = TaskStatuses.LandingForDrill)
        {
            if (GetDistance(Waypoint) > 3)
            {
                LockYawToPoint(Waypoint);
                ThrustersController.Forward = 1;
                ThrustersController.ForwardSpeed = SpeedCorrection(GetDistance(Waypoint));
            }
            else
            {
                if(Kok.GetShipSpeed() < 5)
                {
                    StopAllActivities();
                    TaskStatus = NextStatus;
                }
                
            }
        }

        private float SpeedCorrection(double Distance)
        {
            float Speed;
            if(!WeakShip)
            {
                if (Distance < 800 && Distance > 200) Speed = 50;
                else if (Distance < 200 && Distance > 100) Speed = 20;
                else if (Distance < 100 && Distance > 30) Speed = 10;
                else if (Distance < 30 && Distance > 10) Speed = 5;
                else if (Distance < 10 && Distance > 5) Speed = 2;
                else if (Distance < 5 && Distance > 2) Speed = 0.3f;
                else if (Distance < 2 && Distance > 0.1f) Speed = 0.2f;
                else if (Distance < 0.1f) Speed = 0;
                else Speed = 100;
                if (Distance < 5 && CargoFullness > 80) Speed *= 2;
                return Speed;
            } else
            {
                if (Distance < 1600 && Distance > 400) Speed = 50;
                else if (Distance < 400 && Distance > 100) Speed = 20;
                else if (Distance < 100 && Distance > 30) Speed = 10;
                else if (Distance < 30 && Distance > 10) Speed = 5;
                else if (Distance < 10 && Distance > 5) Speed = 2;
                else if (Distance < 5 && Distance > 2) Speed = 0.3f;
                else if (Distance < 2 && Distance > 0.1f) Speed = 0.2f;
                else if (Distance < 0.1f) Speed = 0;
                else Speed = 100;
                if (Distance < 5 && CargoFullness > 80) Speed *= 2;
                return Speed;
            }
           
        }

        private float GetFTD(Vector3D Point)
        {
            Vector3D Ordinate;
            Ordinate = Point - Kok.GetPosition();
            double ft = Ordinate.Dot(Kok.WorldMatrix.Forward);
            ft = Math.Round(ft, 2);
            //Log("FTD: " + ft.ToString());
            return (float)ft;
        }

        private float GetLTD(Vector3D Point)
        {
            Vector3D Ordinate;
            Ordinate = Point - Kok.GetPosition();
            double lt = Ordinate.Dot(Kok.WorldMatrix.Left);
            lt = Math.Round(lt, 2);
            //Log("LTD: " + lt.ToString(), true);
            return (float)lt;
        }

        private void AlignShipCenterByPoint(Vector3D Point, float FixedSpeed = 0)
        {
            Vector3D Ordinate;
            Ordinate = Point - Kok.GetPosition();

            double XSpeed = ThrustersController.GetHorizontalSpeed();
            double ZSpeed = ThrustersController.GetSideSpeed();

            double ft = Ordinate.Dot(Kok.WorldMatrix.Forward);
            double lt = Ordinate.Dot(Kok.WorldMatrix.Left);

            ft = Math.Round(ft, 2);
            lt = Math.Round(lt, 2);

            Log("FTD: " + ft.ToString());
            Log("LTD: " + lt.ToString(), true);

            if (ft > 0)
            {
                if (XSpeed < AdjustKoef)
                {
                    ThrustersController.Forward = 1;
                    if(FixedSpeed > 0) ThrustersController.ForwardSpeed = FixedSpeed;
                    else ThrustersController.ForwardSpeed = (float) Math.Abs(ft);
                } else
                {
                    ThrustersController.Forward = 0;
                    ThrustersController.ForwardSpeed = 0;
                }
            } else
            {
                ThrustersController.Forward = 0;
                ThrustersController.ForwardSpeed = 0;
            }
            
            if (ft < 0)
            {
                if (XSpeed < AdjustKoef)
                {
                    ThrustersController.Backward = 1;
                    if (FixedSpeed > 0) ThrustersController.BackwardSpeed = FixedSpeed;
                    else ThrustersController.BackwardSpeed = (float)Math.Abs(ft);
                }
                else
                {
                    ThrustersController.Backward = 0;
                    ThrustersController.BackwardSpeed = 0;
                }
            } else
            {
                ThrustersController.Backward = 0;
                ThrustersController.BackwardSpeed = 0;
            }

            if (lt < 0)
            {
                if (ZSpeed < AdjustKoef)
                {
                    ThrustersController.Left = 1;
                    if (FixedSpeed > 0) ThrustersController.LeftSpeed = FixedSpeed;
                    else ThrustersController.LeftSpeed = (float)Math.Abs(lt);
                }
                else
                {
                    ThrustersController.Left = 0;
                    ThrustersController.LeftSpeed = 0;
                }
            }  else
            {
                ThrustersController.Left = 0;
                ThrustersController.LeftSpeed = 0;
            }
            
            if(lt > 0)
            { 
                if (ZSpeed < AdjustKoef)
                {
                    ThrustersController.Right = 1;
                    if (FixedSpeed > 0) ThrustersController.RightSpeed = FixedSpeed;
                    else ThrustersController.RightSpeed = (float)Math.Abs(lt);
                } else
                {
                    ThrustersController.Right = 0;
                    ThrustersController.RightSpeed = 0;
                }
            } else
            {
                ThrustersController.Right = 0;
                ThrustersController.RightSpeed = 0;
            }
        }
        private double GetConnectorAlt()
        {
            var Data = Conn.CustomData.Split(';');
            Double ConnectorAlt;
            Double.TryParse(Data[2], out ConnectorAlt);

            return ConnectorAlt;
        }

        private Vector3D GetConnectionSafePoint()
        {
            var Data = Conn.CustomData.Split(';');
            Vector3D SafeConnectionPoint;

            Vector3D.TryParse(Data[1], out SafeConnectionPoint);

            return SafeConnectionPoint;
        }

        private void SaveConnectorPosition()
        {
            Vector3D ConnectorCoords = Kok.GetPosition();
            Vector3D ConnectionDirection = Kok.WorldMatrix.Forward;
            Vector3D SafeConnectionPoint = Kok.GetPosition() + Vector3D.Normalize(Kok.WorldMatrix.Backward) * SafePointDistance;
            double SafePointAlt = CurrentAltitude;
            Conn.CustomData = ConnectorCoords.ToString() + ";" + SafeConnectionPoint + ";" + SafePointAlt.ToString() + ";" + ConnectionDirection.ToString();
        }

        private Vector3D GetConnectorPoint()
        {
            var Data = Conn.CustomData.Split(';');
            Vector3D ConnectorPoint;

            Vector3D.TryParse(Data[0], out ConnectorPoint);

            return ConnectorPoint;
        }

        private Vector3D GetConnectionDirection()
        {
            var Data = Conn.CustomData.Split(';');
            Vector3D ConnectionDirection;

            Vector3D.TryParse(Data[3], out ConnectionDirection);

            return ConnectionDirection;
        }

        //Удерживаем горизонт
        private void KeepHorizon()
        {
            Vector3D GravityVector = Kok.GetNaturalGravity();
            Vector3D GravNorm = Vector3D.Normalize(GravityVector);

            double gf = GravNorm.Dot(Kok.WorldMatrix.Forward);
            double gu = GravNorm.Dot(Kok.WorldMatrix.Up);
            double gl = GravNorm.Dot(Kok.WorldMatrix.Left);


            float RollInput = (float)Math.Atan2(gl, -gu);
            float PitchInput = -(float)Math.Atan2(gf, -gu);
            float YawInput = Kok.RotationIndicator.Y;

            foreach (IMyGyro gyro in Gyros)
            {
                gyro.GyroOverride = true;
                gyro.Roll = RollInput;
                gyro.Pitch = PitchInput;
                gyro.Yaw = YawInput;
            }
        }
        private Vector3D GetCurrentPoint()
        {
            var Data = Cargo.CustomData.Split(';');
            Vector3D CurrentP;

            Vector3D.TryParse(Data[0], out CurrentP);

            return CurrentP;
        }

        private double GetCurrentLine()
        {
            var Data = Cargo.CustomData.Split(';');
            double Variable;

            Double.TryParse(Data[1], out Variable);

            return Variable;
        }

        private double GetStepsPerLine()
        {
            var Data = Cargo.CustomData.Split(';');
            double Variable;

            Double.TryParse(Data[2], out Variable);

            return Variable;
        }

        private StepDirs GetStepDir()
        {
            var Data = Cargo.CustomData.Split(';');
            StepDirs Variable;

            Enum.TryParse(Data[3], out Variable);

            return Variable;
        }

        private double GetSignCount()
        {
            var Data = Cargo.CustomData.Split(';');
            double Variable;

            Double.TryParse(Data[4], out Variable);

            return Variable;
        }

        private double GetDrillPoint()
        {
            var Data = Cargo.CustomData.Split(';');
            double Variable;

            Double.TryParse(Data[5], out Variable);

            return Variable;
        }
        void ReleaseGyros()
        {
            foreach (IMyGyro gyro in Gyros)
            {
                gyro.GyroOverride = false;
            }
        }

        public void Save()
        {
            Me.CustomData = MyGlobalStorage;
        }

        /**
         * Удерживаем высоту на заданной в настройках отметке
         * param name="AltitudeParam" Желаемая высота
         * param name="TakeOffSpeed" Желаемая скорость подъема
         * */
        void AdjustAltitude(double AltitudeParam = 0, float TakeOffSpeed = 0, bool Multiply = false)
        {
            float AltitudeDifference;
            double TargetAltitude;
            float  TargetTakeOffSpeed;

            if (AltitudeParam > 0) TargetAltitude = AltitudeParam;
            else TargetAltitude = SafetyAltitude;
            AltitudeDifference = (float)Math.Round(Math.Abs((TargetAltitude - CurrentAltitude)));
            
            if (AltitudeDifference > MaximumTakesOffSpeed) AltitudeDifference = MaximumTakesOffSpeed;

            if (TakeOffSpeed > 0) TargetTakeOffSpeed = TakeOffSpeed;
            else TargetTakeOffSpeed = AltitudeDifference;

            //Выравнивание высоты
            if (CurrentAltitude < TargetAltitude)
            {
                if (CargoFullness > 80 && Multiply) TargetTakeOffSpeed *= (float)SpeedMultiplier;
                ThrustersController.Bottom = 0;
                ThrustersController.DownSpeed = 0;

                ThrustersController.Top = 1;
                ThrustersController.TopSpeed = TargetTakeOffSpeed;
            }
            else
            {
                if (CargoFullness > 80 && Multiply) TargetTakeOffSpeed /= (float)SpeedMultiplier;
                ThrustersController.Top = 0;
                ThrustersController.TopSpeed = 0;

                ThrustersController.Bottom = 1;
                ThrustersController.DownSpeed = TargetTakeOffSpeed;
            }
        }

        /**
         *  param name="Message" Сообщение
         *  param name="Append"  Добавить?
         */
        private void Log(string Message, bool Append = false, int SurfaceNum = 1, int Dob = 0)
        {
            if (Dob == 0)
            {
                IMyTextSurface KokLCD;
                IMyTextSurfaceProvider KokSurf;
                KokSurf = Kok as IMyTextSurfaceProvider;
                KokLCD = KokSurf.GetSurface(SurfaceNum);
                KokLCD.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                KokLCD.WriteText(Message + "\n", Append);
            } else if(Dob == 1) DOB1.WriteText(Message + "\n", Append);
            else if(Dob == 2) DOB2.WriteText(Message + "\n", Append);
            else if(Dob == 3) DOB3.WriteText(Message + "\n", Append);
        
        }

        /**
         * Класс управления трастерами
         * 
         * 
         */
        public class ThrusterMovement
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public double Left { get; set; }
            public double Right { get; set; }
            public double Forward { get; set; }
            public double Backward { get; set; }

            /**
             * Переменные скоростей в процентах
             */
            public float TopSpeed { get; set; }
            public float DownSpeed { get; set; }
            public float RightSpeed { get; set; }
            public float LeftSpeed { get; set; }
            public float ForwardSpeed { get; set; }
            public float BackwardSpeed { get; set; }

            List<IMyThrust> Thrusters { get; }

            private IMyShipController Cockpit;
            private Program Parent;

            double OldVerticalDistance = 0;
            double OldHorizontalDistance = 0;
            double OldSideDistance = 0;

            public ThrusterMovement(double TopDirection, double BottomDirection, double LeftDirection, double RightDirection, double ForwardDirection, double BackwardDirection, List<IMyThrust> Thrstrs, IMyShipController Kok, Program ParentClass)
            {
                Top = TopDirection;
                Bottom = BottomDirection;
                Left = LeftDirection;
                Right = RightDirection;
                Forward = ForwardDirection;
                Backward = BackwardDirection;

                TopSpeed = 0;
                DownSpeed = 0;
                LeftSpeed = 0;
                RightSpeed = 0;
                ForwardSpeed = 0;
                BackwardSpeed = 0;

                Cockpit = Kok;
                Thrusters = Thrstrs;
                Parent = ParentClass;
            }

            public float GetVerticalSpeed()
            {
                if (OldVerticalDistance != 0)
                {
                    double NewVerticalDistance;
                    double VerticalDistancePerTick;

                    Cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out NewVerticalDistance);
                    
                    VerticalDistancePerTick = Math.Round(Math.Abs(NewVerticalDistance - OldVerticalDistance), 3);
                    OldVerticalDistance = NewVerticalDistance;
                    
                    return (float) VerticalDistancePerTick * 60;
                }
                else
                {   
                    Cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out OldVerticalDistance);
     
                    return 0;
                }
            }

            public float GetHorizontalSpeed()
            {
                if (OldHorizontalDistance != 0)
                {
                    double NewHorizontalDistance;
                    double HorizontalDistancePerTick;

                    NewHorizontalDistance = Cockpit.GetPosition().Z;
                    HorizontalDistancePerTick = Math.Round(Math.Abs(NewHorizontalDistance - OldHorizontalDistance), 3);

                    OldHorizontalDistance = NewHorizontalDistance;
                    return (float)HorizontalDistancePerTick * 60;
                }
                else
                {
                    OldHorizontalDistance = Cockpit.GetPosition().Z;
                    return 0;
                }
            }

            public float GetSideSpeed()
            {
                if (OldSideDistance != 0)
                {
                    double NewSideDistance;
                    double SideDistancePerTick;

                    NewSideDistance = Cockpit.GetPosition().X;
                    SideDistancePerTick = Math.Round(Math.Abs(NewSideDistance - OldSideDistance), 3);

                    OldSideDistance = NewSideDistance;
                    return (float)SideDistancePerTick * 60;
                }
                else
                {
                    OldSideDistance = Cockpit.GetPosition().X;
                    return 0;
                }
            }

            public void StopThrust()
            {
                TopSpeed = 0;
                DownSpeed = 0;
                LeftSpeed = 0;
                RightSpeed = 0;
                ForwardSpeed = 0;
                BackwardSpeed = 0;

                Top = 0;
                Bottom = 0;
                Left = 0;
                Right = 0;
                Forward = 0;
                Backward = 0;

                Cockpit.DampenersOverride = true;
                ReleaseTrusters();
            }

            public void ReleaseTrusters()
            {
                foreach (IMyThrust Thruster in this.Thrusters)
                {
                    Thruster.ThrustOverridePercentage = 0;
                }
            }

            public void ThrustToDirection()
            {
                float VerticalSpeed = GetVerticalSpeed();
                float HorizontalSpeed = GetHorizontalSpeed();
                float SideSpeed = GetSideSpeed();

                Matrix CockipitMatrix = new MatrixD();
                Matrix ThrusterMatrix = new MatrixD();

                Parent.Log("Top: " + TopSpeed.ToString(), false, 2);
                Parent.Log("Bottom: " + DownSpeed.ToString(), true, 2);
                Parent.Log("Left: " + LeftSpeed.ToString(), true, 2);
                Parent.Log("Right: " + RightSpeed.ToString(), true, 2);
                Parent.Log("Forward: " + ForwardSpeed.ToString(), true, 2);
                Parent.Log("Backward: " + BackwardSpeed.ToString(), true, 2);

                //Получаем вектора ориентации Кокпита
                Cockpit.Orientation.GetMatrix(out CockipitMatrix);

                if (this.Thrusters.Count > 0)
                {
                    foreach (IMyThrust Thruster in this.Thrusters)
                    {
                        //Получаем вектора ориентации трастера
                        Thruster.Orientation.GetMatrix(out ThrusterMatrix);

                        if (CockipitMatrix.Backward == ThrusterMatrix.Forward)
                        {
                            if (this.Forward == 1 && Cockpit.GetShipSpeed() < this.ForwardSpeed)
                            {
                                Thruster.ThrustOverridePercentage = this.ForwardSpeed;
                            }
                            else
                            {
                                Thruster.ThrustOverridePercentage = 0;
                            }
                        }
                        if (CockipitMatrix.Forward == ThrusterMatrix.Forward)
                        {
                            if (this.Backward == 1 && Cockpit.GetShipSpeed() < this.BackwardSpeed)
                            {
                                Thruster.ThrustOverridePercentage = this.BackwardSpeed;
                            }
                            else
                            {
                                Thruster.ThrustOverridePercentage = 0;
                            }
                        }
                        if (CockipitMatrix.Left == ThrusterMatrix.Forward)
                        {
                            if (this.Left == 1 && Cockpit.GetShipSpeed() < this.LeftSpeed)
                            {
                                Thruster.ThrustOverridePercentage = this.LeftSpeed;
                            }
                            else
                            {
                                Thruster.ThrustOverridePercentage = 0;
                            }
                        }
                        if (CockipitMatrix.Right == ThrusterMatrix.Forward)
                        {
                            if (this.Right == 1 && Cockpit.GetShipSpeed() < this.RightSpeed)
                            {
                                Thruster.ThrustOverridePercentage = this.RightSpeed;
                            }
                            else
                            {
                                Thruster.ThrustOverridePercentage = 0;
                            }
                        }

                        if (CockipitMatrix.Down == ThrusterMatrix.Forward)
                        {
                            if (this.Top == 1 && (VerticalSpeed < this.TopSpeed))
                            {
                                Thruster.ThrustOverridePercentage = this.TopSpeed;
                            }
                            else
                            {
                                Thruster.ThrustOverridePercentage = 0;
                            }

                        }
                        if (CockipitMatrix.Up == ThrusterMatrix.Forward)
                        {

                            if (this.Bottom == 1 && VerticalSpeed < this.DownSpeed)
                            {
                                Thruster.ThrustOverridePercentage = this.DownSpeed;
                            }
                            else
                            {
                                Thruster.ThrustOverridePercentage = 0;
                            }
                        }

                    }
                }
                
                if (this.Bottom == 1 && VerticalSpeed < this.DownSpeed)
                {
                    Cockpit.DampenersOverride = false;
                }
                else Cockpit.DampenersOverride = true;
            }
        }
    }
}
