using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    internal class Program : MyGridProgram {
        // Prefix and suffix in group / block name (group name is read from between these two)
        private const string GROUP_PREFIX = @"[Inventory:";
        private const string GROUP_SUFFIX = @"]";

        // Tag for display(s) that show list of states of all groups.
        private const string MAIN_DISPLAY_TAG = @"[Inventory]";

        // Two numbers are considered equal if their difference is less than this value.
        // (Used if command uses (not)equal operator)
        private const float TOLERANCE = 0.001f;

        // Prefix before display index in 'display' block
        private const string SURFACE_INDEX_PREFIX = "INVENTORY:";

        // Prefix and suffix for configuration in custom data
        private const string CONFIG_START_PREFIX = @"[Inventory:";
        private const string CONFIG_END_PREFIX = @"[/Inventory:";
        private const string CONFIG_SUFFIX = @"]";

        // End of configuration
        ////////////////////////////////////////////////////////////

        private readonly List<IMyTextSurface> _Displays = new List<IMyTextSurface>();
        private readonly List<Group> _Groups = new List<Group>();

        public Program() {
            List<IMyTerminalBlock> displays = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(displays, o => o.CustomName.Contains(MAIN_DISPLAY_TAG));

            foreach (IMyTerminalBlock block in displays) {
                IMyTextSurfaceProvider provider = block as IMyTextSurfaceProvider;
                if (provider != null) {
                    int surfaceIndex = GetTextSurfaceIndex(block);
                    IMyTextSurface surface = provider.GetSurface(surfaceIndex);
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    _Displays.Add(surface);
                } else {
                    Echo("Cannot use block '" + block.CustomName + "' as display.");
                }
            }

            IEnumerable<KeyValuePair<string, List<IMyTerminalBlock>>> groups = ResolveGroups();

            foreach (KeyValuePair<string, List<IMyTerminalBlock>> pair in groups) {
                if (pair.Key == "") {
                    Echo("Found unnamed group. Ignoring.");
                    continue;
                }

                Group group = ResolveGroupManager(pair.Key, pair.Value);
                if (group != null) {
                    _Groups.Add(group);
                }
            }

            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        // ReSharper disable once UnusedMember.Global
        public void Main( /*string argument, UpdateType updateSource*/) {
            foreach (IMyTextSurface display in _Displays) {
                display.WriteText("InventoryEvent 1.0 status:\n\n");
            }

            foreach (Group group in _Groups) {
                CalcVolumes(group);
                ProcessCommands(group);

                foreach (IMyTextSurface display in group.Displays) {
                    UpdateDisplay(display, group);
                }

                foreach (IMyTextSurface display in _Displays) {
                    UpdateDisplay(display, group, true);
                }
            }
        }

        private int GetTextSurfaceIndex(IMyTerminalBlock block) {
            IMyTextSurfaceProvider displayProvider = (IMyTextSurfaceProvider) block;
            int surfaceIndex = 0;
            if (displayProvider.SurfaceCount > 1) {
                string indexRaw = block.CustomData.Split('\n').FirstOrDefault(o => o.StartsWith(SURFACE_INDEX_PREFIX));
                if (indexRaw != null) {
                    if (!int.TryParse(indexRaw.Substring(SURFACE_INDEX_PREFIX.Length), out surfaceIndex)) {
                        Echo("Warning: Unable parse display index from '" + indexRaw + "'. First display will be used.");
                    }
                }
            }

            return surfaceIndex;
        }

        private IEnumerable<KeyValuePair<string, List<IMyTerminalBlock>>> ResolveGroups() {
            Dictionary<string, List<IMyTerminalBlock>> dict = new Dictionary<string, List<IMyTerminalBlock>>();
            Action<string, IMyTerminalBlock> addBlock = (groupName, block) => {
                List<IMyTerminalBlock> list;
                if (!dict.TryGetValue(groupName, out list)) {
                    list = new List<IMyTerminalBlock>();
                    dict.Add(groupName, list);
                    Echo("Found new group '" + groupName + "' ...");
                }

                list.Add(block);
            };

            List<IMyBlockGroup> blockGroups = new List<IMyBlockGroup>();
            GridTerminalSystem.GetBlockGroups(blockGroups, o => o.Name.Contains(GROUP_PREFIX));

            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            foreach (IMyBlockGroup blockGroup in blockGroups) {
                string groupName = GetGroupName(blockGroup.Name);

                blockGroup.GetBlocks(blocks);
                foreach (IMyTerminalBlock block in blocks) {
                    IMyTextPanel panel = block as IMyTextPanel;
                    if (panel != null) {
                        panel.ContentType = ContentType.TEXT_AND_IMAGE;
                    }

                    addBlock(groupName, block);
                }
            }

            blocks.Clear();
            GridTerminalSystem.GetBlocksOfType(blocks, o => o.CustomName.Contains(GROUP_PREFIX));
            foreach (IMyTerminalBlock block in blocks) {
                IMyTextPanel panel = block as IMyTextPanel;
                if (panel != null) {
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;
                }

                string groupName = GetGroupName(block.CustomName);
                addBlock(groupName, block);
            }

            return dict;
        }

        private Group ResolveGroupManager(string groupName, List<IMyTerminalBlock> blocks) {
            List<IMyTerminalBlock> cargo = new List<IMyTerminalBlock>();
            List<IMyTextSurface> displays = new List<IMyTextSurface>();
            List<Command> commands = new List<Command>();

            foreach (IMyTerminalBlock block in blocks) {
                Echo("Processing block '" + block.CustomName + "' ...");
                if (block.HasInventory) {
                    cargo.Add(block);
                }

                IMyTextSurface surface = block as IMyTextSurface;
                if (surface != null) {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    displays.Add(surface);
                }

                bool foundConfig = false;
                // ReSharper disable once StringIndexOfIsCultureSpecific.1
                int start = block.CustomData.IndexOf(CONFIG_START_PREFIX + groupName + CONFIG_SUFFIX);
                if (start != -1) {
                    // ReSharper disable once StringIndexOfIsCultureSpecific.1
                    int end = block.CustomData.IndexOf(CONFIG_END_PREFIX + groupName + CONFIG_SUFFIX);
                    if (end != -1) {
                        string configLines = block.CustomData.Substring(start, end - start);
                        foundConfig = true;

                        foreach (string line in configLines.Split('\n')) {
                            if (line.TrimStart().StartsWith(";")) {
                                continue;
                            }

                            int index = line.IndexOf(';');
                            string commandConfig = (index != -1 ? line.Substring(0, index - 1) : line).Trim();

                            try {
                                commands.Add(ParseCommand(block, commandConfig));
                                Echo("Successfully parsed command '" + line + "' on block '" + block.CustomName + "'.");
                            } catch (Exception e) {
                                Echo("Warning: Unable to parse command '" + line + "' on block '" + block.CustomName + "'. Ignoring. (Error message: '" + e.Message + "'");
                            }
                        }
                    }
                }

                if (!foundConfig) {
                    Echo("Adding list of commands into '" + block.CustomName + "' CustomData.");

                    List<ITerminalAction> actions = new List<ITerminalAction>();
                    block.GetActions(actions);

                    IEnumerable<string> lines = actions.Select(o => o.Id + " (" + o.Name + ")");
                    block.CustomData += "\n\n[Inventory:" + groupName + "]\n" +
                                        string.Join("\n; ", lines) + "\n" +
                                        "[/Inventory:" + groupName + "]";
                }
            }

            if (cargo.Count == 0) {
                Echo("Warning: Group " + groupName + " has no cargo. Ignoring.");
                return null;
            }

            return new Group {
                GroupName = groupName,
                Cargo = cargo,
                Displays = displays,
                Commands = commands
            };
        }

        private static string GetGroupName(string blockOrGroupName) {
            // ReSharper disable once StringIndexOfIsCultureSpecific.1
            int start = blockOrGroupName.IndexOf(GROUP_PREFIX) + GROUP_PREFIX.Length;
            // ReSharper disable once StringIndexOfIsCultureSpecific.2
            int end = blockOrGroupName.IndexOf(GROUP_SUFFIX, start);
            return blockOrGroupName.Substring(start, end - start);
        }

        private static Command ParseCommand(IMyTerminalBlock block, string configuration) {
            string[] tokens = configuration.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 3) {
                throw new Exception("Invalid command syntax.");
            }

            string volume = tokens[2];
            bool percents = volume.EndsWith("%");
            if (percents) {
                volume = volume.Substring(0, volume.Length - 1);
            }

            float value = float.Parse(volume);

            Func<float, float, bool> op = GetOperatorFunction(tokens[1]);

            Func<Group, bool> canExecute;
            if (percents) {
                canExecute = manager => op(manager.PercentUsed, value);
            } else {
                canExecute = manager => op(manager.UsedVolume, value);
            }

            return new Command {
                CanExecute = canExecute,
                Execute = () => block.GetActionWithName(tokens[0]).Apply(block)
            };
        }

        private static Func<float, float, bool> GetOperatorFunction(string @operator) {
            switch (@operator) {
                case "=":
                case "==":
                    return (a, b) => Math.Abs(a - b) < TOLERANCE;
                case "!=":
                case "<>":
                    return (a, b) => Math.Abs(a - b) > TOLERANCE;
                case "<":
                    return (a, b) => a < b;
                case "<=":
                    return (a, b) => a <= b;
                case ">":
                    return (a, b) => a > b;
                case ">=":
                    return (a, b) => a >= b;
                default:
                    throw new Exception("Unknown operator '" + @operator + "'");
            }
        }

        private static void CalcVolumes(Group group) {
            group.TotalVolume = 0;
            group.UsedVolume = 0;
            foreach (IMyTerminalBlock block in group.Cargo) {
                for (int i = 0; i < block.InventoryCount; i++) {
                    IMyInventory inv = block.GetInventory(i);
                    group.TotalVolume += (float) inv.MaxVolume;
                    group.UsedVolume += (float) inv.CurrentVolume;
                }
            }
        }

        private static void ProcessCommands(Group group) {
            foreach (Command command in group.Commands) {
                bool canExecute = command.CanExecute(group);
                if (canExecute && !command.Executed) {
                    command.Execute();
                    command.Executed = true;
                } else {
                    command.Executed = false;
                }
            }
        }

        private static void UpdateDisplay(IMyTextSurface surface, Group group, bool append = false) {
            surface.WriteText(
                group.GroupName + ": " +
                group.UsedVolume.ToString("N3") + "L of " +
                group.TotalVolume.ToString("N3") + "L (" +
                group.PercentUsed.ToString("N1") + "%)\n", append);
        }

        private class Group {
            public List<IMyTerminalBlock> Cargo;
            public List<Command> Commands;
            public List<IMyTextSurface> Displays;
            public string GroupName;
            public float TotalVolume;
            public float UsedVolume;
            public float PercentUsed => UsedVolume * 100.0f / TotalVolume;
        }

        private class Command {
            public Func<Group, bool> CanExecute;
            public Action Execute;
            public bool Executed;
        }
    }
}
