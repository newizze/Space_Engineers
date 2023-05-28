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

namespace MicroDrone
{
    public sealed class Program : MyGridProgram
    {
        /// Start of the script

        String ShipControllerName = "DrillingBotCockpit";
        String StatusDisplay = "DrillingStatusDisplay";
        String ConnectorName = "DrillingBotConnector";
        Double SafeAltitude = 100;
        Double DrillingDepth = 17;

        List<IMyGyro> Gyros = new List<IMyGyro>();
        List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();
        List<IMyCargoContainer> Cargos = new List<IMyCargoContainer>();
        List<IMyThrust> Thrusters = new List<IMyThrust>();
        List<IMyShipDrill> Drills = new List<IMyShipDrill>();

        IMyTextSurface Display;
        IMyShipController Cockpit;
        IMyShipConnector Connector;

        GyrosControllerClass GyrosController;
        StatusDisplayControllerClass StatusDisplayController;
        SequenceControllerClass SequenceController;
        EnergyControllerClass EnergyController;
        CargoControllerClass CargoController;
        NavigationControllerClass NavigationController;
        MovementControllerClass MovementController;
        ConnectorControllerClass ConnectorController;
        DrillsControllerClass DrillingController;
        MemoryControllerClass MemoryController;

        Boolean IsProcessStarted = false;

        Vector3D DrillingCoordinates;

        String NameOfDrillingPoint;

