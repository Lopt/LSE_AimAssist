/*
Copyright © 2016 Leto
This work is free. You can redistribute it and/or modify it under the
terms of the Do What The Fuck You Want To Public License, Version 2,
as published by Sam Hocevar. See http://www.wtfpl.net/ for more details.
*/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Collections;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage;
using VRage.ObjectBuilders;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.Components;
using VRageMath;
using Sandbox.Engine.Multiplayer;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;


namespace LSE.AimHelper
{


    public class AimHelper<T> : LSE.GameLogicComponent
    {
        public static double MAX_DISTANCE = 5 * 1000; // meters
        public static double MAX_DISTANCE_SQUARED = MAX_DISTANCE * MAX_DISTANCE;

        public IMyEntity Target;
        public IMyGps CurrentGPS;
        public bool HadLocalPlayerAccess;
        
        public bool FirstStart = true;


        public bool DEBUG = false;
        public LineD? DrawLine;

        public class AimIndicator
        {
            public Vector3D PredictedPosition;
            public string Name;
        }

        public override void UpdateBeforeSimulation()
        {
            base.UpdateAfterSimulation();

            if (MyAPIGateway.Session.LocalHumanPlayer == null)
            {
                return;
            }

            if (FirstStart)
            {
                CreateUI();
            }


            
            if (DEBUG && DrawLine != null)
            {
                var color = new Vector4(128, 127, 127, 255);
                //var bb1= Target.WorldAABBHr;
                MySimpleObjectDraw.DrawLine(DrawLine.Value.From, DrawLine.Value.To, "Metal", ref color, 0.1f);
            }
            if (DEBUG && Target != null)
            {
                var bb1 = Target.WorldAABB;
                var corners1 = bb1.GetCorners();

                var red = new Vector4(255, 0, 0, 255);
                foreach (var corner in corners1)
                {
                    foreach (var endCorner in corners1)
                    {
                        MySimpleObjectDraw.DrawLine(corner, endCorner, "Metal", ref red, 0.05f);                
                    }
                }
            }
            try
            {
                
                if (MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity.Entity == Entity)
                {
                    HadLocalPlayerAccess = true;
                    SetGPS();
                    if (Target == null)
                    {
                        RemoveGPS();
                    }
                }
                else if (HadLocalPlayerAccess)
                {
                    HadLocalPlayerAccess = false;
                    RemoveGPS();
                }
            }
            catch (Exception error)
            {
                //MyAPIGateway.Utilities.ShowNotification(error.Message);
            }
        }

        void RemoveGPS()
        {
            if (CurrentGPS != null)
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(CurrentGPS);
                CurrentGPS = null;
            };
        }

        void SetGPS()
        {
            if (Target != null)
            {
                if ((Target.Physics.CenterOfMassWorld - Entity.GetPosition()).LengthSquared() > MAX_DISTANCE_SQUARED)
                {
                    Target = null;
                    RemoveGPS();
                    return;
                }
                else if (Target.Visible == false) // cloaking device ;-) and ships which are out of sight 
                {
                    Target = null;
                    RemoveGPS();
                    return;
                }
                else
                {
                    var indicator = CalculateAimPosition(Target);
                    if (indicator != null)
                    {
                        if (CurrentGPS == null)
                        {
                            CurrentGPS = MyAPIGateway.Session.GPS.Create(indicator.Name, "Aim Assist", indicator.PredictedPosition, true, true);
                        }
                        else
                        {
                            CurrentGPS.Coords = indicator.PredictedPosition;
                            CurrentGPS.UpdateHash();
                        }
                        CurrentGPS.DiscardAt = new TimeSpan(0, 0, 1);
                        MyAPIGateway.Session.GPS.AddLocalGps(CurrentGPS);
                    }
                }
            }
        }

        public Vector3D ApproximatePosition(
            Vector3D ownPos,
            Vector3D ownVelocity,
            AmmoType ammoDef,
            Vector3D enemyPos,
            Vector3D enemyVelocity,
            double oldTime,
            int depth)
        {
            enemyVelocity = (enemyVelocity - ownVelocity);
            ownVelocity = Vector3D.Zero;
            var enemyCalcPos = enemyPos + enemyVelocity * oldTime;
            var ownCalcPos = ownPos + ownVelocity * oldTime;
            var bulletVelocity = (enemyCalcPos - ownPos);
            bulletVelocity.Normalize();
            if (oldTime == 0)
            {
                oldTime = 0.1;
            }
            bulletVelocity = bulletVelocity * ammoDef.GetPathAt(oldTime) / oldTime + ownVelocity;

            var pathLength = (enemyCalcPos - ownCalcPos).Length();

            var newTime = pathLength / bulletVelocity.Length();
            if (depth == 0 || (newTime - oldTime) < 0.000001)
            {
                return enemyCalcPos; // ownCalcPos + bulletVelocity * tCenter;
            }

            return ApproximatePosition(ownPos, ownVelocity, ammoDef, enemyPos, enemyVelocity, newTime, depth - 1);

        }

        private Vector3 m_lastCenter;
        // abusing of the physic center to determinate if something in the ship changed
        // so it's not necessary to calculate every time
        private MyDefinitionId m_lastId;
        private Vector3I? m_lastReturn;
        public Vector3I? CalculateWeaponCenter(MyDefinitionId id)
        {
            var grid = ((IMyCubeBlock)Entity).CubeGrid;

            if (m_lastCenter != grid.Physics.Center || m_lastId != id)
            {
                m_lastCenter = grid.Physics.Center;
                m_lastId = id;
                var weapons = new List<IMySlimBlock>();
                grid.GetBlocks(weapons, (x) => (x.BlockDefinition.Id == id));
                var position = Vector3I.Zero;
                foreach (var weapon in weapons)
                {
                    position += weapon.Position;
                }

                if (weapons.Count() != 0)
                {
                    m_lastReturn = position / weapons.Count();
                }
                else
                {
                    m_lastReturn = null;
                }
            }
            return m_lastReturn;
        }


