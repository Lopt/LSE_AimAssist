﻿/*
Copyright © 2016 Leto
This work is free. You can redistribute it and/or modify it under the
terms of the Do What The Fuck You Want To Public License, Version 2,
as published by Sam Hocevar. See http://www.wtfpl.net/ for more details.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;

namespace LSE.Control
{
    public class ButtonControl<T> : BaseControl<T>
    {
        public ButtonControl(
            IMyTerminalBlock block,
            string internalName,
            string title)
            : base(block, internalName, title)
        {
        }

        public override void OnCreateUI()
        {
            var button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>(InternalName);
            button.Title = VRage.Utils.MyStringId.GetOrCompute(Title);
            button.Action = OnAction;
            button.Enabled = Enabled;
            button.Visible = ShowControl;
            MyAPIGateway.TerminalControls.AddControl<T>(button);
        }

        public virtual void OnAction(IMyTerminalBlock block)
        {
        }

    }
}   