        public Program()
        {
            Cockpit = GridTerminalSystem.GetBlockWithName(ShipControllerName) as IMyShipController;
            Display = GridTerminalSystem.GetBlockWithName(StatusDisplay) as IMyTextSurface;
            

            if (Cockpit == null)
            {
                Echo("No ship controller found.");
                return;
            }

            if (Display == null)
            {
                Echo("No display found.");
                return;
            }

            InitControllers();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        void InitControllers()
        {
            //Initializing Memory controller
            MemoryController = new MemoryControllerClass(Me);

            //Initializing Gyroscopes Controller
            GridTerminalSystem.GetBlocksOfType<IMyGyro>(Gyros, Gyro => Gyro.CubeGrid == Cockpit.CubeGrid);
            GyrosController = new GyrosControllerClass(Gyros, Cockpit);

            //Initializing Status Display Controller
            StatusDisplayController = new StatusDisplayControllerClass(Display);

            //Initializing Energy Controller
            GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(Batteries, Battery => Battery.CubeGrid == Cockpit.CubeGrid);
            EnergyController = new EnergyControllerClass(Batteries);

            //Initializing Cargos Controller
            GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(Cargos, Cargo => Cargo.CubeGrid == Cockpit.CubeGrid);
            CargoController = new CargoControllerClass(Cargos);

            //Initializing Navigation Controller
            NavigationController = new NavigationControllerClass(Cockpit, SafeAltitude, MemoryController.GetNextDrillingPointWithStepsData());

            //Initializing Movement Controller
            GridTerminalSystem.GetBlocksOfType<IMyThrust>(Thrusters, Thruster => Thruster.CubeGrid == Cockpit.CubeGrid);
            MovementController = new MovementControllerClass(Cockpit, Thrusters);

            //Initializing Connector 
            Connector = GridTerminalSystem.GetBlockWithName(ConnectorName) as IMyShipConnector;
            ConnectorController = new ConnectorControllerClass(Connector, MemoryController);

            // Initializing Drilling Controller
            GridTerminalSystem.GetBlocksOfType<IMyShipDrill>(Drills, Drill => Drill.CubeGrid == Cockpit.CubeGrid);
            DrillingController = new DrillsControllerClass(Drills);

            //Initializing Sequence Controller
            SequenceController = new SequenceControllerClass(
                GyrosController, 
                StatusDisplayController, 
                EnergyController, 
                CargoController, 
                NavigationController, 
                MovementController,
                ConnectorController,
                DrillingController,
                MemoryController,
                SafeAltitude,
                DrillingDepth
            );
        }

        void Main(String Argument)
        {
            StatusDisplayController.Clear();
            StatusDisplayController.AddLine("** DRILLING SHIP STATUS **");
            StatusDisplayController.AddLine("- Energy amount: " + EnergyController.GetEnergyAmount() + "%");
            StatusDisplayController.AddLine("- Cargo fullness: " + CargoController.GetCargosFullness() + "%");


            if (DrillingCoordinates.Length() == 0) GetInitialDrillingData();

            if (DrillingCoordinates.Length() == 0) StatusDisplayController.AddLine("Specify coordinates!");
            else StatusDisplayController.AddLine("- GPS point: " + NameOfDrillingPoint);

            if (MemoryController.GetBaseConnectorPosition() == "") StatusDisplayController.AddLine("Save connector!");
            else StatusDisplayController.AddLine("- Connector found");

            if (Argument == "Start")
            {
                if (DrillingCoordinates.Length() != 0 && ConnectorController.GetConnectorPoint().Length() != 0)
                {
                    IsProcessStarted = true;
                    SequenceController.SetDrillingCoordinates(DrillingCoordinates);
                }
            }
            else if (Argument == "Stop")
            {
                IsProcessStarted = false;
            } 
            else if (Argument == "Test")
            {
                Connector.Connect();
            } 
            else if (Argument == "SaveConn")
            {
                MemoryController.SaveBaseConnectorPosition(Cockpit);
                MemoryController.SaveConnectorAltitude(MovementController.GetShipAltitude().ToString());
            }

            if (IsProcessStarted)
            {
                StatusDisplayController.AddLine("- Controlled by: AI");
                SequenceController.Control();
            }
            else
            {
                SequenceController.StopAllActivities();
                StatusDisplayController.AddLine("- Controlled by: Player");
            }
            
            StatusDisplayController.ShowLines();

        }

        private Vector3D GetInitialDrillingData()
        {
            List<String> GPSData = MemoryController.GetInitialDrillingData();
            Double DirectionX, DirectionY, DirectionZ;
            Double.TryParse(GPSData[2], out DirectionX);
            Double.TryParse(GPSData[3], out DirectionY);
            Double.TryParse(GPSData[4], out DirectionZ);
            NameOfDrillingPoint = GPSData[1];
            return DrillingCoordinates = new Vector3D(DirectionX, DirectionY, DirectionZ);
        }

        class MemoryControllerClass
        {
            IMyProgrammableBlock Me;
            List<String> MemoryData = new List<string>() { "", "", "", "", "", "" };
            public MemoryControllerClass(IMyProgrammableBlock ProgrammableBlock)
            {
                Me = ProgrammableBlock;
                String[] StringMemoryData = Me.CustomData.ToString().Split('\n');
                foreach(var Item in StringMemoryData.Select((Value, Idx) => new { Idx, Value}))
                {
                    MemoryData[Item.Idx] = Item.Value; 
                }
            }

            public List<String> GetInitialDrillingData()
            {
                String DataLines = MemoryData[0];
                List<String> GPSData = new List<string>();
                if (DataLines.Count() > 0)
                {
                    GPSData = DataLines.Split(':').ToList();
                    if (GPSData.Count() > 2)
                    {
                        return GPSData;
                       
                    }
                }
                return GPSData;
            }
            
            public void SaveBaseConnectorPosition(IMyShipController Cockpit)
            {
                Vector3D ConnectorPosition = Cockpit.GetPosition();
                SaveToMemory(ConnectorPosition.ToString(), 1);
                Matrix CockipitMatrix;
                Cockpit.Orientation.GetMatrix(out CockipitMatrix);
                Vector3D CockpitForwardVector;
                CockpitForwardVector = Cockpit.GetPosition() + Vector3D.Normalize(Cockpit.WorldMatrix.Forward) * 100;
                SaveConnectorForwardDirection(CockpitForwardVector.ToString());
            }

            public String GetBaseConnectorPosition()
            {
                return GetFromMemory(1);
            }

            public void SaveNextDrillingPointWithStepsData(String Data)
            {
                SaveToMemory(Data, 2);
            }

            public void SaveConnectorForwardDirection(String Data)
            {
                SaveToMemory(Data, 3);
            }
            
            public void SaveConnectorAltitude(String Data)
            {
                SaveToMemory(Data, 4);
            }

            public String GetConnectorAltitude()
            {
                return GetFromMemory(4);
            }

            public String GetConnectorForwardDirection()
            {
                return GetFromMemory(3);
            }

            public String GetNextDrillingPointWithStepsData()
            {
                return GetFromMemory(2);
            }

            private void SaveToMemory(String Data, int Idx)
            {
                String DataToSave = "";
                int i = 1;
                MemoryData[Idx] = Data;
                MemoryData.ForEach((Line) =>
                {
                    if (i < MemoryData.Count()) DataToSave += Line + "\n";
                    else DataToSave += Line;
                    i++;
                });

                Me.CustomData = DataToSave;
            }

            private String GetFromMemory(int Idx)
            {
                return MemoryData[Idx];
            }

        }

        class MovementControllerClass
        {
            IMyShipController Cockpit;
            List<IMyThrust> Thrusters;
            List<IMyThrust> ToForwardThrusters;
            List<IMyThrust> ToBackwardThrusters;
            List<IMyThrust> ToLeftThrusters;
            List<IMyThrust> ToRightThrusters;
            List<IMyThrust> ToUpThrusters;
            List<IMyThrust> ToDownThrusters;

            public MovementControllerClass(IMyShipController ShipController, List<IMyThrust> ThrustersList)
            {
                Cockpit = ShipController;
                Thrusters = ThrustersList;

                ToForwardThrusters = GetThrustersForDirection("Forward");
                ToBackwardThrusters = GetThrustersForDirection("Backward");
                ToLeftThrusters = GetThrustersForDirection("Left");
                ToRightThrusters = GetThrustersForDirection("Right");
                ToUpThrusters = GetThrustersForDirection("Up");
                ToDownThrusters = GetThrustersForDirection("Down");
            }

            public double CalculateSpeedMatrix(double Distance)
            {
                double Speed;

                if (Distance > 40) Speed = Distance / 10;
                else Speed = 3;
                return Speed;
            }

            public double GetShipAltitude()
            {
                double CurrentAltitude;
                Cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out CurrentAltitude);
                return CurrentAltitude;
            }

            public double GetShipDepth(Vector3D ReferencePoint, Double ReferenceAlt)
            {
                Vector3D PointToDevice = Cockpit.GetPosition() - ReferencePoint;
                Vector3D GravityDirection = Vector3D.Normalize(Cockpit.GetNaturalGravity());

                double AltFromReferencePoint = Vector3D.Dot(PointToDevice, GravityDirection);

                if (AltFromReferencePoint > 0) return -Math.Round(ReferenceAlt - AltFromReferencePoint);
                else return -Math.Round(Math.Abs(AltFromReferencePoint) + ReferenceAlt);
            }

            public void GoToDirectionWithSpeed(Vector3D Direction, Vector3 Speed)
            {
                //Forward & Backward
                if(Direction.X == 1)
                {
                    SetSpeed(ToForwardThrusters, Speed.X);
                    SetSpeed(ToBackwardThrusters, 0);
                } else if(Direction.X == -1)
                {
                    SetSpeed(ToBackwardThrusters, Speed.X);
                    SetSpeed(ToForwardThrusters, 0);
                }
                else
                {
                    SetSpeed(ToForwardThrusters, 0);
                    SetSpeed(ToBackwardThrusters, 0);
                }

                //Up & Down
                if(Direction.Y == 1)
                {
                    SetSpeedForUpAndDownMovement(ToUpThrusters, 1, Speed.Y);
                } else if(Direction.Y == -1)
                {
                    double BottomSpeed = Speed.Y > 5 ? 5 : Speed.Y;
                    SetSpeedForUpAndDownMovement(ToUpThrusters, -1, BottomSpeed);
                }
                else
                {
                    SetSpeed(ToUpThrusters, 0);
                    SetSpeed(ToDownThrusters, 0);
                }

                //Left & Right
                if(Direction.Z == 1)
                {
                    SetSpeed(ToLeftThrusters, Speed.Z);
                    SetSpeed(ToRightThrusters, 0);
                } else if(Direction.Z == -1)
                {
                    SetSpeed(ToLeftThrusters, 0);
                    SetSpeed(ToRightThrusters, Speed.Z);
                }
                else
                {
                    SetSpeed(ToLeftThrusters, 0);
                    SetSpeed(ToRightThrusters, 0);
                }
            }

            public List<Vector3D> GetShipCenterToVectorDirectionAndSpeed(Vector3D TargetVector)
            {
                Vector3D DevicePosition = Cockpit.GetPosition();
                Vector3D GravityVector = Vector3D.Normalize(Cockpit.GetNaturalGravity());

                // Calculate vector from the line point to the device position
                Vector3D LineToPoint = DevicePosition - TargetVector;

                // Project this vector onto the line direction
                Vector3D Projection = Vector3D.Dot(LineToPoint, GravityVector) * GravityVector;

                // Subtract the projection from lineToPoint to get the shortest vector to the line
                Vector3D ShortestVector = LineToPoint - Projection;

                double ForwardDeviation = ShortestVector.Dot(Cockpit.WorldMatrix.Forward);
                double LeftDeviation = ShortestVector.Dot(Cockpit.WorldMatrix.Left);
                double XDirection, ZDirection;
                double XSpeed, ZSpeed;


                if (ForwardDeviation < 0)
                {
                    XDirection = 1;
                    XSpeed = Math.Abs(ForwardDeviation > 2 ? 2 : ForwardDeviation);
                } else if(ForwardDeviation > 0)
                {
                    XDirection = -1;
                    XSpeed = Math.Abs(ForwardDeviation > 2 ? 2 : ForwardDeviation);
                }
                else
                {
                    XDirection = 0;
                    XSpeed = 0;
                }

                if(LeftDeviation > 0)
                {
                    ZDirection = -1;
                    ZSpeed = Math.Abs(LeftDeviation > 2 ? 2 : LeftDeviation);
                } else if(LeftDeviation < 0)
                {
                    ZDirection = +1;
                    ZSpeed = Math.Abs(LeftDeviation > 2 ? 2 : LeftDeviation);
                }
                else
                {
                    ZDirection = 0;
                    ZSpeed = 0;
                }

                List<Vector3D> Result = new List<Vector3D>();
                Result.Add(new Vector3D(XDirection, 0, ZDirection));
                Result.Add(new Vector3D(XSpeed, 0, ZSpeed));

                return Result;
            }
            
            private void SetSpeedForUpAndDownMovement(List<IMyThrust> ThrustersToSet, Double Direction, Double Speed)
            {
                float ShipMass = Cockpit.CalculateShipMass().PhysicalMass;
 
                Double RequiredThrustOverride = (ShipMass * Cockpit.GetNaturalGravity().Length()) / ThrustersToSet.Count;

                // Set the thrust override of each thruster to a percentage of its maximum thrust based on ships mass and desired speed
                foreach (IMyThrust Thruster in ThrustersToSet)
                {
                    if (Thruster.IsFunctional)
                    {
                        if(GetVerticalSpeed() > Speed)
                        {
                            Thruster.ThrustOverridePercentage = 0;
                            Cockpit.DampenersOverride = true;
                        } else
                        {
                            if (Direction == -1)
                            {
                                Cockpit.DampenersOverride = false;
                            }
                            else
                            {
                                Cockpit.DampenersOverride = true;
                                Thruster.ThrustOverride = (float) RequiredThrustOverride + 30000;
                                //Thruster.ThrustOverridePercentage = 1;
                            }
                        }
                    }
                }
            }

            private void SetSpeed(List<IMyThrust> ThrustersToSet, Double Speed)
            {
                foreach (IMyThrust Thruster in ThrustersToSet)
                {
                    if (Thruster.IsFunctional)
                    {
                        if(Cockpit.GetShipSpeed() < Speed)
                        Thruster.ThrustOverridePercentage = 1;
                        else Thruster.ThrustOverridePercentage = 0;
                    }
                }
            }

            private List<IMyThrust> GetThrustersForDirection(String Direction)
            {
                List<IMyThrust> DirectionThrusters = new List<IMyThrust>();
                Matrix ThrusterMatrix = new MatrixD();
                Thrusters.ForEach((Thruster) => {
                    Thruster.Orientation.GetMatrix(out ThrusterMatrix);
                    if (DetermineDirection(ThrusterMatrix.Forward) == Direction)
                    {
                        DirectionThrusters.Add(Thruster);
                        Thruster.CustomName = Direction + " thruster";
                    }
                });
                return DirectionThrusters;
            }

            public void StopMovement()
            {
                GoToDirectionWithSpeed(new Vector3D(0, 0, 0), new Vector3(0, 0, 0));
                Cockpit.DampenersOverride = true;
            }

            private String DetermineDirection(Vector3 Direction)
            {
                Matrix CockpitMatrix = new MatrixD();
                Cockpit.Orientation.GetMatrix(out CockpitMatrix);

                if(CockpitMatrix.Backward == Direction)
                {
                    return "Forward";
                } 
                else if(CockpitMatrix.Forward == Direction)
                {
                    return "Backward";
                } 
                else if(CockpitMatrix.Right == Direction)
                {
                    return "Left";
                } 
                else if(CockpitMatrix.Left == Direction)
                {
                    return "Right";
                } 
                else if(CockpitMatrix.Down == Direction)
                {
                    return "Up";
                } 
                else if(CockpitMatrix.Up == Direction)
                {
                    return "Down";
                }
                return "";
            }

            public double GetHorizontalSpeed()
            {
                Vector3D GravityVector = Vector3D.Normalize(Cockpit.GetNaturalGravity());

                // Get the total velocity of the ship
                Vector3D TotalVelocity = Cockpit.GetShipVelocities().LinearVelocity;

                // Project the total velocity onto the gravity vector to get the vertical speed
                Vector3D VerticalVelocity = Vector3D.Dot(TotalVelocity, GravityVector) * GravityVector;

                // Subtract the vertical speed from the total speed to get the horizontal speed
                Vector3D HorizontalVelocity = TotalVelocity - VerticalVelocity;

                // Get the magnitude of the horizontal speed
                return Math.Round(HorizontalVelocity.Length());
            }

            public double GetVerticalSpeed()
            {
                Vector3D GravityVector = Vector3D.Normalize(Cockpit.GetNaturalGravity());

                // Get the total velocity of the ship
                Vector3D TotalVelocity = Cockpit.GetShipVelocities().LinearVelocity;

                // Project the total velocity onto the gravity vector to get the vertical speed
                Vector3D VerticalVelocity = Vector3D.Dot(TotalVelocity, GravityVector) * GravityVector;

                // Get the magnitude of the horizontal speed
                return Math.Round(VerticalVelocity.Length());
            }
        }