        MyDefinitionId m_lastWeaponId;
        public AimIndicator CalculateAimPosition(IMyEntity target)
        {
            var ownGrid = (Entity as IMyCubeBlock).CubeGrid;
            var currentWeaponDef = Helper.GetCurrentWeapon((IMyCubeBlock)Entity);
            if (currentWeaponDef == null)
            {
                RemoveGPS();
                return null;
            }
            var ammoTypes = Helper.GetCurrentAmmos((IMyCubeBlock)Entity);
            if (ammoTypes.Count() > 0)
            {
                var usedAmmo = ammoTypes.First();
                var weaponCenter = CalculateWeaponCenter(currentWeaponDef.defId); // Vector3I.Zero;// 
                if (weaponCenter == null)
                {
                    return null;
                }

                var cameraPos = MyAPIGateway.Session.Camera.Position;
                if (Entity is Sandbox.ModAPI.Ingame.IMyCockpit && MyAPIGateway.Session.CameraController.IsInFirstPersonView)
                {
                    cameraPos = Entity.GetPosition();
                }

                var ownPos = (Entity as IMyCubeBlock).CubeGrid.GridIntegerToWorld(weaponCenter.Value); // Entity.GetPosition()
                
                var targetPos = ApproximatePosition(
                    ownPos,
                    ownGrid.Physics.LinearVelocity,
                    usedAmmo,
                    target.Physics.CenterOfMassWorld,
                    target.Physics.LinearVelocity,
                    0.0f,
                    20);

                var indicatorName = "OUT OF RANGE";
                if ((targetPos - ownPos).LengthSquared() < Math.Pow(usedAmmo.Trajectory, 2))
                {
                    indicatorName = "TARGET";
                }

                return new AimIndicator()
                {
                    PredictedPosition = targetPos + (cameraPos - ownPos),
                    Name = indicatorName
                };
            }
            return null;
        }

        public bool ReturnFalse(IMyTerminalBlock block)
        {
            return false;
        }

        public void CreateUI()
        {
            new LockAction<T>((IMyTerminalBlock)Entity, "LockTarget", "Lock Target");
        }

    }

    public class LockAction<T> : LSE.Control.ControlAction<T>
    {

        public LockAction(
            IMyTerminalBlock block,
            string internalName,
            string name)
            : base(block, internalName, name)
        {
        }

        public double Distance(IMyTerminalBlock block, IMyEntity entity)
        {
            // the entity may not be in direct sight of the block - because the AABB is too big.
            // here the AABB size will be added, so it's a tendencially higher chance to lock smaller targets if both intersect
            var additionalLengthSqaured = entity.LocalAABB.Size.LengthSquared();
            
            //var forward = block.WorldMatrix.Forward;

            return (block.GetPosition() - entity.GetPosition()).LengthSquared() + additionalLengthSqaured;
        }


        public override void OnAction(IMyTerminalBlock block)
        {
            try
            {

                var aimHelper = block.GameLogic.GetAs<AimHelper<T>>();
                if (MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity.Entity == block)
                {
                    var player = MyAPIGateway.Session.LocalHumanPlayer;
                    //MyAPIGateway.Session.CameraController.
                    var cockpitMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
                    if (block is Sandbox.ModAPI.Ingame.IMyCockpit)
                    {
                        cockpitMatrix = block.WorldMatrix;
                    }

                    var endPos = cockpitMatrix.Translation + cockpitMatrix.Forward * AimHelper<T>.MAX_DISTANCE;

                    var line = new LineD(cockpitMatrix.Translation, endPos);
                    //var color = new Vector4(128, 127, 127, 127);
                    //MySimpleObjectDraw.DrawLine(line.From, line.To, "Metal", ref color, 0.1f);
                    var entities = new HashSet<IMyEntity>();
                    aimHelper.DrawLine = line;
                    MyAPIGateway.Entities.GetEntities(entities, (x) => x.WorldAABB.Intersects(ref line) && x is IMyCubeGrid);
                    
                    IMyEntity nextTarget = null;
                    double lowestDistance = double.PositiveInfinity;
                    entities.Remove(block.CubeGrid);
                    foreach (var entity in entities)
                    {
                        var distance = Distance(block, entity);
                        if (distance < lowestDistance)
                        {
                            lowestDistance = distance;
                            nextTarget = entity;
                        }

                        /*
                        foreach (var owner in (entity as IMyCubeGrid).BigOwners)
                        {
                            if (MyAPIGateway.Session.LocalHumanPlayer.GetRelationTo(owner) == MyRelationsBetweenPlayerAndBlock.Enemies)
                            {
                            }
                        }
                                             break;
    */
                    }
                    aimHelper.Target = nextTarget;
                }
            }
            catch (Exception error)
            {
                //MyAPIGateway.Utilities.ShowNotification(error.Message);
            }
        }
    }


    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Cockpit))]
    public class Cockpit : AimHelper<Sandbox.ModAPI.Ingame.IMyCockpit>
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl))]
    public class RemoteControl : AimHelper<Sandbox.ModAPI.Ingame.IMyRemoteControl>
    {
    }

}