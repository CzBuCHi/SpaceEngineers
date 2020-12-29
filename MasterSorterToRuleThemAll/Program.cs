using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;

namespace IngameScript {
    public class Program : MyGridProgram {
        // Tag used to recognize master sorter
        private const string MASTER = "[MASTER]";

        // Tag for slaves
        private const string SLAVE = "[SLAVE]";

        public void Main(string argument, UpdateType updateSource) {
            // Finding master ...
            List<IMyConveyorSorter> masters = new List<IMyConveyorSorter>();
            GridTerminalSystem.GetBlocksOfType(masters, b => b.CustomName.Contains(MASTER) && b.CubeGrid == Me.CubeGrid);

            if (masters.Count == 0) {
                Echo("Error: Cannot find master.");
                return;
            }

            if (masters.Count > 1) {
                Echo("Error: Found more than one master.");
                return;
            }

            // and slaves ...
            List<IMyConveyorSorter> slaves = new List<IMyConveyorSorter>();
            GridTerminalSystem.GetBlocksOfType(slaves, b => b.CustomName.Contains(SLAVE) && b.CubeGrid == Me.CubeGrid);

            if (slaves.Count == 0) {
                Echo("Error: Cannot find any slave.");
                return;
            }

            // Copy filter info from master to slaves
            IMyConveyorSorter master = masters[0];

            List<MyInventoryItemFilter> items = new List<MyInventoryItemFilter>();
            master.GetFilterList(items);

            foreach (IMyConveyorSorter slave in slaves) {
                slave.DrainAll = master.DrainAll;
                slave.SetFilter(master.Mode, items);
            }
        }
    }
}