        public class GyrosControllerClass
        {
            List<IMyGyro> Gyros;
            IMyShipController Cockpit;
            public GyrosControllerClass(List<IMyGyro> Gyroscopes, IMyShipController ShipController)
            {
                Gyros = Gyroscopes;
                Cockpit = ShipController;
            }

            public void KeepHorizon()
            {
                Vector3D GravityVector = Cockpit.GetNaturalGravity();
                Vector3D GravNorm = Vector3D.Normalize(GravityVector);

                double GravityDotForward = GravNorm.Dot(Cockpit.WorldMatrix.Forward);
                double GravityDotUp = GravNorm.Dot(Cockpit.WorldMatrix.Up);
                double GravityDotLeft = GravNorm.Dot(Cockpit.WorldMatrix.Left);


                float RollInput = (float) Math.Atan2(GravityDotLeft, -GravityDotUp);
                float PitchInput = -(float) Math.Atan2(GravityDotForward, -GravityDotUp);
                float YawInput = Cockpit.RotationIndicator.Y;

                foreach (IMyGyro Gyro in Gyros)
                {
                    Gyro.GyroOverride = true;
                    Gyro.Roll = RollInput;
                    Gyro.Pitch = PitchInput;
                    Gyro.Yaw = YawInput;
                }
            }

