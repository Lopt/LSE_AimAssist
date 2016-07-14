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

        public Control.SwitchControl<T> ShootLockTarget;

        public class AimIndicator
        {
            public Vector3D PredictedPosition;
            public string Name;
        }

        long LastShootTime;
        public bool DidShoot()
        {
            var currentWeaponDef = Helper.GetCurrentWeapon((IMyCubeBlock)Entity);
            if (currentWeaponDef == null)
            {
                return false;
            }
            var weapons = new List<IMySlimBlock>();
            (Entity as IMyCubeBlock).CubeGrid.GetBlocks(weapons, (x) => (x.BlockDefinition.Id == currentWeaponDef.defId));
            if (weapons.Count() > 0)
            {
                var ob = weapons[0].GetObjectBuilder();
                long newShootTime = 0;
                if (ob is MyObjectBuilder_SmallGatlingGun)
                {
                    newShootTime = (ob as MyObjectBuilder_SmallGatlingGun).GunBase.LastShootTime;
                }
                else if  (ob is MyObjectBuilder_SmallMissileLauncher)
                {
                    newShootTime = (ob as MyObjectBuilder_SmallMissileLauncher).GunBase.LastShootTime;
                }
                else if  (ob is MyObjectBuilder_SmallMissileLauncherReload)
                {
                    newShootTime = (ob as MyObjectBuilder_SmallMissileLauncherReload).GunBase.LastShootTime;
                }
                if (newShootTime > LastShootTime)
                {
                    LastShootTime = newShootTime;
                    return true;
                }
            }
            return false;
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
                if (ShootLockTarget.Getter((IMyTerminalBlock)Entity) && DidShoot())
                {
                    var newTarget = FindTargetByPrediction();
                    if (newTarget != null)
                    {
                        Target = newTarget;
                    }
                }


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
                // MyAPIGateway.Utilities.ShowNotification(error.Message);
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

        public IMyEntity FindTargetByPrediction()
        {
            var currentWeaponDef = Helper.GetCurrentWeapon((IMyCubeBlock)Entity);
            if (currentWeaponDef == null)
            {
                RemoveGPS();
                return null;
            }
            var usedAmmo = Helper.GetFirstAmmo((IMyCubeBlock)Entity);
            if (usedAmmo == null)
            {
                return null;
            }

            var entities = new HashSet<IMyEntity>();
            var line = GetTargetLine();            

            MyAPIGateway.Entities.GetEntities(entities, (x) => (x is IMyCubeGrid &&
                x.WorldAABB.Translate(CalculateAimPosition(x, currentWeaponDef.defId, usedAmmo).PredictedPosition - x.Physics.CenterOfMassWorld).Intersects(ref line)));
            entities.Remove((Entity as IMyCubeBlock).CubeGrid);
            return GetClosestShip(entities);    
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
                    var currentWeaponDef = Helper.GetCurrentWeapon((IMyCubeBlock)Entity);
                    if (currentWeaponDef == null)
                    {
                        RemoveGPS();
                        return;
                    }
                    var usedAmmo = Helper.GetFirstAmmo((IMyCubeBlock)Entity);
                    if (usedAmmo == null)
                    {
                        return;
                    }

                    var indicator = CalculateAimPosition(Target, currentWeaponDef.defId, usedAmmo);
                    if (indicator != null)
                    {
                        if (CurrentGPS == null)
                        {
                            CurrentGPS = MyAPIGateway.Session.GPS.Create(indicator.Name, "Aim Assist", indicator.PredictedPosition, true, true);
                        }
                        else
                        {
                            CurrentGPS.Name = indicator.Name;
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
            var relEnemyVelocity = (enemyVelocity - ownVelocity) / 2;
            var enemyCalcPos = enemyPos + relEnemyVelocity * oldTime;
            //var ownCalcPos = ownPos;// +ownVelocity * oldTime;
            var bulletVelocity = (enemyCalcPos - ownPos);
            bulletVelocity.Normalize();
            if (oldTime == 0)
            {
                oldTime = 0.01;
            }
            bulletVelocity = bulletVelocity * ammoDef.GetPathAt(oldTime) / oldTime; //+ ownVelocity;

            var pathLength = (enemyCalcPos - ownPos).Length();

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
        public AimIndicator CalculateAimPosition(IMyEntity target, MyDefinitionId weaponId, AmmoType usedAmmo)
        {
            var ownGrid = (Entity as IMyCubeBlock).CubeGrid;
            var weaponCenter = CalculateWeaponCenter(weaponId); // Vector3I.Zero;// 
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

        public bool ReturnFalse(IMyTerminalBlock block)
        {
            return false;
        }

        public void CreateUI()
        {
            new LockAction<T>((IMyTerminalBlock)Entity, "LockTarget", "Lock Target");
            ShootLockTarget = new Control.SwitchControl<T>((IMyTerminalBlock)Entity, "LockOnShoot", "Auto Lock Target", "On", "Off", true);
        }

        public IMyEntity GetClosestShip(HashSet<IMyEntity> entities)
        {
            double lowestDistance = double.PositiveInfinity;
            IMyEntity nextTarget = null;
            foreach (var entity in entities)
            {
                var distance = Distance(entity);
                if (distance < lowestDistance)
                {
                    lowestDistance = distance;
                    nextTarget = entity;
                }
            }
            return nextTarget;
        }

        public double Distance(IMyEntity foreignEntity)
        {
            // the entity may not be in direct sight of the block - because the AABB is too big.
            // here the AABB size will be added, so it's a tendencially higher chance to lock smaller targets if both intersect
            var additionalLengthSqaured = foreignEntity.LocalAABB.Size.LengthSquared();

            //var forward = block.WorldMatrix.Forward;

            return (Entity.GetPosition() - foreignEntity.Physics.CenterOfMassWorld).LengthSquared() + additionalLengthSqaured;
        }

        public LineD GetTargetLine()
        {
            var cockpitMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            if (Entity is Sandbox.ModAPI.Ingame.IMyCockpit)
            {
                cockpitMatrix = Entity.WorldMatrix;
            }

            var endPos = cockpitMatrix.Translation + cockpitMatrix.Forward * MAX_DISTANCE;

            return new LineD(cockpitMatrix.Translation, endPos);
        }




    }

    public class LockAction<T> : LSE.Control.ControlAction<T>
    {

        public LockAction(
            IMyTerminalBlock block,
            string internalName,
            string name)
            : base(block, internalName, name, "")
        {
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
                    var line = aimHelper.GetTargetLine();
                    //var color = new Vector4(128, 127, 127, 127);
                    //MySimpleObjectDraw.DrawLine(line.From, line.To, "Metal", ref color, 0.1f);
                    var entities = new HashSet<IMyEntity>();
                    aimHelper.DrawLine = line;
                    MyAPIGateway.Entities.GetEntities(entities, (x) => x.WorldAABB.Intersects(ref line) && x is IMyCubeGrid);
                    
                    entities.Remove(block.CubeGrid);
                    var nextTarget = aimHelper.GetClosestShip(entities);
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