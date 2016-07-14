using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using Ingame = VRage.Game.ModAPI.Ingame;
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

    public class AmmoType
    {
        public string Name
        {
            get
            {
                return Definition.DisplayNameText;
            }
        }
        public MyAmmoDefinition Definition;


        public double InitialSpeed = 0.0;
        public double Acceleration = 0.0;
        public double MaximumSpeed = 0.0;
        public double Trajectory
        {
            get
            {
                return Definition.MaxTrajectory;
            }
        }

        public double GetPathAt(double time)
        {
            return Math.Min(InitialSpeed + Acceleration * time, MaximumSpeed) * time;
        }
    }


    public class Helper
    {
        static public AmmoType AmmoToAmmoType(MyAmmoDefinition ammoDef)
        {
            var ammoType = new AmmoType() { Definition = ammoDef, MaximumSpeed = ammoDef.DesiredSpeed };
            if (ammoDef is MyMissileAmmoDefinition)
            {
                var missileDef = (MyMissileAmmoDefinition)ammoDef;
                ammoType.InitialSpeed = missileDef.MissileInitialSpeed;
                ammoType.Acceleration = missileDef.MissileAcceleration;
            }
            else
            {
                ammoType.InitialSpeed = ammoDef.DesiredSpeed;
                ammoType.Acceleration = 0.0f;
            }
            return ammoType;
        }

        static public MyObjectBuilder_ToolbarItemWeapon GetCurrentWeapon(IMyCubeBlock seat)
        {
            var seatOB = seat.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_ShipController;
            if (seatOB.Toolbar == null || !seatOB.Toolbar.SelectedSlot.HasValue)
            {
                return null;
            }
            var toolbar = seatOB.Toolbar;
            var item = toolbar.Slots[toolbar.SelectedSlot.Value];
            if (!(item.Data is MyObjectBuilder_ToolbarItemWeapon))
            {
                return null;
            }
            return item.Data as MyObjectBuilder_ToolbarItemWeapon;
        }

        static public AmmoType GetFirstAmmo(IMyCubeBlock seat)
        {
            var ammos = GetCurrentAmmos(seat);
            if (ammos.Count() > 0)
            {
                return ammos.First();
            }
            return null;
        }

        //static public IMyEntity GetWeapon


        static private MyObjectBuilder_ToolbarItemWeapon m_savedWeaponType;
        static private HashSet<AmmoType> m_ammoTypes = new HashSet<AmmoType>();

        static public HashSet<AmmoType> GetCurrentAmmos(IMyCubeBlock seat)
        {
            var grid = seat.CubeGrid;
            var ammos = new HashSet<MyAmmoDefinition>();

            var weapon = GetCurrentWeapon(seat);
            if (m_savedWeaponType == weapon)
            {
                return m_ammoTypes;
            }
            m_savedWeaponType = weapon;
            m_ammoTypes.Clear();

            if (weapon == null)
            {
                return m_ammoTypes;
            }
            if (weapon.DefinitionId.TypeId != typeof(MyObjectBuilder_SmallGatlingGun) &&
               weapon.DefinitionId.TypeId != typeof(MyObjectBuilder_SmallMissileLauncher) &&
               weapon.DefinitionId.TypeId != typeof(MyObjectBuilder_SmallMissileLauncherReload))
            {
                return m_ammoTypes;
            }
            var blocks = new List<IMySlimBlock>();

            grid.GetBlocks(blocks, (x) => x.BlockDefinition.Id == weapon.DefinitionId);

            foreach (var slim in blocks)
            {
                MyObjectBuilder_GunBase gunBase;
                var ob = slim.FatBlock.GetObjectBuilderCubeBlock(false);
                if (ob is MyObjectBuilder_SmallGatlingGun)
                {
                    gunBase = (ob as MyObjectBuilder_SmallGatlingGun).GunBase;
                }
                else
                {
                    gunBase = (ob as MyObjectBuilder_SmallMissileLauncher).GunBase;
                }
                var def = MyDefinitionManager.Static.GetCubeBlockDefinition(slim.FatBlock.BlockDefinition) as MyWeaponBlockDefinition;
                var wepDef = MyDefinitionManager.Static.GetWeaponDefinition(def.WeaponDefinitionId);
                var currentMag = gunBase.CurrentAmmoMagazineName;
                foreach (var magId in wepDef.AmmoMagazinesId)
                {
                    var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magId);
                    var ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(magDef.AmmoDefinitionId);
                    if (magId.SubtypeName == currentMag)
                    {
                        ammos.Add(ammoDef);
                    }
                }
            }

            foreach (var ammo in ammos)
            {
                m_ammoTypes.Add(AmmoToAmmoType(ammo));
            }

            return m_ammoTypes;
        }
    }
}