            public void LookToNextPoint(Vector3D Point)
            {
                KeepHorizon();
                Vector3D DirectionNormalized = Vector3D.Normalize(Point - Cockpit.GetPosition());
                double DirectionDotForward = DirectionNormalized.Dot(Cockpit.WorldMatrix.Forward);
                double DirectionDotLeft = DirectionNormalized.Dot(Cockpit.WorldMatrix.Left);
                float YawInput = (float)Math.Atan2(-DirectionDotLeft, DirectionDotForward);

                foreach (IMyGyro gyro in Gyros)
                {
                    gyro.GyroOverride = true;
                    gyro.Yaw = YawInput;
                }
            }

            public void ReleaseGyros()
            {
                foreach (IMyGyro Gyro in Gyros)
                {
                    Gyro.GyroOverride = false;
                    Gyro.Roll = 0;
                    Gyro.Pitch = 0;
                    Gyro.Yaw = 0;
                }
            }
        }

        class NavigationControllerClass
        {
            double MaxCountOfDrillPoint = 25;
            double DrillPoint = 0;
            double StepsPerLine = 1;
            double CurrentLine = 0;
            double SignCount = 0;
            double StepWidth = 9; // Длина бурильщика
            double DeviceWidthDifference = 2; //Разница с длиной бурильщика

            double SafeAltitude;
            string StepsData;

            enum StepDirs { Left, Right, Up, Down };

            StepDirs StepDir = StepDirs.Up;

            IMyShipController Cockpit;

            Vector3D CurrentPoint;

            public NavigationControllerClass(IMyShipController CockpitInstance, double SafeAltitudeGlobal, string StepsDataString)
            {
                Cockpit = CockpitInstance;
                SafeAltitude = SafeAltitudeGlobal;
                ReloadCurrents(StepsDataString);
            }

            public void ReloadCurrents(string StepsDataString)
            {
                StepsData = StepsDataString;
                if (StepsData != "")
                {
                    CurrentPoint = GetCurrentPoint();
                    CurrentLine = GetCurrentLine();
                    StepsPerLine = GetStepsPerLine();
                    StepDir = GetStepDir();
                    SignCount = GetSignCount();
                    DrillPoint = GetDrillPoint();
                }
            }

            public Vector3D CalculatePointAboveDrilling(Vector3D GPSPoint, Double Altitude)
            {
                Vector3D GravityVectorNormalized = Vector3D.Normalize(Cockpit.GetNaturalGravity());
                return GPSPoint - GravityVectorNormalized * Altitude;
            }

            public Double GetDistanceToPointVector(Vector3D Point)
            {
                Vector3D DevicePosition = Cockpit.GetPosition();
                Vector3D GravityVector = Vector3D.Normalize(Cockpit.GetNaturalGravity());

                // Calculate vector from the line point to the device position
                Vector3D LineToPoint = DevicePosition - Point;

                // Project this vector onto the line direction
                Vector3D Projection = Vector3D.Dot(LineToPoint, GravityVector) * GravityVector;

                // Subtract the projection from lineToPoint to get the shortest vector to the line
                Vector3D ShortestVector = LineToPoint - Projection;

                // Return the length of this vector, which is the shortest distance to the line
                return Math.Round(ShortestVector.Length());
            }

            public String CalculateNextPoint(Vector3D FirstPoint)
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

                    if (StepDir == StepDirs.Right)
                    {
                        if (CurrentPoint.Length() == 0) CurrentPoint = FirstPoint + Cockpit.WorldMatrix.Right * StepWidth;
                        else CurrentPoint += Cockpit.WorldMatrix.Right * StepWidth;
                    }
                    if (StepDir == StepDirs.Down)
                    {
                        if (CurrentPoint.Length() == 0) CurrentPoint = FirstPoint + Cockpit.WorldMatrix.Backward * (StepWidth- DeviceWidthDifference);
                        else CurrentPoint += Cockpit.WorldMatrix.Backward * (StepWidth - DeviceWidthDifference);
                    }
                    if (StepDir == StepDirs.Left)
                    {
                        if (CurrentPoint.Length() == 0) CurrentPoint = FirstPoint + Cockpit.WorldMatrix.Left * StepWidth;
                        else CurrentPoint += Cockpit.WorldMatrix.Left * StepWidth;
                    }
                    if (StepDir == StepDirs.Up)
                    {
                        if (CurrentPoint.Length() == 0) CurrentPoint = FirstPoint + Cockpit.WorldMatrix.Forward * (StepWidth - DeviceWidthDifference);
                        else CurrentPoint += Cockpit.WorldMatrix.Forward * (StepWidth - DeviceWidthDifference);
                    }

                    DrillPoint++;
                    string DataToSave = CurrentPoint.ToString() + ";" + CurrentLine.ToString() + ";" + StepsPerLine.ToString() + ";" + StepDir.ToString() + ";" + SignCount.ToString() + ";" + DrillPoint.ToString();
                    return DataToSave;
                }
                return "";
            }

            public Vector3D GetCurrentPoint()
            {
                var Data = StepsData.Split(';');
                Vector3D CurrentP;

                Vector3D.TryParse(Data[0], out CurrentP);

                return CurrentP;
            }

            private double GetCurrentLine()
            {
                var Data = StepsData.Split(';');
                double Variable;

                Double.TryParse(Data[1], out Variable);

                return Variable;
            }

            private double GetStepsPerLine()
            {
                var Data = StepsData.Split(';');
                double Variable;

                Double.TryParse(Data[2], out Variable);

                return Variable;
            }

            private StepDirs GetStepDir()
            {
                var Data = StepsData.Split(';');
                StepDirs Variable;

                Enum.TryParse(Data[3], out Variable);

                return Variable;
            }

            private double GetSignCount()
            {
                var Data = StepsData.Split(';');
                double Variable;

                Double.TryParse(Data[4], out Variable);

                return Variable;
            }

            private double GetDrillPoint()
            {
                var Data = StepsData.Split(';');
                double Variable;

                Double.TryParse(Data[5], out Variable);

                return Variable;
            }
        }

        class DrillsControllerClass
        {
            List<IMyShipDrill> Drills;
            public DrillsControllerClass(List<IMyShipDrill> DrillsList)
            {
                Drills = DrillsList;
            }

            public void StartDrills()
            {
                Drills.ForEach((Drill) =>
                {
                    Drill.Enabled = true;
                });
            }

            public void StopDrills()
            {
                Drills.ForEach((Drill) =>
                {
                    Drill.Enabled = false;
                });
            }
        } 

        class SequenceControllerClass 
        {
            enum TaskStatuses { Default, MoveToPoint, GoDownToDrill, Drilling, UpToReadyForDrillingPosition, GoToNextDrillingPosition, MoveToBase, BaseDisconnect, GoDownToConnector, BaseConnect, CargoUnloading };
            TaskStatuses TaskStatus = TaskStatuses.Default;

            double SafeAltitude;
            double ReadyForDrillingAltitude = 10;
            double DrillingDepth;

            private GyrosControllerClass GyrosController;
            private StatusDisplayControllerClass StatusDisplayController;
            private EnergyControllerClass EnergyController;
            private CargoControllerClass CargoController;
            private NavigationControllerClass NavigationController;
            private MovementControllerClass MovementController;
            private ConnectorControllerClass ConnectorController;
            private DrillsControllerClass DrillsController;
            private MemoryControllerClass MemoryController;

            Vector3D DrillingCoordinates;

            public SequenceControllerClass(
                GyrosControllerClass GyrosControllerInstance, 
                StatusDisplayControllerClass StatusDisplayControllerInstance,
                EnergyControllerClass EnergyControllerInstance,
                CargoControllerClass CargoControllerInstance,
                NavigationControllerClass NavigationControllerInstance,
                MovementControllerClass MovementControllerInstance,
                ConnectorControllerClass ConnectorControllerInstance,
                DrillsControllerClass DrillsControllerInstance,
                MemoryControllerClass MemoryControllerInstance,
                double Altitude,
                double Depth
                )
            {
                GyrosController = GyrosControllerInstance;
                StatusDisplayController = StatusDisplayControllerInstance;
                EnergyController = EnergyControllerInstance;
                CargoController = CargoControllerInstance;
                NavigationController = NavigationControllerInstance;
                MovementController = MovementControllerInstance;
                ConnectorController = ConnectorControllerInstance;
                DrillsController = DrillsControllerInstance;
                MemoryController = MemoryControllerInstance;
                SafeAltitude = Altitude;
                DrillingDepth = Depth;
            }

            public void SetDrillingCoordinates(Vector3D DrillingCoordinatesGlobal)
            {
                DrillingCoordinates = DrillingCoordinatesGlobal;
            }

            public void Control()
            {
                StatusDisplayController.AddLine("");
                StatusDisplayController.AddLine("Sequence status: " + TaskStatus.ToString());
                ProcessSequence();
            }

            private void ProcessSequence()
            {
                // Go & recharge batteries if needed
                if (
                    (EnergyController.IsChargeRequired() || CargoController.GetCargosFullness() == 100) &&
                    (
                        TaskStatus != TaskStatuses.MoveToBase &&
                        TaskStatus != TaskStatuses.GoDownToConnector &&
                        TaskStatus != TaskStatuses.BaseConnect &&
                        TaskStatus != TaskStatuses.CargoUnloading
                    )
                )
                {
                    MovementController.StopMovement();
                    GyrosController.ReleaseGyros();
                    DrillsController.StopDrills();
                    TaskStatus = TaskStatuses.MoveToBase;
                }

                switch (TaskStatus)
                {
                    case TaskStatuses.Default:
                        {
                            TaskStatus = TaskStatuses.MoveToPoint;
                            break;
                        }
                    case TaskStatuses.MoveToPoint:
                        {
                            if(MoveToPoint(CalculateCurrentPoint()) == "Arrived")
                            {
                                if (TaskStatus != TaskStatuses.MoveToBase) TaskStatus = TaskStatuses.GoDownToDrill;
                                else TaskStatus = TaskStatuses.GoDownToConnector;
                            }
                            break;
                        }
                    case TaskStatuses.GoDownToDrill:
                        {
                            if(GoToReadyForDrillingPoint(CalculateCurrentPoint()) == "Arrived")
                            {
                                TaskStatus = TaskStatuses.Drilling;
                            }
                            break;
                        }
                    case TaskStatuses.Drilling:
                        {
                            if(Drilling(CalculateCurrentPoint()) == "Arrived")
                            {
                                TaskStatus = TaskStatuses.UpToReadyForDrillingPosition;
                            }
                            break;
                        }
                    case TaskStatuses.UpToReadyForDrillingPosition:
                        { 
                            if(GoToReadyForDrillingPoint(CalculateCurrentPoint()) == "Arrived")
                            {
                                MemoryController.SaveNextDrillingPointWithStepsData(NavigationController.CalculateNextPoint(DrillingCoordinates));
                                NavigationController.ReloadCurrents(MemoryController.GetNextDrillingPointWithStepsData());
                                TaskStatus = TaskStatuses.GoToNextDrillingPosition;
                            }
                            break;
                        }
                    case TaskStatuses.GoToNextDrillingPosition:
                        {
                            if(GoToNextDrillingPosition(CalculateCurrentPoint()) == "Arrived")
                            {
                                TaskStatus = TaskStatuses.Drilling;
                            }
                            break;
                        }
                    case TaskStatuses.MoveToBase:
                        {
                            if (MoveToBase() == "Arrived")
                            {
                                TaskStatus = TaskStatuses.GoDownToConnector;
                            }
                            break;
                        }
                    case TaskStatuses.GoDownToConnector:
                        {
                            if (GoDownToConnector() == "Arrived")
                            {
                                TaskStatus = TaskStatuses.BaseConnect;
                            }
                            break;
                        }
                    case TaskStatuses.BaseConnect:
                        {
                           if (BaseConnect() == "Charged")
                           {
                                TaskStatus = TaskStatuses.MoveToPoint;
                           }
                            break;
                        }
                }
            }

            private String BaseConnect()
            {
                if (CargoController.GetCargosFullness() == 0 && !EnergyController.IsChargeRequired())
                {
                    ConnectorController.Disconnect();
                    return "Charged";
                }

                if (ConnectorController.IsConnected()) return "Charging";
                else
                {
                    Vector3D MovementDirection;

                    double ConnectorAltitude = Convert.ToDouble(MemoryController.GetConnectorAltitude());
                    Vector3D Point = DetermineNextPoint(ConnectorController.GetConnectorPoint(), ConnectorAltitude);
                    List<Vector3D> AlignData = MovementController.GetShipCenterToVectorDirectionAndSpeed(Point);

                    if (MovementController.GetShipAltitude() < ConnectorAltitude) MovementDirection = new Vector3D(AlignData[0].X, 1, AlignData[0].Z);
                    else if (MovementController.GetShipAltitude() > ConnectorAltitude) MovementDirection = new Vector3D(AlignData[0].X, -1, AlignData[0].Z);
                    else MovementDirection = new Vector3D(AlignData[0].X, 0, AlignData[0].Z);

                    MovementController.GoToDirectionWithSpeed(MovementDirection, new Vector3D(AlignData[1].X, 0.1f, AlignData[1].Z));
                    ConnectorController.TryToConnect();
                    return "Connecting";
                }

            }

            private String GoDownToConnector()
            {
                GyrosController.LookToNextPoint(DetermineNextPoint(ConnectorController.GetConnectorForwardDirection(), 1));
                double ConnectorAltitude = Convert.ToDouble(MemoryController.GetConnectorAltitude());
                Vector3D Point = DetermineNextPoint(ConnectorController.GetConnectorPoint(), ConnectorAltitude);
                List<Vector3D> AlignData = MovementController.GetShipCenterToVectorDirectionAndSpeed(Point);
                Vector3D MovementDirection;

                double Distance = NavigationController.GetDistanceToPointVector(Point);
                double Speed = MovementController.GetHorizontalSpeed();
                double ShipAltitude = MovementController.GetShipAltitude();

                if (Math.Abs(ShipAltitude - ConnectorAltitude) > 0.3)
                {
                    StatusDisplayController.AddLine("Action: Moving");
                    //Move forward only if altitude > 80% of Target Altitude

                    if (MovementController.GetShipAltitude() < ConnectorAltitude) MovementDirection = new Vector3D(AlignData[0].X, 1, AlignData[0].Z);
                    else if (MovementController.GetShipAltitude() > ConnectorAltitude) MovementDirection = new Vector3D(AlignData[0].X, -1, AlignData[0].Z);
                    else MovementDirection = new Vector3D(AlignData[0].X, 0, AlignData[0].Z);

                    MovementController.GoToDirectionWithSpeed(MovementDirection, new Vector3D(AlignData[1].X, 0.1f, AlignData[1].Z));
                    return "Moving";
                }
                else return "Arrived";

                return "Moving";

            }

            private String MoveToBase()
            {
                var Data = MemoryController.GetBaseConnectorPosition().Split(';');
                Vector3D CurrentP;

                Vector3D.TryParse(Data[0], out CurrentP);
                return MoveToPoint(CurrentP);
            }

            private Vector3D CalculateCurrentPoint()
            {
                String NextPointData = MemoryController.GetNextDrillingPointWithStepsData();
                if(NextPointData != "")
                {
                    return NavigationController.GetCurrentPoint();
                } else
                {
                    return DrillingCoordinates;
                }
            }

            private String GoToNextDrillingPosition(Vector3D NextDrillingPoint)
            {
                GyrosController.LookToNextPoint(ConnectorController.GetConnectorPoint());
                Vector3D Point = DetermineNextPoint(NextDrillingPoint, ReadyForDrillingAltitude);
                List<Vector3D> AlignData = MovementController.GetShipCenterToVectorDirectionAndSpeed(Point);
                Vector3D MovementDirection;

                double Distance = NavigationController.GetDistanceToPointVector(Point);
                double Speed = MovementController.GetHorizontalSpeed();

                if (Distance > 2)
                {
                    StatusDisplayController.AddLine("Action: Moving");
                    //Move forward only if altitude > 80% of Target Altitude
                    if (MovementController.GetShipAltitude() / ReadyForDrillingAltitude * 100 > 80)
                    {
                        if (MovementController.GetShipAltitude() < ReadyForDrillingAltitude) MovementDirection = new Vector3D(AlignData[0].X, 1, AlignData[0].Z);
                        else if (MovementController.GetShipAltitude() > ReadyForDrillingAltitude) MovementDirection = new Vector3D(AlignData[0].X, -1, AlignData[0].Z);
                        else MovementDirection = new Vector3D(AlignData[0].X, 0, AlignData[0].Z);
                    }
                    else MovementDirection = new Vector3D(AlignData[0].X, 1, AlignData[0].Z);

                    MovementController.GoToDirectionWithSpeed(MovementDirection, new Vector3D(AlignData[1].X, 0.1f, AlignData[1].Z));
                    return "Moving";
                }
                else
                {
                    if (Speed < 5)
                    {
                        MovementController.StopMovement();
                        GyrosController.ReleaseGyros();
                        return "Arrived";
                    }
                    else return "Moving";
                }
            }

            private String Drilling(Vector3D DrillingPoint)
            {
                Vector3D Point = DetermineNextPoint(DrillingPoint, SafeAltitude);
                List<Vector3D> AlignData = MovementController.GetShipCenterToVectorDirectionAndSpeed(Point);
                double Depth = MovementController.GetShipDepth(Point, SafeAltitude);

                StatusDisplayController.AddLine("Depth: " + Depth);
                StatusDisplayController.AddLine("Action: Drilling");

                GyrosController.LookToNextPoint(ConnectorController.GetConnectorPoint());

                MovementController.GoToDirectionWithSpeed(new Vector3D(AlignData[0].X, -1, AlignData[0].Z), new Vector3D(AlignData[1].X, 0.1f, AlignData[1].Z));
                DrillsController.StartDrills();

                if (Depth < DrillingDepth)
                {
                    return "Moving";
                }
                else
                {
                    DrillsController.StopDrills();
                    return "Arrived";
                }
                
                return "";
            }

            private String GoToReadyForDrillingPoint(Vector3D ReadyDrillingPoint)
            {
                Vector3D Point = DetermineNextPoint(ReadyDrillingPoint, SafeAltitude);
                double Depth = MovementController.GetShipDepth(Point, SafeAltitude);
                double Speed = MovementController.GetVerticalSpeed();
                double Altitude = MovementController.GetShipAltitude();
                double ReferenceAltDepth = Depth + ReadyForDrillingAltitude;
                List<Vector3D> AlignData = MovementController.GetShipCenterToVectorDirectionAndSpeed(Point);

                GyrosController.LookToNextPoint(ConnectorController.GetConnectorPoint());

                StatusDisplayController.AddLine("Speed: " + Speed);
                StatusDisplayController.AddLine("Altitude: " + Altitude);
                StatusDisplayController.AddLine("Depth: " + Depth);
                StatusDisplayController.AddLine("Deviation (F:L): " + Math.Round(AlignData[1].X) + ':' + Math.Round(AlignData[1].Z));

                if (ReferenceAltDepth < 0)
                {
                    StatusDisplayController.AddLine("Action: Moving Down");
                    MovementController.GoToDirectionWithSpeed(new Vector3D(AlignData[0].X, -1, AlignData[0].Z), new Vector3D(AlignData[1].X, 5, AlignData[1].Z));
                    return "Moving";
                }
                else if (ReferenceAltDepth > 0)
                {
                    StatusDisplayController.AddLine("Action: Moving Up");
                    MovementController.GoToDirectionWithSpeed(new Vector3D(AlignData[0].X, 1, AlignData[0].Z), new Vector3D(AlignData[1].X, 2, AlignData[1].Z));
                    return "Moving";
                } else
                {
                    MovementController.StopMovement();
                    return "Arrived";
                }

                return "";
            }

            private string MoveToPoint(Vector3D PointCoordinates, double PointAlt = -1)
            {
                double TargetAlt;
                Vector3D MovementDirection;
                
                if (PointAlt == -1) TargetAlt = SafeAltitude;
                else TargetAlt = PointAlt;

                Vector3D Point = DetermineNextPoint(PointCoordinates, TargetAlt);
                
                double Distance = NavigationController.GetDistanceToPointVector(Point);
                double Depth = MovementController.GetShipDepth(Point, SafeAltitude);
                double Speed = MovementController.GetHorizontalSpeed();

                StatusDisplayController.AddLine("Distance to point: " + Distance);
                StatusDisplayController.AddLine("Speed: " + Speed);

                if (Distance > 2)
                {
                    StatusDisplayController.AddLine("Action: Moving");
                    //Move forward only if altitude > 80% of Target Altitude
                    double DirectionKoef = 1;
                    DirectionKoef = Depth >= 0 ? -1 : 1;
                    StatusDisplayController.AddLine("Direction Koef: " + DirectionKoef);
                    StatusDisplayController.AddLine("Ship Altitude: " + MovementController.GetShipAltitude());
                    StatusDisplayController.AddLine("Ship Depth: " + Depth);
                    if (DirectionKoef * MovementController.GetShipAltitude() / TargetAlt * 100 > 80)
                    {
                        GyrosController.LookToNextPoint(Point);
                        if (DirectionKoef * MovementController.GetShipAltitude() < TargetAlt)
                        {
                            MovementDirection = new Vector3D(1, 1, 0);
                            StatusDisplayController.AddLine("Vertical direction: Up");
                        }
                        else if (DirectionKoef * MovementController.GetShipAltitude() > TargetAlt)
                        {
                            MovementDirection = new Vector3D(1, -1, 0);
                            StatusDisplayController.AddLine("Vertical direction: Down");
                        }
                        else MovementDirection = new Vector3D(1, 0, 0);
                    } else MovementDirection = new Vector3D(0, 1, 0);
                    StatusDisplayController.AddLine("Speed matrix : " + MovementController.CalculateSpeedMatrix(Distance));

                    MovementController.GoToDirectionWithSpeed(MovementDirection, new Vector3(MovementController.CalculateSpeedMatrix(Distance), MovementController.CalculateSpeedMatrix(Distance), 0));
                    return "Moving";
                }
                else
                {
                    if (Speed < 5)
                    {
                        MovementController.StopMovement();
                        GyrosController.ReleaseGyros();
                        return "Arrived";
                    } else return "Moving";
                }
            }

            private Vector3D DetermineNextPoint(Vector3D Point, double WithAlt)
            {
                return NavigationController.CalculatePointAboveDrilling(Point, WithAlt);
            }

            public void StopAllActivities()
            {
                GyrosController.ReleaseGyros();
                DrillsController.StopDrills();
                MovementController.StopMovement();
                TaskStatus = TaskStatuses.Default;
            }
        }

        class CargoControllerClass
        {
            List<IMyCargoContainer> Cargos = new List<IMyCargoContainer>();
            public CargoControllerClass(List<IMyCargoContainer> CargosList)
            {
                Cargos = CargosList;
            }

            public Double GetCargosFullness()
            {
                float CurrentCargosVolume = 0;
                float MaxCargosVolume = 0;
                Cargos.ForEach((Cargo) =>
                {
                    CurrentCargosVolume += (float) Cargo.GetInventory().CurrentVolume;
                    MaxCargosVolume += (float) Cargo.GetInventory().MaxVolume;
                });

                return Math.Round(CurrentCargosVolume / MaxCargosVolume * 100);
            }
        }

        class ConnectorControllerClass
        {
            IMyShipConnector Connector;
            MemoryControllerClass MemoryController;


            public ConnectorControllerClass(IMyShipConnector ConnectorInstance, MemoryControllerClass MemoryControllerInstance)
            {
                Connector = ConnectorInstance;
                MemoryController = MemoryControllerInstance;
            }

            public void TryToConnect()
            {
                Connector.ToggleConnect();
            }

            public void Disconnect()
            {
                Connector.Disconnect();
            }

            public IMyShipConnector ConnectorInstance()
            {
                return Connector;
            }

            public Boolean IsConnected()
            {
                return Connector.Status == MyShipConnectorStatus.Connected;
            } 

            public Vector3D GetConnectorPoint()
            {
                Vector3D CurrentP;
                Vector3D.TryParse(MemoryController.GetBaseConnectorPosition(), out CurrentP);
                return CurrentP;
            }
            public Vector3D GetConnectorForwardDirection()
            {
                Vector3D CurrentP;
                Vector3D.TryParse(MemoryController.GetConnectorForwardDirection(), out CurrentP);
                return CurrentP;
            }
        }

        class EnergyControllerClass
        {
            private List<IMyBatteryBlock> Batteries;
            private Boolean IsRechargeRequired = false;
            private Double MinimalPower = 10;
            public EnergyControllerClass(List<IMyBatteryBlock> BatteriesList)
            {
                Batteries = BatteriesList;
            }

            public String GetEnergyAmount()
            {
                float CurrentStoredPower = 0;
                float MaxStoredPower = 0;
                foreach (IMyBatteryBlock Battery in Batteries)
                {
                    MaxStoredPower += Battery.MaxStoredPower;
                    CurrentStoredPower += Battery.CurrentStoredPower;
                }
                return (CurrentStoredPower * 100 / MaxStoredPower).ToString();
            }

            public Boolean IsChargeRequired()
            {
                float MaxStoredPower = 0;
                float CurrentStoredPower = 0;
                foreach (IMyBatteryBlock Battery in Batteries)
                {
                    MaxStoredPower += Battery.MaxStoredPower;
                    CurrentStoredPower += Battery.CurrentStoredPower;
                }

                if ((CurrentStoredPower * 100 / MaxStoredPower) < MinimalPower) IsRechargeRequired = true;

                if (IsRechargeRequired && (CurrentStoredPower * 100 / MaxStoredPower) == 100)
                {
                    IsRechargeRequired = false;
                }
                return IsRechargeRequired;
            }
        }

        class StatusDisplayControllerClass
        {
            IMyTextSurface Display;
            List<String> TextLines = new List<String>();
            public StatusDisplayControllerClass(IMyTextSurface TextSurface)
            {
                Display = TextSurface;
                Display.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                Display.FontSize = 1F;
                Display.FontColor = Color.Red;
                Display.TextPadding = 4;
            }

            public void AddLine(String Line)
            {
                TextLines.Add(Line);
            }

            public void ShowLines()
            {
                String Message = "";
                if (TextLines.Count > 0)
                {
                    TextLines.ForEach((Line) =>
                    {
                        Message += Line + "\n";
                    });
                }
                Display.WriteText(Message, false);
            }

            public void Clear()
            {
                TextLines = new List<String>();
            }
        }

        /// End of the script

    }
}
